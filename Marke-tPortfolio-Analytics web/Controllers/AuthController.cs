using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    /// <summary>
    /// Gère l'authentification : connexion, inscription, déconnexion.
    ///
    /// N'hérite PAS de BaseController — pages publiques, pas de vérification session.
    /// Si l'utilisateur est déjà connecté → redirection Dashboard.
    /// </summary>
    public class AuthController : Controller
    {
        private readonly IApiService _api;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IApiService api, ILogger<AuthController> logger)
        {
            _api = api;
            _logger = logger;
        }

        private bool IsAuthenticated()
            => HttpContext.Session.GetInt32("UserId") != null;

        // ════════════════════════════════════════════════════════════════════
        // LOGIN
        // ════════════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (IsAuthenticated())
                return RedirectToAction("Index", "Dashboard");

            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _api.LoginAsync(model.Email, model.Password);

            if (user == null)
            {
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

            // Ouvrir la session
            Controllers.BaseController.SetSession(
                HttpContext.Session,
                user.Id,
                user.FullName,
                user.Email
            );

            _logger.LogInformation("Connexion : {Email} (Id={Id})", user.Email, user.Id);

            // Redirection post-login
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);

            return RedirectToAction("Index", "Dashboard");
        }

        // ════════════════════════════════════════════════════════════════════
        // REGISTER
        // ════════════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult Register()
        {
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

            var user = await _api.RegisterAsync(model.FullName, model.Email, model.Password);

            if (user == null)
            {
                // L'API retourne null si email déjà pris (409 Conflict) ou erreur serveur
                ModelState.AddModelError(string.Empty,
                    "Impossible de créer le compte. Cet email est peut-être déjà utilisé.");
                return View(model);
            }

            _logger.LogInformation("Inscription : {Email} (Id={Id})", user.Email, user.Id);

            // Connexion automatique après inscription
            Controllers.BaseController.SetSession(
                HttpContext.Session,
                user.Id,
                user.FullName,
                user.Email
            );

            TempData["SuccessMessage"] = $"Bienvenue sur catalis, {user.FullName} ! Définissez votre premier objectif.";

            return RedirectToAction("Wizard", "Goal", new { step = 1 });
        }

        // ════════════════════════════════════════════════════════════════════
        // LOGOUT
        // ════════════════════════════════════════════════════════════════════

        [HttpGet]
        public IActionResult Logout()
        {
            Controllers.BaseController.ClearSession(HttpContext.Session);
            _logger.LogInformation("Déconnexion");
            return RedirectToAction("Login", "Auth");
        }
    }
}
