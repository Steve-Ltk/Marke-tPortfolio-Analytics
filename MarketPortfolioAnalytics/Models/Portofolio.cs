using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MarketPortfolioAnalytics.Models
{
    /// <summary>
    /// Représente un portefeuille financier appartenant à un utilisateur.
    ///
    /// Un portefeuille :
    ///   - appartient à un seul AppUser
    ///   - contient plusieurs actifs (Asset) via la table d'association Position
    ///
    /// La relation Portfolio ↔ Asset est de type Many-to-Many.
    /// Position est la table d'association qui porte les informations
    /// supplémentaires (quantité, prix d'achat, date d'achat).
    /// </summary>
    [Table("Portfolio")]
    public class Portfolio
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Nom du portefeuille (ex: "Mon portefeuille tech").</summary>
        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        /// <summary>
        /// Devise de référence du portefeuille — code ISO 4217, 3 lettres.
        /// Défaut : "EUR".
        /// Note : les actifs peuvent être dans une devise différente.
        /// La conversion multidevise est une évolution future.
        /// </summary>
        [Required]
        [MaxLength(3)]
        public string Currency { get; set; } = "EUR";

        /// <summary>Date de création du portefeuille (UTC, automatique).</summary>
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ── Lien vers le propriétaire ─────────────────────────────────────────

        /// <summary>Identifiant de l'utilisateur propriétaire.</summary>
        [Required]
        public int UserId { get; set; }

        /// <summary>
        /// Navigation vers l'utilisateur propriétaire.
        /// JsonIgnore : évite la boucle Portfolio → AppUser → ListePortfolios → Portfolio.
        /// </summary>
        [JsonIgnore]
        [ForeignKey(nameof(UserId))]
        public virtual AppUser? User { get; set; }

        // ── Navigation vers les positions ─────────────────────────────────────

        /// <summary>
        /// Liste des positions détenues dans ce portefeuille.
        /// Chaque position représente un actif avec sa quantité et son prix d'achat.
        /// Incluse dans la réponse uniquement sur GET /details.
        /// </summary>
        public virtual ICollection<Position>? ListePositions { get; set; }
    }
}
