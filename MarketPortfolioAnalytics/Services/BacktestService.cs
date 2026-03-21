using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace MarketPortfolioAnalytics.Services
{
    // Rejoue l'historique réel du portefeuille sur une période passée
    // Répond à : "si j'avais eu ce portefeuille il y a 2 ans, qu'est-ce qui se serait passé ?"
    // Compare avec un benchmark (ex: SPY = S&P 500) pour mesurer la surperformance
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
            // Dates invalides → impossible de backtester
            if (req.From >= req.To) return null;

            // Charge le portefeuille avec ses positions et actifs
            var portfolio = await _context.Portfolio
                .Include(p => p.ListePositions!)
                    .ThenInclude(pos => pos.Asset)
                .FirstOrDefaultAsync(p => p.Id == portfolioId);

            if (portfolio is null) return null;

            var positions = portfolio.ListePositions?.ToList() ?? new List<Position>();
            if (positions.Count == 0) return null;

            var assetIds = positions.Select(p => p.AssetId).ToList();
            // Charge les prix historiques sur la période du backtest
            var allPrices = await _analytics.LoadPricesAsync(assetIds, req.From, req.To);

            // Groupe par actif pour les curseurs
            var pricesByAsset = allPrices
                .GroupBy(ap => ap.AssetId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(ap => ap.Date).ToList());

            // Collecte toutes les dates disponibles sur la période -> jours de bourse
            var tradingDates = allPrices
                .Select(ap => ap.Date.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            // Moins de 5 jours → backtest non significatif
            if (tradingDates.Count < 5) return null;

            // Taux de change vers la devise du portefeuille
            var fxRates = await _analytics.GetFxRatesAsync(positions, portfolio.Currency);

            // Conversion en double pour la boucle de simulation
            var fxDouble = fxRates.ToDictionary(
                kv => kv.Key,
                kv => (double)kv.Value);

            // Quantités initiales de chaque actif
            var quantities = positions.ToDictionary(
                p => p.AssetId,
                p => (double)p.Quantity);

            //  Valeur initiale du portefeuille au premier jour 
            // Calcule la valeur totale au premier jour de bourse
            double initTotal = assetIds.Sum(id =>
            {
                if (!pricesByAsset.TryGetValue(id, out var prices)) return 0.0;
                // Cherche le prix disponible au premier jour (ou avant)
                var p = prices.FirstOrDefault(ap => ap.Date.Date <= tradingDates[0].Date);
                return p is not null
                    ? (double)p.Close * quantities.GetValueOrDefault(id)
                      * fxDouble.GetValueOrDefault(id, 1.0)
                    : 0.0;
            });

            // Poids initiaux pour le rééquilibrage
            // Poids de chaque actif au départ (valeur actif / valeur totale)
            var initialWeights = assetIds.ToDictionary(
                id => id,
                id =>
                {
                    if (!pricesByAsset.TryGetValue(id, out var prices))
                        return 1.0 / assetIds.Count; // poids égal si pas de prix
                    var p = prices.FirstOrDefault(ap => ap.Date.Date <= tradingDates[0].Date);
                    if (p is null || initTotal <= 0)
                        return 1.0 / assetIds.Count;
                    double fxRate = fxDouble.GetValueOrDefault(id, 1.0);
                    // Poids = valeur de cette position / valeur totale
                    return (double)p.Close * quantities.GetValueOrDefault(id) * fxRate / initTotal;
                });

            //  Curseurs O(n) par actif 

            // Un curseur par actif → position courante dans sa liste de prix
            // O(n) = chaque prix est lu une seule fois (pas de recherche à chaque date)
            var cursors = assetIds.ToDictionary(id => id, _ => 0);

            DateTime? lastRebalance = null;
            var portSeries = new List<(DateTime date, double value)>(tradingDates.Count);

             // Boucle principale : calcule la valeur chaque jour
            foreach (var date in tradingDates)
            {
                // Avance les curseurs jusqu'à la date courante
                foreach (var id in assetIds)
                {
                    if (!pricesByAsset.TryGetValue(id, out var prices)) continue;
                    int c = cursors[id];
                    // Forward-fill : avance tant que le prix suivant est <= date
                    while (c + 1 < prices.Count
                           && prices[c + 1].Date.Date <= date.Date)
                        c++;
                    cursors[id] = c;
                }

                // Calcule la valeur totale du portefeuille ce jour
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
                // BuyAndHold = jamais de rééquilibrage
                // Monthly/Quarterly/Annually = on remet les poids à leur valeur initiale
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

                        // Recalcule la quantité pour retrouver le poids cible
                        // quantité = (valeur_totale × poids_cible) / prix_actuel
                        if (priceInPortCurrency > 0)
                            quantities[id] = totalValue * targetWeight / priceInPortCurrency;
                    }

                    lastRebalance = date;
                }
            }

            if (portSeries.Count < 2) return null;
            // Calculs finaux

            double[] rawValues = portSeries.Select(p => p.value).ToArray();
            // Normalise en base 100 pour le graphique (premier point = 100)
            double[] normalized = FinancialMath.NormalizeToBase100(rawValues);
            double[] dailyReturns = FinancialMath.SimpleReturns(rawValues);
            double[] drawdowns = FinancialMath.DrawdownSeries(rawValues);

            // Benchmark  
            double[]? benchmarkReturns = null;
            List<BacktestTimePoint>? benchmarkSeries = null;

            if (!string.IsNullOrWhiteSpace(req.BenchmarkTicker))
            {
                // Cherche le benchmark en base (doit être importé au préalable)
                var benchAsset = await _context.Asset
                    .FirstOrDefaultAsync(a =>
                        a.Ticker == req.BenchmarkTicker.Trim().ToUpper());

                if (benchAsset is not null)
                {
                    // Charge les prix du benchmark sur la même période
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

                        // Série normalisée du benchmark pour le graphique
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

            // Construction du résultat final
            return new BacktestResult
            {
                PortfolioId = portfolioId,
                From = req.From,
                To = req.To,
                Rebalancing = req.Rebalancing,

                // Rendement total sur toute la période
                TotalReturnPct = Math.Round((rawValues[^1] / rawValues[0] - 1.0) * 100, 4),
                // Métriques annualisées
                AnnualizedReturnPct = Math.Round(FinancialMath.AnnualizedReturn(dailyReturns) * 100, 4),
                VolatilityPct = Math.Round(FinancialMath.AnnualizedVolatility(dailyReturns) * 100, 4),
                SharpeRatio = Math.Round(FinancialMath.SharpeRatio(dailyReturns, req.RiskFreeRate), 4),
                SortinoRatio = Math.Round(FinancialMath.SortinoRatio(dailyReturns, req.RiskFreeRate), 4),
                MaxDrawdownPct = Math.Round(FinancialMath.MaxDrawdown(rawValues) * 100, 4),
                CalmarRatio = Math.Round(FinancialMath.CalmarRatio(dailyReturns, rawValues), 4),

                // Beta et Alpha vs benchmark (1.0 et 0.0 si pas de benchmark)
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

                // Série temporelle normalisée base 100 pour le graphique
                PortfolioTimeSeries = portSeries.Select((p, i) => new BacktestTimePoint
                {
                    Date = p.date,
                    Value = Math.Round(normalized[i], 4),
                    DailyReturnPct = i > 0
                        ? Math.Round(dailyReturns[i - 1] * 100, 4)
                        : 0.0
                }).ToList(),

                BenchmarkTimeSeries = benchmarkSeries,

                // Série de drawdown jour par jour pour le graphique
                DrawdownSeries = portSeries.Select((p, i) => new DrawdownPoint
                {
                    Date = p.date,
                    DrawdownPct = Math.Round(drawdowns[i] * 100, 4)
                }).ToList(),

                // Rendements mensuels pour la heatmap
                MonthlyReturns = portSeries
                    .GroupBy(p => new { p.date.Year, p.date.Month })
                    .Select(g =>
                    {
                        var ordered = g.OrderBy(v => v.date).ToList();
                        return new MonthlyReturn
                        {
                            Year = g.Key.Year,
                            Month = g.Key.Month,
                            // Rendement du mois = (dernier jour / premier jour) - 1
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

        // Décide si on doit rééquilibrer à cette date selon la fréquence choisie
        // Retourne true la première fois (lastRebalance == null)
        private static bool ShouldRebalance(
            DateTime date,
            DateTime? lastRebalance,
            RebalancingFrequency freq)
        {
            // Première fois → on rééquilibre toujours
            if (lastRebalance is null)
                return true;

            return freq switch
            {
                // Mensuel : le mois ou l'année a changé depuis le dernier rééquilibrage
                RebalancingFrequency.Monthly =>
                    date.Month != lastRebalance.Value.Month
                    || date.Year != lastRebalance.Value.Year,

                // Trimestriel : le trimestre ((mois-1)/3) a changé
                RebalancingFrequency.Quarterly =>
                    (date.Month - 1) / 3 != (lastRebalance.Value.Month - 1) / 3
                    || date.Year != lastRebalance.Value.Year,

                // Annuel : l'année a changé
                RebalancingFrequency.Annually =>
                    date.Year != lastRebalance.Value.Year,

                // BuyAndHold → jamais (géré avant d'appeler cette méthode)
                _ => false
            };
        }
    }
}
