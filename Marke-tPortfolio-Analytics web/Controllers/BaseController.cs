using Marke_tPortfolio_Analytics_web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    /// <summary>
    /// Controller de base dont héritent TOUS les controllers authentifiés.
    ///
    /// Responsabilités :
    ///   1. Vérifier la session avant chaque action (redirect vers Login si absente)
    ///   2. Exposer des helpers pour accéder à l'utilisateur courant
    ///   3. Centraliser les messages TempData (succès / erreur)
    ///   4. Injecter IApiService accessible dans tous les controllers enfants
    ///
    /// NB : AuthController n'hérite PAS de BaseController (pages publiques).
    /// </summary>
    public abstract class BaseController : Controller
    {
        protected readonly IApiService ApiService;
        protected readonly ILogger Logger;

        // Clés de session
        private const string SessionKeyUserId = "UserId";
        private const string SessionKeyUserName = "UserName";
        private const string SessionKeyUserEmail = "UserEmail";

        // Clés TempData
        private const string TempSuccess = "SuccessMessage";
        private const string TempError = "ErrorMessage";
        private const string TempWarning = "WarningMessage";

        protected BaseController(IApiService apiService, ILogger logger)
        {
            ApiService = apiService;
            Logger = logger;
        }

        // ── Vérification session avant chaque action ──────────────────────────

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (GetUserId() == null)
            {
                // Sauvegarde de l'URL demandée pour redirection post-login
                var returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;
                context.Result = RedirectToAction("Login", "Auth",
                    new { returnUrl = returnUrl.ToString() });
                return;
            }

            // Injecte les infos user dans ViewData pour les vues partielles (sidebar, topbar)
            ViewData["CurrentUserId"] = GetUserId();
            ViewData["CurrentUserName"] = GetUserName();
            ViewData["CurrentUserEmail"] = GetUserEmail();

            base.OnActionExecuting(context);
        }

        // ── Accesseurs session ────────────────────────────────────────────────

        protected int? GetUserId()
            => HttpContext.Session.GetInt32(SessionKeyUserId);

        protected string GetUserName()
            => HttpContext.Session.GetString(SessionKeyUserName) ?? "Utilisateur";

        protected string GetUserEmail()
            => HttpContext.Session.GetString(SessionKeyUserEmail) ?? string.Empty;

        protected int GetUserIdOrThrow()
            => GetUserId() ?? throw new UnauthorizedAccessException("Session expirée.");

        // ── Setters session (utilisés dans AuthController) ────────────────────

        public static void SetSession(ISession session, int userId, string userName, string email)
        {
            session.SetInt32(SessionKeyUserId, userId);
            session.SetString(SessionKeyUserName, userName);
            session.SetString(SessionKeyUserEmail, email);
        }

        public static void ClearSession(ISession session)
        {
            session.Clear();
        }

        // ── Messages TempData ─────────────────────────────────────────────────

        protected void SetSuccess(string message)
            => TempData[TempSuccess] = message;

        protected void SetError(string message)
            => TempData[TempError] = message;

        protected void SetWarning(string message)
            => TempData[TempWarning] = message;

        // ── Helper : année de fondation du portefeuille (affichage) ──────────

        protected static string FormatCurrency(decimal value, string currency = "EUR")
        {
            return currency switch
            {
                "USD" => $"${value:N2}",
                "EUR" => $"€{value:N2}",
                "GBP" => $"£{value:N2}",
                _ => $"{value:N2} {currency}"
            };
        }

        protected static string FormatPercent(double value)
            => $"{(value >= 0 ? "+" : "")}{value:F2}%";

        protected static string FormatPercent(decimal value)
            => $"{(value >= 0 ? "+" : "")}{value:F2}%";
    }
}

