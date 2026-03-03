using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace MarketPortfolioAnalytics.Services
{
    /// <summary>
    /// Service d'accès à l'API Financial Modeling Prep (FMP).
    ///
    /// Endpoints utilisés (tous sur /stable/) :
    ///
    ///   1. GET /stable/profile?symbol=AAPL&apikey=...
    ///      → Profil d'un actif : nom, devise, place de cotation, secteur, ISIN.
    ///      → Utilisé pour valider un ticker et créer un Stock ou un Bond.
    ///
    ///   2. GET /stable/historical-prices?symbol=AAPL&from=2023-01-01&to=2024-01-01&apikey=...
    ///      → Prix historiques journaliers OHLCV.
    ///      → Utilisé pour alimenter la table AssetPrice.
    ///
    ///   3. GET /stable/company-notes?symbol=T&apikey=...
    ///      → Informations sur les obligations (CouponRate, MaturityDate).
    ///      → FMP ne fournit pas ces données dans un format structuré sur le plan gratuit.
    ///      → On les extrait du champ "title" avec des expressions régulières.
    ///      → Peut retourner null si l'information n'est pas disponible.
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
        // GET /stable/profile?symbol={ticker}&apikey={key}
        //
        // Réponse FMP (tableau d'objets) :
        // [
        //   {
        //     "symbol":           "AAPL",
        //     "companyName":      "Apple Inc.",
        //     "currency":         "USD",
        //     "exchangeShortName":"NASDAQ",
        //     "sector":           "Technology",
        //     "isin":             "US0378331005",
        //     ...
        //   }
        // ]
        //
        // Retourne null si le ticker est inconnu ou si FMP retourne une erreur.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Récupère le profil complet d'un actif depuis FMP.
        /// Utilisé lors de la création d'un Stock ou d'un Bond.
        /// </summary>
        public async Task<FmpProfile?> GetProfileAsync(string ticker)
        {
            string symbol = ticker.Trim().ToUpper();
            string url = $"{_opt.BaseUrl}/stable/profile?symbol={symbol}&apikey={_opt.ApiKey}";

            string? json = await GetJsonAsync(url);
            if (json is null) return null;

            using var doc = JsonDocument.Parse(json);

            // FMP retourne un tableau — on prend le premier élément
            if (doc.RootElement.ValueKind != JsonValueKind.Array
                || doc.RootElement.GetArrayLength() == 0)
                return null;

            var item = doc.RootElement[0];

            // Lecture des champs — on tolère l'absence de certains champs optionnels
            string? sym = ReadString(item, "symbol");
            string? name = ReadString(item, "companyName") ?? ReadString(item, "name");
            string? currency = ReadString(item, "currency");
            string? exchange = ReadString(item, "exchangeShortName") ?? ReadString(item, "exchange");
            string? sector = ReadString(item, "sector");
            string? isin = ReadString(item, "isin");

            // Symbol, Name et Currency sont obligatoires pour créer un actif
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

        // ═══════════════════════════════════════════════════════════════════════
        // ENDPOINT 2 — Prix historiques journaliers
        // GET /stable/historical-prices?symbol={ticker}&from={date}&to={date}&apikey={key}
        //
        // Réponse FMP (tableau d'objets) :
        // [
        //   {
        //     "date":   "2024-01-15",
        //     "open":   183.63,
        //     "high":   184.26,
        //     "low":    182.42,
        //     "close":  183.31,
        //     "volume": 49765800
        //   },
        //   ...
        // ]
        //
        // Les dates sont retournées du plus récent au plus ancien.
        // On retourne une liste vide si aucune donnée n'est disponible.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Récupère les prix historiques journaliers d'un actif sur une période.
        /// Utilisé pour alimenter la table AssetPrice lors de la synchronisation.
        /// </summary>
        public async Task<List<FmpHistoricalPrice>> GetHistoricalPricesAsync(
            string ticker, DateTime from, DateTime to)
        {
            string symbol = ticker.Trim().ToUpper();
            string fromStr = from.ToString("yyyy-MM-dd");
            string toStr = to.ToString("yyyy-MM-dd");

            string url = $"{_opt.BaseUrl}/stable/historical-prices"
                       + $"?symbol={symbol}&from={fromStr}&to={toStr}&apikey={_opt.ApiKey}";

            string? json = await GetJsonAsync(url);
            if (json is null) return new List<FmpHistoricalPrice>();

            using var doc = JsonDocument.Parse(json);

            // FMP retourne directement un tableau pour cet endpoint
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new List<FmpHistoricalPrice>();

            var prices = new List<FmpHistoricalPrice>();

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                // "date" est obligatoire — on ignore les lignes sans date valide
                string? dateStr = ReadString(item, "date");
                if (!DateTime.TryParse(dateStr, out var date))
                    continue;

                // "close" est obligatoire — on ignore les lignes sans prix de clôture
                if (!item.TryGetProperty("close", out var closeEl)
                    || closeEl.ValueKind != JsonValueKind.Number)
                    continue;

                decimal close = closeEl.GetDecimal();
                if (close <= 0) continue;

                // Les autres champs (open, high, low, volume) sont optionnels
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

        // ═══════════════════════════════════════════════════════════════════════
        // ENDPOINT 3 — Informations obligation
        // GET /stable/company-notes?symbol={ticker}&apikey={key}
        //
        // FMP ne propose pas d'endpoint dédié aux obligations sur le plan gratuit.
        // On interroge "company-notes" qui liste les titres des notes de l'entreprise.
        // Ces titres contiennent parfois le taux et la date d'échéance sous la forme :
        //   "4.35% Notes due September 15, 2028"
        //
        // On extrait ces informations avec des expressions régulières.
        // Cette approche est fragile et peut retourner null — c'est documenté et accepté.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tente de récupérer le taux de coupon et la date d'échéance d'une obligation.
        /// Retourne null si l'information n'est pas disponible.
        /// </summary>
        public async Task<FmpBondInfo?> GetBondAsync(string ticker)
        {
            string symbol = ticker.Trim().ToUpper();
            string url = $"{_opt.BaseUrl}/stable/company-notes?symbol={symbol}&apikey={_opt.ApiKey}";

            string? json = await GetJsonAsync(url);
            if (json is null) return null;

            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array
                || doc.RootElement.GetArrayLength() == 0)
                return null;

            // On cherche le titre le plus informatif (celui qui contient "%" et une année)
            string? bestTitle = null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                string? title = ReadString(item, "title");
                if (string.IsNullOrWhiteSpace(title)) continue;

                // On préfère un titre qui contient à la fois un taux (%) et une date
                if (title.Contains('%') && Regex.IsMatch(title, @"\b20\d{2}\b"))
                {
                    bestTitle = title;
                    break;
                }

                bestTitle ??= title; // garde le premier trouvé en fallback
            }

            if (bestTitle is null) return null;

            decimal? couponRate = ParseCouponRate(bestTitle);
            DateTime? maturityDate = ParseMaturityDate(bestTitle);

            // Retourne null si on n'a pu extraire aucune information utile
            if (couponRate is null && maturityDate is null)
                return null;

            return new FmpBondInfo(couponRate, maturityDate);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // HELPERS PRIVÉS
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Effectue un appel HTTP GET et retourne le corps de la réponse en string.
        /// Retourne null en cas d'erreur HTTP ou d'exception réseau.
        /// </summary>
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
                // Erreur réseau (timeout, DNS...) → on retourne null proprement
                return null;
            }
        }

        /// <summary>Lit un champ string depuis un JsonElement, retourne null si absent.</summary>
        private static string? ReadString(JsonElement el, string property)
        {
            if (el.TryGetProperty(property, out var prop)
                && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
            return null;
        }

        /// <summary>Lit un champ decimal depuis un JsonElement, retourne null si absent.</summary>
        private static decimal? ReadDecimal(JsonElement el, string property)
        {
            if (el.TryGetProperty(property, out var prop)
                && prop.ValueKind == JsonValueKind.Number)
                return prop.GetDecimal();
            return null;
        }

        /// <summary>Lit un champ long depuis un JsonElement, retourne null si absent.</summary>
        private static long? ReadLong(JsonElement el, string property)
        {
            if (el.TryGetProperty(property, out var prop)
                && prop.ValueKind == JsonValueKind.Number)
                return prop.GetInt64();
            return null;
        }

        /// <summary>
        /// Extrait un taux de coupon d'un titre textuel.
        /// Exemples reconnus : "4.35%", "5%", "0.5%"
        /// </summary>
        private static decimal? ParseCouponRate(string text)
        {
            // Capture un nombre (entier ou décimal) suivi de "%"
            // Exemple : "4.35% Notes due..." → 4.35
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

        /// <summary>
        /// Extrait une date d'échéance d'un titre textuel.
        /// Formats reconnus :
        ///   - ISO : "2028-09-15"
        ///   - Littéral : "September 15, 2028" ou "Sep 15 2028"
        ///   - Année seule : "2028" (fallback → 31 décembre de l'année)
        /// </summary>
        private static DateTime? ParseMaturityDate(string text)
        {
            // Format ISO : 2028-09-15
            var iso = Regex.Match(text, @"\b(20\d{2})-(\d{2})-(\d{2})\b");
            if (iso.Success && DateTime.TryParse(iso.Value, out var d1))
                return d1.Date;

            // Format littéral : "September 15, 2028" ou "Sep 15 2028"
            var literal = Regex.Match(text,
                @"\b(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+\d{1,2},?\s+20\d{2}\b",
                RegexOptions.IgnoreCase);

            if (literal.Success && DateTime.TryParse(
                literal.Value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var d2))
                return d2.Date;

            // Fallback : année seule → 31 décembre
            var yearOnly = Regex.Match(text, @"\b(20\d{2})\b");
            if (yearOnly.Success && int.TryParse(yearOnly.Value, out int year))
                return new DateTime(year, 12, 31);

            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RECORDS — structures de données retournées par FmpService
    // On utilise des records (C# 9+) : immutables, concis, parfaits pour des DTOs
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Profil d'un actif retourné par FMP (/stable/profile).
    /// Contient les métadonnées nécessaires à la création d'un Stock ou d'un Bond.
    /// </summary>
    public record FmpProfile(
        string Symbol,
        string Name,
        string Currency,
        string? Exchange,
        string? Sector,
        string? Isin
    );

    /// <summary>
    /// Prix journalier d'un actif retourné par FMP (/stable/historical-prices).
    /// Correspond exactement aux champs de la table AssetPrice.
    /// </summary>
    public record FmpHistoricalPrice(
        DateTime Date,
        decimal? Open,
        decimal? High,
        decimal? Low,
        decimal Close,
        long? Volume
    );

    /// <summary>
    /// Informations spécifiques à une obligation, extraites de FMP (/stable/company-notes).
    /// Peut être partielle (un seul des deux champs renseigné).
    /// </summary>
    public record FmpBondInfo(
        decimal? CouponRate,
        DateTime? MaturityDate
    );
}
