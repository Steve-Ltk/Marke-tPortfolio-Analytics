using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;

namespace Marke_tPortfolio_Analytics_web.Services
{
    public interface IApiService
    {
        // AUTH & UTILISATEURS
        Task<AppUser?> LoginAsync(string email, string password);
        Task<AppUser?> RegisterAsync(string fullName, string email, string password);
        Task<AppUser?> GetUserByIdAsync(int id);
        Task<bool> UpdateUserAsync(int id, string fullName, string email);
        Task<bool> ChangePasswordAsync(int id, string currentPassword, string newPassword);

        // PORTEFEUILLES
        Task<List<Portfolio>> GetPortfoliosByUserAsync(int userId);
        Task<Portfolio?> GetPortfolioByIdAsync(int id);
        Task<Portfolio?> GetPortfolioDetailsAsync(int id);
        Task<Portfolio?> CreatePortfolioAsync(string name, string currency, int userId);
        Task<bool> UpdatePortfolioAsync(int id, string name, string currency);
        Task<bool> DeletePortfolioAsync(int id);

        // POSITIONS — clé composite (PortfolioId, AssetId)
        Task<List<Position>?> GetPositionsByPortfolioAsync(int portfolioId);
        Task<Position?> GetPositionByKeyAsync(int portfolioId, int assetId);
        Task<Position?> CreatePositionAsync(int portfolioId, int assetId,
                                                   decimal quantity, decimal avgBuyPrice,
                                                   DateTime buyDate);
        Task<bool> UpdatePositionAsync(int portfolioId, int assetId,
                                                   decimal quantity, decimal avgBuyPrice,
                                                   DateTime buyDate);
        Task<bool> DeletePositionAsync(int portfolioId, int assetId);

        // ACTIFS
        Task<List<Asset>> GetAllAssetsAsync();
        Task<Asset?> GetAssetByIdAsync(int id);
        Task<Asset?> GetAssetByTickerAsync(string ticker);
        Task<Asset?> ImportStockFromFmpAsync(string ticker);

        Task<Asset?> ImportBondFromFmpAsync(string ticker);

        Task<bool> DeleteAssetAsync(int id);

        // PRIX & TAUX
        Task<(decimal Price, decimal ChangePercent)> GetQuoteAsync(string ticker);
        Task<decimal> GetExchangeRateAsync(string from, string to);

        Task<decimal?> GetLatestPriceAsync(string ticker);

        // ANALYTICS — vrais types des modèles backend
        // MonteCarloRequest  : HorizonDays, NumSimulations
        // BacktestRequest    : From, To, BenchmarkTicker, Rebalancing
        // OptimizationRequest: From, To, Target (enum), RiskFreeRate
        // CompareRequest     : PortfolioIds, From, To
        Task<PortfolioAnalyticsResult?> AnalyzePortfolioAsync(
            int portfolioId, DateTime from, DateTime to, double riskFreeRate = 0.03);

        Task<MonteCarloResult?> RunMonteCarloAsync(
            int portfolioId, MonteCarloRequest request);

        Task<BacktestResult?> RunBacktestAsync(
            int portfolioId, BacktestRequest request);

        Task<OptimizationResult?> OptimizePortfolioAsync(
            int portfolioId, OptimizationRequest request);

        Task<PortfolioComparisonResult?> ComparePortfoliosAsync(CompareRequest request);
    }
}
