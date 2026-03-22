using System.ComponentModel.DataAnnotations;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    // ViewModel du formulaire de connexion
    public class LoginViewModel
    {
        [Required(ErrorMessage = "L'adresse email est obligatoire.")]
        [EmailAddress(ErrorMessage = "Format d'email invalide.")]
        [Display(Name = "Adresse email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
        [DataType(DataType.Password)]
        [Display(Name = "Mot de passe")]
        public string Password { get; set; } = string.Empty;

        // URL de retour après connexion réussie
        // Ex : l'user essaie /Portfolios -> redirigé vers Login -> après connexion retourne /Portfolios
        // Url.IsLocalUrl() vérifie que c'est une URL locale avant de rediriger        public string? ReturnUrl { get; set; }
    }

    // ViewModel du formulaire d'inscription
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Le nom complet est obligatoire.")]
        [MaxLength(150, ErrorMessage = "150 caractères maximum.")]
        [Display(Name = "Nom complet")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "L'adresse email est obligatoire.")]
        [EmailAddress(ErrorMessage = "Format d'email invalide.")]
        [MaxLength(256)]
        [Display(Name = "Adresse email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
        [MinLength(8, ErrorMessage = "Minimum 8 caractères.")]
        [DataType(DataType.Password)]
        [Display(Name = "Mot de passe")]
        public string Password { get; set; } = string.Empty;

        // [Compare] -> ASP.NET vérifie automatiquement que ConfirmPassword == Password
        // Si différent -> erreur de validation avant même d'entrer dans le controller
        [Required(ErrorMessage = "Veuillez confirmer le mot de passe.")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Les mots de passe ne correspondent pas.")]
        [Display(Name = "Confirmer le mot de passe")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
