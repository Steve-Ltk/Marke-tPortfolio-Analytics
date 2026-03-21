using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace MarketPortfolioAnalytics.Models
{
    // Représente un utilisateur de la plateforme.
    // Un utilisateur peut posséder plusieurs portefeuilles.
    [Table("AppUser")]
    public class AppUser
    {
        [Key]
        public int Id { get; set; }

        // Nom complet de l'utilisateur
        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = null!;

        // Email unique — sert d'identifiant de connexion.
        [Required]
        [EmailAddress]
        [MaxLength(256)]
        public string Email { get; set; } = null!;

        // Rôle de l'utilisateur : "User" (défaut) ou "Admin".
        // Contrôlé dans le contrôleur — jamais modifiable librement par le client.
        [Required]
        [MaxLength(20)]
        public string Role { get; set; } = "User";


        // Indique si le compte est actif.
        // On ne supprime jamais un utilisateur en base (soft delete).
        // IsActive = false -> le compte est désactivé.
        [Required]
        public bool IsActive { get; set; } = true;

        //Date de création du compte (UTC, automatique).
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // PasswordHash : mot de passe transformé en hash illisible via PasswordHasher.
        // [JsonIgnore] : ne part JAMAIS dans une réponse JSON. Sécurité absolue.
        // ValidateNever : ASP.NET ne le valide pas à la réception (il est vide à ce stade).
        [JsonIgnore]
        [ValidateNever]
        public string PasswordHash { get; set; } = string.Empty;

        // <summary>Date de la dernière modification du mot de passe.
        public DateTime? PasswordUpdatedAt { get; set; }

        // Mot de passe en clair — reçu uniquement lors de la création ou du changement.
        // NotMapped : aucune colonne en base.
        // Ignoré en sérialisation JSON si null.
        [NotMapped]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Password { get; set; }

        // [JsonIgnore] sur Asset : évite la boucle AssetPrice -> Asset -> Prices -> AssetPrice.
        [JsonIgnore]
        public virtual ICollection<Portfolio>? ListePortfolios { get; set; }
    }
}
