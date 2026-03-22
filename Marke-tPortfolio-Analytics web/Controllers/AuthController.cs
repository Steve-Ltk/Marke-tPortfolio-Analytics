using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    // Gère l'authentification : connexion, inscription, déconnexion.
    // N'hérite PAS de BaseController -> pages publiques, pas de vérification session.
    // Si l'utilisateur est déjà connecté -> redirection automatique vers Dashboard
    public class AuthController : Controller
    {
        private readonly IApiService _api;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IApiService api, ILogger<AuthController> logger)
        {
            _api = api;
            _logger = logger;
        }
       // Vérifie si une session existe -> utilisé dans chaque action pour rediriger si déjà connecté
        private bool IsAuthenticated()
            => HttpContext.Session.GetInt32("UserId") != null;

        // GET /Auth/Login → affiche la page de connexion
        // Si déjà connecté → redirect Dashboard directement
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (IsAuthenticated())
                return RedirectToAction("Index", "Dashboard");

            // Passe returnUrl au ViewModel pour le récupérer après la soumission du formulaire
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        // POST /Auth/Login -> traite le formulaire de connexion
        [HttpPost]
        [ValidateAntiForgeryToken] // protection CSRF -> vérifie le token caché dans le formulaire
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            // ModelState.IsValid = tous les [Required] et [EmailAddress] sont satisfaits
            // Si invalide -> réaffiche le formulaire avec les erreurs de validation
            if (!ModelState.IsValid)
                return View(model);

            // Appelle le backend -> null si email/mdp incorrect
            var user = await _api.LoginAsync(model.Email, model.Password);

            if (user == null)
            {
                // Ajoute une erreur globale (pas liée à un champ spécifique)
                ModelState.AddModelError(string.Empty,
                    "Email ou mot de passe incorrect. Vérifiez vos identifiants.");
                return View(model);
            }

            if (!user.IsActive)
            {
                ModelState.AddModelError(string.Empty,
                    "Ce compte est désactivé. Contactez l'administrateur.");
                return View(model);
            }

            // Connexion réussie → ouvre la session avec les infos de l'utilisateur
            // Appel statique → pas besoin d'instance de BaseController
            Controllers.BaseController.SetSession(
                HttpContext.Session,
                user.Id,
                user.FullName,
                user.Email
            );

            _logger.LogInformation("Connexion : {Email} (Id={Id})", user.Email, user.Id);

            // Redirige vers l'URL demandée avant la connexion (si existante et locale)
            // Url.IsLocalUrl -> sécurité : évite les redirections vers des sites externes
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);
                
            // Sinon -> Dashboard par défaut
            return RedirectToAction("Index", "Dashboard");
        }

        [HttpGet]
        public IActionResult Register()
        {
            // Déjà connecté -> pas besoin de s'inscrire
            if (IsAuthenticated())
                return RedirectToAction("Index", "Dashboard");

            return View(new RegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Crée le compte via le backend -> null si email déjà pris ou erreur serveur
            var user = await _api.RegisterAsync(model.FullName, model.Email, model.Password);

            if (user == null)
            {
                // L'API retourne null si email déjà pris (409 Conflict) ou erreur serveur
                ModelState.AddModelError(string.Empty,
                    "Impossible de créer le compte. Cet email est peut-être déjà utilisé.");
                return View(model);
            }

            _logger.LogInformation("Inscription : {Email} (Id={Id})", user.Email, user.Id);

            // Connexion automatique après inscription → pas besoin de se connecter manuellement
            Controllers.BaseController.SetSession(
                HttpContext.Session,
                user.Id,
                user.FullName,
                user.Email
            );

            // Message de bienvenue affiché sur la prochaine page
            TempData["SuccessMessage"] = $"Bienvenue sur catalis, {user.FullName} ! Définissez votre premier objectif.";

            // Redirige vers le wizard de création d'objectif (onboarding)
            return RedirectToAction("Wizard", "Goal", new { step = 1 });
        }

         // GET /Auth/Logout -> efface la session et redirige vers Login
         // Pas de POST car pas de données sensibles envoyée (on supprime juste la session)
        [HttpGet]
        public IActionResult Logout()
        {
            // Efface toutes les clés de session → l'utilisateur est déconnecté
            Controllers.BaseController.ClearSession(HttpContext.Session);
            _logger.LogInformation("Déconnexion");
            return RedirectToAction("Login", "Auth");
        }
    }
}
