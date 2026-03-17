using MarketPortfolioAnalytics.Models;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    /// <summary>
    /// Toutes les données nécessaires pour afficher le Dashboard.
    /// Calculées côté serveur dans DashboardController à partir des données API.
    /// </summary>
    public class DashboardViewModel
    {
        // ── Identité ──────────────────────────────────────────────────────
        public string UserName { get; set; } = string.Empty;

        // ── KPIs principaux ───────────────────────────────────────────────
        public decimal ValeurTotaleEur { get; set; }
        public decimal ValeurTotaleUsd { get; set; }
        public decimal RendementTotal { get; set; }   // en %
        public decimal RendementMensuel { get; set; }   // en %
        public decimal SharpeRatio { get; set; }
        public decimal MaxDrawdown { get; set; }   // en % (valeur négative)
        public decimal TauxEurUsd { get; set; }

        // ── Score investisseur (0-100) ────────────────────────────────────
        public int ScoreInvestisseur { get; set; }
        public string ScoreNiveau { get; set; } = string.Empty; // "Danger","Insuffisant","Bon","Excellent"
        public string ScoreMessage { get; set; } = string.Empty;
        public List<string> ScorePills { get; set; } = new();

        // ── Portefeuilles ─────────────────────────────────────────────────
        public List<Portfolio> Portfolios { get; set; } = new();
        public int NbPortfolios => Portfolios.Count;
        public bool HasPortfolios => Portfolios.Any();

        // ── Positions (toutes confondues, pour la table) ──────────────────
        public List<PositionDashboard> Positions { get; set; } = new();

        // ── Allocation (donut) ────────────────────────────────────────────
        public List<AllocationItem> Allocation { get; set; } = new();

        // ── Sparkline performances (12 points) ───────────────────────────
        public List<decimal> PerformanceCourbe { get; set; } = new();
        public List<decimal> BenchmarkCourbe { get; set; } = new();

        // ── Helpers couleur score ─────────────────────────────────────────
        public string ScoreCouleur => ScoreInvestisseur switch
        {
            < 40 => "var(--red)",
            < 70 => "var(--amber)",
            _ => "var(--green)"
        };

        // Calcule le stroke-dashoffset pour l'anneau SVG (cercle r=34, périmètre=213.6)
        public double ScoreOffset => 213.6 - (213.6 * ScoreInvestisseur / 100.0);
    }

    public class PositionDashboard
    {
        public string Ticker { get; set; } = string.Empty;
        public string Nom { get; set; } = string.Empty;
        public decimal Quantite { get; set; }
        public decimal PrixMoyen { get; set; }
        public decimal PrixActuel { get; set; }
        public decimal ValeurEur { get; set; }
        public decimal PnlPct { get; set; }  // en %
        public decimal Poids { get; set; }  // en % du total
        public string Devise { get; set; } = "USD";
        public string Type { get; set; } = "Stock"; // Stock | Bond
    }

    public class AllocationItem
    {
        public string Ticker { get; set; } = string.Empty;
        public decimal Poids { get; set; }  // en %
        public string Couleur { get; set; } = "#00d084";
    }
}
