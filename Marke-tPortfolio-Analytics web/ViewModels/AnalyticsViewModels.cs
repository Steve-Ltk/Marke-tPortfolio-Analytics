using MarketPortfolioAnalytics.Models;
using MarketPortfolioAnalytics.Models.Analytics;
using static Marke_tPortfolio_Analytics_web.Helpers.InsightHelper;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    // ════════════════════════════════════════════════════════════════════
    // ANALYSE PRINCIPALE
    // PortfolioAnalyticsResult contient :
    //   AnnualizedReturn, Volatility, SharpeRatio, MaxDrawdown,
    //   TotalReturnPct, TotalPnL, TotalCurrentValue, TotalCostBasis
    // Il N'a PAS : Alpha, Beta, VaR95, CalmarRatio (disponibles dans BacktestResult)
    // ════════════════════════════════════════════════════════════════════

    public class AnalyticsIndexViewModel
    {
        public List<Portfolio> Portfolios { get; set; } = new();
        public int SelectedPortfolioId { get; set; }
        public string SelectedPortfolioName { get; set; } = string.Empty;
        public DateTime DateDebut { get; set; } = DateTime.UtcNow.AddYears(-1);
        public DateTime DateFin { get; set; } = DateTime.UtcNow;
        public double TauxSansRisque { get; set; } = 4.5;
        public bool HasResult => Analyse != null;

        public PortfolioAnalyticsResult? Analyse { get; set; }

        // Insights — basés sur les champs réels de PortfolioAnalyticsResult
        public InsightResult? InsightSharpe { get; set; }
        public InsightResult? InsightVolatilite { get; set; }  // Volatility * 100
        public InsightResult? InsightDrawdown { get; set; }  // MaxDrawdown * 100
        public InsightResult? InsightCagr { get; set; }  // AnnualizedReturn * 100

        public string InsightGlobal { get; set; } = string.Empty;
        public string InsightNiveau { get; set; } = string.Empty;
    }
}
