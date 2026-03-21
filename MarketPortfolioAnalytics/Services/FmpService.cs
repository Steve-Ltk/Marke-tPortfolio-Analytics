using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MarketPortfolioAnalytics.Services
{
    /// <summary>
    /// Service d'accès à l'API Financial Modeling Prep (FMP).
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
            catch (JsonException) { return null; }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ENDPOINT 2 — Prix historiques journaliers
        // ═══════════════════════════════════════════════════════════════════════

        public async Task<List<FmpHistoricalPrice>> GetHistoricalPricesAsync(
            string ticker, DateTime from, DateTime to)
        {
            string symbol = ticker.Trim().ToUpper();
            string fromStr = from.ToString("yyyy-MM-dd");
            string toStr = to.ToString("yyyy-MM-dd");

            var urls = new[]
            {
                $"{_opt.BaseUrl}/stable/historical-price-eod/light?symbol={symbol}&from={fromStr}&to={toStr}&apikey={_opt.ApiKey}",
                $"{_opt.BaseUrl}/stable/historical-prices?symbol={symbol}&from={fromStr}&to={toStr}&apikey={_opt.ApiKey}",
                $"{_opt.BaseUrl}/api/v3/historical-price-full/{symbol}?from={fromStr}&to={toStr}&apikey={_opt.ApiKey}",
                $"{_opt.BaseUrl}/api/v3/historical-price-full/{symbol}?apikey={_opt.ApiKey}",
            };

            foreach (var url in urls)
            {
                string? json = await GetJsonAsync(url);
                if (json is null) continue;

                var parsed = ParseHistoricalPrices(json);
                var inRange = parsed
                    .Where(p => p.Date >= from.Date && p.Date <= to.Date)
                    .ToList();

                if (inRange.Count > 0)
                    return inRange;
            }

            return new List<FmpHistoricalPrice>();
        }

        private List<FmpHistoricalPrice> ParseHistoricalPrices(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                JsonElement pricesArray;

                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    pricesArray = doc.RootElement;
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("historical", out var hist)
                        && hist.ValueKind == JsonValueKind.Array)
                    {
                        pricesArray = hist;
                    }
                    else if (doc.RootElement.TryGetProperty("historicalStockList", out var stockList)
                             && stockList.ValueKind == JsonValueKind.Array
                             && stockList.GetArrayLength() > 0
                             && stockList[0].TryGetProperty("historical", out var innerHist)
                             && innerHist.ValueKind == JsonValueKind.Array)
                    {
                        pricesArray = innerHist;
                    }
                    else return new List<FmpHistoricalPrice>();
                }
                else return new List<FmpHistoricalPrice>();

                var prices = new List<FmpHistoricalPrice>();

                foreach (var item in pricesArray.EnumerateArray())
                {
                    string? dateStr = ReadString(item, "date");
                    if (!DateTime.TryParse(dateStr, out var date)) continue;

                    decimal close = 0;
                    bool closeFound = false;

                    foreach (var key in new[] { "price", "close", "adjClose", "Close", "AdjClose", "Price" })
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

                    if (!closeFound || close <= 0) continue;

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
                    { bestTitle = title; break; }
                    bestTitle ??= title;
                }

                if (bestTitle is null) return null;

                decimal? couponRate = ParseCouponRate(bestTitle);
                DateTime? maturityDate = ParseMaturityDate(bestTitle);

                return couponRate is null && maturityDate is null
                    ? null
                    : new FmpBondInfo(couponRate, maturityDate);
            }
            catch (JsonException) { return null; }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ENDPOINT 4 — Prix temps réel d'un actif  ✅ DANS LA CLASSE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Retourne le prix actuel d'un actif via FMP.
        /// Appelé par AssetsController.GetPrice().
        /// ✅ Utilise _opt (pas _options) — champ correct de cette classe.
        /// ✅ Pas de _logger — FmpService n'en a pas.
        /// </summary>
        public async Task<decimal?> GetLatestPriceAsync(string ticker)
        {
            string symbol = ticker.Trim().ToUpper();

            // Essai 1 : stable/quote
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
                        foreach (var key in new[] { "price", "previousClose" })
                        {
                            if (first.TryGetProperty(key, out var el)
                                && el.ValueKind == JsonValueKind.Number)
                            {
                                decimal v = el.GetDecimal();
                                if (v > 0) return v;
                            }
                        }
                    }
                }
                catch (JsonException) { }
            }

            return null;
        }

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
                        if (first.TryGetProperty("price", out var p)
                            && p.ValueKind == JsonValueKind.Number)
                            price = p.GetDecimal();
                        if (first.TryGetProperty("changePercentage", out var c)
                            && c.ValueKind == JsonValueKind.Number)
                            change = c.GetDecimal();
                        if (price > 0) return (price, change);
                    }
                }
                catch (JsonException) { }
            }
            return (0m, 0m);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ENDPOINT 5 — Taux de change spot  ✅ DANS LA CLASSE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Retourne le taux de change entre deux devises.
        /// Ex : GetExchangeRateAsync("EUR","USD") → ~1.086
        /// ✅ Utilise _opt (pas _options) — champ correct de cette classe.
        /// ✅ Pas de _logger — FmpService n'en a pas.
        /// </summary>
        public async Task<decimal> GetExchangeRateAsync(string fromCurrency, string toCurrency)
        {
            if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
                return 1m;

            string from = fromCurrency.Trim().ToUpper();
            string to = toCurrency.Trim().ToUpper();

            // Paire directe (ex : EURUSD)
            decimal rate = await FetchForexRateAsync($"{from}{to}");
            if (rate > 0m) return rate;

            // Paire inverse (ex : USDEUR → 1/USDEUR)
            decimal inverse = await FetchForexRateAsync($"{to}{from}");
            if (inverse > 0m) return 1m / inverse;

            return 1m; // Fallback neutre
        }

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
                    foreach (var key in new[] { "price", "previousClose", "ask", "bid" })
                    {
                        if (!item.TryGetProperty(key, out var el)) continue;
                        if (el.ValueKind != JsonValueKind.Number) continue;
                        decimal v = el.GetDecimal();
                        if (v > 0m) return v;
                    }
                }
                catch (JsonException) { }
            }

            return 0m;
        }



        // ═══════════════════════════════════════════════════════════════════════
        // HELPERS PRIVÉS
        // ═══════════════════════════════════════════════════════════════════════

        private async Task<string?> GetJsonAsync(string url)
        {
            try
            {
                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException) { return null; }
        }

        private static string? ReadString(JsonElement el, string property)
        {
            return el.TryGetProperty(property, out var prop)
                && prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;
        }

        private static decimal? ReadDecimal(JsonElement el, string property)
        {
            return el.TryGetProperty(property, out var prop)
                && prop.ValueKind == JsonValueKind.Number
                ? prop.GetDecimal()
                : null;
        }

        private static long? ReadLong(JsonElement el, string property)
        {
            return el.TryGetProperty(property, out var prop)
                && prop.ValueKind == JsonValueKind.Number
                ? prop.GetInt64()
                : null;
        }

        private static decimal? ParseCouponRate(string text)
        {
            var match = Regex.Match(text, @"(?<!\d)(\d{1,2}(\.\d{1,4})?)\s*%", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            return decimal.TryParse(match.Groups[1].Value,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out decimal rate) ? rate : null;
        }

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

    } // ← FIN de la classe FmpService

    // ═══════════════════════════════════════════════════════════════════════════
    // RECORDS — EN DEHORS de la classe, dans le même namespace  ✅
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