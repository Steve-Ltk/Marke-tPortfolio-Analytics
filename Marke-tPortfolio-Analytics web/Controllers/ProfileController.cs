using Marke_tPortfolio_Analytics_web.Helpers;
using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    public class ProfileController : BaseController
    {
        public ProfileController(IApiService api, ILogger<ProfileController> logger)
            : base(api, logger) { }

        // ── Index ─────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            int userId = GetUserId() ?? 0;
            var user = await ApiService.GetUserByIdAsync(userId);
            if (user == null) return RedirectToAction("Login", "Auth");

            var portfolios = await ApiService.GetPortfoliosByUserAsync(userId);
            int nbPos = 0;
            decimal valeur = 0m;
            var taux = await ApiService.GetExchangeRateAsync("EUR", "USD");

            foreach (var p in portfolios)
            {
                var positions = await ApiService.GetPositionsByPortfolioAsync(p.Id);
                if (positions == null) continue;
                nbPos += positions.Count;

                foreach (var pos in positions)
                {
                    var asset = await ApiService.GetAssetByIdAsync(pos.AssetId);
                    if (asset == null) continue;
                    decimal prix = await ApiService.GetLatestPriceAsync(asset.Ticker) ?? pos.AvgBuyPrice;
                    bool isUsd = AssetHelper.IsUsd(asset);
                    decimal val = prix * pos.Quantity;
                    valeur += isUsd && taux > 0 ? val / taux : val;
                }
            }

            return View(new ProfileViewModel
            {
                UserId = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                NbPortfolios = portfolios.Count,
                NbPositions = nbPos,
                ValeurTotale = Math.Round(valeur, 2),
                MembreDepuis = user.CreatedAt.ToString("MMMM yyyy")
            });
        }

        // ── Update infos ──────────────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(ProfileViewModel model)
        {
            if (!ModelState.IsValid) return View("Index", model);

            int userId = GetUserId() ?? 0;
            var ok = await ApiService.UpdateUserAsync(userId, model.FullName, model.Email);

            if (!ok)
            {
                ModelState.AddModelError(string.Empty, "Mise à jour impossible. Email déjà utilisé ?");
                return View("Index", model);
            }

            // Mettre à jour la session
            HttpContext.Session.SetString("UserName", model.FullName);
            HttpContext.Session.SetString("UserEmail", model.Email);

            SetSuccess("Profil mis à jour.");
            return RedirectToAction(nameof(Index));
        }

        // ── Change password ───────────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(
            int userId,
            string CurrentPassword = "",
            string NewPassword = "",
            string ConfirmPassword = "")
        {
            if (NewPassword != ConfirmPassword)
            {
                TempData["PwdError"] = "Les mots de passe ne correspondent pas.";
                return RedirectToAction(nameof(Index));
            }

            if (NewPassword.Length < 8)
            {
                TempData["PwdError"] = "Le mot de passe doit contenir au moins 8 caractères.";
                return RedirectToAction(nameof(Index));
            }

            var ok = await ApiService.ChangePasswordAsync(
                GetUserId() ?? 0, CurrentPassword, NewPassword);

            if (!ok)
            {
                TempData["PwdError"] = "Mot de passe actuel incorrect.";
                return RedirectToAction(nameof(Index));
            }

            SetSuccess("Mot de passe modifié avec succès.");
            return RedirectToAction(nameof(Index));
        }
    }
}
