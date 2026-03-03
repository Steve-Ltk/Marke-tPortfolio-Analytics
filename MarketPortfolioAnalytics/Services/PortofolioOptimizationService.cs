using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace MarketPortfolioAnalytics.Services
{
    /// <summary>
    /// Optimisation Markowitz (mean-variance optimization).
    ///
    /// Principe :
    ///   On génère NumPortfolios allocations aléatoires sur l'espace des poids,
    ///   on calcule pour chacune le rendement attendu, la volatilité et le Sharpe,
    ///   puis on retourne l'allocation qui maximise la cible choisie
    ///   (MaxSharpe / MinVolatility / MaxReturn).
    ///
    ///   L'ensemble des allocations explorées constitue la frontière efficiente.
    ///
    /// Limite : la méthode est par simulation aléatoire (Monte Carlo sur les poids)
    /// et non par résolution analytique exacte — suffisant pour un projet académique.
    /// </summary>
    public class PortfolioOptimizationService
    {
        private readonly MarketPortfolioAnalyticsContext _context;
        private readonly PortfolioAnalyticsService _analytics;

        public PortfolioOptimizationService(
            MarketPortfolioAnalyticsContext context,
            PortfolioAnalyticsService analytics)
        {
            _context = context;
            _analytics = analytics;
        }

        public async Task<OptimizationResult?> OptimizeAsync(
            int portfolioId, OptimizationRequest req)
        {
            // Chargement du portefeuille avec ses positions et actifs
            var portfolio = await _context.Portfolio
                .Include(p => p.ListePositions!)
                    .ThenInclude(pos => pos.Asset)
                .FirstOrDefaultAsync(p => p.Id == portfolioId);

            if (portfolio is null) return null;

            var positions = portfolio.ListePositions?.ToList() ?? new List<Position>();

            if (positions.Count < 2)
                return null;   // Markowitz nécessite au moins 2 actifs

            var assetIds = positions.Select(p => p.AssetId).ToList();

            // ── Rendements journaliers ────────────────────────────────────────
            var returnsDict = await _analytics.GetDailyReturnsAsync(
                assetIds, req.From, req.To);

            // On exige au moins 30 jours de données par actif
            var validIds = returnsDict
                .Where(kv => kv.Value.Length >= 30)
                .Select(kv => kv.Key)
                .ToList();

            if (validIds.Count < 2)
                return null;

            var validPositions = positions
                .Where(p => validIds.Contains(p.AssetId))
                .ToList();

            int n = validIds.Count;

            // Alignement : tous les actifs sur la même longueur (la plus courte)
            int minLen = returnsDict
                .Where(kv => validIds.Contains(kv.Key))
                .Min(kv => kv.Value.Length);

            double[][] returnsMatrix = validIds
                .Select(id => returnsDict[id].TakeLast(minLen).ToArray())
                .ToArray();

            // Rendement annualisé attendu par actif
            double[] expectedReturns = returnsMatrix
                .Select(r => FinancialMath.AnnualizedReturn(r))
                .ToArray();

            // Matrice de covariance annualisée n × n
            double[,] covMatrix = FinancialMath.CovarianceMatrix(returnsMatrix);

            // ── Simulation de portefeuilles aléatoires ────────────────────────
            // Pour chaque simulation, on génère des poids aléatoires (Dirichlet),
            // on calcule les métriques du portefeuille, et on garde le meilleur.

            var rng = new Random(42);   // graine fixe → résultats reproductibles
            var frontier = new List<(double[] weights, double ret, double vol, double sharpe)>
                (req.NumPortfolios);

            double[] bestWeights = Array.Empty<double>();
            double bestMetric = double.MinValue;

            for (int s = 0; s < req.NumPortfolios; s++)
            {
                double[] w = GenerateRandomWeights(n, rng);
                double ret = FinancialMath.PortfolioReturn(w, expectedReturns);
                double vol = FinancialMath.PortfolioVolatility(w, covMatrix);
                double sharpe = vol > 0 ? (ret - req.RiskFreeRate) / vol : 0.0;

                frontier.Add((w, ret, vol, sharpe));

                // Métrique à maximiser selon la cible choisie
                double metric = req.Target switch
                {
                    OptimizationTarget.MaxSharpe => sharpe,
                    OptimizationTarget.MinVolatility => -vol,   // négatif car on maximise
                    OptimizationTarget.MaxReturn => ret,
                    _ => sharpe
                };

                if (metric > bestMetric)
                {
                    bestMetric = metric;
                    bestWeights = w;
                }
            }

            if (bestWeights.Length == 0)
                return null;

            // ── Poids actuels du portefeuille (pour comparaison) ──────────────
            var closePrices = await _analytics.GetClosePricesAsync(
                validIds, req.From, req.To);

            double[] currentWeights = ComputeCurrentWeights(
                validPositions, validIds, closePrices);

            double currentRet = FinancialMath.PortfolioReturn(currentWeights, expectedReturns);
            double currentVol = FinancialMath.PortfolioVolatility(currentWeights, covMatrix);
            double currentSharpe = currentVol > 0
                ? (currentRet - req.RiskFreeRate) / currentVol
                : 0.0;

            // ── Métriques du portefeuille optimal ─────────────────────────────
            double optRet = FinancialMath.PortfolioReturn(bestWeights, expectedReturns);
            double optVol = FinancialMath.PortfolioVolatility(bestWeights, covMatrix);
            double optSharpe = optVol > 0 ? (optRet - req.RiskFreeRate) / optVol : 0.0;

            // Actifs pour les labels
            var assets = validPositions
                .Select(p => p.Asset)
                .Where(a => a is not null)
                .ToList();

            return new OptimizationResult
            {
                PortfolioId = portfolioId,
                Target = req.Target,
                RiskFreeRate = req.RiskFreeRate,

                OptimalWeights = BuildAllocations(validIds, bestWeights, assets!),
                OptimalReturn = Math.Round(optRet * 100, 4),
                OptimalVolatility = Math.Round(optVol * 100, 4),
                OptimalSharpe = Math.Round(optSharpe, 4),

                CurrentWeights = BuildAllocations(validIds, currentWeights, assets!),
                CurrentReturn = Math.Round(currentRet * 100, 4),
                CurrentVolatility = Math.Round(currentVol * 100, 4),
                CurrentSharpe = Math.Round(currentSharpe, 4),

                // Frontière efficiente : tous les points explorés, triés par volatilité
                EfficientFrontier = frontier
                    .OrderBy(p => p.vol)
                    .Select(p => new EfficientFrontierPoint
                    {
                        ExpectedReturn = Math.Round(p.ret * 100, 4),
                        Volatility = Math.Round(p.vol * 100, 4),
                        SharpeRatio = Math.Round(p.sharpe, 4)
                    })
                    .ToList()
            };
        }

        // ── Helpers privés ────────────────────────────────────────────────────

        /// <summary>
        /// Génère des poids aléatoires dont la somme vaut exactement 1.
        /// Méthode : distribution de Dirichlet via -log(U) normalisé.
        /// Garantit des poids positifs et uniformément répartis sur le simplex.
        /// </summary>
        private static double[] GenerateRandomWeights(int n, Random rng)
        {
            // -log(uniform) suit une distribution exponentielle
            var raw = Enumerable.Range(0, n)
                .Select(_ => -Math.Log(rng.NextDouble()))
                .ToArray();

            double sum = raw.Sum();

            return raw.Select(r => r / sum).ToArray();
        }

        /// <summary>
        /// Calcule les poids actuels du portefeuille basés sur les valeurs de marché.
        /// w_i = (quantité_i × prix_actuel_i) / valeur_totale
        /// </summary>
        private static double[] ComputeCurrentWeights(
            List<Position> positions,
            List<int> validIds,
            Dictionary<int, double[]> closePrices)
        {
            double[] values = validIds.Select(id =>
            {
                var pos = positions.FirstOrDefault(p => p.AssetId == id);
                if (pos is null) return 0.0;

                var prices = closePrices.GetValueOrDefault(id);
                double lastPrice = prices?.Length > 0
                    ? prices[^1]
                    : (double)pos.AvgBuyPrice;

                return lastPrice * (double)pos.Quantity;
            }).ToArray();

            double total = values.Sum();

            if (total <= 0)
                return validIds.Select(_ => 1.0 / validIds.Count).ToArray();

            return values.Select(v => v / total).ToArray();
        }

        /// <summary>Construit la liste des allocations pour la réponse JSON.</summary>
        private static List<AssetAllocation> BuildAllocations(
            List<int> ids,
            double[] weights,
            List<Asset> assets)
            => ids.Select((id, i) =>
            {
                var asset = assets.FirstOrDefault(a => a.Id == id);
                return new AssetAllocation
                {
                    AssetId = id,
                    Ticker = asset?.Ticker ?? id.ToString(),
                    AssetName = asset?.Name ?? string.Empty,
                    WeightPct = Math.Round(weights[i] * 100, 2)
                };
            }).ToList();
    }
}
