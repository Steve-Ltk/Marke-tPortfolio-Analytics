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

        Task<AppUser?> LoginAsync(string email, string password);
        Task<AppUser?> RegisterAsync(string fullName, string email, string password);
        Task<AppUser?> GetUserByIdAsync(int id);
        Task<bool> UpdateUserAsync(int id, string fullName, string email);

        /// <summary>
        /// ✅ CORRIGÉ Phase 2 : envoie newPassword en string brute (PATCH /{id}/password).
        /// Le paramètre currentPassword est ignoré côté backend (non vérifié).
        /// </summary>
        Task<bool> ChangePasswordAsync(int id, string currentPassword, string newPassword);

        // ══════════════════════════════════════════════════════════════════════
        // PORTEFEUILLES
        // ══════════════════════════════════════════════════════════════════════

        Task<List<Portfolio>> GetPortfoliosByUserAsync(int userId);
        Task<Portfolio?> GetPortfolioByIdAsync(int id);
        Task<Portfolio?> GetPortfolioDetailsAsync(int id);

        /// <summary>✅ Phase 3 : accepte un objet Portfolio complet.</summary>
        Task<Portfolio?> CreatePortfolioAsync(Portfolio portfolio);

        /// <summary>✅ Phase 3 : accepte un objet Portfolio complet.</summary>
        Task<bool> UpdatePortfolioAsync(int id, Portfolio portfolio);

        Task<bool> DeletePortfolioAsync(int id);

        // ══════════════════════════════════════════════════════════════════════
        // POSITIONS
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>✅ Phase 3 : liste des positions d'un portefeuille.</summary>
        Task<List<Position>?> GetPositionsByPortfolioAsync(int portfolioId);

        /// <summary>✅ Phase 3 : position par Id.</summary>
        Task<Position?> GetPositionByIdAsync(int id);

        /// <summary>✅ Phase 3 : crée une position via objet Position.</summary>
        Task<Position?> CreatePositionAsync(Position position);

        /// <summary>✅ Phase 3 : met à jour une position via objet Position.</summary>
        Task<bool> UpdatePositionAsync(int id, Position position);

        /// <summary>✅ Phase 3 : supprime une position par son Id direct.</summary>
        Task<bool> DeletePositionAsync(int positionId);

        // ══════════════════════════════════════════════════════════════════════
        // ACTIFS
        // ══════════════════════════════════════════════════════════════════════

        Task<List<Asset>> GetAllAssetsAsync();
        Task<Asset?> GetAssetByIdAsync(int id);
        Task<Asset?> GetAssetByTickerAsync(string ticker);
        Task<Asset?> ImportStockFromFmpAsync(string ticker);

        // ══════════════════════════════════════════════════════════════════════
        // PRIX & TAUX DE CHANGE — ✅ Ajoutés Phase 3
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Prix en temps réel d'un actif via FMP.
        /// GET /api/Assets/price/{symbol}
        /// Retourne null si introuvable ou erreur FMP.
        /// </summary>
        Task<decimal?> GetLatestPriceAsync(string symbol);

        /// <summary>
        /// Taux de change entre deux devises via FMP.
        /// GET /api/Assets/exchange-rate?from=EUR&amp;to=USD
        /// Retourne 1 en cas d'erreur (pas de conversion).
        /// </summary>
        Task<decimal> GetExchangeRateAsync(string from, string to);

        // ══════════════════════════════════════════════════════════════════════
        // ANALYTICS
        // ══════════════════════════════════════════════════════════════════════

        Task<PortfolioAnalyticsResult?> AnalyzePortfolioAsync(
            int portfolioId, DateTime from, DateTime to, double riskFreeRate = 0.03);

        Task<MonteCarloResult?> RunMonteCarloAsync(int portfolioId, MonteCarloRequest request);
        Task<BacktestResult?> RunBacktestAsync(int portfolioId, BacktestRequest request);
        Task<OptimizationResult?> OptimizePortfolioAsync(int portfolioId, OptimizationRequest request);
        Task<PortfolioComparisonResult?> ComparePortfoliosAsync(CompareRequest request);
    }
}
