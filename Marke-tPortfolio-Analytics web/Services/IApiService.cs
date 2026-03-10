using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace Marke_tPortfolio_Analytics_web.Services
{
    /// <summary>
    /// Contrat de communication avec l'API backend MarketPortfolioAnalytics.
    /// Tous les controllers passent exclusivement par cette interface.
    /// Aucun controller n'appelle HttpClient directement.
    /// </summary>
    public interface IApiService
    {
        // ══════════════════════════════════════════════════════════════════════
        // AUTHENTIFICATION & UTILISATEURS
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Connexion : vérifie email + mot de passe.
        /// Retourne l'utilisateur si valide, null sinon.
        /// Nécessite POST /api/AppUsers/login dans le backend.
        /// </summary>
        Task<AppUser?> LoginAsync(string email, string password);

        /// <summary>Inscription : crée un nouvel utilisateur.</summary>
        Task<AppUser?> RegisterAsync(string fullName, string email, string password);

        /// <summary>Récupère un utilisateur par son Id.</summary>
        Task<AppUser?> GetUserByIdAsync(int id);

        /// <summary>Met à jour les informations d'un utilisateur (nom, email).</summary>
        Task<bool> UpdateUserAsync(int id, string fullName, string email);

        /// <summary>Change le mot de passe d'un utilisateur.</summary>
        Task<bool> ChangePasswordAsync(int id, string currentPassword, string newPassword);

        // ══════════════════════════════════════════════════════════════════════
        // PORTEFEUILLES
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Récupère tous les portefeuilles d'un utilisateur.
        /// GET /api/Portfolios?userId={userId}
        /// </summary>
        Task<List<Portfolio>> GetPortfoliosByUserAsync(int userId);

        /// <summary>
        /// Récupère un portefeuille par Id (sans positions).
        /// GET /api/Portfolios/{id}
        /// </summary>
        Task<Portfolio?> GetPortfolioByIdAsync(int id);

        /// <summary>
        /// Récupère un portefeuille avec toutes ses positions et actifs liés.
        /// GET /api/Portfolios/{id}/details
        /// </summary>
        Task<Portfolio?> GetPortfolioDetailsAsync(int id);

        /// <summary>
        /// Crée un nouveau portefeuille.
        /// POST /api/Portfolios
        /// </summary>
        Task<Portfolio?> CreatePortfolioAsync(string name, string currency, int userId);

        /// <summary>
        /// Met à jour un portefeuille existant.
        /// PUT /api/Portfolios/{id}
        /// </summary>
        Task<bool> UpdatePortfolioAsync(int id, string name, string currency, int userId);

        /// <summary>
        /// Supprime un portefeuille.
        /// DELETE /api/Portfolios/{id}
        /// </summary>
        Task<bool> DeletePortfolioAsync(int id);

        // ══════════════════════════════════════════════════════════════════════
        // POSITIONS
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ajoute une position dans un portefeuille.
        /// La position est envoyée via POST /api/Portfolios (body Position).
        /// </summary>
        Task<bool> AddPositionAsync(int portfolioId, int assetId, decimal quantity,
                                    decimal avgBuyPrice, DateTime buyDate);

        /// <summary>
        /// Met à jour une position (quantité, prix moyen).
        /// PUT via la route positions.
        /// </summary>
        Task<bool> UpdatePositionAsync(int portfolioId, int assetId, decimal quantity,
                                       decimal avgBuyPrice, DateTime buyDate);

        /// <summary>
        /// Supprime une position d'un portefeuille.
        /// </summary>
        Task<bool> DeletePositionAsync(int portfolioId, int assetId);

        // ══════════════════════════════════════════════════════════════════════
        // ACTIFS
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Récupère tous les actifs disponibles dans la plateforme.
        /// GET /api/Assets
        /// </summary>
        Task<List<Asset>> GetAllAssetsAsync();

        /// <summary>
        /// Récupère un actif par son Id.
        /// GET /api/Assets/{id}
        /// </summary>
        Task<Asset?> GetAssetByIdAsync(int id);

        /// <summary>
        /// Récupère un actif par son ticker boursier.
        /// GET /api/Assets/by-ticker/{ticker}
        /// </summary>
        Task<Asset?> GetAssetByTickerAsync(string ticker);

        /// <summary>
        /// Importe une action depuis l'API FMP par son ticker.
        /// POST /api/Assets/stocks/from-fmp
        /// </summary>
        Task<Asset?> ImportStockFromFmpAsync(string ticker);

        // ══════════════════════════════════════════════════════════════════════
        // ANALYTICS
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Analyse complète d'un portefeuille (rendement, Sharpe, volatilité...).
        /// GET /api/Analytics/portfolios/{id}/analyze?from=&to=&riskFreeRate=
        /// </summary>
        Task<PortfolioAnalyticsResult?> AnalyzePortfolioAsync(
            int portfolioId, DateTime from, DateTime to, double riskFreeRate = 0.03);

        /// <summary>
        /// Simulation Monte Carlo.
        /// POST /api/Analytics/portfolios/{id}/montecarlo
        /// </summary>
        Task<MonteCarloResult?> RunMonteCarloAsync(int portfolioId, MonteCarloRequest request);

        /// <summary>
        /// Backtesting historique vs benchmark.
        /// POST /api/Analytics/portfolios/{id}/backtest
        /// </summary>
        Task<BacktestResult?> RunBacktestAsync(int portfolioId, BacktestRequest request);

        /// <summary>
        /// Optimisation Markowitz (MaxSharpe / MinVolatility / MaxReturn).
        /// POST /api/Analytics/portfolios/{id}/optimize
        /// </summary>
        Task<OptimizationResult?> OptimizePortfolioAsync(int portfolioId, OptimizationRequest request);

        /// <summary>
        /// Comparaison multi-portefeuilles.
        /// POST /api/Analytics/portfolios/compare
        /// </summary>
        Task<PortfolioComparisonResult?> ComparePortfoliosAsync(CompareRequest request);
    }
}

