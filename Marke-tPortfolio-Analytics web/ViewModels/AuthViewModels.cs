using System.ComponentModel.DataAnnotations;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    // ══════════════════════════════════════════════════════════════════════
    // LOGIN
    // ══════════════════════════════════════════════════════════════════════

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

        /// <summary>URL de retour après connexion réussie (ex: /Portfolios).</summary>
        public string? ReturnUrl { get; set; }
    }

    // ══════════════════════════════════════════════════════════════════════
    // REGISTER
    // ══════════════════════════════════════════════════════════════════════

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

        [Required(ErrorMessage = "Veuillez confirmer le mot de passe.")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Les mots de passe ne correspondent pas.")]
        [Display(Name = "Confirmer le mot de passe")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
