using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace MarketPortfolioAnalytics.Services
{
    // Optimisation de portefeuille par simulation de Markowitz
    // Génère NumPortfolios allocations aléatoires et retourne la meilleure
    // selon la cible : MaxSharpe, MinVolatility, ou MaxReturn
    // Nécessite au moins 2 actifs avec 30 jours de données chacun
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
            // Charge le portefeuille avec positions et actifs
            var portfolio = await _context.Portfolio
                .Include(p => p.ListePositions!)
                    .ThenInclude(pos => pos.Asset)
                .FirstOrDefaultAsync(p => p.Id == portfolioId);

            if (portfolio is null) return null;

            var positions = portfolio.ListePositions?.ToList() ?? new List<Position>();

            if (positions.Count < 2)
                return null;   // Markowitz nécessite au moins 2 actifs

            var assetIds = positions.Select(p => p.AssetId).ToList();

            // Récupère les rendements journaliers de chaque actif
            var returnsDict = await _analytics.GetDailyReturnsAsync(
                assetIds, req.From, req.To);

            // Garde uniquement les actifs avec au moins 30 jours de données
            // Moins de 30 jours → pas assez pour estimer la covariance de façon fiable
            var validIds = returnsDict
                .Where(kv => kv.Value.Length >= 30)
                .Select(kv => kv.Key)
                .ToList();

            // Besoin d'au moins 2 actifs valides → sinon pas de frontière efficiente
            if (validIds.Count < 2)
                return null;

            var validPositions = positions
                .Where(p => validIds.Contains(p.AssetId))
                .ToList();

            int n = validIds.Count; // nombre d'actifs valides

            // Alignement : tous les actifs sur la même longueur (la plus courte)
            // Tous les actifs doivent avoir la même longueur pour la matrice de covariance
            // On prend la longueur minimale → tous alignés sur la même période
            int minLen = returnsDict
                .Where(kv => validIds.Contains(kv.Key))
                .Min(kv => kv.Value.Length);

            // Matrice de rendements : returnsMatrix[i] = rendements journaliers de l'actif i
            double[][] returnsMatrix = validIds
                .Select(id => returnsDict[id].TakeLast(minLen).ToArray())
                .ToArray();

            // Rendement annualisé attendu par actif
            double[] expectedReturns = returnsMatrix
                .Select(r => FinancialMath.AnnualizedReturn(r))
                .ToArray();

            // Matrice de covariance annualisée n × n
            double[,] covMatrix = FinancialMath.CovarianceMatrix(returnsMatrix);

            // Simulation de NumPortfolios allocations aléatoires
            // Pour chaque simulation, on génère des poids aléatoires (Dirichlet),
            // on calcule les métriques du portefeuille, et on garde le meilleur.

            var rng = new Random(42);   // graine fixe -> résultats reproductibles
            var frontier = new List<(double[] weights, double ret, double vol, double sharpe)>
                (req.NumPortfolios);

            double[] bestWeights = Array.Empty<double>();
            double bestMetric = double.MinValue; // on cherche le maximum

            for (int s = 0; s < req.NumPortfolios; s++)
            {
                // Génère des poids aléatoires dont la somme = 1 (distribution Dirichlet)
                double[] w = GenerateRandomWeights(n, rng);
                double ret = FinancialMath.PortfolioReturn(w, expectedReturns);
                double vol = FinancialMath.PortfolioVolatility(w, covMatrix);
                double sharpe = vol > 0 ? (ret - req.RiskFreeRate) / vol : 0.0;

                frontier.Add((w, ret, vol, sharpe));

                // Sélectionne la métrique à maximiser selon la cible
                double metric = req.Target switch
                {
                    OptimizationTarget.MaxSharpe => sharpe, // maximise Sharpe
                    OptimizationTarget.MinVolatility => -vol, // minimise vol → maximise -vol
                    OptimizationTarget.MaxReturn => ret, // maximise rendement
                    _ => sharpe
                };

                // Garde les meilleurs poids trouvés jusqu'ici
                if (metric > bestMetric)
                {
                    bestMetric = metric;
                    bestWeights = w;
                }
            }

            if (bestWeights.Length == 0)
                return null;

            // Poids actuels du portefeuille (pour comparaison) 

            // Calcule les poids réels actuels basés sur les valeurs de marché
            var closePrices = await _analytics.GetClosePricesAsync(
                validIds, req.From, req.To);

            double[] currentWeights = ComputeCurrentWeights(
                validPositions, validIds, closePrices);

            // Métriques du portefeuille avec les poids actuels
            double currentRet = FinancialMath.PortfolioReturn(currentWeights, expectedReturns);
            double currentVol = FinancialMath.PortfolioVolatility(currentWeights, covMatrix);
            double currentSharpe = currentVol > 0
                ? (currentRet - req.RiskFreeRate) / currentVol
                : 0.0;

            // Métriques du portefeuille avec les poids optimaux
            double optRet = FinancialMath.PortfolioReturn(bestWeights, expectedReturns);
            double optVol = FinancialMath.PortfolioVolatility(bestWeights, covMatrix);
            double optSharpe = optVol > 0 ? (optRet - req.RiskFreeRate) / optVol : 0.0;

            // Actifs pour les labels dans la réponse JSON
            var assets = validPositions
                .Select(p => p.Asset)
                .Where(a => a is not null)
                .ToList();

            return new OptimizationResult
            {
                PortfolioId = portfolioId,
                Target = req.Target,
                RiskFreeRate = req.RiskFreeRate,

                // Poids optimaux trouvés + leurs métriques
                // × 100 car les valeurs brutes sont en décimal (0.12 = 12%)
                OptimalWeights = BuildAllocations(validIds, bestWeights, assets!),
                OptimalReturn = Math.Round(optRet * 100, 4),
                OptimalVolatility = Math.Round(optVol * 100, 4),
                OptimalSharpe = Math.Round(optSharpe, 4),

                // Poids actuels + leurs métriques (pour montrer l'amélioration possible)
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

        // Génère des poids aléatoires dont la somme = exactement 1
        // Distribution de Dirichlet via -log(Uniforme) normalisé
        // Garantit des poids positifs uniformément répartis sur le simplex
        private static double[] GenerateRandomWeights(int n, Random rng)
        {
            // -log(uniform) suit une distribution exponentielle
            var raw = Enumerable.Range(0, n)
                .Select(_ => -Math.Log(rng.NextDouble()))
                .ToArray();

            double sum = raw.Sum();

            // Normalise pour que la somme = 1
            return raw.Select(r => r / sum).ToArray();
        }

        // Calcule les poids actuels du portefeuille basés sur les valeurs de marché réelles
        // poids_i = (quantité_i × prix_actuel_i) / valeur_totale
        private static double[] ComputeCurrentWeights(
            List<Position> positions,
            List<int> validIds,
            Dictionary<int, double[]> closePrices)
        {
            // Valeur de marché de chaque actif
            double[] values = validIds.Select(id =>
            {
                var pos = positions.FirstOrDefault(p => p.AssetId == id);
                if (pos is null) return 0.0;

                var prices = closePrices.GetValueOrDefault(id);
                // [^1] = dernier prix disponible
                double lastPrice = prices?.Length > 0
                    ? prices[^1]
                    : (double)pos.AvgBuyPrice; // fallback sur le prix d'achat si pas de prix

                return lastPrice * (double)pos.Quantity;
            }).ToArray();

            double total = values.Sum();

            // Si valeur totale nulle → poids égaux (évite division par zéro)
            if (total <= 0)
                return validIds.Select(_ => 1.0 / validIds.Count).ToArray();

            // Chaque poids = valeur de l'actif / valeur totale
            return values.Select(v => v / total).ToArray();
        }

        // Construit la liste des allocations pour la réponse JSON
        // Associe chaque poids à son ticker et nom d'actif
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
