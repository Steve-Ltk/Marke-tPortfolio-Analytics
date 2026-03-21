using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MarketPortfolioAnalytics.Models
{
    // Ces 3 attributs permettent à .NET de savoir si le JSON représente
    // un Stock ou un Bond quand il reçoit/envoie des données.
    // Le champ "assetType" dans le JSON joue le rôle de discriminateur.
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "assetType")]
    [JsonDerivedType(typeof(Stock), "Stock")]
    [JsonDerivedType(typeof(Bond), "Bond")]
    [Table("Asset")]

    // Fiche de base commune à tous les actifs financiers.
    // Stock et Bond héritent de cette classe et ajoutent leurs infos spécifiques.
    // EF crée une table "Asset" en base avec ces colonnes communes.
    public class Asset
    {
        [Key]
        public int Id { get; set; } // le numéro unique de la fiche

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = null!; // "Apple Inc."

        [Required]
        [MaxLength(20)]
        public string Ticker { get; set; } = null!; // "AAPL" — le surnom boursier

       
        [MaxLength(50)]
        public string? Exchange { get; set; } // "NASDAQ" — où il est coté

        [Required]
        [MaxLength(3)]
        public string Currency { get; set; } = null!; // "USD" — dans quelle devise

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // quand on l'a ajouté

        // [JsonIgnore] : l'historique de prix n'est jamais inclus quand on
        // retourne un actif en JSON. Trop lourd (des centaines de lignes).
        // On les récupère séparément via /api/AssetPrices/by-asset/{id}.
        public virtual ICollection<AssetPrice>? Prices { get; set; }
    }

    // Action boursière. Hérite d'Asset.
    // EF crée une table "Stock" avec juste Sector et ISIN.
    // Pour avoir un Stock complet, EF joint la table Asset + Stock automatiquement.
    [Table("Stock")]
    public class Stock : Asset
    {
        [MaxLength(100)]
        public string? Sector { get; set; }

        [MaxLength(12)]
        public string? ISIN { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════

    // Obligation financière. Hérite d'Asset.
    // Ajoute le taux du coupon (ex: 3.125%) et la date d'échéance.
    // Pas de prix temps réel FMP sur plan gratuit — on stocke juste les métadonnées.
    [Table("Bond")]
    public class Bond : Asset
    {
        public DateTime? MaturityDate { get; set; }

        // Taux du coupon en pourcentage (ex: 4.35 = 4.35% par an). Optionnel.
        // decimal(6,4) : jusqu'à 99.9999%, précision 4 décimales.
        [Column(TypeName = "decimal(6,4)")]
        public decimal? CouponRate { get; set; }
    }
}
