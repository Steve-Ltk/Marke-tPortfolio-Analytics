using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace MarketPortfolioAnalytics.Models
{
    /// <summary>
    /// Représente un utilisateur de la plateforme.
    /// Un utilisateur peut posséder plusieurs portefeuilles.
    /// </summary>
    [Table("AppUser")]
    public class AppUser
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Nom complet de l'utilisateur.</summary>
        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = null!;

        /// <summary>
        /// Email unique — sert d'identifiant de connexion.
        /// L'unicité est enforced en base via un index unique dans le DbContext.
        /// </summary>
        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string Email { get; set; } = null!;

        /// <summary>
        /// Rôle de l'utilisateur : "User" (défaut) ou "Admin".
        /// Contrôlé dans le contrôleur — jamais modifiable librement par le client.
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Role { get; set; } = "User";

        /// <summary>
        /// Indique si le compte est actif.
        /// On ne supprime jamais un utilisateur en base (soft delete).
        /// IsActive = false → le compte est désactivé.
        /// </summary>
        [Required]
        public bool IsActive { get; set; } = true;

        /// <summary>Date de création du compte (UTC, automatique).</summary>
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Hash bcrypt du mot de passe.
        /// JsonIgnore : jamais sérialisé ni retourné au client.
        /// ValidateNever : ASP.NET ne le valide pas à la réception (il est vide à ce stade).
        /// </summary>
        [JsonIgnore]
        [ValidateNever]
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>Date de la dernière modification du mot de passe.</summary>
        public DateTime? PasswordUpdatedAt { get; set; }

        /// <summary>
        /// Mot de passe en clair — reçu uniquement lors de la création ou du changement.
        /// NotMapped : aucune colonne en base.
        /// Ignoré en sérialisation JSON si null.
        /// </summary>
        [NotMapped]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Password { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        /// <summary>
        /// Liste des portefeuilles appartenant à cet utilisateur.
        /// JsonIgnore : évite la boucle AppUser → Portfolio → AppUser.
        /// </summary>
        [JsonIgnore]
        public virtual ICollection<Portfolio>? ListePortfolios { get; set; }
    }
}
