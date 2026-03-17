using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace Marke_tPortfolio_Analytics_web.Services
{
    /// <summary>
    /// Implémentation concrète de IApiService.
    /// Tous les appels HTTP vers le backend passent ici.
    /// </summary>
    public class ApiService : IApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ApiService> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ApiService(IHttpClientFactory httpClientFactory, ILogger<ApiService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        // ── Helpers privés ────────────────────────────────────────────────────

        private HttpClient CreateClient() => _httpClientFactory.CreateClient("ApiClient");

        private async Task<T?> GetAsync<T>(string url) where T : class
        {
            try
            {
                var client = CreateClient();
                var response = await client.GetAsync(url);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
            }
            catch (Exception ex) { _logger.LogError(ex, "Erreur GET {Url}", url); return null; }
        }

        private async Task<T?> PostAsync<T>(string url, object body) where T : class
        {
            try
            {
                var client = CreateClient();
                var json = JsonSerializer.Serialize(body, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);
                if (!response.IsSuccessStatusCode) { _logger.LogWarning("POST {Url} → {Status}", url, response.StatusCode); return null; }
                if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
                return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
            }
            catch (Exception ex) { _logger.LogError(ex, "Erreur POST {Url}", url); return null; }
        }

        private async Task<bool> PostBoolAsync(string url, object body)
        {
            try
            {
                var client = CreateClient();
                var json = JsonSerializer.Serialize(body, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { _logger.LogError(ex, "Erreur POST {Url}", url); return false; }
        }

        private async Task<bool> PutAsync(string url, object body)
        {
            try
            {
                var client = CreateClient();
                var json = JsonSerializer.Serialize(body, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PutAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { _logger.LogError(ex, "Erreur PUT {Url}", url); return false; }
        }

        private async Task<bool> PatchAsync(string url, object body)
        {
            try
            {
                var client = CreateClient();
                var json = JsonSerializer.Serialize(body, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
                var response = await client.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { _logger.LogError(ex, "Erreur PATCH {Url}", url); return false; }
        }

        /// <summary>PATCH avec string brute (pour PATCH /{id}/password).</summary>
        private async Task<bool> PatchStringAsync(string url, string value)
        {
            try
            {
                var client = CreateClient();
                var content = new StringContent(
                    JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
                var response = await client.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { _logger.LogError(ex, "Erreur PATCH string {Url}", url); return false; }
        }

        private async Task<bool> DeleteAsync(string url)
        {
            try
            {
                var client = CreateClient();
                var response = await client.DeleteAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) { _logger.LogError(ex, "Erreur DELETE {Url}", url); return false; }
        }

        // ══════════════════════════════════════════════════════════════════════
        // AUTHENTIFICATION & UTILISATEURS
        // ══════════════════════════════════════════════════════════════════════

        public async Task<AppUser?> LoginAsync(string email, string password)
            => await PostAsync<AppUser>("api/AppUsers/login", new { email, password });

        public async Task<AppUser?> RegisterAsync(string fullName, string email, string password)
            => await PostAsync<AppUser>("api/AppUsers", new
            {
                fullName,
                email,
                password,
                role = "User",
                isActive = true
            });

        public async Task<AppUser?> GetUserByIdAsync(int id)
            => await GetAsync<AppUser>($"api/AppUsers/{id}");

        public async Task<bool> UpdateUserAsync(int id, string fullName, string email)
            => await PutAsync($"api/AppUsers/{id}", new { id, fullName, email });

        /// <summary>
        /// ✅ CORRIGÉ Phase 2 : envoie newPassword comme string JSON brute.
        /// Le backend PATCH /{id}/password accepte [FromBody] string newPassword.
        /// currentPassword ignoré (non vérifié côté backend).
        /// </summary>
        public async Task<bool> ChangePasswordAsync(int id, string currentPassword, string newPassword)
            => await PatchStringAsync($"api/AppUsers/{id}/password", newPassword);

        // ══════════════════════════════════════════════════════════════════════
        // PORTEFEUILLES
        // ══════════════════════════════════════════════════════════════════════

        public async Task<List<Portfolio>> GetPortfoliosByUserAsync(int userId)
        {
            var result = await GetAsync<List<Portfolio>>($"api/Portfolios?userId={userId}");
            return result ?? new();
        }

        public async Task<Portfolio?> GetPortfolioByIdAsync(int id)
            => await GetAsync<Portfolio>($"api/Portfolios/{id}");

        public async Task<Portfolio?> GetPortfolioDetailsAsync(int id)
            => await GetAsync<Portfolio>($"api/Portfolios/{id}/details");

        /// <summary>✅ Phase 3 : crée un portefeuille via objet Portfolio complet.</summary>
        public async Task<Portfolio?> CreatePortfolioAsync(Portfolio portfolio)
            => await PostAsync<Portfolio>("api/Portfolios", new
            {
                name = portfolio.Name,
                description = portfolio.Description,
                currency = portfolio.Currency,
                userId = portfolio.UserId
            });

        /// <summary>✅ Phase 3 : met à jour un portefeuille via objet Portfolio.</summary>
        public async Task<bool> UpdatePortfolioAsync(int id, Portfolio portfolio)
            => await PutAsync($"api/Portfolios/{id}", new
            {
                id = id,
                name = portfolio.Name,
                description = portfolio.Description,
                currency = portfolio.Currency,
                userId = portfolio.UserId
            });

        public async Task<bool> DeletePortfolioAsync(int id)
            => await DeleteAsync($"api/Portfolios/{id}");

        // ══════════════════════════════════════════════════════════════════════
        // POSITIONS  
        // ══════════════════════════════════════════════════════════════════════

        public async Task<List<Position>?> GetPositionsByPortfolioAsync(int portfolioId)
            => await GetAsync<List<Position>>($"api/Portfolios/{portfolioId}/positions");

        public async Task<Position?> GetPositionByIdAsync(int id)
            => await GetAsync<Position>($"api/Positions/{id}");

        public async Task<Position?> CreatePositionAsync(Position position)
            => await PostAsync<Position>(
                $"api/Portfolios/{position.PortfolioId}/positions", new
                {
                    portfolioId = position.PortfolioId,
                    assetId = position.AssetId,
                    quantity = position.Quantity,
                    purchasePrice = position.PurchasePrice,
                    purchaseDate = position.PurchaseDate.ToString("yyyy-MM-dd")
                });

        public async Task<bool> UpdatePositionAsync(int id, Position position)
            => await PutAsync($"api/Positions/{id}", new
            {
                id = id,
                portfolioId = position.PortfolioId,
                assetId = position.AssetId,
                quantity = position.Quantity,
                purchasePrice = position.PurchasePrice,
                purchaseDate = position.PurchaseDate.ToString("yyyy-MM-dd")
            });

        public async Task<bool> DeletePositionAsync(int positionId)
            => await DeleteAsync($"api/Positions/{positionId}");

        // ══════════════════════════════════════════════════════════════════════
        // ACTIFS
        // ══════════════════════════════════════════════════════════════════════

        public async Task<List<Asset>> GetAllAssetsAsync()
        {
            var result = await GetAsync<List<Asset>>("api/Assets");
            return result ?? new();
        }

        public async Task<Asset?> GetAssetByIdAsync(int id)
            => await GetAsync<Asset>($"api/Assets/{id}");

        public async Task<Asset?> GetAssetByTickerAsync(string ticker)
            => await GetAsync<Asset>($"api/Assets/by-ticker/{ticker.ToUpper()}");

        public async Task<Asset?> ImportStockFromFmpAsync(string ticker)
            => await PostAsync<Asset>("api/Assets/stocks/from-fmp",
                new { ticker = ticker.ToUpper() });

        // ══════════════════════════════════════════════════════════════════════
        // PRIX & TAUX DE CHANGE
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Prix temps réel d'un actif.
        /// GET /api/Assets/price/{symbol}
        /// </summary>
        public async Task<decimal?> GetLatestPriceAsync(string symbol)
        {
            try
            {
                var client = CreateClient();
                var response = await client.GetAsync($"api/Assets/price/{symbol.ToUpper()}");
                if (!response.IsSuccessStatusCode) return null;
                var text = await response.Content.ReadAsStringAsync();
                if (decimal.TryParse(text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var price))
                    return price;
                // Si l'API retourne un objet JSON { "price": 123.45 }
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("price", out var priceElem))
                    return (decimal)priceElem.GetDouble();
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur GetLatestPriceAsync {Symbol}", symbol);
                return null;
            }
        }

        /// <summary>
        /// Taux de change entre deux devises.
        /// GET /api/Assets/exchange-rate?from=EUR&amp;to=USD
        /// Retourne 1.0 en cas d'erreur.
        /// </summary>
        public async Task<decimal> GetExchangeRateAsync(string from, string to)
        {
            try
            {
                if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return 1m;
                var client = CreateClient();
                var response = await client.GetAsync(
                    $"api/Assets/exchange-rate?from={from}&to={to}");
                if (!response.IsSuccessStatusCode) return 1m;
                var text = await response.Content.ReadAsStringAsync();
                if (decimal.TryParse(text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var rate) && rate > 0)
                    return rate;
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("rate", out var rateElem))
                    return (decimal)rateElem.GetDouble();
                return 1m;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur GetExchangeRateAsync {From}/{To}", from, to);
                return 1m; // Pas de crash — conversion neutre
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ANALYTICS
        // ══════════════════════════════════════════════════════════════════════

        public async Task<PortfolioAnalyticsResult?> AnalyzePortfolioAsync(
            int portfolioId, DateTime from, DateTime to, double riskFreeRate = 0.03)
        {
            var url = $"api/Analytics/portfolios/{portfolioId}/analyze" +
                      $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&riskFreeRate={riskFreeRate}";
            return await GetAsync<PortfolioAnalyticsResult>(url);
        }

        public async Task<MonteCarloResult?> RunMonteCarloAsync(
            int portfolioId, MonteCarloRequest request)
            => await PostAsync<MonteCarloResult>(
                $"api/Analytics/portfolios/{portfolioId}/montecarlo", request);

        public async Task<BacktestResult?> RunBacktestAsync(
            int portfolioId, BacktestRequest request)
            => await PostAsync<BacktestResult>(
                $"api/Analytics/portfolios/{portfolioId}/backtest", request);

        public async Task<OptimizationResult?> OptimizePortfolioAsync(
            int portfolioId, OptimizationRequest request)
            => await PostAsync<OptimizationResult>(
                $"api/Analytics/portfolios/{portfolioId}/optimize", request);

        public async Task<PortfolioComparisonResult?> ComparePortfoliosAsync(
            CompareRequest request)
            => await PostAsync<PortfolioComparisonResult>(
                "api/Analytics/portfolios/compare", request);
    }
}
