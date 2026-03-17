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
        [Range(0.0001, double.MaxValue, ErrorMessage = "La quantité doit être > 0.")]
        [Display(Name = "Quantité")]
        public decimal Quantity { get; set; }

        /// <summary>✅ Correspond à Position.AvgBuyPrice</summary>
        [Required(ErrorMessage = "Le prix d'achat est obligatoire.")]
        [Range(0.0001, double.MaxValue, ErrorMessage = "Le prix doit être > 0.")]
        [Display(Name = "Prix d'achat moyen")]
        public decimal AvgBuyPrice { get; set; }

        /// <summary>✅ Correspond à Position.BuyDate</summary>
        [Required(ErrorMessage = "La date d'achat est obligatoire.")]
        [Display(Name = "Date d'achat")]
        [DataType(DataType.Date)]
        public DateTime BuyDate { get; set; } = DateTime.Today;

        public List<AssetSelectItem> Assets { get; set; } = new();
    }

    // ════════════════════════════════════════════════════════════════════
    // ÉDITION — clé composite (PortfolioId + AssetId), PAS d'Id simple
    // ════════════════════════════════════════════════════════════════════

    public class PositionEditViewModel
    {
        /// <summary>✅ 1ère partie de la clé composite</summary>
        public int PortfolioId { get; set; }
        /// <summary>✅ 2ème partie de la clé composite</summary>
        public int AssetId { get; set; }

        public string PortfolioName { get; set; } = string.Empty;
        public string AssetTicker { get; set; } = string.Empty;
        public string AssetNom { get; set; } = string.Empty;

        [Required(ErrorMessage = "La quantité est obligatoire.")]
        [Range(0.0001, double.MaxValue, ErrorMessage = "La quantité doit être > 0.")]
        [Display(Name = "Quantité")]
        public decimal Quantity { get; set; }

        /// <summary>✅ Correspond à Position.AvgBuyPrice</summary>
        [Required(ErrorMessage = "Le prix d'achat est obligatoire.")]
        [Range(0.0001, double.MaxValue, ErrorMessage = "Le prix doit être > 0.")]
        [Display(Name = "Prix d'achat moyen")]
        public decimal AvgBuyPrice { get; set; }

        /// <summary>✅ Correspond à Position.BuyDate</summary>
        [Required(ErrorMessage = "La date d'achat est obligatoire.")]
        [Display(Name = "Date d'achat")]
        [DataType(DataType.Date)]
        public DateTime BuyDate { get; set; }
    }

    // ── Select actifs ─────────────────────────────────────────────────
    public class AssetSelectItem
    {
        public int Id { get; set; }
        public string Ticker { get; set; } = string.Empty;
        public string Nom { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}