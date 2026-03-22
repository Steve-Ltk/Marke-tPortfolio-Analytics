using System.ComponentModel.DataAnnotations;
using MarketPortfolioAnalytics.Models;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
     // ViewModel de la liste des portefeuilles
    public class PortfolioIndexViewModel
    {
        public List<PortfolioCard> Portfolios { get; set; } = new();
        public decimal ValeurTotaleEur { get; set; }
        public bool HasPortfolios => Portfolios.Any();
    }

    // Une carte portefeuille dans la liste
    public class PortfolioCard
    {
        public Portfolio Portfolio { get; set; } = null!;
        public decimal ValeurEur { get; set; }
        public decimal RendementPct { get; set; }
        public int NbPositions { get; set; }

        // Couleur et signe du rendement -> vert si positif, rouge si négatif
        public string RendementCouleur => RendementPct >= 0 ? "var(--green)" : "var(--red)";
        public string RendementSigne => RendementPct >= 0 ? "▲" : "▼";
    }

    // ViewModel du détail d'un portefeuille avec ses positions
    public class PortfolioDetailsViewModel
    {
        public Portfolio Portfolio { get; set; } = null!;
        public List<PositionDetail> Positions { get; set; } = new();
        public decimal ValeurTotaleEur { get; set; }
        public decimal TauxEurUsd { get; set; }
        public bool HasPositions => Positions.Any();
    }

     // Détail d'une position dans un portefeuille
    // Enrichit Position avec les données calculées (valeur, P&L, poids...)
    public class PositionDetail
    {
        public Position Position { get; set; } = null!;
        public string Ticker { get; set; } = string.Empty;
        public string NomActif { get; set; } = string.Empty;
        public string TypeActif { get; set; } = "Stock";
        public decimal PrixActuel { get; set; }
        public decimal ValeurEur { get; set; }
        public decimal PnlPct { get; set; }
        public decimal PnlEur { get; set; }
        public decimal Poids { get; set; }
        public string Devise { get; set; } = "USD";

        // Couleur et signe du P&L -> vert si gain, rouge si perte
        public string PnlCouleur => PnlPct >= 0 ? "var(--green)" : "var(--red)";
        public string PnlSigne => PnlPct >= 0 ? "+" : "";
    }

    // ViewModel du formulaire de création de portefeuille
    public class PortfolioCreateViewModel
    {
        [Required(ErrorMessage = "Le nom est obligatoire.")]
        [MaxLength(100)]
        [Display(Name = "Nom du portefeuille")]
        public string Name { get; set; } = string.Empty;

        // Fixé à EUR en V1 -> l'user ne choisit plus la devise
        [Required]
        [Display(Name = "Devise de référence")]
        public string Currency { get; set; } = "EUR";
    }

   // ViewModel du formulaire de modification de portefeuille
    public class PortfolioEditViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Le nom est obligatoire.")]
        [MaxLength(100)]
        [Display(Name = "Nom du portefeuille")]
        public string Name { get; set; } = string.Empty;

        // Fixé à EUR en V1 -> même logique que PortfolioCreateViewModel
        [Required]
        [Display(Name = "Devise de référence")]
        public string Currency { get; set; } = "EUR";
    }
}
