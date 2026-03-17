using System.ComponentModel.DataAnnotations;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    /// <summary>
    /// ViewModel pour Profile/Index.cshtml.
    /// Couvre : informations personnelles + stats + changement mot de passe.
    /// </summary>
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

        // Statistiques affichées sur la page
        public int NbPortfolios { get; set; }
        public int NbPositions { get; set; }
        public decimal ValeurTotale { get; set; }
        public string MembreDepuis { get; set; } = string.Empty;
    }
}