using Marke_tPortfolio_Analytics_web.Helpers;
using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    // Gère la création, modification et suppression des positions.
    // Une position = un actif dans un portefeuille avec quantité + prix d'achat.
    // Vérifie l'ownership du portefeuille sur chaque action sensible.
    public class PositionsController : BaseController
    {
        public PositionsController(IApiService api, ILogger<PositionsController> logger)
            : base(api, logger) { }

        // GET /Positions/Create?portfolioId=3 -> formulaire d'ajout de position
        [HttpGet]
        public async Task<IActionResult> Create(int portfolioId)
        {
            var portfolio = await ApiService.GetPortfolioByIdAsync(portfolioId);

            // Portefeuille inexistant -> redirect liste
            if (portfolio == null)
                return RedirectToAction("Index", "Portfolios");

            // Charge tous les actifs pour le menu déroulant du formulaire
            var assets = await ApiService.GetAllAssetsAsync();

            return View(new PositionCreateViewModel
            {
                PortfolioId = portfolioId,
                PortfolioName = portfolio.Name,
                BuyDate = DateTime.Today, // date par défaut = aujourd'hui
                // Transforme chaque actif en item de sélection pour le <select>
                Assets = assets.Select(a => new AssetSelectItem
                {
                    Id = a.Id,
                    Ticker = a.Ticker,
                    Nom = a.Name,
                    Type = AssetHelper.GetTypeLabel(a) // "Stock" ou "Bond"
                }).ToList()
            });
        }

        // POST /Positions/Create -> crée la position via le backend
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PositionCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                // Recharge la liste des actifs pour réafficher le formulaire avec les erreurs
                var assets = await ApiService.GetAllAssetsAsync();
                model.Assets = assets.Select(a => new AssetSelectItem
                {
                    Id = a.Id,
                    Ticker = a.Ticker,
                    Nom = a.Name,
                    Type = AssetHelper.GetTypeLabel(a)
                }).ToList();
                return View(model);
            }

            // Appelle POST /api/Positions avec les données du formulaire
            var created = await ApiService.CreatePositionAsync(
                model.PortfolioId, model.AssetId,
                model.Quantity, model.AvgBuyPrice, model.BuyDate);

            if (created == null)
            {
                // null -> actif déjà dans ce portefeuille (409 Conflict) ou erreur backend
                ModelState.AddModelError(string.Empty,
                    "Erreur. Cet actif est peut-être déjà dans ce portefeuille.");
                var assets = await ApiService.GetAllAssetsAsync();
                model.Assets = assets.Select(a => new AssetSelectItem
                {
                    Id = a.Id,
                    Ticker = a.Ticker,
                    Nom = a.Name,
                    Type = AssetHelper.GetTypeLabel(a)
                }).ToList();
                return View(model);
            }

            SetSuccess("Position ajoutée avec succès.");
            // Redirige vers le détail du portefeuille après création
            return RedirectToAction("Details", "Portfolios",
                new { id = model.PortfolioId });
        }

        // GET /Positions/Edit?portfolioId=1&assetId=3 -> formulaire de modification
        // Clé composite (portfolioId + assetId) -> pas d'Id simple
        [HttpGet]
        public async Task<IActionResult> Edit(int portfolioId, int assetId)
        {
            var portfolio = await ApiService.GetPortfolioByIdAsync(portfolioId);
            // Ownership check -> 404 si pas le bon user
            if (portfolio == null || portfolio.UserId != GetUserId())
                return RedirectToAction("Index", "Portfolios");

            // Récupère la position par sa clé composite
            var position = await ApiService.GetPositionByKeyAsync(portfolioId, assetId);
            if (position == null) return NotFound();

            var asset = await ApiService.GetAssetByIdAsync(assetId);

            return View(new PositionEditViewModel
            {
                PortfolioId = portfolioId,
                AssetId = assetId,
                PortfolioName = portfolio.Name,
                AssetTicker = asset?.Ticker ?? "—",
                AssetNom = asset?.Name ?? "—",
                Quantity = position.Quantity,
                AvgBuyPrice = position.AvgBuyPrice,
                BuyDate = position.BuyDate
            });
        }

        // POST /Positions/Edit -> met à jour quantité, prix moyen et date
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(PositionEditViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var portfolio = await ApiService.GetPortfolioByIdAsync(model.PortfolioId);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return NotFound();

            var ok = await ApiService.UpdatePositionAsync(
                model.PortfolioId, model.AssetId,
                model.Quantity, model.AvgBuyPrice, model.BuyDate);

            if (!ok)
            {
                ModelState.AddModelError(string.Empty, "Erreur lors de la mise à jour.");
                return View(model);
            }

            SetSuccess("Position mise à jour.");
            return RedirectToAction("Details", "Portfolios",
                new { id = model.PortfolioId });
        }

        // POST /Positions/Delete -> supprime une position du portefeuille
        // L'actif reste en base -> seule la relation position est supprimée
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int portfolioId, int assetId)
        {
            var portfolio = await ApiService.GetPortfolioByIdAsync(portfolioId);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return NotFound();

            await ApiService.DeletePositionAsync(portfolioId, assetId);
            SetSuccess("Position supprimée.");
            return RedirectToAction("Details", "Portfolios",
                new { id = portfolioId });
        }
    }
}
