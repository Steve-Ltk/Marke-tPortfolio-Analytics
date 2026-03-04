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
    ///   Cette approche capture automatiquement les corrélations entre actifs.
    ///
    /// Modèle GBM :
    ///   V(t+1) = V(t) × exp( (μ - σ²/2) + σ × Z )
    ///   où Z ~ N(0,1) est un choc aléatoire journalier
    ///
    /// DEVISE :
    ///   La série historique est construite en devise portefeuille (via FX rates).
    ///   Tous les résultats (InitialValue, VaR, CVaR, percentiles) sont donc
    ///   exprimés dans la devise du portefeuille (ex : EUR).
    ///
    /// GARANTIE CVaR >= VaR :
    ///   Double filet de sécurité (double + decimal) garantit CVaR95 >= VaR95 >= 0.
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
            DateTime histTo = (req.HistoryTo ?? DateTime.UtcNow.Date).Date;
            DateTime histFrom = (req.HistoryFrom ?? histTo.AddYears(-2)).Date;

            var portfolio = await _context.Portfolio
                .Include(p => p.ListePositions!)
                    .ThenInclude(pos => pos.Asset)
                .FirstOrDefaultAsync(p => p.Id == portfolioId);

            if (portfolio is null) return null;

            var positions = portfolio.ListePositions?.ToList()
                            ?? new List<Position>();

            if (positions.Count == 0) return null;

            var assetIds = positions.Select(p => p.AssetId).ToList();
            var allPrices = await _analytics.LoadPricesAsync(assetIds, histFrom, histTo);

            var pricesByAsset = allPrices
                .GroupBy(ap => ap.AssetId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(ap => ap.Date).ToList());

            // ── Taux de change vers la devise du portefeuille ─────────────────
            var fxRates = await _analytics.GetFxRatesAsync(positions, portfolio.Currency);

            // ── Série historique en devise portefeuille ───────────────────────
            var (_, portValues) = _analytics.BuildPortfolioSeries(
                positions, pricesByAsset, fxRates);

            if (portValues.Length < 20)
                return null;

            // ── Estimation des paramètres GBM ─────────────────────────────────
            double[] portReturns = FinancialMath.SimpleReturns(portValues);
            double muDaily = FinancialMath.Mean(portReturns);
            double sigmaDaily = FinancialMath.StdDev(portReturns);
            double drift = muDaily - 0.5 * sigmaDaily * sigmaDaily;

            double initVal = portValues[^1];   // valeur actuelle en devise portefeuille
            decimal initialValue = (decimal)initVal;

            if (initialValue <= 0) return null;

            int T = req.HorizonDays;
            int N = req.NumSimulations;
            var rng = new Random(42);

            // ── Simulation des N chemins sur T jours ──────────────────────────
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

            double p5Threshold = FinancialMath.Percentile(finalVals, 0.05);
            double p1Threshold = FinancialMath.Percentile(finalVals, 0.01);

            // VaR = perte absolue depuis la valeur initiale (positif = perte)
            double rawVar95 = initVal - p5Threshold;
            double rawVar99 = initVal - p1Threshold;

            // CVaR95 = perte moyenne dans les 5% pires scénarios
            var tail95 = finalVals.Where(v => v <= p5Threshold).ToArray();
            double rawCvar95 = tail95.Length > 0
                ? initVal - tail95.Average()
                : rawVar95;

            // ── GARANTIE NIVEAU 1 — double precision ───────────────────────────
            // Assure : safeCvar95 >= safeVar95 >= 0
            double safeVar95 = Math.Max(0.0, rawVar95);
            double safeVar99 = Math.Max(0.0, rawVar99);
            double safeCvar95 = Math.Max(safeVar95, Math.Max(0.0, rawCvar95));

            // ── GARANTIE NIVEAU 2 — decimal precision ──────────────────────────
            // La conversion double→decimal peut introduire un epsilon d'arrondi.
            // Ce second filet garantit l'invariant CVaR95 >= VaR95 après conversion.
            decimal decVar95 = (decimal)safeVar95;
            decimal decVar99 = (decimal)safeVar99;
            decimal decCvar95 = (decimal)safeCvar95;

            if (decCvar95 < decVar95)
                decCvar95 = decVar95;   // protection absolue post-conversion

            return new MonteCarloResult
            {
                PortfolioId = portfolioId,
                HorizonDays = T,
                NumSimulations = N,
                InitialValue = initialValue,   // en devise portefeuille (ex : EUR)

                Percentile5 = (decimal)p5Threshold,
                Percentile25 = (decimal)FinancialMath.Percentile(finalVals, 0.25),
                Median = (decimal)FinancialMath.Percentile(finalVals, 0.50),
                Percentile75 = (decimal)FinancialMath.Percentile(finalVals, 0.75),
                Percentile95 = (decimal)FinancialMath.Percentile(finalVals, 0.95),

                // camelCase → "vaR95"  / "vaR99"  / "cVaR95"
                // Aucun [JsonPropertyName] sur ces propriétés → Postman reçoit les bonnes clés
                VaR95 = decVar95,
                VaR99 = decVar99,
                CVaR95 = decCvar95,   // invariant CVaR95 >= VaR95 garanti par les deux niveaux

                ProbabilityOfLossPct = Math.Round(
                    (double)finalVals.Count(v => v < initVal) / N * 100, 2),
                ExpectedFinalReturnPct = Math.Round(
                    (finalVals.Average() / initVal - 1.0) * 100, 4),

                TimeSeries = timeSeries,
                FinalValueHistogram = BuildHistogram(finalVals, 20)
            };
        }

        // ── Helper privé ──────────────────────────────────────────────────────

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