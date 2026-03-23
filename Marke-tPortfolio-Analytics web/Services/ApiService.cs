using System.Net.Http.Json;
using System.Text;
using System.Globalization;
using System.Text.Json;
using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace Marke_tPortfolio_Analytics_web.Services
{
    // Service qui fait le pont entre les controllers MVC frontend et l'API backend.
    // TOUS les appels HTTP vers le backend passent par ici.
    // Les controllers ne font jamais de HttpClient directement — ils passent par ApiService.
    // Si l'URL d'un endpoint backend change -> on modifie uniquement ce fichier.
    public class ApiService : IApiService
    {
        // _factory : fabrique de HttpClient -> crée un client nommé "ApiClient"
        // configuré dans Program.cs (BaseAddress = http://localhost:5154)
        // _logger : pour tracer les erreurs HTTP dans la console
        private readonly IHttpClientFactory _factory;
        private readonly ILogger<ApiService> _logger;

        // Options JSON partagées par toute la classe
        // PropertyNameCaseInsensitive -> "sharpeRatio" et "SharpeRatio" sont traités pareil
        // CamelCase → les propriétés C# sont envoyées en camelCase dans le JSON
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

        // Crée un HttpClient configuré avec l'adresse de base du backend
        // "ApiClient" = nom déclaré dans Program.cs -> builder.Services.AddHttpClient("ApiClient", ...)
        private HttpClient Client() => _factory.CreateClient("ApiClient");

        // Sérialise un objet C# en JSON pour l'envoyer dans le body d'une requête HTTP
        // StringContent = contenu textuel, encodé en UTF-8, type "application/json"
        private static StringContent Serialize(object body)
            => new(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json");

        // Helper GET : envoie une requête GET et désérialise la réponse en objet T
        // Retourne null si : 404, erreur réseau, ou exception
        // T : class -> contrainte : T doit être une classe (pas un int ou bool)
        private async Task<T?> GetAsync<T>(string url) where T : class
        {
            try
            {
                var r = await Client().GetAsync(url);

                // 404 = ressource introuvable -> null sans exception
                if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                // Autre erreur HTTP -> lance une exception (attrapée par le catch)
                r.EnsureSuccessStatusCode();

                // Désérialise le JSON de la réponse en objet de type T
                return await r.Content.ReadFromJsonAsync<T>(_json);
            }
            catch (Exception ex) 
            { 
                // Log l'erreur dans la console avec l'URL concernée
                _logger.LogError(ex, "GET {Url}", url); 
                return null; // jamais d'exception qui remonte au controller
            }
        }

        // Helper POST : envoie un objet en JSON et retourne la réponse désérialisée en T
        // Retourne null si : erreur HTTP, 204 No Content, ou exception
        private async Task<T?> PostAsync<T>(string url, object body) where T : class
        {
            try
            {
                var r = await Client().PostAsync(url, Serialize(body));
                if (!r.IsSuccessStatusCode)
                { 
                    // Log le code HTTP d'erreur pour debug (ex: 400, 409, 500)
                    _logger.LogWarning("POST {Url} → {S}", url, r.StatusCode); 
                    return null; 
                }

                // 204 No Content = succès mais rien à retourner (ex: après une mise à jour)
                if (r.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
                return await r.Content.ReadFromJsonAsync<T>(_json);
            }
            catch (Exception ex) { _logger.LogError(ex, "POST {Url}", url); return null; }
        }

        // Helper PUT : envoie une mise à jour et retourne true si succès
        // Utilisé pour les opérations qui ne retournent pas d'objet (NoContent 204)
        private async Task<bool> PutAsync(string url, object body)
        {
            try
            {
                var r = await Client().PutAsync(url, Serialize(body));
                return r.IsSuccessStatusCode; // true = 200 ou 204, false = 400/404/500...
            }
            catch (Exception ex) { _logger.LogError(ex, "PUT {Url}", url); return false; }
        }

        // Helper PATCH : envoie une modification partielle avec une string en body
        // Utilisé pour changer le mot de passe (body = string JSON du mot de passe)
        // HttpMethod.Patch n'a pas de méthode dédiée dans HttpClient -> on crée la requête manuellement
        private async Task<bool> PatchStringAsync(string url, string value)
        {
            try
            {
                var content = new StringContent(
                    JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

                // HttpRequestMessage = requête HTTP manuelle (méthode + URL + body)
                var req = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
                var r = await Client().SendAsync(req);
                return r.IsSuccessStatusCode;
            }
            catch (Exception ex) { _logger.LogError(ex, "PATCH {Url}", url); return false; }
        }

        // Helper DELETE : envoie une suppression et retourne true si succès
        private async Task<bool> DeleteAsync(string url)
        {
            try { return (await Client().DeleteAsync(url)).IsSuccessStatusCode; }
            catch (Exception ex) { _logger.LogError(ex, "DELETE {Url}", url); return false; }
        }

        // Envoie email + mot de passe au backend -> retourne l'utilisateur si valide, null sinon
        public Task<AppUser?> LoginAsync(string email, string password)
            => PostAsync<AppUser>("api/AppUsers/login", new { email, password });

        // Crée un nouveau compte → retourne l'utilisateur créé
        // "role = User" et "isActive = true" imposés ici car le backend les vérifie aussi
        public Task<AppUser?> RegisterAsync(string fullName, string email, string password)
            => PostAsync<AppUser>("api/AppUsers",
                new { fullName, email, password, role = "User", isActive = true });

        public Task<AppUser?> GetUserByIdAsync(int id)
            => GetAsync<AppUser>($"api/AppUsers/{id}");

        // Met à jour nom et/ou email → retourne true si succès
        public Task<bool> UpdateUserAsync(int id, string fullName, string email)
            => PutAsync($"api/AppUsers/{id}", new { id, fullName, email });

        // Change le mot de passe -> vérifie l'ancien avant d'accepter le nouveau
        public async Task<bool> ChangePasswordAsync(int id, string currentPassword, string newPassword)
        {
            try
            {
                var body = new { currentPassword, newPassword };
                var content = new StringContent(
                    JsonSerializer.Serialize(body, _json),
                    System.Text.Encoding.UTF8,
                    "application/json");

                 // PATCH /api/AppUsers/{id}/password
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

        // Retourne tous les portefeuilles d'un utilisateur
        // ?? new() = si GetAsync retourne null -> retourne une liste vide (jamais null)
        public async Task<List<Portfolio>> GetPortfoliosByUserAsync(int userId)
            => await GetAsync<List<Portfolio>>($"api/Portfolios?userId={userId}") ?? new();

        public Task<Portfolio?> GetPortfolioByIdAsync(int id)
            => GetAsync<Portfolio>($"api/Portfolios/{id}");

        // Version avec positions et actifs inclus (Include + ThenInclude côté backend)
        public Task<Portfolio?> GetPortfolioDetailsAsync(int id)
            => GetAsync<Portfolio>($"api/Portfolios/{id}/details");

        // Crée un portefeuille → envoie name, currency, userId
        public Task<Portfolio?> CreatePortfolioAsync(string name, string currency, int userId)
            => PostAsync<Portfolio>("api/Portfolios", new { name, currency, userId });

        // Met à jour nom et devise uniquement -> UserId et CreatedAt sont immuables côté backend
        public Task<bool> UpdatePortfolioAsync(int id, string name, string currency)
            => PutAsync($"api/Portfolios/{id}", new { id, name, currency });

        public Task<bool> DeletePortfolioAsync(int id)
            => DeleteAsync($"api/Portfolios/{id}");


        // Retourne toutes les positions d'un portefeuille
        // Null possible si le portefeuille n'existe pas (différent de liste vide)
        public Task<List<Position>?> GetPositionsByPortfolioAsync(int portfolioId)
            => GetAsync<List<Position>>($"api/Portfolios/{portfolioId}/positions");

        // Retourne une position par sa clé composite (portfolioId + assetId)
        public Task<Position?> GetPositionByKeyAsync(int portfolioId, int assetId)
            => GetAsync<Position>($"api/Positions/{portfolioId}/{assetId}");

        // Crée une position → POST /api/Positions avec le body complet
        // buyDate formaté en "yyyy-MM-dd" → format attendu par le backend
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

        // Met à jour quantité, prix moyen et date d'achat d'une position
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

        // Retourne tous les actifs en base → ?? new() = jamais null
        public async Task<List<Asset>> GetAllAssetsAsync()
            => await GetAsync<List<Asset>>("api/Assets") ?? new();

        public Task<Asset?> GetAssetByIdAsync(int id)
            => GetAsync<Asset>($"api/Assets/{id}");

        public Task<Asset?> GetAssetByTickerAsync(string ticker)
            => GetAsync<Asset>($"api/Assets/by-ticker/{ticker.ToUpper()}");

        // Importe une action depuis FMP → POST api/Assets/stocks/from-fmp
        public Task<Asset?> ImportStockFromFmpAsync(string ticker)
            => PostAsync<Asset>("api/Assets/stocks/from-fmp",
                new { ticker = ticker.ToUpper() });

        // Importe une obligation depuis FMP → POST api/Assets/bonds/from-fmp
        public Task<Asset?> ImportBondFromFmpAsync(string ticker)
           => PostAsync<Asset>("api/Assets/bonds/from-fmp",
                new { ticker = ticker.ToUpper() });

        // Supprime un actif → retourne false si utilisé dans un portefeuille
        public Task<bool> DeleteAssetAsync(int id)
          => DeleteAsync($"api/Assets/{id}");
        
        // Retourne le prix actuel d'un actif en decimal
        // Cas particulier : le backend retourne un decimal brut (pas un objet JSON)
        // -> on lit le texte et on le parse manuellement
        public async Task<decimal?> GetLatestPriceAsync(string ticker)
        {
            try
            {
                var r = await Client().GetAsync(
                    $"api/Assets/price/{ticker.Trim().ToUpper()}");
                if (!r.IsSuccessStatusCode) return null;
                var text = await r.Content.ReadAsStringAsync();

                // TryParse avec InvariantCulture -> évite le problème de séparateur décimal
                // "182.50" doit être parsé avec le point, pas la virgule (culture FR)
                return decimal.TryParse(text,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var p) ? p : null;
            }
            catch (Exception ex)
            { _logger.LogError(ex, "GetLatestPrice {T}", ticker); return null; }
        }

        // Retourne le taux de change entre deux devises
        // Retourne 1m (taux neutre) si FMP ne répond pas -> pas de conversion
        public async Task<decimal> GetExchangeRateAsync(string from, string to)
        {
            // Même devise -> taux = 1, pas besoin d'appeler le backend
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

        // Retourne le prix ET la variation journalière en un seul appel
        // Retourne (0, 0) si FMP ne répond pas -> le frontend affiche "—" pour la variation
        public async Task<(decimal Price, decimal ChangePercent)> GetQuoteAsync(string ticker)
        {
            try
            {
                var r = await Client().GetAsync(
                    $"api/Assets/quote/{ticker.Trim().ToUpper()}");
                if (!r.IsSuccessStatusCode) return (0m, 0m);

                var text = await r.Content.ReadAsStringAsync();

                // Parse manuel du JSON -> TryGetProperty évite de planter si un champ manque
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

        // Analyse complète d'un portefeuille sur une période
        public Task<PortfolioAnalyticsResult?> AnalyzePortfolioAsync(
            int portfolioId, DateTime from, DateTime to, double riskFreeRate = 0.045)
            => GetAsync<PortfolioAnalyticsResult>(
                $"api/Analytics/portfolios/{portfolioId}/analyze" +
                $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&riskFreeRate={riskFreeRate.ToString(CultureInfo.InvariantCulture)}");

        
        // Simulation Monte Carlo -> POST avec l'objet MonteCarloRequest en body
        public Task<MonteCarloResult?> RunMonteCarloAsync(
            int portfolioId, MonteCarloRequest req)
            => PostAsync<MonteCarloResult>(
                $"api/Analytics/portfolios/{portfolioId}/montecarlo", req);

        // Backtest historique -> POST avec BacktestRequest en body
        public Task<BacktestResult?> RunBacktestAsync(
            int portfolioId, BacktestRequest req)
            => PostAsync<BacktestResult>(
                $"api/Analytics/portfolios/{portfolioId}/backtest", req);

        // Optimisation Markowitz -> POST avec OptimizationRequest en body
        public Task<OptimizationResult?> OptimizePortfolioAsync(
            int portfolioId, OptimizationRequest req)
            => PostAsync<OptimizationResult>(
                $"api/Analytics/portfolios/{portfolioId}/optimize", req);

        // Comparaison de plusieurs portefeuilles -> POST avec CompareRequest en body
        public Task<PortfolioComparisonResult?> ComparePortfoliosAsync(CompareRequest req)
            => PostAsync<PortfolioComparisonResult>(
                "api/Analytics/portfolios/compare", req);
    }
}
