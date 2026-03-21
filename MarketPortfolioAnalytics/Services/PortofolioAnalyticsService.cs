using Microsoft.EntityFrameworkCore;
using MarketPortfolioAnalytics.Data;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace MarketPortfolioAnalytics.Services
{
    // Service central d'analyse financière.
    // Utilisé directement par AnalyticsController ET par MonteCarloService,
    // BacktestService et PortfolioOptimizationService qui ont besoin de ses utilitaires.
    // Toute la logique de conversion de devise est ici.
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

        // Analyse complète d'un portefeuille sur une période donnée
        // Retourne : valeur actuelle, P&L, rendement annualisé, volatilité, Sharpe, drawdown
        // + le détail de chaque position (poids, P&L individuel, prix actuel...)
        // Retourne null si le portefeuille n'existe pas
        public async Task<PortfolioAnalyticsResult?> AnalyzeAsync(
            int portfolioId, DateTime from, DateTime to, double riskFreeRate = 0.03)
        {
            // Charge le portefeuille avec ses positions ET les actifs de chaque position
            // Include + ThenInclude = une seule requête SQL avec jointures
            var portfolio = await _context.Portfolio
                .Include(p => p.ListePositions!)
                    .ThenInclude(pos => pos.Asset)
                .FirstOrDefaultAsync(p => p.Id == portfolioId);

            // Portefeuille introuvable -> null signale l'erreur au controller
            if (portfolio is null)
                return null;

            // Récupère les positions ou liste vide si aucune
            var positions = portfolio.ListePositions?.ToList() ?? new List<Position>();

            // Portefeuille vide → retourne un résultat vide (pas null)
            // On peut analyser un portefeuille vide → tout est à zéro
            if (positions.Count == 0)
                return BuildEmptyResult(portfolio, from, to);

            // Récupère les ids de tous les actifs du portefeuille
            var assetIds = positions.Select(p => p.AssetId).ToList();

            // Charge les prix historiques depuis la base (+ fetch FMP si manquants)
            var allPrices = await LoadPricesAsync(assetIds, from, to);

            // Groupe les prix par actif pour accès rapide : assetId -> liste de prix
            var pricesByAsset = allPrices
                .GroupBy(ap => ap.AssetId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(ap => ap.Date).ToList());

            // Calcule les taux de change vers la devise du portefeuille (ex: USD → EUR)
            // Un seul appel FMP par devise -> cache interne dans la méthode
            var fxRates = await GetFxRatesAsync(positions, portfolio.Currency);

            // Calcul par position
            var positionResults = new List<PositionAnalyticsResult>();
            decimal totalCurrentValue = 0; // valeur totale en devise portefeuille
            decimal totalCostBasis = 0; // coût total d'achat en devise portefeuille

            foreach (var pos in positions)
            {
                // Pas de prix pour cet actif sur la période → on l'ignore
                if (!pricesByAsset.TryGetValue(pos.AssetId, out var prices)
                    || prices.Count == 0)
                    continue;

                // Taux de change pour cet actif (ex: 0.92 si l'actif est en USD et le portfolio en EUR)
                decimal fxRate = fxRates.GetValueOrDefault(pos.AssetId, 1m);

                // Prix le plus récent converti en devise portefeuille
                // prices[^1] = dernier élément du tableau 
                decimal latestClose = prices[^1].Close * fxRate;
                // Prix d'achat moyen aussi converti en devise portefeuille
                decimal avgBuyConverted = pos.AvgBuyPrice * fxRate;
                // Valeur actuelle = prix actuel × quantité (en devise portefeuille)
                decimal currentValue = latestClose * pos.Quantity;
                // Coût total = prix d'achat × quantité (en devise portefeuille)
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
                    // Rendement en % : (valeur actuelle / coût) - 1) × 100
                    ReturnPct = costBasis > 0
                        ? (currentValue - costBasis) / costBasis * 100
                        : 0
                });
            }

            // Calcule le poids de chaque position dans le portefeuille total
            // Ex : AAPL vaut 5000€ sur 10000€ total → poids = 50%
            if (totalCurrentValue > 0)
                foreach (var pr in positionResults)
                    pr.WeightPct = (double)(pr.CurrentValue / totalCurrentValue * 100);

            // Construit la série de valeur journalière du portefeuille entier
            // Avec conversion FX appliquée à chaque prix
            var (_, portValues) = BuildPortfolioSeries(positions, pricesByAsset, fxRates);

            // Calcule les rendements journaliers depuis la série de valeurs
            // Besoin d'au moins 2 points pour avoir 1 rendement
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
                // Rendement total en % depuis l'achat
                TotalReturnPct = totalCostBasis > 0
                    ? (totalCurrentValue - totalCostBasis) / totalCostBasis * 100
                    : 0,
                // Métriques annualisées → besoin d'au moins 2 rendements journaliers
                // Round(x, 4) = 4 décimales → les controllers arrondissent à 2 pour l'affichage
                AnnualizedReturn = dailyReturns.Length > 1
                    ? Math.Round(FinancialMath.AnnualizedReturn(dailyReturns) * 100, 4)
                    : 0,
                Volatility = dailyReturns.Length > 1
                    ? Math.Round(FinancialMath.AnnualizedVolatility(dailyReturns) * 100, 4)
                    : 0,
                SharpeRatio = dailyReturns.Length > 1
                    ? Math.Round(FinancialMath.SharpeRatio(dailyReturns, riskFreeRate), 4)
                    : 0,
                // MaxDrawdown calculé sur les VALEURS (pas les rendements)
                MaxDrawdown = portValues.Length > 1
                    ? Math.Round(FinancialMath.MaxDrawdown(portValues) * 100, 4)
                    : 0,

                Positions = positionResults
            };
        }

        // Compare plusieurs portefeuilles sur la même période
        // Appelle AnalyzeAsync pour chaque portefeuille et agrège les résultats
        public async Task<PortfolioComparisonResult> CompareAsync(
            List<int> portfolioIds, DateTime from, DateTime to, double riskFreeRate = 0.03)
        {
            var summaries = new List<PortfolioSummary>();

            // Analyse chaque portefeuille un par un
            foreach (var id in portfolioIds)
            {
                var result = await AnalyzeAsync(id, from, to, riskFreeRate);
                // Si un portefeuille est introuvable → on l'ignore sans planter
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

        // Charge les prix historiques depuis la base de données
        // Si un actif a moins de 5 jours de données -> fetch automatique depuis FMP
        // C'est ici que le "auto-fetch" se déclenche quand on clique sur "Analyser"
        // "public" car utilisé par MonteCarloService, BacktestService, OptimizationService
        public async Task<List<AssetPrice>> LoadPricesAsync(
              List<int> assetIds, DateTime from, DateTime to)
        {
            // Charge tous les prix de la période depuis la base
            var prices = await _context.AssetPrice
                .Where(ap => assetIds.Contains(ap.AssetId)
                          && ap.Date >= from.Date
                          && ap.Date <= to.Date)
                .OrderBy(ap => ap.AssetId)
                .ThenBy(ap => ap.Date)
                .ToListAsync();

            // Aussi les actifs qui n'ont aucun prix du tout
            var assetIdsWithPrices = prices.Select(p => p.AssetId).Distinct().ToList();
            var assetIdsMissing = assetIds.Except(assetIdsWithPrices).ToList();

            // Trouve les actifs qui n'ont pas assez de données (moins de 5 jours)
            var assetIdsInsuffisant = assetIds
                .Where(id => prices.Count(p => p.AssetId == id) < 5)
                .ToList();

            // Union des deux listes -> tous les actifs à fetcher depuis FMP
            var toFetch = assetIdsMissing
                .Union(assetIdsInsuffisant)
                .Distinct()
                .ToList();

            // Si tous les actifs ont assez de données -> pas besoin d'appeler FMP
            if (toFetch.Any())
            {
                foreach (var assetId in toFetch)
                {
                    var asset = await _context.Asset.FindAsync(assetId);
                    if (asset == null) continue;

                    // Récupère l'actif depuis la base pour avoir son ticker
                    var fmpPrices = await _fmp.GetHistoricalPricesAsync(
                        asset.Ticker, from, to);

                    // FMP n'a rien retourné pour cet actif -> on passe au suivant
                    if (fmpPrices.Count == 0) continue;

                    // Récupère les dates déjà en base pour éviter les doublons
                    var existingDates = (await _context.AssetPrice
                        .Where(ap => ap.AssetId == assetId
                                  && ap.Date >= from.Date
                                  && ap.Date <= to.Date)
                        .Select(ap => ap.Date.Date)
                        .ToListAsync())
                        .ToHashSet(); // HashSet -> vérification O(1) au lieu de O(n)

                    // Filtre : garde seulement les prix qui ne sont pas encore en base
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

                    // Insère en masse -> EF génère un seul INSERT groupé
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

        // Calcule les taux de change entre la devise de chaque actif et la devise du portefeuille
        // Ex : actif en USD, portefeuille en EUR → taux = 0.92 (1 USD = 0.92 EUR)
        // Cache par devise : FMP n'est appelé qu'une seule fois par paire de devises
        // Retourne assetId → taux de change à appliquer sur les prix de cet actif
        public async Task<Dictionary<int, decimal>> GetFxRatesAsync(
            List<Position> positions,
            string portfolioCurrency)
        {
            var result = new Dictionary<int, decimal>();
            // Cache : devise source -> taux (évite d'appeler FMP plusieurs fois pour USD par exemple)
            var rateCache = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            string targetCurrency = portfolioCurrency.Trim().ToUpper();

            foreach (var pos in positions)
            {
                // Devise de l'actif (ex: "USD" pour AAPL)
                string assetCurrency = pos.Asset?.Currency?.Trim().ToUpper() ?? targetCurrency;

                // Vérifie si on a déjà calculé ce taux -> évite un appel FMP redondant
                if (!rateCache.TryGetValue(assetCurrency, out decimal rate))
                {
                    // Même devise -> taux 1 (pas de conversion)
                    rate = string.Equals(assetCurrency, targetCurrency, StringComparison.OrdinalIgnoreCase)
                        ? 1m
                        : await _fmp.GetExchangeRateAsync(assetCurrency, targetCurrency);

                    // Stocke dans le cache pour les autres actifs dans la même devise
                    rateCache[assetCurrency] = rate;
                }

                // Associe le taux à l'assetId
                result[pos.AssetId] = rate;
            }

            return result;
        }

        // Construit la série temporelle de valeur TOTALE du portefeuille jour par jour
        // Utilise un curseur O(n) par actif -> parcourt les prix une seule fois (efficace)
        // Si un actif n'a pas de prix un jour donné -> utilise le dernier prix connu (forward-fill)
        // fxRates optionnel -> si fourni, convertit chaque prix en devise portefeuille
        // Retourne (dates[], values[]) → deux tableaux de même longueur
        // Ressource: Claude IA
        public (DateTime[] dates, double[] values) BuildPortfolioSeries(
            List<Position> positions,
            Dictionary<int, List<AssetPrice>> pricesByAsset,
            Dictionary<int, decimal>? fxRates = null)
        {
            // Collecte toutes les dates disponibles pour tous les actifs
            // Distinct -> une seule fois par date même si plusieurs actifs ont ce jour
            var allDates = pricesByAsset.Values
                .SelectMany(prices => prices.Select(p => p.Date.Date))
                .Distinct()
                .OrderBy(d => d)
                .ToArray();

            var values = new double[allDates.Length];

            // Curseur par actif : position courante dans sa liste de prix
            // Commence à 0 (premier prix) pour chaque actif
            var cursors = pricesByAsset.ToDictionary(kv => kv.Key, _ => 0);

            // Pour chaque date dans l'ordre chronologique
            for (int di = 0; di < allDates.Length; di++)
            {
                DateTime date = allDates[di];
                double dayValue = 0.0; // valeur totale du portefeuille ce jour

                foreach (var pos in positions)
                {
                    // Cet actif n'a pas de prix du tout -> on l'ignore
                    if (!pricesByAsset.TryGetValue(pos.AssetId, out var prices))
                        continue;

                    int c = cursors[pos.AssetId]; // position actuelle du curseur

                    // Avance le curseur tant que le prix suivant est <= la date courante
                    // "forward-fill" : si pas de prix ce jour -> garde le dernier connu
                    while (c + 1 < prices.Count
                           && prices[c + 1].Date.Date <= date)
                        c++;

                    // Sauvegarde la nouvelle position du curseur pour la prochaine date
                    cursors[pos.AssetId] = c;

                    // Prix valide pour ce jour (ou forward-fill du dernier connu)
                    if (prices[c].Date.Date <= date)
                    {
                        // Taux de change → 1.0 si pas de conversion nécessaire
                        double fxRate = fxRates is not null
                            ? (double)fxRates.GetValueOrDefault(pos.AssetId, 1m)
                            : 1.0;

                        // Valeur de cette position = prix × quantité × taux de change
                        dayValue += (double)prices[c].Close * (double)pos.Quantity * fxRate;
                    }
                }

                values[di] = dayValue;
            }

            return (allDates, values);
        }

        // Retourne les rendements journaliers par actif sur une période
        // Utilisé par PortfolioOptimizationService pour construire la matrice de covariance
        public async Task<Dictionary<int, double[]>> GetDailyReturnsAsync(
            List<int> assetIds, DateTime from, DateTime to)
        {
            var prices = await LoadPricesAsync(assetIds, from, to);

            return prices
                .GroupBy(ap => ap.AssetId)
                .ToDictionary(
                    g => g.Key,
                    // SimpleReturns transforme les prix en rendements journaliers
                    g => FinancialMath.SimpleReturns(
                        g.OrderBy(p => p.Date)
                         .Select(p => (double)p.Close)
                         .ToArray()));
        }

        // Retourne les prix de clôture bruts par actif sur une période
        // Utilisé par PortfolioOptimizationService pour calculer les poids actuels
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

        // Construit un résultat vide pour un portefeuille sans positions
        // Tout est à zéro → pas d'erreur, juste rien à afficher
        // "static" → n'utilise pas _context ou _fmp → fonction pure
        private static PortfolioAnalyticsResult BuildEmptyResult(
            Portfolio portfolio, DateTime from, DateTime to)
            => new()
            {
                PortfolioId = portfolio.Id,
                PortfolioName = portfolio.Name,
                From = from,
                To = to
                // Tous les autres champs restent à leur valeur par défaut (0)
            };
    }
}
