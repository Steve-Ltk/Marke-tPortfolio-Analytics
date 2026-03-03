using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace MarketPortfolioAnalytics.Services
{
    /// <summary>
    /// Simulation Monte Carlo par Geometric Brownian Motion (GBM).
    ///
    /// Principe :
    ///   On estime μ (rendement moyen journalier) et σ (volatilité journalière)
    ///   directement sur la SÉRIE DE VALEUR AGRÉGÉE du portefeuille.
    ///   Cette approche est correcte car elle capture automatiquement les
    ///   corrélations entre actifs — contrairement à une simulation actif par actif.
    ///
    /// Modèle GBM :
    ///   V(t+1) = V(t) × exp( (μ - σ²/2) + σ × Z )
    ///   où Z ~ N(0,1) est un choc aléatoire journalier
    ///
    ///   (μ - σ²/2) est la dérive corrigée d'Itô — garantit que E[V(t)] = V(0) × e^(μt)
    /// </summary>
    public class MonteCarloService
    {
        private readonly MarketPortfolioAnalyticsContext _context;
        private readonly PortfolioAnalyticsService _analytics;

        public MonteCarloService(
            MarketPortfolioAnalyticsContext context,
            PortfolioAnalyticsService analytics)
        {
            _context = context;
            _analytics = analytics;
        }

        public async Task<MonteCarloResult?> SimulateAsync(
            int portfolioId, MonteCarloRequest req)
        {
            // Période historique pour estimer μ et σ
            DateTime histTo = (req.HistoryTo ?? DateTime.UtcNow.Date).Date;
            DateTime histFrom = (req.HistoryFrom ?? histTo.AddYears(-2)).Date;

            // Chargement du portefeuille
            var portfolio = await _context.Portfolio
                .Include(p => p.ListePositions!)
                    .ThenInclude(pos => pos.Asset)
                .FirstOrDefaultAsync(p => p.Id == portfolioId);

            if (portfolio is null) return null;

            var positions = portfolio.ListePositions?.ToList()
                            ?? new List<Position>();

            if (positions.Count == 0) return null;

            // ── Construction de la série historique du portefeuille ───────────
            var assetIds = positions.Select(p => p.AssetId).ToList();
            var allPrices = await _analytics.LoadPricesAsync(assetIds, histFrom, histTo);

            var pricesByAsset = allPrices
                .GroupBy(ap => ap.AssetId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(ap => ap.Date).ToList());

            var (_, portValues) = _analytics.BuildPortfolioSeries(positions, pricesByAsset);

            if (portValues.Length < 20)
                return null;   // pas assez d'historique pour estimer les paramètres

            // ── Estimation des paramètres GBM sur la série agrégée ────────────
            double[] portReturns = FinancialMath.SimpleReturns(portValues);
            double muDaily = FinancialMath.Mean(portReturns);
            double sigmaDaily = FinancialMath.StdDev(portReturns);

            // Dérive corrigée d'Itô : drift = μ - σ²/2
            // Sans cette correction, la simulation surestimerait le rendement espéré
            double drift = muDaily - 0.5 * sigmaDaily * sigmaDaily;

            double initVal = portValues[^1];   // valeur actuelle du portefeuille
            decimal initialValue = (decimal)initVal;

            if (initialValue <= 0) return null;

            int T = req.HorizonDays;
            int N = req.NumSimulations;
            var rng = new Random(42);   // graine fixe → reproductible

            // ── Simulation des N chemins sur T jours ──────────────────────────
            // allPaths[sim][jour] = valeur du portefeuille à ce jour pour cette simulation
            var allPaths = new double[N][];

            for (int sim = 0; sim < N; sim++)
            {
                allPaths[sim] = new double[T + 1];
                allPaths[sim][0] = initVal;
                double val = initVal;

                for (int t = 1; t <= T; t++)
                {
                    double z = FinancialMath.NormalRandom(rng);
                    val *= Math.Exp(drift + sigmaDaily * z);
                    allPaths[sim][t] = val;
                }
            }

            // ── Série temporelle des percentiles (max 100 points) ─────────────
            int step = Math.Max(1, T / 100);
            var timeSeries = new List<MonteCarloTimePoint>();

            for (int t = 0; t <= T; t += step)
            {
                var dayVals = allPaths
                    .Select(path => path[t])
                    .OrderBy(v => v)
                    .ToArray();

                timeSeries.Add(new MonteCarloTimePoint
                {
                    Day = t,
                    P5 = (decimal)FinancialMath.Percentile(dayVals, 0.05),
                    P25 = (decimal)FinancialMath.Percentile(dayVals, 0.25),
                    Median = (decimal)FinancialMath.Percentile(dayVals, 0.50),
                    P75 = (decimal)FinancialMath.Percentile(dayVals, 0.75),
                    P95 = (decimal)FinancialMath.Percentile(dayVals, 0.95)
                });
            }

            // ── Métriques sur les valeurs finales ─────────────────────────────
            double[] finalVals = allPaths
                .Select(p => p[T])
                .OrderBy(v => v)
                .ToArray();

            double var95 = initVal - FinancialMath.Percentile(finalVals, 0.05);
            double var99 = initVal - FinancialMath.Percentile(finalVals, 0.01);

            // CVaR95 : moyenne des valeurs finales dans les 5% pires scénarios
            double p5Threshold = FinancialMath.Percentile(finalVals, 0.05);
            var tail = finalVals.Where(v => v <= p5Threshold).ToArray();
            double cvar95 = tail.Length > 0
                ? initVal - tail.Average()
                : var95;

            return new MonteCarloResult
            {
                PortfolioId = portfolioId,
                HorizonDays = T,
                NumSimulations = N,
                InitialValue = initialValue,

                Percentile5 = (decimal)FinancialMath.Percentile(finalVals, 0.05),
                Percentile25 = (decimal)FinancialMath.Percentile(finalVals, 0.25),
                Median = (decimal)FinancialMath.Percentile(finalVals, 0.50),
                Percentile75 = (decimal)FinancialMath.Percentile(finalVals, 0.75),
                Percentile95 = (decimal)FinancialMath.Percentile(finalVals, 0.95),

                VaR95 = (decimal)Math.Max(0.0, var95),
                VaR99 = (decimal)Math.Max(0.0, var99),
                CVaR95 = (decimal)Math.Max(0.0, cvar95),

                ProbabilityOfLossPct = Math.Round(
                    (double)finalVals.Count(v => v < initVal) / N * 100, 2),
                ExpectedFinalReturnPct = Math.Round(
                    (finalVals.Average() / initVal - 1.0) * 100, 4),

                TimeSeries = timeSeries,
                FinalValueHistogram = BuildHistogram(finalVals, 20)
            };
        }

        // ── Helper privé ──────────────────────────────────────────────────────

        /// <summary>
        /// Construit un histogramme de fréquences sur un tableau de valeurs triées.
        /// Divise l'intervalle [min, max] en `buckets` tranches égales.
        /// </summary>
        private static List<MonteCarloHistogramBucket> BuildHistogram(
            double[] sortedValues, int buckets)
        {
            if (sortedValues.Length == 0) return new List<MonteCarloHistogramBucket>();

            double min = sortedValues[0];
            double max = sortedValues[^1];
            double width = (max - min) / buckets;

            if (width <= 0) return new List<MonteCarloHistogramBucket>();

            return Enumerable.Range(0, buckets).Select(i =>
            {
                double lo = min + i * width;
                double hi = lo + width;
                bool isLast = i == buckets - 1;

                int count = sortedValues.Count(v =>
                    v >= lo && (isLast ? v <= hi : v < hi));

                return new MonteCarloHistogramBucket
                {
                    MinValue = (decimal)lo,
                    MaxValue = (decimal)hi,
                    Count = count,
                    FrequencyPct = Math.Round((double)count / sortedValues.Length * 100, 2)
                };
            }).ToList();
        }
    }
}
