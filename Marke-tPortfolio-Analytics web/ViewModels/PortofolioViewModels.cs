using System.ComponentModel.DataAnnotations;
using MarketPortfolioAnalytics.Models;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    // ════════════════════════════════════════════════════════════════════
    // LISTE
    // ════════════════════════════════════════════════════════════════════

    public class PortfolioIndexViewModel
    {
        public List<PortfolioCard> Portfolios { get; set; } = new();
        public decimal ValeurTotaleEur { get; set; }
        public bool HasPortfolios => Portfolios.Any();
    }

    public class PortfolioCard
    {
        public Portfolio Portfolio { get; set; } = null!;
        public decimal ValeurEur { get; set; }
        public decimal RendementPct { get; set; }
        public int NbPositions { get; set; }

        public string RendementCouleur => RendementPct >= 0 ? "var(--green)" : "var(--red)";
        public string RendementSigne => RendementPct >= 0 ? "▲" : "▼";
    }

    // ════════════════════════════════════════════════════════════════════
    // DÉTAIL
    // ════════════════════════════════════════════════════════════════════

    public class PortfolioDetailsViewModel
    {
        public Portfolio Portfolio { get; set; } = null!;
        public List<PositionDetail> Positions { get; set; } = new();
        public decimal ValeurTotaleEur { get; set; }
        public decimal TauxEurUsd { get; set; }
        public bool HasPositions => Positions.Any();
    }

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

        public string PnlCouleur => PnlPct >= 0 ? "var(--green)" : "var(--red)";
        public string PnlSigne => PnlPct >= 0 ? "+" : "";
    }

    // ════════════════════════════════════════════════════════════════════
    // CRÉATION — sans Description (Portfolio n'a pas ce champ)
    // ════════════════════════════════════════════════════════════════════

    public class PortfolioCreateViewModel
    {
        [Required(ErrorMessage = "Le nom est obligatoire.")]
        [MaxLength(100)]
        [Display(Name = "Nom du portefeuille")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Devise de référence")]
        public string Currency { get; set; } = "EUR";
    }

    // ════════════════════════════════════════════════════════════════════
    // ÉDITION — sans Description
    // ════════════════════════════════════════════════════════════════════

    public class PortfolioEditViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Le nom est obligatoire.")]
        [MaxLength(100)]
        [Display(Name = "Nom du portefeuille")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Devise de référence")]
        public string Currency { get; set; } = "EUR";
    }
}