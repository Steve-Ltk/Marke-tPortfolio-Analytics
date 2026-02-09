using System.Text.Json;
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
