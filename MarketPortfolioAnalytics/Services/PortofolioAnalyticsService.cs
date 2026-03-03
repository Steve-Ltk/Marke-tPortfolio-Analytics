using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace MarketPortfolioAnalytics.Services
{
    /// <summary>
    /// Analyse financière de base d'un portefeuille.
    /// Calcule la valeur, le P&L, et les métriques de performance sur une période.
    ///
    /// Ce service est aussi utilisé par les 3 autres services (Optimization,
    /// MonteCarlo, Backtest) pour charger les prix et construire les séries temporelles.
    /// C'est pourquoi ses méthodes utilitaires sont publiques.
    /// </summary>
    public class PortfolioAnalyticsService
    {
        private readonly MarketPortfolioAnalyticsContext _context;

        public PortfolioAnalyticsService(MarketPortfolioAnalyticsContext context)
        {
            _context = context;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ANALYSE PRINCIPALE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Analyse complète d'un portefeuille sur une période.
        /// Retourne null si le portefeuille n'existe pas.
        /// Retourne un résultat vide (sans métriques) si le portefeuille n'a pas de positions
        /// ou si aucun prix n'est disponible sur la période.
        /// </summary>
        public async Task<PortfolioAnalyticsResult?> AnalyzeAsync(
            int portfolioId, DateTime from, DateTime to, double riskFreeRate = 0.03)
        {
            // Chargement du portefeuille avec ses positions et actifs
            var portfolio = await _context.Portfolio
                .Include(p => p.ListePositions!)
                    .ThenInclude(pos => pos.Asset)
                .FirstOrDefaultAsync(p => p.Id == portfolioId);

            if (portfolio is null)
                return null;

            var positions = portfolio.ListePositions?.ToList() ?? new List<Position>();

            if (positions.Count == 0)
                return BuildEmptyResult(portfolio, from, to);

            // Chargement des prix historiques pour tous les actifs du portefeuille
            var assetIds = positions.Select(p => p.AssetId).ToList();
            var allPrices = await LoadPricesAsync(assetIds, from, to);

            // Regroupement par actif : Dictionary<assetId, List<AssetPrice> trié par date>
            var pricesByAsset = allPrices
                .GroupBy(ap => ap.AssetId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(ap => ap.Date).ToList());

            // ── Analyse par position ──────────────────────────────────────────
            var positionResults = new List<PositionAnalyticsResult>();
            decimal totalCurrentValue = 0;
            decimal totalCostBasis = 0;

            foreach (var pos in positions)
            {
                if (!pricesByAsset.TryGetValue(pos.AssetId, out var prices)
                    || prices.Count == 0)
                    continue;   // pas de prix disponible pour cet actif

                decimal latestClose = prices[^1].Close;         // dernier prix connu
                decimal currentValue = latestClose * pos.Quantity;
                decimal costBasis = pos.AvgBuyPrice * pos.Quantity;

                totalCurrentValue += currentValue;
                totalCostBasis += costBasis;

                positionResults.Add(new PositionAnalyticsResult
                {
                    AssetId = pos.AssetId,
                    Ticker = pos.Asset?.Ticker ?? string.Empty,
                    AssetName = pos.Asset?.Name ?? string.Empty,
                    Quantity = pos.Quantity,
                    AvgBuyPrice = pos.AvgBuyPrice,
                    CurrentPrice = latestClose,
                    CurrentValue = currentValue,
                    CostBasis = costBasis,
                    PnL = currentValue - costBasis,
                    ReturnPct = costBasis > 0
                        ? (currentValue - costBasis) / costBasis * 100
                        : 0
                });
            }

            // Calcul des poids : % de la valeur totale du portefeuille
            if (totalCurrentValue > 0)
                foreach (var pr in positionResults)
                    pr.WeightPct = (double)(pr.CurrentValue / totalCurrentValue * 100);

            // ── Série temporelle du portefeuille ──────────────────────────────
            var (_, portValues) = BuildPortfolioSeries(positions, pricesByAsset);

            double[] dailyReturns = portValues.Length > 1
                ? FinancialMath.SimpleReturns(portValues)
                : Array.Empty<double>();

            // ── Résultat final ────────────────────────────────────────────────
            return new PortfolioAnalyticsResult
            {
                PortfolioId = portfolioId,
                PortfolioName = portfolio.Name,
                From = from,
                To = to,
                TotalCurrentValue = totalCurrentValue,
                TotalCostBasis = totalCostBasis,
                TotalPnL = totalCurrentValue - totalCostBasis,
                TotalReturnPct = totalCostBasis > 0
                    ? (totalCurrentValue - totalCostBasis) / totalCostBasis * 100
                    : 0,

                // Métriques annualisées — en % pour l'affichage
                AnnualizedReturn = dailyReturns.Length > 1
                    ? Math.Round(FinancialMath.AnnualizedReturn(dailyReturns) * 100, 4)
                    : 0,
                Volatility = dailyReturns.Length > 1
                    ? Math.Round(FinancialMath.AnnualizedVolatility(dailyReturns) * 100, 4)
                    : 0,
                SharpeRatio = dailyReturns.Length > 1
                    ? Math.Round(FinancialMath.SharpeRatio(dailyReturns, riskFreeRate), 4)
                    : 0,
                MaxDrawdown = portValues.Length > 1
                    ? Math.Round(FinancialMath.MaxDrawdown(portValues) * 100, 4)
                    : 0,

                Positions = positionResults
            };
        }

        /// <summary>
        /// Compare plusieurs portefeuilles sur une même période.
        /// Les portefeuilles introuvables sont silencieusement ignorés.
        /// </summary>
        public async Task<PortfolioComparisonResult> CompareAsync(
            List<int> portfolioIds, DateTime from, DateTime to, double riskFreeRate = 0.03)
        {
            var summaries = new List<PortfolioSummary>();

            foreach (var id in portfolioIds)
            {
                var result = await AnalyzeAsync(id, from, to, riskFreeRate);
                if (result is null) continue;

                summaries.Add(new PortfolioSummary
                {
                    PortfolioId = result.PortfolioId,
                    PortfolioName = result.PortfolioName,
                    AnnualizedReturn = result.AnnualizedReturn,
                    Volatility = result.Volatility,
                    SharpeRatio = result.SharpeRatio,
                    MaxDrawdown = result.MaxDrawdown,
                    TotalReturnPct = result.TotalReturnPct
                });
            }

            return new PortfolioComparisonResult
            {
                From = from,
                To = to,
                Portfolios = summaries
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MÉTHODES UTILITAIRES — utilisées par les autres services
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Charge les prix de clôture pour une liste d'actifs sur une période.
        /// Résultat trié par AssetId puis par Date.
        /// </summary>
        public async Task<List<AssetPrice>> LoadPricesAsync(
            List<int> assetIds, DateTime from, DateTime to)
            => await _context.AssetPrice
                .Where(ap => assetIds.Contains(ap.AssetId)
                          && ap.Date >= from.Date
                          && ap.Date <= to.Date)
                .OrderBy(ap => ap.AssetId)
                .ThenBy(ap => ap.Date)
                .ToListAsync();

        /// <summary>
        /// Construit la série temporelle de valeur du portefeuille.
        /// Pour chaque date de trading, calcule : Σ (quantité × prix de clôture).
        ///
        /// Utilise un forward-fill par curseur (O(n)) :
        /// si un actif n'a pas de prix à une date donnée, on utilise le dernier
        /// prix connu pour cet actif (le curseur ne recule jamais).
        ///
        /// Retourne (dates[], values[]) — deux tableaux de même longueur.
        /// </summary>
        public (DateTime[] dates, double[] values) BuildPortfolioSeries(
            List<Position> positions,
            Dictionary<int, List<AssetPrice>> pricesByAsset)
        {
            // Ensemble des dates de trading disponibles (union de tous les actifs)
            var allDates = pricesByAsset.Values
                .SelectMany(prices => prices.Select(p => p.Date.Date))
                .Distinct()
                .OrderBy(d => d)
                .ToArray();

            var values = new double[allDates.Length];

            // Curseur par actif — pointe sur le dernier prix connu
            var cursors = pricesByAsset.ToDictionary(kv => kv.Key, _ => 0);

            for (int di = 0; di < allDates.Length; di++)
            {
                DateTime date = allDates[di];
                double dayValue = 0.0;

                foreach (var pos in positions)
                {
                    if (!pricesByAsset.TryGetValue(pos.AssetId, out var prices))
                        continue;

                    int c = cursors[pos.AssetId];

                    // Avance le curseur tant que le prix suivant est disponible
                    // et que sa date est ≤ la date courante
                    while (c + 1 < prices.Count
                           && prices[c + 1].Date.Date <= date)
                        c++;

                    cursors[pos.AssetId] = c;

                    if (prices[c].Date.Date <= date)
                        dayValue += (double)prices[c].Close * (double)pos.Quantity;
                }

                values[di] = dayValue;
            }

            return (allDates, values);
        }

        /// <summary>
        /// Retourne les rendements journaliers par actif sur une période.
        /// Dictionary : assetId → double[] de rendements simples.
        /// </summary>
        public async Task<Dictionary<int, double[]>> GetDailyReturnsAsync(
            List<int> assetIds, DateTime from, DateTime to)
        {
            var prices = await LoadPricesAsync(assetIds, from, to);

            return prices
                .GroupBy(ap => ap.AssetId)
                .ToDictionary(
                    g => g.Key,
                    g => FinancialMath.SimpleReturns(
                        g.OrderBy(p => p.Date)
                         .Select(p => (double)p.Close)
                         .ToArray()));
        }

        /// <summary>
        /// Retourne les prix de clôture bruts par actif sur une période.
        /// Dictionary : assetId → double[] de prix.
        /// </summary>
        public async Task<Dictionary<int, double[]>> GetClosePricesAsync(
            List<int> assetIds, DateTime from, DateTime to)
        {
            var prices = await LoadPricesAsync(assetIds, from, to);

            return prices
                .GroupBy(ap => ap.AssetId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(p => p.Date)
                          .Select(p => (double)p.Close)
                          .ToArray());
        }

        // ── Helper privé ──────────────────────────────────────────────────────

        private static PortfolioAnalyticsResult BuildEmptyResult(
            Portfolio portfolio, DateTime from, DateTime to)
            => new()
            {
                PortfolioId = portfolio.Id,
                PortfolioName = portfolio.Name,
                From = from,
                To = to
            };
    }
}
