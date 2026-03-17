using MarketPortfolioAnalytics.Models;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    public class DashboardViewModel
    {
        public string UserName { get; set; } = string.Empty;
        public decimal ValeurTotaleEur { get; set; }
        public decimal ValeurTotaleUsd { get; set; }
        public decimal RendementTotal { get; set; }
        public decimal SharpeRatio { get; set; }
        public decimal MaxDrawdown { get; set; }
        public decimal TauxEurUsd { get; set; }

        public int ScoreInvestisseur { get; set; }
        public string ScoreNiveau { get; set; } = string.Empty;
        public string ScoreMessage { get; set; } = string.Empty;
        public List<string> ScorePills { get; set; } = new();

        public List<Portfolio> Portfolios { get; set; } = new();
        public List<PositionDashboard> Positions { get; set; } = new();
        public List<AllocationItem> Allocation { get; set; } = new();

        public int NbPortfolios => Portfolios.Count;
        public bool HasPortfolios => Portfolios.Any();

        public string ScoreCouleur => ScoreInvestisseur switch
        {
            < 40 => "var(--red)",
            < 70 => "var(--amber)",
            _ => "var(--green)"
        };

        public double ScoreOffset => 213.6 - 213.6 * ScoreInvestisseur / 100.0;
    }

    public class PositionDashboard
    {
        public string Ticker { get; set; } = string.Empty;
        public string Nom { get; set; } = string.Empty;
        public decimal Quantite { get; set; }
        public decimal AvgBuyPrice { get; set; }   // ✅ Position.AvgBuyPrice
        public decimal PrixActuel { get; set; }
        public decimal ValeurEur { get; set; }
        public decimal PnlPct { get; set; }
        public decimal Poids { get; set; }
        public string Devise { get; set; } = "USD";
        public string TypeActif { get; set; } = "Stock"; // ✅ via AssetHelper
    }

    public class AllocationItem
    {
        public string Ticker { get; set; } = string.Empty;
        public decimal Poids { get; set; }
        public string Couleur { get; set; } = "#00d084";
    }
}