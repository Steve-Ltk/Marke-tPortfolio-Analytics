using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace MarketPortfolioAnalytics.Services
{
    public class FmpService
    {
        private readonly HttpClient _http;
        private readonly FmpOptions _opt;

        public FmpService(HttpClient http, IOptions<FmpOptions> opt)
        {
            _http = http;
            _opt = opt.Value;
        }

        public async Task<(string Symbol, string Name, string Currency, string? Exchange, string? Isin, string? Sector)?>
    GetProfileAsync(string ticker)
        {
            var symbol = ticker.Trim().ToUpper();

            var url = $"{_opt.BaseUrl}/stable/profile?symbol={symbol}&apikey={_opt.ApiKey}";
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;

            var first = doc.RootElement[0];

            if (!first.TryGetProperty("symbol", out var symProp)) return null;
            var sym = symProp.GetString();

            string? name = null;
            if (first.TryGetProperty("companyName", out var cn)) name = cn.GetString();
            else if (first.TryGetProperty("name", out var nm)) name = nm.GetString();

            string? currency = null;
            if (first.TryGetProperty("currency", out var cur)) currency = cur.GetString();

            string? exchange = null;
            if (first.TryGetProperty("exchangeShortName", out var exs)) exchange = exs.GetString();
            else if (first.TryGetProperty("exchange", out var ex)) exchange = ex.GetString();

            string? isin = null;
            if (first.TryGetProperty("isin", out var isinProp)) isin = isinProp.GetString();

            string? sector = null;
            if (first.TryGetProperty("sector", out var sectorProp)) sector = sectorProp.GetString();

            if (string.IsNullOrWhiteSpace(sym) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(currency))
                return null;

            return (
                sym.Trim().ToUpper(),
                name.Trim(),
                currency.Trim().ToUpper(),
                string.IsNullOrWhiteSpace(exchange) ? null : exchange.Trim(),
                string.IsNullOrWhiteSpace(isin) ? null : isin.Trim(),
                string.IsNullOrWhiteSpace(sector) ? null : sector.Trim()
            );
        }


        public async Task<(DateTime? MaturityDate, decimal? CouponRate)?> GetBondAsync(string ticker)
        {
            var symbol = ticker.Trim().ToUpper();
            var url = $"{_opt.BaseUrl}/stable/company-notes?symbol={symbol}&apikey={_opt.ApiKey}";
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;

            var first = doc.RootElement[0];

            string? text = null;
            if (first.TryGetProperty("title", out var titleEl))
                text = titleEl.GetString();

            if (string.IsNullOrWhiteSpace(text))
                return null;

            var coupon = TryParseCouponRate(text);
            var maturity = TryParseMaturityDate(text);

            return (maturity, coupon);

        }

        private static decimal? TryParseCouponRate(string s)
        {
            // capture 5% / 5.25% / 0.5%
            var m = Regex.Match(s, @"(?<!\d)(\d{1,2}(\.\d{1,4})?)\s*%", RegexOptions.IgnoreCase);
            if (!m.Success) return null;

            if (decimal.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var pct))
                return pct; // on stocke 5.25 (= 5.25%)
            return null;
        }

        private static DateTime? TryParseMaturityDate(string s)
        {
            // 2031-06-15
            var iso = Regex.Match(s, @"\b(20\d{2})-(\d{2})-(\d{2})\b");
            if (iso.Success && DateTime.TryParse(iso.Value, out var d1)) return d1.Date;

            // Jun 15 2031 / June 15 2031
            var m = Regex.Match(s, @"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{1,2},?\s+20\d{2}\b",
                RegexOptions.IgnoreCase);
            if (m.Success && DateTime.TryParse(m.Value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var d2))
                return d2.Date;

            // juste une année 2030 (fallback)
            var y = Regex.Match(s, @"\b(20\d{2})\b");
            if (y.Success && int.TryParse(y.Value, out var year))
                return new DateTime(year, 12, 31);

            return null;
        }


        // Retourne (symbol, name, exchange) ou null si introuvable / erreur
        public async Task<(string Symbol, string Name, string? Exchange)?> GetQuoteMinimalAsync(string ticker)
        {
            var symbol = ticker.Trim().ToUpper();

            var url = $"{_opt.BaseUrl}/stable/quote?symbol={symbol}&apikey={_opt.ApiKey}";
            var resp = await _http.GetAsync(url);

            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);

            // La réponse est un tableau: [ { ... } ]
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;

            var first = doc.RootElement[0];

            // Sécurise les champs
            if (!first.TryGetProperty("symbol", out var symProp)) return null;
            if (!first.TryGetProperty("name", out var nameProp)) return null;

            var sym = symProp.GetString();
            var name = nameProp.GetString();

            if (string.IsNullOrWhiteSpace(sym) || string.IsNullOrWhiteSpace(name))
                return null;

            string? exchange = null;
            if (first.TryGetProperty("exchange", out var exchProp))
                exchange = exchProp.GetString();

            return (sym.Trim().ToUpper(), name.Trim(), string.IsNullOrWhiteSpace(exchange) ? null : exchange.Trim());
        }
    }
}
