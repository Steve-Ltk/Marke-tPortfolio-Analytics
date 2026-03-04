using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace MarketPortfolioAnalytics.Services
{
    /// <summary>
    /// Service d'accès à l'API Financial Modeling Prep (FMP).
    ///
    /// Endpoints utilisés :
    ///
    ///   1. GET /stable/profile?symbol=AAPL&apikey=...
    ///      → Profil d'un actif : nom, devise, place de cotation, secteur, ISIN.
    ///
    ///   2. GET /api/v3/historical-price-full/{symbol}?from=...&to=...&apikey=...
    ///      → Prix historiques journaliers OHLCV.
    ///      → Stratégie 3-URLs avec filtrage local de date pour compatibilité plan gratuit.
    ///
    ///   3. GET /stable/company-notes?symbol=T&apikey=...
    ///      → Informations sur les obligations (CouponRate, MaturityDate).
    /// </summary>
    public class FmpService
    {
        private readonly HttpClient _http;
        private readonly FmpOptions _opt;

        public FmpService(HttpClient http, IOptions<FmpOptions> opt)
        {
            _http = http;
            _opt = opt.Value;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ENDPOINT 1 — Profil d'un actif
        // ═══════════════════════════════════════════════════════════════════════

        public async Task<FmpProfile?> GetProfileAsync(string ticker)
        {
            string symbol = ticker.Trim().ToUpper();
            string url = $"{_opt.BaseUrl}/stable/profile?symbol={symbol}&apikey={_opt.ApiKey}";

            string? json = await GetJsonAsync(url);
            if (json is null) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Array
                    || doc.RootElement.GetArrayLength() == 0)
                    return null;

                var item = doc.RootElement[0];

                string? sym = ReadString(item, "symbol");
                string? name = ReadString(item, "companyName") ?? ReadString(item, "name");
                string? currency = ReadString(item, "currency");
                string? exchange = ReadString(item, "exchangeShortName") ?? ReadString(item, "exchange");
                string? sector = ReadString(item, "sector");
                string? isin = ReadString(item, "isin");

                if (string.IsNullOrWhiteSpace(sym)
                    || string.IsNullOrWhiteSpace(name)
                    || string.IsNullOrWhiteSpace(currency))
                    return null;

                return new FmpProfile(
                    Symbol: sym.Trim().ToUpper(),
                    Name: name.Trim(),
                    Currency: currency.Trim().ToUpper(),
                    Exchange: string.IsNullOrWhiteSpace(exchange) ? null : exchange.Trim(),
                    Sector: string.IsNullOrWhiteSpace(sector) ? null : sector.Trim(),
                    Isin: string.IsNullOrWhiteSpace(isin) ? null : isin.Trim()
                );
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ENDPOINT 2 — Prix historiques journaliers
        //
        // STRATÉGIE 3-URLs pour compatibilité maximum avec le plan gratuit FMP :
        //
        //   URL 1 : v3 avec filtrage serveur (from/to) — optimal
        //           /api/v3/historical-price-full/{symbol}?from=...&to=...&apikey=...
        //
        //   URL 2 : v3 SANS filtrage de date — fallback plan gratuit
        //           /api/v3/historical-price-full/{symbol}?apikey=...
        //           → Le plan gratuit peut ignorer/rejeter les paramètres from/to.
        //             Dans ce cas on récupère tout et on filtre localement.
        //
        //   URL 3 : endpoint stable — dernier recours
        //           /stable/historical-prices?symbol=...&from=...&to=...&apikey=...
        //
        // FORMATS DE RÉPONSE FMP GÉRÉS :
        //
        //   Format A — tableau direct (stable, plan premium) :
        //   [{date, open, high, low, close, volume}, ...]
        //
        //   Format B — objet avec "historical" (v3, standard) :
        //   {"symbol": "AAPL", "historical": [{...}]}
        //
        //   Format C — objet avec "historicalStockList" (v3, multi-symboles) :
        //   {"historicalStockList": [{"symbol": "AAPL", "historical": [{...}]}]}
        //
        // FILTRAGE DE DATE :
        //   Toujours appliqué LOCALEMENT après parsing pour garantir la cohérence
        //   même si le serveur n'a pas filtré (URL 2).
        // ═══════════════════════════════════════════════════════════════════════

        public async Task<List<FmpHistoricalPrice>> GetHistoricalPricesAsync(
            string ticker, DateTime from, DateTime to)
        {
            string symbol = ticker.Trim().ToUpper();
            string fromStr = from.ToString("yyyy-MM-dd");
            string toStr = to.ToString("yyyy-MM-dd");

            // STRATÉGIE 4 URLs — essayées dans l'ordre, arrêt à la première qui retourne des données.
            //
            // URL 1 : endpoint stable/eod/light — CONFIRMÉ fonctionnel sur le plan gratuit.
            //         Format : [{symbol, date, price, volume}]  ← champ "price" et non "close" !
            //
            // URL 2 : endpoint stable/historical-prices — ancien endpoint stable
            //         Format : [{symbol, date, price, volume}]  (même format que URL 1)
            //
            // URL 3 : v3 avec filtrage serveur — endpoint standard FMP
            //         Format : {"symbol":"AAPL","historical":[{date, open, high, low, close, ...}]}
            //
            // URL 4 : v3 sans date — le plan gratuit peut ignorer from/to ; on filtre localement
            //         Format : identique à URL 3
            var urls = new[]
            {
                // URL 1 — endpoint EOD light (plan gratuit confirmé, champ "price")
                $"{_opt.BaseUrl}/stable/historical-price-eod/light" +
                    $"?symbol={symbol}&from={fromStr}&to={toStr}&apikey={_opt.ApiKey}",

                // URL 2 — endpoint stable classique (même format que URL 1)
                $"{_opt.BaseUrl}/stable/historical-prices" +
                    $"?symbol={symbol}&from={fromStr}&to={toStr}&apikey={_opt.ApiKey}",

                // URL 3 — v3 avec dates (champ "close" dans un objet "historical")
                $"{_opt.BaseUrl}/api/v3/historical-price-full/{symbol}" +
                    $"?from={fromStr}&to={toStr}&apikey={_opt.ApiKey}",

                // URL 4 — v3 sans dates (fallback si le plan gratuit ignore from/to)
                $"{_opt.BaseUrl}/api/v3/historical-price-full/{symbol}" +
                    $"?apikey={_opt.ApiKey}",
            };

            foreach (var url in urls)
            {
                string? json = await GetJsonAsync(url);
                if (json is null) continue;

                var parsed = ParseHistoricalPrices(json);

                // Filtrage local par plage de dates — indispensable pour l'URL 4 (sans dates)
                var inRange = parsed
                    .Where(p => p.Date >= from.Date && p.Date <= to.Date)
                    .ToList();

                if (inRange.Count > 0)
                    return inRange;
            }

            return new List<FmpHistoricalPrice>();
        }

        /// <summary>
        /// Parse un corps JSON FMP et retourne les prix historiques.
        ///
        /// FORMATS GÉRÉS :
        ///
        ///   Format A — tableau direct avec champ "price" (endpoint /stable/historical-price-eod/light) :
        ///   [{"symbol":"AAPL", "date":"2023-12-29", "price":192.53, "volume":42672148}, ...]
        ///
        ///   Format B — tableau direct avec champ "close" (autres endpoints stable) :
        ///   [{"date":"2024-01-15", "open":183.63, "high":184.26, "low":182.42, "close":183.31}, ...]
        ///
        ///   Format C — objet avec "historical" (v3 standard) :
        ///   {"symbol":"AAPL", "historical":[{date, open, high, low, close, ...}]}
        ///
        ///   Format D — objet avec "historicalStockList" (v3 multi-symboles) :
        ///   {"historicalStockList":[{"symbol":"AAPL","historical":[...]}]}
        ///
        /// Retourne une liste vide en cas d'erreur de parsing.
        /// </summary>
        private List<FmpHistoricalPrice> ParseHistoricalPrices(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                JsonElement pricesArray;

                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    // Format A ou B — tableau direct
                    pricesArray = doc.RootElement;
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("historical", out var hist)
                        && hist.ValueKind == JsonValueKind.Array)
                    {
                        // Format C — {"symbol":"AAPL","historical":[...]}
                        pricesArray = hist;
                    }
                    else if (doc.RootElement.TryGetProperty("historicalStockList", out var stockList)
                             && stockList.ValueKind == JsonValueKind.Array
                             && stockList.GetArrayLength() > 0
                             && stockList[0].TryGetProperty("historical", out var innerHist)
                             && innerHist.ValueKind == JsonValueKind.Array)
                    {
                        // Format D — {"historicalStockList":[{"symbol":"AAPL","historical":[...]}]}
                        pricesArray = innerHist;
                    }
                    else
                    {
                        // Réponse d'erreur FMP : {"Error Message":"Limit Reach."} ou similaire
                        return new List<FmpHistoricalPrice>();
                    }
                }
                else
                {
                    return new List<FmpHistoricalPrice>();
                }

                var prices = new List<FmpHistoricalPrice>();

                foreach (var item in pricesArray.EnumerateArray())
                {
                    // "date" obligatoire
                    string? dateStr = ReadString(item, "date");
                    if (!DateTime.TryParse(dateStr, out var date))
                        continue;

                    // Prix de clôture : essaie "price" EN PREMIER (format EOD light),
                    // puis "close", "adjClose" et variantes (formats v3 et stable classique).
                    // FMP peut retourner ces valeurs comme nombre ou comme chaîne.
                    decimal close = 0;
                    bool closeFound = false;

                    string[] priceKeys = { "price", "close", "adjClose", "Close", "AdjClose", "Price" };
                    foreach (var key in priceKeys)
                    {
                        if (!item.TryGetProperty(key, out var priceEl)) continue;

                        if (priceEl.ValueKind == JsonValueKind.Number)
                        {
                            close = priceEl.GetDecimal();
                            closeFound = true;
                            break;
                        }
                        else if (priceEl.ValueKind == JsonValueKind.String)
                        {
                            string? s = priceEl.GetString();
                            if (decimal.TryParse(s,
                                System.Globalization.NumberStyles.Number,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var parsed))
                            {
                                close = parsed;
                                closeFound = true;
                                break;
                            }
                        }
                    }

                    if (!closeFound || close <= 0) continue;

                    // Champs OHLC optionnels (absents du format EOD light)
                    decimal? open = ReadDecimal(item, "open");
                    decimal? high = ReadDecimal(item, "high");
                    decimal? low = ReadDecimal(item, "low");
                    long? volume = ReadLong(item, "volume");

                    prices.Add(new FmpHistoricalPrice(
                        Date: date.Date,
                        Open: open,
                        High: high,
                        Low: low,
                        Close: close,
                        Volume: volume
                    ));
                }

                return prices;
            }
            catch (JsonException)
            {
                return new List<FmpHistoricalPrice>();
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ENDPOINT 3 — Informations obligation
        // ═══════════════════════════════════════════════════════════════════════

        public async Task<FmpBondInfo?> GetBondAsync(string ticker)
        {
            string symbol = ticker.Trim().ToUpper();
            string url = $"{_opt.BaseUrl}/stable/company-notes?symbol={symbol}&apikey={_opt.ApiKey}";

            string? json = await GetJsonAsync(url);
            if (json is null) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Array
                    || doc.RootElement.GetArrayLength() == 0)
                    return null;

                string? bestTitle = null;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    string? title = ReadString(item, "title");
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    if (title.Contains('%') && Regex.IsMatch(title, @"\b20\d{2}\b"))
                    {
                        bestTitle = title;
                        break;
                    }
                    bestTitle ??= title;
                }

                if (bestTitle is null) return null;

                decimal? couponRate = ParseCouponRate(bestTitle);
                DateTime? maturityDate = ParseMaturityDate(bestTitle);

                if (couponRate is null && maturityDate is null)
                    return null;

                return new FmpBondInfo(couponRate, maturityDate);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // HELPERS PRIVÉS
        // ═══════════════════════════════════════════════════════════════════════

        private async Task<string?> GetJsonAsync(string url)
        {
            try
            {
                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return null;
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        private static string? ReadString(JsonElement el, string property)
        {
            if (el.TryGetProperty(property, out var prop)
                && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
            return null;
        }

        private static decimal? ReadDecimal(JsonElement el, string property)
        {
            if (el.TryGetProperty(property, out var prop)
                && prop.ValueKind == JsonValueKind.Number)
                return prop.GetDecimal();
            return null;
        }

        private static long? ReadLong(JsonElement el, string property)
        {
            if (el.TryGetProperty(property, out var prop)
                && prop.ValueKind == JsonValueKind.Number)
                return prop.GetInt64();
            return null;
        }

        private static decimal? ParseCouponRate(string text)
        {
            var match = Regex.Match(text,
                @"(?<!\d)(\d{1,2}(\.\d{1,4})?)\s*%",
                RegexOptions.IgnoreCase);

            if (!match.Success) return null;

            if (decimal.TryParse(
                match.Groups[1].Value,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal rate))
                return rate;

            return null;
        }

        private static DateTime? ParseMaturityDate(string text)
        {
            var iso = Regex.Match(text, @"\b(20\d{2})-(\d{2})-(\d{2})\b");
            if (iso.Success && DateTime.TryParse(iso.Value, out var d1))
                return d1.Date;

            var literal = Regex.Match(text,
                @"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+\d{1,2},?\s+20\d{2}\b",
                RegexOptions.IgnoreCase);

            if (literal.Success && DateTime.TryParse(
                literal.Value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var d2))
                return d2.Date;

            var yearOnly = Regex.Match(text, @"\b(20\d{2})\b");
            if (yearOnly.Success && int.TryParse(yearOnly.Value, out int year))
                return new DateTime(year, 12, 31);

            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RECORDS
    // ═══════════════════════════════════════════════════════════════════════════

    public record FmpProfile(
        string Symbol,
        string Name,
        string Currency,
        string? Exchange,
        string? Sector,
        string? Isin
    );

    public record FmpHistoricalPrice(
        DateTime Date,
        decimal? Open,
        decimal? High,
        decimal? Low,
        decimal Close,
        long? Volume
    );

    public record FmpBondInfo(
        decimal? CouponRate,
        DateTime? MaturityDate
    );
}