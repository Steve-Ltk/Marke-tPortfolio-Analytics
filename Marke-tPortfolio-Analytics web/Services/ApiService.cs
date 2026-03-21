using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace Marke_tPortfolio_Analytics_web.Services
{
    public class ApiService : IApiService
    {
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<ApiService> _logger;

        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ApiService(IHttpClientFactory factory, ILogger<ApiService> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        // ── Helpers HTTP ──────────────────────────────────────────────────────

        private HttpClient Client() => _factory.CreateClient("ApiClient");

        private static StringContent Serialize(object body)
            => new(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");

        private async Task<T?> GetAsync<T>(string url) where T : class
        {
            try
            {
                var r = await Client().GetAsync(url);
                if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                r.EnsureSuccessStatusCode();
                return await r.Content.ReadFromJsonAsync<T>(_json);
            }
            catch (Exception ex) { _logger.LogError(ex, "GET {Url}", url); return null; }
        }

        private async Task<T?> PostAsync<T>(string url, object body) where T : class
        {
            try
            {
                var r = await Client().PostAsync(url, Serialize(body));
                if (!r.IsSuccessStatusCode)
                { _logger.LogWarning("POST {Url} → {S}", url, r.StatusCode); return null; }
                if (r.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
                return await r.Content.ReadFromJsonAsync<T>(_json);
            }
            catch (Exception ex) { _logger.LogError(ex, "POST {Url}", url); return null; }
        }

        private async Task<bool> PutAsync(string url, object body)
        {
            try
            {
                var r = await Client().PutAsync(url, Serialize(body));
                return r.IsSuccessStatusCode;
            }
            catch (Exception ex) { _logger.LogError(ex, "PUT {Url}", url); return false; }
        }

        private async Task<bool> PatchStringAsync(string url, string value)
        {
            try
            {
                var content = new StringContent(
                    JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
                var req = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
                var r = await Client().SendAsync(req);
                return r.IsSuccessStatusCode;
            }
            catch (Exception ex) { _logger.LogError(ex, "PATCH {Url}", url); return false; }
        }

        private async Task<bool> DeleteAsync(string url)
        {
            try { return (await Client().DeleteAsync(url)).IsSuccessStatusCode; }
            catch (Exception ex) { _logger.LogError(ex, "DELETE {Url}", url); return false; }
        }

        // ══════════════════════════════════════════════════════════════════════
        // AUTH & UTILISATEURS
        // ══════════════════════════════════════════════════════════════════════

        public Task<AppUser?> LoginAsync(string email, string password)
            => PostAsync<AppUser>("api/AppUsers/login", new { email, password });

        public Task<AppUser?> RegisterAsync(string fullName, string email, string password)
            => PostAsync<AppUser>("api/AppUsers",
                new { fullName, email, password, role = "User", isActive = true });

        public Task<AppUser?> GetUserByIdAsync(int id)
            => GetAsync<AppUser>($"api/AppUsers/{id}");

        public Task<bool> UpdateUserAsync(int id, string fullName, string email)
            => PutAsync($"api/AppUsers/{id}", new { id, fullName, email });

        public async Task<bool> ChangePasswordAsync(int id, string currentPassword, string newPassword)
        {
            try
            {
                var body = new { currentPassword, newPassword };
                var content = new StringContent(
                    JsonSerializer.Serialize(body, _json),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var req = new HttpRequestMessage(HttpMethod.Patch,
                    $"api/AppUsers/{id}/password")
                {
                    Content = content
                };

                var response = await Client().SendAsync(req);

                // 401 = mot de passe actuel incorrect (à distinguer d'une erreur serveur)
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return false;

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChangePassword user {Id}", id);
                return false;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // PORTEFEUILLES
        // ══════════════════════════════════════════════════════════════════════

        public async Task<List<Portfolio>> GetPortfoliosByUserAsync(int userId)
            => await GetAsync<List<Portfolio>>($"api/Portfolios?userId={userId}") ?? new();

        public Task<Portfolio?> GetPortfolioByIdAsync(int id)
            => GetAsync<Portfolio>($"api/Portfolios/{id}");

        public Task<Portfolio?> GetPortfolioDetailsAsync(int id)
            => GetAsync<Portfolio>($"api/Portfolios/{id}/details");

        /// <summary>
        /// POST /api/Portfolios — envoie name, currency, userId.
        /// Portfolio.Description n'existe PAS dans le modèle backend.
        /// </summary>
        public Task<Portfolio?> CreatePortfolioAsync(string name, string currency, int userId)
            => PostAsync<Portfolio>("api/Portfolios", new { name, currency, userId });

        /// <summary>
        /// PUT /api/Portfolios/{id} — met à jour name et currency uniquement.
        /// UserId et CreatedAt sont immuables côté backend.
        /// </summary>
        public Task<bool> UpdatePortfolioAsync(int id, string name, string currency)
            => PutAsync($"api/Portfolios/{id}", new { id, name, currency });

        public Task<bool> DeletePortfolioAsync(int id)
            => DeleteAsync($"api/Portfolios/{id}");

        // ══════════════════════════════════════════════════════════════════════
        // POSITIONS — clé composite (PortfolioId, AssetId)
        // POST   /api/positions
        // GET    /api/Portfolios/{id}/positions
        // GET    /api/Positions/{portfolioId}/{assetId}
        // PUT    /api/Positions/{portfolioId}/{assetId}
        // DELETE /api/Positions/{portfolioId}/{assetId}
        // ══════════════════════════════════════════════════════════════════════

        public Task<List<Position>?> GetPositionsByPortfolioAsync(int portfolioId)
            => GetAsync<List<Position>>($"api/Portfolios/{portfolioId}/positions");

        public Task<Position?> GetPositionByKeyAsync(int portfolioId, int assetId)
            => GetAsync<Position>($"api/Positions/{portfolioId}/{assetId}");

        public Task<Position?> CreatePositionAsync(
            int portfolioId, int assetId, decimal quantity,
            decimal avgBuyPrice, DateTime buyDate)
            => PostAsync<Position>("api/Positions", new
            {
                portfolioId,
                assetId,
                quantity,
                avgBuyPrice,
                buyDate = buyDate.ToString("yyyy-MM-dd")
            });

        public Task<bool> UpdatePositionAsync(
            int portfolioId, int assetId, decimal quantity,
            decimal avgBuyPrice, DateTime buyDate)
            => PutAsync($"api/Positions/{portfolioId}/{assetId}", new
            {
                portfolioId,
                assetId,
                quantity,
                avgBuyPrice,
                buyDate = buyDate.ToString("yyyy-MM-dd")
            });

        public Task<bool> DeletePositionAsync(int portfolioId, int assetId)
            => DeleteAsync($"api/Positions/{portfolioId}/{assetId}");

        // ══════════════════════════════════════════════════════════════════════
        // ACTIFS
        // ══════════════════════════════════════════════════════════════════════

        public async Task<List<Asset>> GetAllAssetsAsync()
            => await GetAsync<List<Asset>>("api/Assets") ?? new();

        public Task<Asset?> GetAssetByIdAsync(int id)
            => GetAsync<Asset>($"api/Assets/{id}");

        public Task<Asset?> GetAssetByTickerAsync(string ticker)
            => GetAsync<Asset>($"api/Assets/by-ticker/{ticker.ToUpper()}");

        public Task<Asset?> ImportStockFromFmpAsync(string ticker)
            => PostAsync<Asset>("api/Assets/stocks/from-fmp",
                new { ticker = ticker.ToUpper() });

        public Task<Asset?> ImportBondFromFmpAsync(string ticker)
           => PostAsync<Asset>("api/Assets/bonds/from-fmp",
                new { ticker = ticker.ToUpper() });

        public Task<bool> DeleteAssetAsync(int id)
          => DeleteAsync($"api/Assets/{id}");
        // ══════════════════════════════════════════════════════════════════════
        // PRIX & TAUX DE CHANGE
        // ══════════════════════════════════════════════════════════════════════

        public async Task<decimal?> GetLatestPriceAsync(string ticker)
        {
            try
            {
                var r = await Client().GetAsync(
                    $"api/Assets/price/{ticker.Trim().ToUpper()}");
                if (!r.IsSuccessStatusCode) return null;
                var text = await r.Content.ReadAsStringAsync();
                return decimal.TryParse(text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var p) ? p : null;
            }
            catch (Exception ex)
            { _logger.LogError(ex, "GetLatestPrice {T}", ticker); return null; }
        }

        public async Task<decimal> GetExchangeRateAsync(string from, string to)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return 1m;
            try
            {
                var r = await Client().GetAsync(
                    $"api/Assets/exchange-rate?from={from}&to={to}");
                if (!r.IsSuccessStatusCode) return 1m;
                var text = await r.Content.ReadAsStringAsync();
                return decimal.TryParse(text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var rate) && rate > 0 ? rate : 1m;
            }
            catch (Exception ex)
            { _logger.LogError(ex, "GetExchangeRate {F}/{T}", from, to); return 1m; }
        }

        public async Task<(decimal Price, decimal ChangePercent)> GetQuoteAsync(string ticker)
        {
            try
            {
                var r = await Client().GetAsync(
                    $"api/Assets/quote/{ticker.Trim().ToUpper()}");
                if (!r.IsSuccessStatusCode) return (0m, 0m);

                var text = await r.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                var root = doc.RootElement;

                decimal price = 0m, change = 0m;

                if (root.TryGetProperty("price", out var p)
                    && p.ValueKind == System.Text.Json.JsonValueKind.Number)
                    price = p.GetDecimal();

                if (root.TryGetProperty("change", out var c)
                    && c.ValueKind == System.Text.Json.JsonValueKind.Number)
                    change = c.GetDecimal();

                return (price, change);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQuoteAsync {Ticker}", ticker);
                return (0m, 0m);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ANALYTICS
        // ══════════════════════════════════════════════════════════════════════

        public Task<PortfolioAnalyticsResult?> AnalyzePortfolioAsync(
            int portfolioId, DateTime from, DateTime to, double riskFreeRate = 0.03)
            => GetAsync<PortfolioAnalyticsResult>(
                $"api/Analytics/portfolios/{portfolioId}/analyze" +
                $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&riskFreeRate={riskFreeRate}");

        public Task<MonteCarloResult?> RunMonteCarloAsync(
            int portfolioId, MonteCarloRequest req)
            => PostAsync<MonteCarloResult>(
                $"api/Analytics/portfolios/{portfolioId}/montecarlo", req);

        public Task<BacktestResult?> RunBacktestAsync(
            int portfolioId, BacktestRequest req)
            => PostAsync<BacktestResult>(
                $"api/Analytics/portfolios/{portfolioId}/backtest", req);

        public Task<OptimizationResult?> OptimizePortfolioAsync(
            int portfolioId, OptimizationRequest req)
            => PostAsync<OptimizationResult>(
                $"api/Analytics/portfolios/{portfolioId}/optimize", req);

        public Task<PortfolioComparisonResult?> ComparePortfoliosAsync(CompareRequest req)
            => PostAsync<PortfolioComparisonResult>(
                "api/Analytics/portfolios/compare", req);
    }
}