using System.ComponentModel.DataAnnotations;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    // ViewModel du formulaire d'ajout de position
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

        [Required(ErrorMessage = "Le prix d'achat est obligatoire.")]
        [Range(0.0001, double.MaxValue, ErrorMessage = "Le prix doit être > 0.")]
        [Display(Name = "Prix d'achat moyen")]
        public decimal AvgBuyPrice { get; set; }

        /// AvgBuyPrice -> prix moyen d'achat en devise NATIVE de l'actif
        // Stocké tel quel en base -> la conversion EUR se fait à l'affichage
        [Required(ErrorMessage = "La date d'achat est obligatoire.")]
        [Display(Name = "Date d'achat")]
        [DataType(DataType.Date)]
        public DateTime BuyDate { get; set; } = DateTime.Today;
        
        // Liste des actifs pour le menu déroulant du formulaire
        // Chargée dans PositionsController.Create (GET)
        public List<AssetSelectItem> Assets { get; set; } = new();
    }

    // ViewModel du formulaire de modification de position
    // Clé composite (PortfolioId + AssetId) -> pas d'Id simple
    public class PositionEditViewModel
    {
        public int PortfolioId { get; set; }
        public int AssetId { get; set; }
        
        // Infos d'affichage -> pas modifiables dans ce formulaire
        public string PortfolioName { get; set; } = string.Empty;
        public string AssetTicker { get; set; } = string.Empty;
        public string AssetNom { get; set; } = string.Empty;

        [Required(ErrorMessage = "La quantité est obligatoire.")]
        [Range(0.0001, double.MaxValue, ErrorMessage = "La quantité doit être > 0.")]
        [Display(Name = "Quantité")]
        public decimal Quantity { get; set; }

        [Required(ErrorMessage = "Le prix d'achat est obligatoire.")]
        [Range(0.0001, double.MaxValue, ErrorMessage = "Le prix doit être > 0.")]
        [Display(Name = "Prix d'achat moyen")]
        public decimal AvgBuyPrice { get; set; }

        [Required(ErrorMessage = "La date d'achat est obligatoire.")]
        [Display(Name = "Date d'achat")]
        [DataType(DataType.Date)]
        public DateTime BuyDate { get; set; }
    }

    // Item de sélection dans le menu déroulant de création de position
    // Version allégée d'Asset -> que les champs nécessaires pour le <select>
    public class AssetSelectItem
    {
        public int Id { get; set; }
        public string Ticker { get; set; } = string.Empty;
        public string Nom { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}
