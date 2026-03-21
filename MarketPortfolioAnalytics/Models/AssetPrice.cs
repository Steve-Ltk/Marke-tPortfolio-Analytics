using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MarketPortfolioAnalytics.Models
{
    // Représente le prix de marché d'un actif à une date donnée.
    // Source des données : API FMP (Financial Modeling Prep).
    // Contrainte d'unicité : un seul enregistrement par (AssetId, Date).
    // Cela garantit qu'on ne peut pas importer deux fois le même prix
    // pour le même actif à la même date (contrôlé en base + dans le contrôleur).
    [Table("AssetPrice")]
    public class AssetPrice
    {
        [Key]
        public int Id { get; set; }

        // Référence vers l'actif dont c'est le prix.
        [Required]
        public int AssetId { get; set; }

        // Navigation vers l'actif.
        // JsonIgnore : évite la boucle AssetPrice → Asset → Prices → AssetPrice.
        [JsonIgnore]
        [ForeignKey(nameof(AssetId))]
        public virtual Asset? Asset { get; set; }

        // Date de cotation.
        // Stockée sans heure (normalisée en .Date à l'insertion).>
        [Required]
        public DateTime Date { get; set; }

        // OHLCV = Open, High, Low, Close, Volume
        // C'est le format standard des données de marché historiques.

        // Prix d'ouverture de la séance. Optionnel.
        [Column(TypeName = "decimal(18,6)")]
        public decimal? Open { get; set; }

        // Prix le plus haut de la séance. Optionnel.
        [Column(TypeName = "decimal(18,6)")]
        public decimal? High { get; set; }

        // Prix le plus bas de la séance. Optionnel.
        [Column(TypeName = "decimal(18,6)")]
        public decimal? Low { get; set; }

        // Prix de clôture. OBLIGATOIRE.
        // C'est le seul prix utilisé dans tous les calculs financiers (rendement, volatilité...).
        // decimal(18,6) : 18 chiffres significatifs, 6 après la virgule.
        // Adapté aux actions (ex: 182.5000), aux cryptos (ex: 0.000012) et aux obligations.
        [Required]
        [Column(TypeName = "decimal(18,6)")]
        public decimal Close { get; set; }

        // Volume échangé sur la séance. Optionnel.
        public long? Volume { get; set; }
    }
}
