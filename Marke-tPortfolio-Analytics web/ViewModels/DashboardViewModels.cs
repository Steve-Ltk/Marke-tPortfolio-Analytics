using MarketPortfolioAnalytics.Models;

// ViewModels du Dashboard — données préparées par DashboardController
// et consommées par Views/Dashboard/Index.cshtml
namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    // ViewModel principal du Dashboard
    // Contient toutes les données affichées sur la page d'accueil
    public class DashboardViewModel
    {
        public string UserName { get; set; } = string.Empty;

        // Valeur totale de tous les portefeuilles en EUR et USD
        public decimal ValeurTotaleEur { get; set; }
        public decimal ValeurTotaleUsd { get; set; }

        // Rendement total depuis les achats en %
        // ex: 12.5 = +12.5% depuis le premier achat
        public decimal RendementTotal { get; set; }

        // Métriques analytiques chargées depuis le backend Analytics
        // Moyenne pondérée par valeur de marché de chaque portefeuille
        // Valent 0 si pas assez d'historique (moins d'1 an)
        public decimal SharpeRatio { get; set; }
        public decimal MaxDrawdown { get; set; }

        // Taux EUR/USD récupéré depuis FMP -> utilisé pour les conversions
        public decimal TauxEurUsd { get; set; }

        // Score investisseur calculé dans DashboardController (0 à 100)
        public int ScoreInvestisseur { get; set; }
        public string ScoreNiveau { get; set; } = string.Empty;
        public string ScoreMessage { get; set; } = string.Empty;
        public List<string> ScorePills { get; set; } = new();

        // Données de navigation
        public List<Portfolio> Portfolios { get; set; } = new();
        public List<PositionDashboard> Positions { get; set; } = new();
        public List<AllocationItem> Allocation { get; set; } = new();

        // Propriétés calculées -> pas stockées, recalculées à chaque accès
        public int NbPortfolios => Portfolios.Count;
        public bool HasPortfolios => Portfolios.Any();

        // true si Sharpe ou MaxDrawdown sont non nuls -> on a des données analytiques à afficher
        // false si pas assez d'historique -> la vue affiche "—" au lieu de 0
        public bool HasMetriquesAnalytiques => SharpeRatio != 0 || MaxDrawdown != 0;

        public string ScoreCouleur => ScoreInvestisseur switch
        {
            < 40 => "var(--red)",
            < 70 => "var(--amber)",
            _ => "var(--green)"
        };

        // Décalage du trait SVG du cercle de progression
        // 213.6 = circonférence du cercle (2 × π × 34)
        // ScoreOffset = 0 -> cercle plein (score 100)
        // ScoreOffset = 213.6 -> cercle vide (score 0)
        public double ScoreOffset => 213.6 - 213.6 * ScoreInvestisseur / 100.0;
    }

    // Une position affichée dans le tableau du Dashboard
    // Version simplifiée de Position -> que ce dont la vue a besoin
    public class PositionDashboard
    {
        public string Ticker { get; set; } = string.Empty;
        public string Nom { get; set; } = string.Empty;
        public decimal Quantite { get; set; }
        public decimal AvgBuyPrice { get; set; }   
        public decimal PrixActuel { get; set; }
        public decimal ValeurEur { get; set; }
        public decimal PnlPct { get; set; }
        public decimal Poids { get; set; }
        public string Devise { get; set; } = "USD";
        public string TypeActif { get; set; } = "Stock";
    }

    // Un actif dans le graphique donut d'allocation
    public class AllocationItem
    {
        public string Ticker { get; set; } = string.Empty;
        public decimal Poids { get; set; }
        public string Couleur { get; set; } = "#00d084";
    }
}
