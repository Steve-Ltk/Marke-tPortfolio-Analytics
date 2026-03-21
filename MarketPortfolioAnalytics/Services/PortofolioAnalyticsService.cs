using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace MarketPortfolioAnalytics.Services
{
    /// <summary>
    /// Analyse financière de base d'un portefeuille.
    ///
    /// CONVERSION DE DEVISE :
    ///   Les actifs peuvent être cotés dans une devise différente de celle du portefeuille
    ///   (ex : actif en USD dans un portefeuille EUR).
    ///
    ///   Approche retenue :
    ///     - Le taux de change spot actuel est récupéré via FMP une seule fois par devise.
    ///     - Il est appliqué à TOUTES les valeurs (actuelles et historiques).
    ///     - Conséquence : la série historique est convertie au taux actuel (approximation).
    ///       Cette simplification est acceptable pour un projet académique.
    ///       En production, il faudrait des séries FX historiques.
    ///
    /// Ce service est aussi utilisé par MonteCarloService, BacktestService et
    /// PortfolioOptimizationService — ses méthodes utilitaires sont donc publiques.
    /// </summary>
    public class PortfolioAnalyticsService
    {
        private readonly MarketPortfolioAnalyticsContext _context;
        private readonly FmpService _fmp;

        public PortfolioAnalyticsService(
            MarketPortfolioAnalyticsContext context,
            FmpService fmp)
        {
            _context = context;
            _fmp = fmp;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ANALYSE PRINCIPALE
        // ═══════════════════════════════════════════════════════════════════════

        public async Task<PortfolioAnalyticsResult?> AnalyzeAsync(
            int portfolioId, DateTime from, DateTime to, double riskFreeRate = 0.03)
        {
            var portfolio = await _context.Portfolio
                .Include(p => p.ListePositions!)
                    .ThenInclude(pos => pos.Asset)
                .FirstOrDefaultAsync(p => p.Id == portfolioId);

            if (portfolio is null)
                return null;

            var positions = portfolio.ListePositions?.ToList() ?? new List<Position>();

            if (positions.Count == 0)
                return BuildEmptyResult(portfolio, from, to);

            var assetIds = positions.Select(p => p.AssetId).ToList();
            var allPrices = await LoadPricesAsync(assetIds, from, to);

            var pricesByAsset = allPrices
                .GroupBy(ap => ap.AssetId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(ap => ap.Date).ToList());

            // ── Taux de change vers la devise du portefeuille ─────────────────
            // Ex : actifs en USD → portefeuille en EUR → taux USD→EUR
            var fxRates = await GetFxRatesAsync(positions, portfolio.Currency);

            // ── Analyse par position ──────────────────────────────────────────
            var positionResults = new List<PositionAnalyticsResult>();
            decimal totalCurrentValue = 0;
            decimal totalCostBasis = 0;

            foreach (var pos in positions)
            {
                if (!pricesByAsset.TryGetValue(pos.AssetId, out var prices)
                    || prices.Count == 0)
                    continue;

                decimal fxRate = fxRates.GetValueOrDefault(pos.AssetId, 1m);

                // Prix de clôture le plus récent converti en devise portefeuille
                decimal latestClose = prices[^1].Close * fxRate;
                decimal avgBuyConverted = pos.AvgBuyPrice * fxRate;

                decimal currentValue = latestClose * pos.Quantity;
                decimal costBasis = avgBuyConverted * pos.Quantity;

                totalCurrentValue += currentValue;
                totalCostBasis += costBasis;

                positionResults.Add(new PositionAnalyticsResult
                {
                    AssetId = pos.AssetId,
                    Ticker = pos.Asset?.Ticker ?? string.Empty,
                    AssetName = pos.Asset?.Name ?? string.Empty,
                    Quantity = pos.Quantity,
                    AvgBuyPrice = avgBuyConverted,     // converti en devise portefeuille
                    CurrentPrice = latestClose,         // converti en devise portefeuille
                    CurrentValue = currentValue,
                    CostBasis = costBasis,
                    PnL = currentValue - costBasis,
                    ReturnPct = costBasis > 0
                        ? (currentValue - costBasis) / costBasis * 100
                        : 0
                });
            }

            if (totalCurrentValue > 0)
                foreach (var pr in positionResults)
                    pr.WeightPct = (double)(pr.CurrentValue / totalCurrentValue * 100);

            // ── Série temporelle avec conversion FX ───────────────────────────
            var (_, portValues) = BuildPortfolioSeries(positions, pricesByAsset, fxRates);

            double[] dailyReturns = portValues.Length > 1
                ? FinancialMath.SimpleReturns(portValues)
                : Array.Empty<double>();

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
        /// </summary>
        public async Task<List<AssetPrice>> LoadPricesAsync(
    List<int> assetIds, DateTime from, DateTime to)
        {
            var prices = await _context.AssetPrice
                .Where(ap => assetIds.Contains(ap.AssetId)
                          && ap.Date >= from.Date
                          && ap.Date <= to.Date)
                .OrderBy(ap => ap.AssetId)
                .ThenBy(ap => ap.Date)
                .ToListAsync();

            // ── Si pas assez de prix en base → fetch FMP ──────────────────────
            var assetIdsWithPrices = prices.Select(p => p.AssetId).Distinct().ToList();
            var assetIdsMissing = assetIds.Except(assetIdsWithPrices).ToList();

            // Aussi les actifs qui ont moins de 5 jours de données
            var assetIdsInsuffisant = assetIds
                .Where(id => prices.Count(p => p.AssetId == id) < 5)
                .ToList();

            var toFetch = assetIdsMissing
                .Union(assetIdsInsuffisant)
                .Distinct()
                .ToList();

            if (toFetch.Any())
            {
                foreach (var assetId in toFetch)
                {
                    var asset = await _context.Asset.FindAsync(assetId);
                    if (asset == null) continue;

                    // Fetch FMP
                    var fmpPrices = await _fmp.GetHistoricalPricesAsync(
                        asset.Ticker, from, to);

                    if (fmpPrices.Count == 0) continue;

                    // Dates déjà en base pour cet actif sur cette période
                    var existingDates = (await _context.AssetPrice
                        .Where(ap => ap.AssetId == assetId
                                  && ap.Date >= from.Date
                                  && ap.Date <= to.Date)
                        .Select(ap => ap.Date.Date)
                        .ToListAsync())
                        .ToHashSet();

                    // Insérer uniquement les nouveaux
                    var toInsert = fmpPrices
                        .Where(p => !existingDates.Contains(p.Date.Date))
                        .Select(p => new AssetPrice
                        {
                            AssetId = assetId,
                            Date = p.Date.Date,
                            Open = p.Open,
                            High = p.High,
                            Low = p.Low,
                            Close = p.Close,
                            Volume = p.Volume
                        })
                        .ToList();

                    if (toInsert.Any())
                    {
                        _context.AssetPrice.AddRange(toInsert);
                        await _context.SaveChangesAsync();
                    }
                }

                // Recharger depuis la base avec les nouveaux prix
                prices = await _context.AssetPrice
                    .Where(ap => assetIds.Contains(ap.AssetId)
                              && ap.Date >= from.Date
                              && ap.Date <= to.Date)
                    .OrderBy(ap => ap.AssetId)
                    .ThenBy(ap => ap.Date)
                    .ToListAsync();
            }

            return prices;
        }

        /// <summary>
        /// Calcule les taux de change spot entre la devise de chaque actif
        /// et la devise cible du portefeuille.
        ///
        /// Retourne un dictionnaire assetId → taux (ex : 1 USD = 0.926 EUR).
        ///
        /// Un cache par devise évite d'appeler FMP plusieurs fois pour la même paire.
        /// Fallback : taux 1.0 si FMP ne répond pas (valeurs non converties).
        /// </summary>
        public async Task<Dictionary<int, decimal>> GetFxRatesAsync(
            List<Position> positions,
            string portfolioCurrency)
        {
            var result = new Dictionary<int, decimal>();
            // Cache : devise source → taux vers devise portefeuille
            var rateCache = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            string targetCurrency = portfolioCurrency.Trim().ToUpper();

            foreach (var pos in positions)
            {
                string assetCurrency = pos.Asset?.Currency?.Trim().ToUpper() ?? targetCurrency;

                if (!rateCache.TryGetValue(assetCurrency, out decimal rate))
                {
                    rate = string.Equals(assetCurrency, targetCurrency, StringComparison.OrdinalIgnoreCase)
                        ? 1m
                        : await _fmp.GetExchangeRateAsync(assetCurrency, targetCurrency);

                    rateCache[assetCurrency] = rate;
                }

                result[pos.AssetId] = rate;
            }

            return result;
        }

        /// <summary>
        /// Construit la série temporelle de valeur du portefeuille.
        ///
        /// Utilise un forward-fill par curseur (O(n)) : si un actif n'a pas de prix
        /// à une date donnée, on utilise le dernier prix connu.
        ///
        /// Paramètre optionnel fxRates : si fourni, chaque prix est multiplié par le taux
        /// de change correspondant avant d'être agrégé. Cela convertit la série en
        /// devise portefeuille (ex : EUR).
        ///
        /// Retourne (dates[], values[]) — deux tableaux de même longueur.
        /// </summary>
        public (DateTime[] dates, double[] values) BuildPortfolioSeries(
            List<Position> positions,
            Dictionary<int, List<AssetPrice>> pricesByAsset,
            Dictionary<int, decimal>? fxRates = null)
        {
            var allDates = pricesByAsset.Values
                .SelectMany(prices => prices.Select(p => p.Date.Date))
                .Distinct()
                .OrderBy(d => d)
                .ToArray();

            var values = new double[allDates.Length];
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

                    while (c + 1 < prices.Count
                           && prices[c + 1].Date.Date <= date)
                        c++;

                    cursors[pos.AssetId] = c;

                    if (prices[c].Date.Date <= date)
                    {
                        double fxRate = fxRates is not null
                            ? (double)fxRates.GetValueOrDefault(pos.AssetId, 1m)
                            : 1.0;

                        dayValue += (double)prices[c].Close * (double)pos.Quantity * fxRate;
                    }
                }

                values[di] = dayValue;
            }

            return (allDates, values);
        }

        /// <summary>
        /// Retourne les rendements journaliers par actif sur une période.
        /// Note : les rendements sont dimensionnels (ratios) — la devise n'affecte pas
        /// ce calcul si le taux de change est supposé constant sur la période.
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

        /// <summary>Retourne les prix de clôture bruts par actif sur une période.</summary>
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