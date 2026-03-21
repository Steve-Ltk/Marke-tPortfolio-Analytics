using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MarketPortfolioAnalytics.Services
{
    // _http : HttpClient injecté par AddHttpClient<FmpService>() dans Program.cs.
    // _opt.Value : extrait la config FMP d'appsettings.json (BaseUrl + ApiKey).
    // Tous les appels vers FMP passent par ce service — un seul endroit à modifier
    // si FMP change ses endpoints.
    public class FmpService
    {
        private readonly HttpClient _http;
        private readonly FmpOptions _opt;

        public FmpService(HttpClient http, IOptions<FmpOptions> opt)
        {
            _http = http;
            _opt = opt.Value;
        }

        // Récupère les infos d'un actif sur FMP : nom, devise, exchange, secteur, ISIN
        // Retourne null si FMP ne connaît pas ce ticker ou si la connexion échoue
        public async Task<FmpProfile?> GetProfileAsync(string ticker)
        {
            // Normalise le ticker en majuscules -> "aapl" devient "AAPL"
            string symbol = ticker.Trim().ToUpper();

            // Construit l'URL avec le ticker et la clé API
            string url = $"{_opt.BaseUrl}/stable/profile?symbol={symbol}&apikey={_opt.ApiKey}";

            // Appelle FMP via le helper privé → retourne null si erreur réseau ou HTTP
            string? json = await GetJsonAsync(url);
            if (json is null) return null;

            try
            {
                // Parse le JSON manuellement -> plus flexible que la désérialisation auto
                // Prendre un texte brut et le transformer en structure exploitable par le programme.
                // "using" -> libère la mémoire du document JSON après ce bloc
                using var doc = JsonDocument.Parse(json);

                // FMP retourne un tableau -> si c'est pas un tableau ou c'est vide -> on arrête
                if (doc.RootElement.ValueKind != JsonValueKind.Array
                    || doc.RootElement.GetArrayLength() == 0)
                    return null;

                // Prend le premier (et seul) élément du tableau
                var item = doc.RootElement[0];

                string? sym = ReadString(item, "symbol");
                string? name = ReadString(item, "companyName") ?? ReadString(item, "name");
                string? currency = ReadString(item, "currency");
                string? exchange = ReadString(item, "exchangeShortName") ?? ReadString(item, "exchange");
                string? sector = ReadString(item, "sector");
                string? isin = ReadString(item, "isin");

                // Si les champs obligatoires manquent -> profil inutilisable -> on retourne null
                if (string.IsNullOrWhiteSpace(sym)
                    || string.IsNullOrWhiteSpace(name)
                    || string.IsNullOrWhiteSpace(currency))
                    return null;

                // Retourne un record FmpProfile avec les données nettoyées
                // Trim() → enlève les espaces avant/après
                // ToUpper() → normalise en majuscules
                return new FmpProfile(
                    Symbol: sym.Trim().ToUpper(),
                    Name: name.Trim(),
                    Currency: currency.Trim().ToUpper(),
                    Exchange: string.IsNullOrWhiteSpace(exchange) ? null : exchange.Trim(),
                    Sector: string.IsNullOrWhiteSpace(sector) ? null : sector.Trim(),
                    Isin: string.IsNullOrWhiteSpace(isin) ? null : isin.Trim()
                );
            }
            // Si le JSON est malformé → on retourne null sans planter l'app
            catch (JsonException) { return null; }
        }

        // 4 URLs de fallback : FMP change ses endpoints selon le plan (gratuit/payant).
        // On essaie dans l'ordre, on s'arrête dès qu'on a des données.
        // continue → si une URL échoue, on passe directement à la suivante.
        // La dernière URL sans filtre date récupère tout → on filtre ensuite en C#.
        public async Task<List<FmpHistoricalPrice>> GetHistoricalPricesAsync(
            string ticker, DateTime from, DateTime to)
        {
            string symbol = ticker.Trim().ToUpper();
            // Formate les dates en "yyyy-MM-dd" pour les URLs FMP
            string fromStr = from.ToString("yyyy-MM-dd");
            string toStr = to.ToString("yyyy-MM-dd");

            // 4 URLs de fallback car FMP change ses endpoints selon le plan (gratuit/payant)
            // On essaie du plus récent au plus ancien
            var urls = new[]
            {
                $"{_opt.BaseUrl}/stable/historical-price-eod/light?symbol={symbol}&from={fromStr}&to={toStr}&apikey={_opt.ApiKey}",
                $"{_opt.BaseUrl}/stable/historical-prices?symbol={symbol}&from={fromStr}&to={toStr}&apikey={_opt.ApiKey}",
                $"{_opt.BaseUrl}/api/v3/historical-price-full/{symbol}?from={fromStr}&to={toStr}&apikey={_opt.ApiKey}",
                $"{_opt.BaseUrl}/api/v3/historical-price-full/{symbol}?apikey={_opt.ApiKey}",
            };
            
            // Essaie chaque URL dans l'ordre
            foreach (var url in urls)
            {
                // Essaie chaque URL dans l'ordre
                string? json = await GetJsonAsync(url);
                // "continue" = passe à l'URL suivante sans exécuter le reste
                if (json is null) continue;

                // Parse le JSON dans n'importe lequel des 3 formats FMP possibles
                var parsed = ParseHistoricalPrices(json);

                // Filtre pour garder uniquement les prix dans la période demandée
                var inRange = parsed
                    .Where(p => p.Date >= from.Date && p.Date <= to.Date)
                    .ToList();

                // On a des données → inutile d'essayer les autres URLs
                if (inRange.Count > 0)
                    return inRange;
            }

            // Aucune des 4 URLs n'a donné de résultats → liste vide
            return new List<FmpHistoricalPrice>();
        }

        // Parse le JSON de prix historiques → gère les 3 formats que FMP peut retourner
        private List<FmpHistoricalPrice> ParseHistoricalPrices(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                JsonElement pricesArray;

                // Format 1 : tableau direct → [{ "date": "...", "close": 182.5 }, ...]
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    pricesArray = doc.RootElement;
                }
                
                // Format 2 : objet avec clé "historical"
                // -> { "historical": [{ "date": "...", "close": 182.5 }, ...] }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("historical", out var hist)
                        && hist.ValueKind == JsonValueKind.Array)
                    {
                        pricesArray = hist;
                    }
                    // Format 3 : encore plus imbriqué -> { "historicalStockList": [{ "historical": [...] }] }
                    else if (doc.RootElement.TryGetProperty("historicalStockList", out var stockList)
                             && stockList.ValueKind == JsonValueKind.Array
                             && stockList.GetArrayLength() > 0
                             && stockList[0].TryGetProperty("historical", out var innerHist)
                             && innerHist.ValueKind == JsonValueKind.Array)
                    {
                        pricesArray = innerHist;
                    }
                    // Format inconnu -> on abandonne
                    else return new List<FmpHistoricalPrice>();
                }
                else return new List<FmpHistoricalPrice>();

                var prices = new List<FmpHistoricalPrice>();

                // Parcourt chaque ligne de prix dans le tableau
                foreach (var item in pricesArray.EnumerateArray())
                {
                    // Lit la date -> si absente ou invalide → on saute cette ligne
                    string? dateStr = ReadString(item, "date");
                    if (!DateTime.TryParse(dateStr, out var date)) continue;

                    decimal close = 0;
                    bool closeFound = false;

                    // Cherche le prix de clôture sous plusieurs noms possibles
                    // FMP utilise "price", "close", "adjClose" selon l'endpoint
                    foreach (var key in new[] { "price", "close", "adjClose", "Close", "AdjClose", "Price" })
                    {
                        if (!item.TryGetProperty(key, out var priceEl)) continue;
                        
                        // Cas 1 : le prix est un nombre → on le prend directement
                        if (priceEl.ValueKind == JsonValueKind.Number)
                        {
                            close = priceEl.GetDecimal();
                            closeFound = true;
                            break;
                        }
                        // Cas 2 : le prix est une chaîne "182.50" → on la convertit
                        // System.Globalization.NumberStyles.Number
                        // System.Globalization.CultureInfo.InvariantCulture
                        else if (priceEl.ValueKind == JsonValueKind.String)
                        {
                            if (decimal.TryParse(priceEl.GetString(),
                                System.Globalization.NumberStyles.Number,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var p))
                            {
                                close = p;
                                closeFound = true;
                                break;
                            }
                        }
                    }

                    // Si pas de prix valide → on saute cette ligne
                    if (!closeFound || close <= 0) continue;

                    // Ajoute le prix à la liste avec tous ses champs OHLCV
                    prices.Add(new FmpHistoricalPrice(
                        Date: date.Date,
                        Open: ReadDecimal(item, "open"),
                        High: ReadDecimal(item, "high"),
                        Low: ReadDecimal(item, "low"),
                        Close: close,
                        Volume: ReadLong(item, "volume")
                    ));
                }

                return prices;
            }
            catch (JsonException) { return new List<FmpHistoricalPrice>(); }
        }

        // Récupère le taux de coupon et la date d'échéance d'une obligation via FMP
        // Retourne null si FMP ne trouve rien pour ce ticker
        public async Task<FmpBondInfo?> GetBondAsync(string ticker)
        {
            string symbol = ticker.Trim().ToUpper();
            string url = $"{_opt.BaseUrl}/stable/company-notes?symbol={symbol}&apikey={_opt.ApiKey}";

            string? json = await GetJsonAsync(url);
            if (json is null) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                
                // FMP retourne un tableau d'obligations -> si vide -> rien à extraire
                if (doc.RootElement.ValueKind != JsonValueKind.Array
                    || doc.RootElement.GetArrayLength() == 0)
                    return null;

                // Cherche le titre le plus informatif dans le tableau
                // On préfère un titre avec "%" (coupon) ET une année (maturité)
                string? bestTitle = null;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    string? title = ReadString(item, "title");
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    // Titre idéal : contient "%" et une année → ex: "3.125% Notes due 2028"
                    if (title.Contains('%') && Regex.IsMatch(title, @"\b20\d{2}\b"))
                    { bestTitle = title; break; }

                    // Titre de secours si on ne trouve pas mieux
                    bestTitle ??= title;
                }

                if (bestTitle is null) return null;

                // Extrait le coupon et la maturité depuis le titre texte
                decimal? couponRate = ParseCouponRate(bestTitle);
                DateTime? maturityDate = ParseMaturityDate(bestTitle);

                // Si on n'a ni coupon ni maturité -> inutile de retourner un objet vide
                return couponRate is null && maturityDate is null
                    ? null
                    : new FmpBondInfo(couponRate, maturityDate);
            }
            catch (JsonException) { return null; }
        }

        // Retourne uniquement le prix actuel d'un actif (pas la variation)
        // Utilisé par GetLatestPriceAsync dans AssetsController
        public async Task<decimal?> GetLatestPriceAsync(string ticker)
        {
            string symbol = ticker.Trim().ToUpper();

            var urls = new[]
            {
                $"{_opt.BaseUrl}/stable/quote?symbol={symbol}&apikey={_opt.ApiKey}",
                $"{_opt.BaseUrl}/api/v3/quote/{symbol}?apikey={_opt.ApiKey}",
            };

            foreach (var url in urls)
            {
                string? json = await GetJsonAsync(url);
                if (json is null) continue;

                try
                {
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.ValueKind == JsonValueKind.Array
                        && doc.RootElement.GetArrayLength() > 0)
                    {
                        var first = doc.RootElement[0];
                        
                        // Essaie "price" puis "previousClose" comme fallback
                        foreach (var key in new[] { "price", "previousClose" })
                        {
                            if (first.TryGetProperty(key, out var el)
                                && el.ValueKind == JsonValueKind.Number)
                            {
                                decimal v = el.GetDecimal();
                                // Prix doit être positif -> 0 signifie que FMP n'a pas de données
                                if (v > 0) return v;
                            }
                        }
                    }
                }
                catch (JsonException) { }
            }

            // Aucune URL n'a retourné de prix valide
            return null;
        }

        // Retourne le prix ET la variation journalière en pourcentage
        // Utilisé par GetQuote dans AssetsController
        public async Task<(decimal price, decimal changePercent)> GetQuoteAsync(string ticker)
        {
            string symbol = ticker.Trim().ToUpper();
            var urls = new[]
            {
        $"{_opt.BaseUrl}/stable/quote?symbol={symbol}&apikey={_opt.ApiKey}",
        $"{_opt.BaseUrl}/api/v3/quote/{symbol}?apikey={_opt.ApiKey}",
            };

            foreach (var url in urls)
            {
                string? json = await GetJsonAsync(url);
                if (json is null) continue;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array
                        && doc.RootElement.GetArrayLength() > 0)
                    {
                        var first = doc.RootElement[0];
                        decimal price = 0m, change = 0m;

                        // Lit le prix -> TryGetProperty + ValueKind = double protection
                        // évite de planter si FMP retourne null ou du texte
                        if (first.TryGetProperty("price", out var p)
                            && p.ValueKind == JsonValueKind.Number)
                            price = p.GetDecimal();
                        
                        if (first.TryGetProperty("changePercentage", out var c)
                            && c.ValueKind == JsonValueKind.Number)
                            change = c.GetDecimal();
                        
                        // Prix valide -> retourne le tuple (price, change)
                        if (price > 0) return (price, change);
                    }
                }
                catch (JsonException) { }
            }
            
            // Rien trouvé → (0, 0) signifie "pas de données"
            return (0m, 0m);
        }

        // Retourne le taux de change entre deux devises
        // Ex : GetExchangeRateAsync("EUR", "USD") → 1.086
        public async Task<decimal> GetExchangeRateAsync(string fromCurrency, string toCurrency)
        {
            // Même devise -> taux = 1 (pas de conversion nécessaire)
            if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
                return 1m;

            string from = fromCurrency.Trim().ToUpper();
            string to = toCurrency.Trim().ToUpper();

            // Paire directe (ex : EURUSD)
            decimal rate = await FetchForexRateAsync($"{from}{to}");
            if (rate > 0m) return rate;

            // Paire inverse (ex : USDEUR -> 1/USDEUR)
            decimal inverse = await FetchForexRateAsync($"{to}{from}");
            if (inverse > 0m) return 1m / inverse;

            return 1m; // Fallback neutre
        }

        // Helper privé pour récupérer le taux d'une paire forex (ex: "EURUSD")
        private async Task<decimal> FetchForexRateAsync(string pair)
        {
            var urls = new[]
            {
                $"{_opt.BaseUrl}/api/v3/quote/{pair}?apikey={_opt.ApiKey}",
                $"{_opt.BaseUrl}/stable/quote?symbol={pair}&apikey={_opt.ApiKey}",
            };

            foreach (var url in urls)
            {
                string? json = await GetJsonAsync(url);
                if (json is null) continue;

                try
                {
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.ValueKind != JsonValueKind.Array
                        || doc.RootElement.GetArrayLength() == 0)
                        continue;

                    var item = doc.RootElement[0];

                    // Essaie plusieurs clés → FMP utilise "price", "ask" ou "bid" selon l'endpoint
                    foreach (var key in new[] { "price", "previousClose", "ask", "bid" })
                    {
                        if (!item.TryGetProperty(key, out var el)) continue;
                        if (el.ValueKind != JsonValueKind.Number) continue;
                        decimal v = el.GetDecimal();
                        // Taux doit être positif -> 0 signifie pas de données
                        if (v > 0m) return v;
                    }
                }
                catch (JsonException) { }
            }

            // Paire non trouvée -> 0 signale l'échec à l'appelant
            return 0m;
        }



        // Fait un appel HTTP GET et retourne le JSON en texte
        // Retourne null si : erreur HTTP, panne réseau, timeout
        // Jamais d'exception qui remonte -> les appelants vérifient juste "if (json is null)"
        private async Task<string?> GetJsonAsync(string url)
        {
            try
            {
                var response = await _http.GetAsync(url);
                // Si FMP retourne 400, 403, 500... -> on retourne null proprement
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsStringAsync();
            }
            // Panne réseau -> null sans planter
            catch (HttpRequestException) { return null; }
        }

        // Lit une propriété string dans un élément JSON
        // Retourne null si la propriété n'existe pas ou n'est pas une string
        // "static" = n'utilise pas _http ou _opt -> fonction pure sans état
        private static string? ReadString(JsonElement el, string property)
        {
            return el.TryGetProperty(property, out var prop)
                && prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;
        }

        // Lit une propriété decimal dans un élément JSON
        // Retourne null si absente ou pas un nombre
        private static decimal? ReadDecimal(JsonElement el, string property)
        {
            return el.TryGetProperty(property, out var prop)
                && prop.ValueKind == JsonValueKind.Number
                ? prop.GetDecimal()
                : null;
        }

        // Lit une propriété long (grand entier) dans un élément JSON
       // Utilisé pour le volume (peut dépasser int.MaxValue = 2 milliards)
        private static long? ReadLong(JsonElement el, string property)
        {
            return el.TryGetProperty(property, out var prop)
                && prop.ValueKind == JsonValueKind.Number
                ? prop.GetInt64()
                : null;
        }

        // Extrait le taux de coupon depuis un titre texte
        // Ex : "3.125% Notes due 2028" -> 3.125
        // Regex : cherche un ou deux chiffres suivis d'un "%" optionnellement avec décimales
        private static decimal? ParseCouponRate(string text)
        {
            var match = Regex.Match(text, @"(?<!\d)(\d{1,2}(\.\d{1,4})?)\s*%", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            return decimal.TryParse(match.Groups[1].Value,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal rate) ? rate : null;
        }

        // Extrait la date de maturité depuis un titre texte
        // Essaie 3 formats dans l'ordre : ISO (2028-06-15), littéral (Jun 15, 2028), année seule (2028)
        private static DateTime? ParseMaturityDate(string text)
        {
            var iso = Regex.Match(text, @"\b(20\d{2})-(\d{2})-(\d{2})\b");
            if (iso.Success && DateTime.TryParse(iso.Value, out var d1)) return d1.Date;

            var literal = Regex.Match(text,
                @"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+\d{1,2},?\s+20\d{2}\b",
                RegexOptions.IgnoreCase);
            if (literal.Success && DateTime.TryParse(literal.Value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d2))
                return d2.Date;

            var yearOnly = Regex.Match(text, @"\b(20\d{2})\b");
            if (yearOnly.Success && int.TryParse(yearOnly.Value, out int year))
                return new DateTime(year, 12, 31);

            return null;
        }

    }

   // Modèle des objets que FmpProfile retourne aux autres couches.

    // Profil complet d'un actif récupéré depuis FMP
    public record FmpProfile(
        string Symbol,
        string Name,
        string Currency,
        string? Exchange,
        string? Sector,
        string? Isin
    );

    // Une ligne de prix historique récupérée depuis FMP
    public record FmpHistoricalPrice(
        DateTime Date,
        decimal? Open,
        decimal? High,
        decimal? Low,
        decimal Close,
        long? Volume
    );

    // Métadonnées d'une obligation récupérées depuis FMP
    public record FmpBondInfo(
        decimal? CouponRate,
        DateTime? MaturityDate
    );
}
