using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MarketPortfolioAnalytics.Models
{
    /// <summary>
    /// Table d'association entre Portfolio et Asset.
    ///
    /// Répond à la question : "Combien d'unités de CET actif
    /// sont détenues dans CE portefeuille, et à quel prix ont-elles été achetées ?"
    ///
    /// Clé primaire composite : (PortfolioId, AssetId)
    ///   → Un même actif ne peut apparaître qu'une seule fois dans un portefeuille.
    ///   → Si l'utilisateur rachète le même actif, il met à jour Quantity et AvgBuyPrice.
    ///
    /// Relation :
    ///   Portfolio  1 ────── * Position * ────── 1  Asset
    ///   (un portefeuille    (table d'association)  (un actif peut être
    ///   a plusieurs                                dans plusieurs portefeuilles)
    ///   positions)
    /// </summary>
    [Table("Position")]
    public class Position
    {
        // ── Clé composite (PortfolioId, AssetId) ─────────────────────────────
        // Configurée en Fluent API dans le DbContext.
        // Les deux colonnes servent à la fois de PK et de FK.

        /// <summary>Référence vers le portefeuille.</summary>
        public int PortfolioId { get; set; }

        /// <summary>
        /// Navigation vers le portefeuille.
        /// JsonIgnore : quand on retourne une Position, on n'a pas besoin
        /// de re-sérialiser tout le portefeuille (qui contient déjà cette position).
        /// </summary>
        [JsonIgnore]
        [ForeignKey(nameof(PortfolioId))]
        public virtual Portfolio? Portfolio { get; set; }

        /// <summary>Référence vers l'actif détenu.</summary>
        public int AssetId { get; set; }

        /// <summary>
        /// Navigation vers l'actif.
        /// Inclus dans la réponse : utile pour afficher Ticker, Name, Currency
        /// sans faire une requête séparée.
        /// </summary>
        [ForeignKey(nameof(AssetId))]
        public virtual Asset? Asset { get; set; }

        // ── Données de la position ────────────────────────────────────────────

        /// <summary>
        /// Nombre d'unités détenues.
        /// decimal pour supporter les fractions (ex: 0.5 Bitcoin, 1000 obligations).
        /// Doit être strictement positif.
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(18,6)")]
        public decimal Quantity { get; set; }

        /// <summary>
        /// Prix moyen d'achat par unité.
        /// Sert à calculer la plus-value latente : PnL = (PrixActuel - AvgBuyPrice) × Quantity.
        /// Doit être strictement positif.
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(18,6)")]
        public decimal AvgBuyPrice { get; set; }

        /// <summary>
        /// Date du premier achat de cet actif dans ce portefeuille.
        /// Sert de référence pour les calculs de performance sur période.
        /// </summary>
        [Required]
        public DateTime BuyDate { get; set; }

        /// <summary>Date de création de l'enregistrement (UTC, automatique).</summary>
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
