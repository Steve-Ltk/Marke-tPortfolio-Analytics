using System.ComponentModel.DataAnnotations;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    // ViewModel de la page Profil utilisateur
    // Couvre les infos personnelles + statistiques + changement de mot de passe
    public class ProfileViewModel
    {
        public int UserId { get; set; }

        [Required(ErrorMessage = "Le nom complet est obligatoire.")]
        [MaxLength(150)]
        [Display(Name = "Nom complet")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "L'email est obligatoire.")]
        [EmailAddress]
        [Display(Name = "Adresse e-mail")]
        public string Email { get; set; } = string.Empty;

        // Statistiques calculées dans ProfileController
        // Affichées dans les 3 cards en haut de la page
        public int NbPortfolios { get; set; }
        public int NbPositions { get; set; }
        public decimal ValeurTotale { get; set; }
        public string MembreDepuis { get; set; } = string.Empty;
    }
}
