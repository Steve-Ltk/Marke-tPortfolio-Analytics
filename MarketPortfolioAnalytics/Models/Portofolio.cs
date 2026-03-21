using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MarketPortfolioAnalytics.Models
{
    // Portefeuille financier appartenant à un utilisateur.
    // Contient des positions (actifs détenus) via la table Position.
    // Currency = devise d'affichage, pas la devise des actifs (eux peuvent être en USD).
    [Table("Portfolio")]
    public class Portfolio
    {
        [Key]
        public int Id { get; set; }

        // Nom du portefeuille (ex: "Mon portefeuille tech")
        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        
        // Devise de référence du portefeuille 
        // Défaut : "EUR".
        // Note : les actifs peuvent être dans une devise différente.
        // La conversion multidevise est une évolution future.
        [Required]
        [MaxLength(3)]
        public string Currency { get; set; } = "EUR";

        //Date de création du portefeuille (UTC, automatique)
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Identifiant de l'utilisateur propriétaire
        [Required]
        public int UserId { get; set; }

        // [JsonIgnore] sur User : évite la boucle infinie Portfolio -> User -> Portfolio.
        // UserId suffit dans le JSON, on n'a pas besoin de tout l'objet User.
        [JsonIgnore]
        [ForeignKey(nameof(UserId))]
        public virtual AppUser? User { get; set; }

        // Liste des positions détenues dans ce portefeuille.
        // Chaque position représente un actif avec sa quantité et son prix d'achat.
        // Incluse dans la réponse uniquement sur GET /details.
        public virtual ICollection<Position>? ListePositions { get; set; }
    }
}
