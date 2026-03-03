using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MarketPortfolioAnalytics.Models
{
    /// <summary>
    /// Classe mère représentant un instrument financier coté en bourse.
    /// Un actif peut être présent dans plusieurs portefeuilles via des positions.
    /// Un actif possède un historique de prix (AssetPrice).
    ///
    /// Héritage TPT (Table Per Type) :
    ///   - La table "Asset" contient les colonnes communes à tous les actifs.
    ///   - La table "Stock" ne contient que les colonnes propres aux actions.
    ///   - La table "Bond"  ne contient que les colonnes propres aux obligations.
    ///   EF fait automatiquement la jointure entre ces tables.
    ///
    /// JsonPolymorphic : permet à ASP.NET de sérialiser/désérialiser correctement
    /// un Stock ou un Bond quand on travaille avec une référence de type Asset.
    /// Le champ "assetType" apparaît dans le JSON pour indiquer le type réel.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "assetType")]
    [JsonDerivedType(typeof(Stock), "Stock")]
    [JsonDerivedType(typeof(Bond), "Bond")]
    [Table("Asset")]
    public class Asset
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Nom complet de l'instrument (ex: "Apple Inc.").</summary>
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = null!;

        /// <summary>
        /// Symbole boursier unique (ex: "AAPL", "MC.PA").
        /// Normalisé en majuscules à l'insertion.
        /// L'unicité est enforced en base via un index unique dans le DbContext.
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Ticker { get; set; } = null!;

        /// <summary>Place de cotation (ex: "NASDAQ", "EURONEXT"). Optionnel.</summary>
        [MaxLength(50)]
        public string? Exchange { get; set; }

        /// <summary>
        /// Devise de cotation de l'actif — code ISO 4217, 3 lettres (ex: "USD", "EUR").
        /// Attention : peut différer de la devise du portefeuille.
        /// </summary>
        [Required]
        [MaxLength(3)]
        public string Currency { get; set; } = null!;

        /// <summary>Date d'ajout de l'actif dans la plateforme (UTC, automatique).</summary>
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ── Navigation ────────────────────────────────────────────────────────
        /// <summary>
        /// Historique des prix de marché de cet actif.
        /// JsonIgnore : on ne retourne pas tout l'historique quand on consulte un actif.
        /// </summary>
        [JsonIgnore]
        public virtual ICollection<AssetPrice>? Prices { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Action (equity) — hérite de Asset.
    /// La table "Stock" en base ne contient que Sector et ISIN.
    /// EF jointure automatiquement avec la table "Asset" sur l'Id.
    /// </summary>
    [Table("Stock")]
    public class Stock : Asset
    {
        /// <summary>Secteur d'activité (ex: "Technology", "Healthcare"). Optionnel.</summary>
        [MaxLength(100)]
        public string? Sector { get; set; }

        /// <summary>
        /// Code ISIN — identifiant international de l'instrument (norme ISO 6166).
        /// Toujours 12 caractères (ex: "US0378331005"). Optionnel.
        /// </summary>
        [MaxLength(12)]
        public string? ISIN { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Obligation (bond) — hérite de Asset.
    /// La table "Bond" en base ne contient que MaturityDate et CouponRate.
    /// EF jointure automatiquement avec la table "Asset" sur l'Id.
    /// </summary>
    [Table("Bond")]
    public class Bond : Asset
    {
        /// <summary>Date d'échéance de l'obligation (maturité). Optionnelle.</summary>
        public DateTime? MaturityDate { get; set; }

        /// <summary>
        /// Taux du coupon en pourcentage (ex: 4.35 = 4.35% par an). Optionnel.
        /// decimal(6,4) : jusqu'à 99.9999%, précision 4 décimales.
        /// </summary>
        [Column(TypeName = "decimal(6,4)")]
        public decimal? CouponRate { get; set; }
    }
}
