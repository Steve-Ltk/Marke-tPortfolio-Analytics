using Marke_tPortfolio_Analytics_web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    // Controller de base dont héritent TOUS les controllers authentifiés.
    // AuthController est le seul qui N'hérite PAS de BaseController (pages publiques).
    // Responsabilités :
    // 1. Vérifie que l'utilisateur est connecté AVANT chaque action -> redirect Login si non
    // 2. Expose les helpers pour lire la session (GetUserId, GetUserName...)
    // 3. Centralise les messages TempData (succès / erreur / avertissement)
    // 4. Injecte IApiService accessible dans tous les controllers enfants
    public abstract class BaseController : Controller
    // "abstract" = cette classe ne peut pas être instanciée directement
    // Elle sert uniquement de base pour les autres controllers
    {
        // ApiService : le service qui appelle le backend → injecté par DI
        // Logger : pour tracer les erreurs → injecté par DI
        // "protected" = accessible dans cette classe ET dans les classes enfants
        // mais pas depuis l'extérieur
        protected readonly IApiService ApiService;
        protected readonly ILogger Logger;

        // Clés de session
        // Clés de session -> chaînes constantes pour éviter les fautes de frappe
        // "private const" = valeur fixe, accessible uniquement dans cette classe
        private const string SessionKeyUserId = "UserId";
        private const string SessionKeyUserName = "UserName";
        private const string SessionKeyUserEmail = "UserEmail";

        // Clés TempData -> messages affichés une seule fois après une action
        // TempData survit à une redirection (contrairement à ViewData)
        private const string TempSuccess = "SuccessMessage";
        private const string TempError = "ErrorMessage";
        private const string TempWarning = "WarningMessage";

        // Constructeur : reçoit ApiService et Logger
        // Tous les controllers enfants appellent ce constructeur avec "base(api, logger)"
        protected BaseController(IApiService apiService, ILogger logger)
        {
            ApiService = apiService;
            Logger = logger;
        }

        // OnActionExecuting = méthode appelée automatiquement AVANT chaque action
        // de chaque controller qui hérite de BaseController
        // C'est ici que la protection "pas connecté -> redirect Login" se fait
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            if (GetUserId() == null)
            {
                // Sauvegarde l'URL demandée pour rediriger après connexion
                // Ex : l'user essaie /Portfolios -> redirigé vers Login -> après connexion retourne /Portfolios
                var returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;
                // "context.Result = ..." = court-circuite l'action -> elle ne s'exécute pas
                context.Result = RedirectToAction("Login", "Auth",
                    new { returnUrl = returnUrl.ToString() });
                return; // stop -> l'action réelle n'est jamais appelée
            }

            // Connecté -> injecte les infos user dans ViewData
           // Utilisé par _Sidebar.cshtml et _Topbar.cshtml pour afficher le nom et email
            ViewData["CurrentUserId"] = GetUserId();
            ViewData["CurrentUserName"] = GetUserName();
            ViewData["CurrentUserEmail"] = GetUserEmail();

            // Appelle la méthode parente -> continue le flux normal
            base.OnActionExecuting(context);
        }

        // Retourne l'Id de l'utilisateur connecté depuis la session
        // Retourne null si pas de session -> utilisé dans OnActionExecuting pour détecter la déconnexion
        protected int? GetUserId()
            => HttpContext.Session.GetInt32(SessionKeyUserId);

        // Retourne le nom de l'utilisateur connecté
        // ?? "Utilisateur" = valeur par défaut si la clé n'existe pas en session
        protected string GetUserName()
            => HttpContext.Session.GetString(SessionKeyUserName) ?? "Utilisateur";

        protected string GetUserEmail()
            => HttpContext.Session.GetString(SessionKeyUserEmail) ?? string.Empty;

        // Retourne l'Id ou lance une exception si pas connecté
        // Utilisé quand on est SÛR que l'utilisateur est connecté (jamais après OnActionExecuting)
        protected int GetUserIdOrThrow()
            => GetUserId() ?? throw new UnauthorizedAccessException("Session expirée.");

        // "public static" = accessible sans instance -> AuthController peut l'appeler
        // directement via "BaseController.SetSession(...)" sans hériter de BaseController public static void SetSession(ISession session, int userId, string userName, string email)
        {
            session.SetInt32(SessionKeyUserId, userId);
            session.SetString(SessionKeyUserName, userName);
            session.SetString(SessionKeyUserEmail, email);
        }

        // Efface toute la session → utilisé lors de la déconnexion
        public static void ClearSession(ISession session)
        {
            session.Clear();
        }

        // TempData = données qui survivent à UNE redirection puis disparaissent
        // Utilisé pour afficher un message après un POST → redirect → GET
        // Ex : "Position ajoutée" après la création d'une position

        // Message vert de succès -> affiché par _AlertMessages.cshtml
        protected void SetSuccess(string message)
            => TempData[TempSuccess] = message;

        // Message rouge d'erreur -> affiché par _AlertMessages.cshtml
        protected void SetError(string message)
            => TempData[TempError] = message;

        // Message orange d'avertissement -> affiché par _AlertMessages.cshtml
        protected void SetWarning(string message)
            => TempData[TempWarning] = message;

        // Formate une valeur monétaire selon la devise
        // "static" = n'utilise pas l'état de la classe -> fonction pure
        protected static string FormatCurrency(decimal value, string currency = "EUR")
        {
            return currency switch
            {
                "USD" => $"${value:N2}", // N2 = 2 décimales avec séparateur de milliers
                "EUR" => $"€{value:N2}",
                "GBP" => $"£{value:N2}",
                _ => $"{value:N2} {currency}" // devise inconnue -> code après le montant
            };
        }

        // Formate un pourcentage avec signe + ou - devant
        // Ex : 5.23 → "+5.23%", -2.1 → "-2.10%"
        protected static string FormatPercent(double value)
            => $"{(value >= 0 ? "+" : "")}{value:F2}%";

        // Pour decimal (même logique)
        protected static string FormatPercent(decimal value)
            => $"{(value >= 0 ? "+" : "")}{value:F2}%";
    }
}

