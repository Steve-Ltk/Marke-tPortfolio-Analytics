using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MarketPortfolioAnalytics.Models
{
    // Table d'association entre Portfolio et Asset.
    // La clé primaire est composite (PortfolioId + AssetId) :
    // un actif ne peut apparaître qu'une seule fois par portefeuille
    [Table("Position")]
    public class Position
    {
        // Clé composite (PortfolioId, AssetId)

        //Référence vers le portefeuille.
        public int PortfolioId { get; set; }

        // Navigation vers le portefeuille.
        // JsonIgnore : quand on retourne une Position, on n'a pas besoin
        // de re-sérialiser tout le portefeuille (qui contient déjà cette position).
        [JsonIgnore]
        [ForeignKey(nameof(PortfolioId))]
        public virtual Portfolio? Portfolio { get; set; }

        // Référence vers l'actif détenu.
        public int AssetId { get; set; }

        // Navigation vers l'actif.
        // Inclus dans la réponse : utile pour afficher Ticker, Name, Currency
        // sans faire une requête séparée.
        [ForeignKey(nameof(AssetId))]
        public virtual Asset? Asset { get; set; }

        // Nombre d'unités détenues.
        // decimal pour supporter les fractions (ex: 0.5 Bitcoin, 1000 obligations).
        // Doit être strictement positif.
        [Required]
        [Column(TypeName = "decimal(18,6)")]
        public decimal Quantity { get; set; }

        // Prix moyen d'achat par unité.
        // Sert à calculer la plus-value latente : PnL = (PrixActuel - AvgBuyPrice) × Quantity.
        // Doit être strictement positif.
        [Required]
        [Column(TypeName = "decimal(18,6)")]
        public decimal AvgBuyPrice { get; set; }

        // Date du premier achat de cet actif dans ce portefeuille.
        // Sert de référence pour les calculs de performance sur période.
        [Required]
        public DateTime BuyDate { get; set; }

        // Date de création de l'enregistrement (UTC, automatique).
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
