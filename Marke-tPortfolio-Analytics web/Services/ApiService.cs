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
    /// Utilise directement les types MarketPortfolioAnalytics.Models (via ProjectReference).
    /// Aucun DTO dupliqué.
    /// </summary>
    public class ApiService : IApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ApiService> _logger;

        // Options de désérialisation JSON partagées
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,  // "var95" et "VaR95" sont équivalents
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ApiService(IHttpClientFactory httpClientFactory, ILogger<ApiService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        // ── Helpers privés ────────────────────────────────────────────────────

        private HttpClient CreateClient() => _httpClientFactory.CreateClient("ApiClient");

        /// <summary>GET générique vers l'API.</summary>
        private async Task<T?> GetAsync<T>(string url) where T : class
        {
            try
            {
                var client = CreateClient();
                var response = await client.GetAsync(url);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur GET {Url}", url);
                return null;
            }
        }

        /// <summary>POST générique vers l'API, retourne T.</summary>
        private async Task<T?> PostAsync<T>(string url, object body) where T : class
        {
            try
            {
                var client = CreateClient();
                var json = JsonSerializer.Serialize(body, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("POST {Url} → {Status}", url, response.StatusCode);
                    return null;
                }

                // Certains endpoints retournent 204 No Content (Delete, certains PUT)
                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                    return null;

                return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur POST {Url}", url);
                return null;
            }
        }

        /// <summary>POST générique vers l'API, retourne bool (succès/échec).</summary>
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur POST {Url}", url);
                return false;
            }
        }

        /// <summary>PUT générique, retourne bool.</summary>
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur PUT {Url}", url);
                return false;
            }
        }

        /// <summary>PATCH générique, retourne bool.</summary>
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur PATCH {Url}", url);
                return false;
            }
        }

        /// <summary>DELETE générique, retourne bool.</summary>
        private async Task<bool> DeleteAsync(string url)
        {
            try
            {
                var client = CreateClient();
                var response = await client.DeleteAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur DELETE {Url}", url);
                return false;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // AUTHENTIFICATION & UTILISATEURS
        // ══════════════════════════════════════════════════════════════════════

        public async Task<AppUser?> LoginAsync(string email, string password)
        {
            return await PostAsync<AppUser>("api/AppUsers/login", new
            {
                email = email,
                password = password
            });
        }

        public async Task<AppUser?> RegisterAsync(string fullName, string email, string password)
        {
            return await PostAsync<AppUser>("api/AppUsers", new
            {
                fullName = fullName,
                email = email,
                password = password,
                role = "User",
                isActive = true
            });
        }

        public async Task<AppUser?> GetUserByIdAsync(int id)
            => await GetAsync<AppUser>($"api/AppUsers/{id}");

        public async Task<bool> UpdateUserAsync(int id, string fullName, string email)
        {
            return await PutAsync($"api/AppUsers/{id}", new
            {
                id = id,
                fullName = fullName,
                email = email
            });
        }

        public async Task<bool> ChangePasswordAsync(int id, string currentPassword, string newPassword)
        {
            try
            {
                var client = CreateClient();
                var json = System.Text.Json.JsonSerializer.Serialize(newPassword);
                var content = new System.Net.Http.StringContent(
                    json, System.Text.Encoding.UTF8, "application/json");

                var request = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Patch,
                    $"api/AppUsers/{id}/password")
                {
                    Content = content
                };

                var response = await client.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur PATCH password userId={Id}", id);
                return false;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // PORTEFEUILLES
        // ══════════════════════════════════════════════════════════════════════

        public async Task<List<Portfolio>> GetPortfoliosByUserAsync(int userId)
        {
            var result = await GetAsync<List<Portfolio>>($"api/Portfolios?userId={userId}");
            return result ?? new List<Portfolio>();
        }

        public async Task<Portfolio?> GetPortfolioByIdAsync(int id)
            => await GetAsync<Portfolio>($"api/Portfolios/{id}");

        public async Task<Portfolio?> GetPortfolioDetailsAsync(int id)
            => await GetAsync<Portfolio>($"api/Portfolios/{id}/details");

        public async Task<Portfolio?> CreatePortfolioAsync(string name, string currency, int userId)
        {
            return await PostAsync<Portfolio>("api/Portfolios", new
            {
                name = name,
                currency = currency,
                userId = userId
            });
        }

        public async Task<bool> UpdatePortfolioAsync(int id, string name, string currency, int userId)
        {
            return await PutAsync($"api/Portfolios/{id}", new
            {
                id = id,
                name = name,
                currency = currency,
                userId = userId
            });
        }

        public async Task<bool> DeletePortfolioAsync(int id)
            => await DeleteAsync($"api/Portfolios/{id}");

        // ══════════════════════════════════════════════════════════════════════
        // POSITIONS
        // ══════════════════════════════════════════════════════════════════════

        public async Task<bool> AddPositionAsync(int portfolioId, int assetId,
            decimal quantity, decimal avgBuyPrice, DateTime buyDate)
        {
            return await PostBoolAsync($"api/Portfolios/{portfolioId}/positions", new
            {
                portfolioId = portfolioId,
                assetId = assetId,
                quantity = quantity,
                avgBuyPrice = avgBuyPrice,
                buyDate = buyDate.ToString("yyyy-MM-dd")
            });
        }

        public async Task<bool> UpdatePositionAsync(int portfolioId, int assetId,
            decimal quantity, decimal avgBuyPrice, DateTime buyDate)
        {
            return await PutAsync($"api/Portfolios/{portfolioId}/positions/{assetId}", new
            {
                portfolioId = portfolioId,
                assetId = assetId,
                quantity = quantity,
                avgBuyPrice = avgBuyPrice,
                buyDate = buyDate.ToString("yyyy-MM-dd")
            });
        }

        public async Task<bool> DeletePositionAsync(int portfolioId, int assetId)
            => await DeleteAsync($"api/Portfolios/{portfolioId}/positions/{assetId}");

        // ══════════════════════════════════════════════════════════════════════
        // ACTIFS
        // ══════════════════════════════════════════════════════════════════════

        public async Task<List<Asset>> GetAllAssetsAsync()
        {
            var result = await GetAsync<List<Asset>>("api/Assets");
            return result ?? new List<Asset>();
        }

        public async Task<Asset?> GetAssetByIdAsync(int id)
            => await GetAsync<Asset>($"api/Assets/{id}");

        public async Task<Asset?> GetAssetByTickerAsync(string ticker)
            => await GetAsync<Asset>($"api/Assets/by-ticker/{ticker.ToUpper()}");

        public async Task<Asset?> ImportStockFromFmpAsync(string ticker)
        {
            return await PostAsync<Asset>("api/Assets/stocks/from-fmp", new
            {
                ticker = ticker.ToUpper()
            });
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
        {
            return await PostAsync<MonteCarloResult>(
                $"api/Analytics/portfolios/{portfolioId}/montecarlo", request);
        }

        public async Task<BacktestResult?> RunBacktestAsync(
            int portfolioId, BacktestRequest request)
        {
            return await PostAsync<BacktestResult>(
                $"api/Analytics/portfolios/{portfolioId}/backtest", request);
        }

        public async Task<OptimizationResult?> OptimizePortfolioAsync(
            int portfolioId, OptimizationRequest request)
        {
            return await PostAsync<OptimizationResult>(
                $"api/Analytics/portfolios/{portfolioId}/optimize", request);
        }

        public async Task<PortfolioComparisonResult?> ComparePortfoliosAsync(
            CompareRequest request)
        {
            return await PostAsync<PortfolioComparisonResult>(
                "api/Analytics/portfolios/compare", request);
        }
    }
}

