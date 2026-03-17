using System.ComponentModel.DataAnnotations;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    // ════════════════════════════════════════════════════════════════════
    // CRÉATION
    // ════════════════════════════════════════════════════════════════════

    public class PositionCreateViewModel
    {
        public int PortfolioId { get; set; }
        public string PortfolioName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Sélectionnez un actif.")]
        [Display(Name = "Actif")]
        public int AssetId { get; set; }

        [Required(ErrorMessage = "La quantité est obligatoire.")]
        [Range(0.0001, double.MaxValue, ErrorMessage = "La quantité doit être supérieure à 0.")]
        [Display(Name = "Quantité")]
        public decimal Quantity { get; set; }

        [Required(ErrorMessage = "Le prix d'achat est obligatoire.")]
        [Range(0.0001, double.MaxValue, ErrorMessage = "Le prix doit être supérieur à 0.")]
        [Display(Name = "Prix d'achat moyen")]
        public decimal PurchasePrice { get; set; }

        [Required(ErrorMessage = "La date d'achat est obligatoire.")]
        [Display(Name = "Date d'achat")]
        [DataType(DataType.Date)]
        public DateTime PurchaseDate { get; set; } = DateTime.Today;

        // Pour le select d'actifs
        public List<AssetSelectItem> Assets { get; set; } = new();
    }

    // ════════════════════════════════════════════════════════════════════
    // ÉDITION
    // ════════════════════════════════════════════════════════════════════

    public class PositionEditViewModel
    {
        public int Id { get; set; }
        public int PortfolioId { get; set; }
        public string PortfolioName { get; set; } = string.Empty;
        public string AssetTicker { get; set; } = string.Empty;
        public string AssetNom { get; set; } = string.Empty;

        [Required(ErrorMessage = "La quantité est obligatoire.")]
        [Range(0.0001, double.MaxValue, ErrorMessage = "La quantité doit être supérieure à 0.")]
        [Display(Name = "Quantité")]
        public decimal Quantity { get; set; }

        [Required(ErrorMessage = "Le prix d'achat est obligatoire.")]
        [Range(0.0001, double.MaxValue, ErrorMessage = "Le prix doit être supérieur à 0.")]
        [Display(Name = "Prix d'achat moyen")]
        public decimal PurchasePrice { get; set; }

        [Required(ErrorMessage = "La date d'achat est obligatoire.")]
        [Display(Name = "Date d'achat")]
        [DataType(DataType.Date)]
        public DateTime PurchaseDate { get; set; }
    }

    // ── Item pour le <select> d'actifs ────────────────────────────────
    public class AssetSelectItem
    {
        public int Id { get; set; }
        public string Ticker { get; set; } = string.Empty;
        public string Nom { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "Stock" | "Bond"
    }
}
