using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace MarketPortfolioAnalytics.Services
{
    /// <summary>
    /// Backtesting historique d'un portefeuille.
    ///
    /// DEVISE :
    ///   Les taux de change spot sont récupérés une fois via FMP et appliqués
    ///   à chaque évaluation journalière. La série de valeurs résultante est
    ///   exprimée dans la devise du portefeuille (ex : EUR).
    ///   Le rééquilibrage tient également compte de la conversion FX.
    /// </summary>
    public class BacktestService
    {
        private readonly MarketPortfolioAnalyticsContext _context;
        private readonly PortfolioAnalyticsService _analytics;

        public BacktestService(
            MarketPortfolioAnalyticsContext context,
            PortfolioAnalyticsService analytics)
        {
            _context = context;
            _analytics = analytics;
        }

        public async Task<BacktestResult?> RunAsync(int portfolioId, BacktestRequest req)
        {
            if (req.From >= req.To) return null;

            var portfolio = await _context.Portfolio
                .Include(p => p.ListePositions!)
                    .ThenInclude(pos => pos.Asset)
                .FirstOrDefaultAsync(p => p.Id == portfolioId);

            if (portfolio is null) return null;

            var positions = portfolio.ListePositions?.ToList() ?? new List<Position>();
            if (positions.Count == 0) return null;

            var assetIds = positions.Select(p => p.AssetId).ToList();
            var allPrices = await _analytics.LoadPricesAsync(assetIds, req.From, req.To);

            var pricesByAsset = allPrices
                .GroupBy(ap => ap.AssetId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(ap => ap.Date).ToList());

            var tradingDates = allPrices
                .Select(ap => ap.Date.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (tradingDates.Count < 5) return null;

            // ── Taux de change vers la devise du portefeuille ─────────────────
            var fxRates = await _analytics.GetFxRatesAsync(positions, portfolio.Currency);

            // Conversion en double pour la boucle de simulation
            var fxDouble = fxRates.ToDictionary(
                kv => kv.Key,
                kv => (double)kv.Value);

            // ── Quantités de travail ──────────────────────────────────────────
            var quantities = positions.ToDictionary(
                p => p.AssetId,
                p => (double)p.Quantity);

            // ── Valeur initiale en devise portefeuille ────────────────────────
            double initTotal = assetIds.Sum(id =>
            {
                if (!pricesByAsset.TryGetValue(id, out var prices)) return 0.0;
                var p = prices.FirstOrDefault(ap => ap.Date.Date <= tradingDates[0].Date);
                return p is not null
                    ? (double)p.Close * quantities.GetValueOrDefault(id)
                      * fxDouble.GetValueOrDefault(id, 1.0)
                    : 0.0;
            });

            // ── Poids initiaux (en valeur de marché convertie) ────────────────
            var initialWeights = assetIds.ToDictionary(
                id => id,
                id =>
                {
                    if (!pricesByAsset.TryGetValue(id, out var prices))
                        return 1.0 / assetIds.Count;
                    var p = prices.FirstOrDefault(ap => ap.Date.Date <= tradingDates[0].Date);
                    if (p is null || initTotal <= 0)
                        return 1.0 / assetIds.Count;
                    double fxRate = fxDouble.GetValueOrDefault(id, 1.0);
                    return (double)p.Close * quantities.GetValueOrDefault(id) * fxRate / initTotal;
                });

            // ── Curseurs O(n) par actif ───────────────────────────────────────
            var cursors = assetIds.ToDictionary(id => id, _ => 0);

            DateTime? lastRebalance = null;
            var portSeries = new List<(DateTime date, double value)>(tradingDates.Count);

            foreach (var date in tradingDates)
            {
                foreach (var id in assetIds)
                {
                    if (!pricesByAsset.TryGetValue(id, out var prices)) continue;
                    int c = cursors[id];
                    while (c + 1 < prices.Count
                           && prices[c + 1].Date.Date <= date.Date)
                        c++;
                    cursors[id] = c;
                }

                // Valeur totale en devise portefeuille
                double totalValue = assetIds.Sum(id =>
                {
                    if (!pricesByAsset.TryGetValue(id, out var prices)) return 0.0;
                    int c = cursors[id];
                    double fxRate = fxDouble.GetValueOrDefault(id, 1.0);
                    return prices[c].Date.Date <= date.Date
                        ? (double)prices[c].Close * quantities.GetValueOrDefault(id) * fxRate
                        : 0.0;
                });

                portSeries.Add((date, totalValue));

                // Rééquilibrage
                if (req.Rebalancing != RebalancingFrequency.BuyAndHold
                    && ShouldRebalance(date, lastRebalance, req.Rebalancing)
                    && totalValue > 0)
                {
                    foreach (var id in assetIds)
                    {
                        if (!pricesByAsset.TryGetValue(id, out var prices)) continue;
                        int c = cursors[id];
                        double priceInAssetCurrency = prices[c].Date.Date <= date.Date
                            ? (double)prices[c].Close
                            : 0.0;
                        double fxRate = fxDouble.GetValueOrDefault(id, 1.0);
                        double priceInPortCurrency = priceInAssetCurrency * fxRate;

                        double targetWeight = initialWeights.GetValueOrDefault(id);

                        if (priceInPortCurrency > 0)
                            quantities[id] = totalValue * targetWeight / priceInPortCurrency;
                    }

                    lastRebalance = date;
                }
            }

            if (portSeries.Count < 2) return null;

            double[] rawValues = portSeries.Select(p => p.value).ToArray();
            double[] normalized = FinancialMath.NormalizeToBase100(rawValues);
            double[] dailyReturns = FinancialMath.SimpleReturns(rawValues);
            double[] drawdowns = FinancialMath.DrawdownSeries(rawValues);

            // ── Benchmark (optionnel) ─────────────────────────────────────────
            double[]? benchmarkReturns = null;
            List<BacktestTimePoint>? benchmarkSeries = null;

            if (!string.IsNullOrWhiteSpace(req.BenchmarkTicker))
            {
                var benchAsset = await _context.Asset
                    .FirstOrDefaultAsync(a =>
                        a.Ticker == req.BenchmarkTicker.Trim().ToUpper());

                if (benchAsset is not null)
                {
                    var benchPrices = await _context.AssetPrice
                        .Where(ap => ap.AssetId == benchAsset.Id
                                  && ap.Date >= req.From.Date
                                  && ap.Date <= req.To.Date)
                        .OrderBy(ap => ap.Date)
                        .ToListAsync();

                    if (benchPrices.Count > 1)
                    {
                        double[] bPrices = benchPrices
                            .Select(p => (double)p.Close)
                            .ToArray();

                        benchmarkReturns = FinancialMath.SimpleReturns(bPrices);
                        double[] bNorm = FinancialMath.NormalizeToBase100(bPrices);

                        benchmarkSeries = benchPrices.Select((p, i) => new BacktestTimePoint
                        {
                            Date = p.Date.Date,
                            Value = Math.Round(bNorm[i], 4),
                            DailyReturnPct = i > 0
                                ? Math.Round(benchmarkReturns[i - 1] * 100, 4)
                                : 0.0
                        }).ToList();
                    }
                }
            }

            // ── Résultat ──────────────────────────────────────────────────────
            return new BacktestResult
            {
                PortfolioId = portfolioId,
                From = req.From,
                To = req.To,
                Rebalancing = req.Rebalancing,

                TotalReturnPct = Math.Round((rawValues[^1] / rawValues[0] - 1.0) * 100, 4),
                AnnualizedReturnPct = Math.Round(FinancialMath.AnnualizedReturn(dailyReturns) * 100, 4),
                VolatilityPct = Math.Round(FinancialMath.AnnualizedVolatility(dailyReturns) * 100, 4),
                SharpeRatio = Math.Round(FinancialMath.SharpeRatio(dailyReturns, req.RiskFreeRate), 4),
                SortinoRatio = Math.Round(FinancialMath.SortinoRatio(dailyReturns, req.RiskFreeRate), 4),
                MaxDrawdownPct = Math.Round(FinancialMath.MaxDrawdown(rawValues) * 100, 4),
                CalmarRatio = Math.Round(FinancialMath.CalmarRatio(dailyReturns, rawValues), 4),

                Beta = benchmarkReturns is not null
                    ? Math.Round(FinancialMath.Beta(dailyReturns, benchmarkReturns), 4)
                    : 1.0,
                Alpha = benchmarkReturns is not null
                    ? Math.Round(FinancialMath.Alpha(dailyReturns, benchmarkReturns, req.RiskFreeRate) * 100, 4)
                    : 0.0,

                BenchmarkTicker = req.BenchmarkTicker,
                BenchmarkReturnPct = benchmarkReturns is not null
                    ? Math.Round(FinancialMath.AnnualizedReturn(benchmarkReturns) * 100, 4)
                    : null,
                BenchmarkVolatilityPct = benchmarkReturns is not null
                    ? Math.Round(FinancialMath.AnnualizedVolatility(benchmarkReturns) * 100, 4)
                    : null,

                PortfolioTimeSeries = portSeries.Select((p, i) => new BacktestTimePoint
                {
                    Date = p.date,
                    Value = Math.Round(normalized[i], 4),
                    DailyReturnPct = i > 0
                        ? Math.Round(dailyReturns[i - 1] * 100, 4)
                        : 0.0
                }).ToList(),

                BenchmarkTimeSeries = benchmarkSeries,

                DrawdownSeries = portSeries.Select((p, i) => new DrawdownPoint
                {
                    Date = p.date,
                    DrawdownPct = Math.Round(drawdowns[i] * 100, 4)
                }).ToList(),

                MonthlyReturns = portSeries
                    .GroupBy(p => new { p.date.Year, p.date.Month })
                    .Select(g =>
                    {
                        var ordered = g.OrderBy(v => v.date).ToList();
                        return new MonthlyReturn
                        {
                            Year = g.Key.Year,
                            Month = g.Key.Month,
                            ReturnPct = ordered.Count > 1
                                ? Math.Round(
                                    (ordered[^1].value / ordered[0].value - 1.0) * 100, 4)
                                : 0.0
                        };
                    })
                    .OrderBy(m => m.Year).ThenBy(m => m.Month)
                    .ToList()
            };
        }

        // ── Helper privé ──────────────────────────────────────────────────────

        private static bool ShouldRebalance(
            DateTime date,
            DateTime? lastRebalance,
            RebalancingFrequency freq)
        {
            if (lastRebalance is null)
                return true;

            return freq switch
            {
                RebalancingFrequency.Monthly =>
                    date.Month != lastRebalance.Value.Month
                    || date.Year != lastRebalance.Value.Year,

                RebalancingFrequency.Quarterly =>
                    (date.Month - 1) / 3 != (lastRebalance.Value.Month - 1) / 3
                    || date.Year != lastRebalance.Value.Year,

                RebalancingFrequency.Annually =>
                    date.Year != lastRebalance.Value.Year,

                _ => false
            };
        }
    }
}