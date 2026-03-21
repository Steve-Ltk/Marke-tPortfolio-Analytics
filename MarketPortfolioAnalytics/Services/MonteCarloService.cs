using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace MarketPortfolioAnalytics.Services
{
    // Simule des milliers de trajectoires futures du portefeuille via GBM
    // (Geometric Brownian Motion = mouvement brownien géométrique)
    // Répond à : "dans 1 an, quelle est la valeur probable de mon portefeuille ?"
    // Utilise PortfolioAnalyticsService pour charger les prix et construire la série historiqueeCarloService
    // Claude IA ( ressource ) 
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

        // Lance la simulation Monte Carlo pour un portefeuille
        // Retourne null si le portefeuille n'existe pas ou pas assez d'historique
        public async Task<MonteCarloResult?> SimulateAsync(
            int portfolioId, MonteCarloRequest req)
        {
            // Période historique pour estimer μ et σ
            // Si non précisée → 2 ans en arrière depuis aujourd'hui
            DateTime histTo = (req.HistoryTo ?? DateTime.UtcNow.Date).Date;
            DateTime histFrom = (req.HistoryFrom ?? histTo.AddYears(-2)).Date;

            // Charge le portefeuille avec ses positions et actifs
            var portfolio = await _context.Portfolio
                .Include(p => p.ListePositions!)
                    .ThenInclude(pos => pos.Asset)
                .FirstOrDefaultAsync(p => p.Id == portfolioId);

            if (portfolio is null) return null;

            var positions = portfolio.ListePositions?.ToList()
                            ?? new List<Position>();

            if (positions.Count == 0) return null

            var assetIds = positions.Select(p => p.AssetId).ToList();
            // Charge les prix historiques (auto-fetch FMP si manquants)
            var allPrices = await _analytics.LoadPricesAsync(assetIds, histFrom, histTo);

            // Groupe par actif pour BuildPortfolioSeries
            var pricesByAsset = allPrices
                .GroupBy(ap => ap.AssetId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(ap => ap.Date).ToList());

            // Taux de change vers la devise du portefeuille
            var fxRates = await _analytics.GetFxRatesAsync(positions, portfolio.Currency);

            // Construit la série de valeur journalière du portefeuille en devise portefeuille
            var (_, portValues) = _analytics.BuildPortfolioSeries(
                positions, pricesByAsset, fxRates);

            // Besoin d'au moins 20 jours pour estimer μ et σ de façon fiable
            if (portValues.Length < 20)
                return null;

            // Estimation des paramètres GBM

            // Rendements journaliers depuis la série historique
            double[] portReturns = FinancialMath.SimpleReturns(portValues);

            // μ = rendement moyen journalier (drift)
            double muDaily = FinancialMath.Mean(portReturns);
            // σ = volatilité journalière
            double sigmaDaily = FinancialMath.StdDev(portReturns);
            // Drift ajusté : μ - σ²/2
            // La correction σ²/2 vient des mathématiques du GBM (correction d'Itô)
            // Sans elle, la simulation surestimerait systématiquement les gains
            double drift = muDaily - 0.5 * sigmaDaily * sigmaDaily;

            // Valeur actuelle du portefeuille = dernier point de la série historique
            // [^1] = dernier élément du tableau 
            double initVal = portValues[^1];   // valeur actuelle en devise portefeuille
            decimal initialValue = (decimal)initVal;

            // Valeur initiale nulle → impossible de simuler
            if (initialValue <= 0) return null;

            int T = req.HorizonDays; // nombre de jours à simuler
            int N = req.NumSimulations; // nombre de trajectoires à générer

            // Graine fixe (42) -> résultats reproductibles à chaque appel
            var rng = new Random(42);

            // Simulation des N chemins sur T jours 

            // allPaths[sim][t] = valeur du portefeuille au jour t pour la simulation sim
            var allPaths = new double[N][];

            for (int sim = 0; sim < N; sim++)
            {
                allPaths[sim] = new double[T + 1];
                allPaths[sim][0] = initVal; // jour 0 = valeur actuelle réelle
                double val = initVal;

                for (int t = 1; t <= T; t++)
                {
                    // Z = choc aléatoire N(0,1) via Box-Muller
                    double z = FinancialMath.NormalRandom(rng);

                    // Formule GBM : V(t+1) = V(t) × exp(drift + σ × Z)
                    // exp() garantit que la valeur ne devient jamais négative
                    val *= Math.Exp(drift + sigmaDaily * z);
                    allPaths[sim][t] = val;
                }
            }

            // Série temporelle des percentiles (max 100 points)

            // On ne garde pas tous les jours -> max 100 points pour alléger le JSON
            int step = Math.Max(1, T / 100);
            var timeSeries = new List<MonteCarloTimePoint>();

            for (int t = 0; t <= T; t += step)
            {
                // Pour ce jour t, collecte les valeurs de toutes les simulations
                var dayVals = allPaths
                    .Select(path => path[t])
                    .OrderBy(v => v) // trie pour calculer les percentiles
                    .ToArray();

                timeSeries.Add(new MonteCarloTimePoint
                {
                    Day = t,
                    // P5 = scénario pessimiste (5% pires cas)
                    P5 = (decimal)FinancialMath.Percentile(dayVals, 0.05),
                    P25 = (decimal)FinancialMath.Percentile(dayVals, 0.25),
                    // Median = scénario central (50% des simulations sont en dessous)
                    Median = (decimal)FinancialMath.Percentile(dayVals, 0.50),
                    P75 = (decimal)FinancialMath.Percentile(dayVals, 0.75),
                    // Median = scénario central (50% des simulations sont en dessous)
                    P95 = (decimal)FinancialMath.Percentile(dayVals, 0.95)
                });
            }

            // Métriques sur les valeurs finales
            // Collecte et trie les valeurs finales de toutes les simulations
            double[] finalVals = allPaths
                .Select(p => p[T])
                .OrderBy(v => v)
                .ToArray();

            // Seuils pour VaR et CVaR
            double p5Threshold = FinancialMath.Percentile(finalVals, 0.05);
            double p1Threshold = FinancialMath.Percentile(finalVals, 0.01);

            // VaR = perte absolue depuis la valeur initiale
            // Ex : initVal=10000, p5=9000 → VaR95=1000 (on risque de perdre 1000€)
            double rawVar95 = initVal - p5Threshold;
            double rawVar99 = initVal - p1Threshold;

            // CVaR95 = // CVaR = perte MOYENNE dans les 5% pires scénarios (plus pessimiste que VaR)
            var tail95 = finalVals.Where(v => v <= p5Threshold).ToArray();
            double rawCvar95 = tail95.Length > 0
                ? initVal - tail95.Average()
                : rawVar95;

            // Double garantie CVaR >= VaR >= 0
            
            // Niveau 1 : protection en double precision
            double safeVar95 = Math.Max(0.0, rawVar95);
            double safeVar99 = Math.Max(0.0, rawVar99);
            double safeCvar95 = Math.Max(safeVar95, Math.Max(0.0, rawCvar95));

            // Niveau 2 : protection après conversion double → decimal (arrondis flottants)
            decimal decVar95 = (decimal)safeVar95;
            decimal decVar99 = (decimal)safeVar99;
            decimal decCvar95 = (decimal)safeCvar95;

            // Garantie absolue : CVaR95 >= VaR95 après conversion
            if (decCvar95 < decVar95)
                decCvar95 = decVar95;   // protection absolue post-conversion

            return new MonteCarloResult
            {
                PortfolioId = portfolioId,
                HorizonDays = T,
                NumSimulations = N,
                // Valeur actuelle réelle du portefeuille (point de départ de la simulation)
                InitialValue = initialValue,   

                // Percentiles des valeurs finales après T jours
                Percentile5 = (decimal)p5Threshold,
                Percentile25 = (decimal)FinancialMath.Percentile(finalVals, 0.25),
                Median = (decimal)FinancialMath.Percentile(finalVals, 0.50),
                Percentile75 = (decimal)FinancialMath.Percentile(finalVals, 0.75),
                Percentile95 = (decimal)FinancialMath.Percentile(finalVals, 0.95),

                VaR95 = decVar95, // perte absolue seuil 95%
                VaR99 = decVar99, // perte absolue seuil 99% (plus conservateur)
                CVaR95 = decCvar95, // perte MOYENNE au-delà du VaR95

                // % de simulations où la valeur finale < valeur initiale = % de perte
                ProbabilityOfLossPct = Math.Round(
                    (double)finalVals.Count(v => v < initVal) / N * 100, 2),

                // Rendement moyen attendu sur toutes les simulations
                ExpectedFinalReturnPct = Math.Round(
                    (finalVals.Average() / initVal - 1.0) * 100, 4),

                TimeSeries = timeSeries,
                FinalValueHistogram = BuildHistogram(finalVals, 20)
            };
        }

        // Construit un histogramme des valeurs finales en N tranches égales
        //  Permet de visualiser la distribution des résultats dans le frontend
        private static List<MonteCarloHistogramBucket> BuildHistogram(
            double[] sortedValues, int buckets)
        {
            if (sortedValues.Length == 0) return new List<MonteCarloHistogramBucket>();

            double min = sortedValues[0]; // pire résultat
            double max = sortedValues[^1]; // meilleur résultat
            double width = (max - min) / buckets; // largeur de chaque tranche

            // Largeur nulle -> tous les résultats identiques -> pas d'histogramme utile
            if (width <= 0) return new List<MonteCarloHistogramBucket>();

            return Enumerable.Range(0, buckets).Select(i =>
            {
                double lo = min + i * width; // borne basse de la tranche
                double hi = lo + width; // borne haute de la tranche
                bool isLast = i == buckets - 1; // dernière tranche -> inclut le max

                // Compte les valeurs dans cette tranche
                int count = sortedValues.Count(v =>
                    v >= lo && (isLast ? v <= hi : v < hi));

                return new MonteCarloHistogramBucket
                {
                    MinValue = (decimal)lo,
                    MaxValue = (decimal)hi,
                    Count = count,
                    // Fréquence en % → combien de simulations tombent dans cette tranche
                    FrequencyPct = Math.Round((double)count / sortedValues.Length * 100, 2)
                };
            }).ToList();
        }
    }
}
