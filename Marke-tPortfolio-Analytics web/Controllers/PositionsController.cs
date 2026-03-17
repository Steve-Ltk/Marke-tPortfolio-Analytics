using Marke_tPortfolio_Analytics_web.ViewModels;
using Marke_tPortfolio_Analytics_web.Services;
using MarketPortfolioAnalytics.Models;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    /// <summary>
    /// Gestion des positions d'un portefeuille (ajout, édition, suppression).
    /// Toujours associé à un portfolioId parent.
    /// </summary>
    public class PositionsController : BaseController
    {
        private readonly IApiService _api;

        public PositionsController(IApiService api) => _api = api;

        // ── CREATE ────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Create(int portfolioId)
        {
            var portfolio = await _api.GetPortfolioByIdAsync(portfolioId);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return NotFound();

            var assets = await _api.GetAllAssetsAsync();

            return View(new PositionCreateViewModel
            {
                PortfolioId = portfolioId,
                PortfolioName = portfolio.Name,
                PurchaseDate = DateTime.Today,
                Assets = assets?.Select(a => new AssetSelectItem
                {
                    Id = a.Id,
                    Ticker = a.Symbol,
                    Nom = a.Name,
                    Type = a.AssetType ?? "Stock"
                }).ToList() ?? new()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PositionCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                // Recharger les actifs si erreur
                var assets = await _api.GetAllAssetsAsync();
                model.Assets = assets?.Select(a => new AssetSelectItem
                {
                    Id = a.Id,
                    Ticker = a.Symbol,
                    Nom = a.Name,
                    Type = a.AssetType ?? "Stock"
                }).ToList() ?? new();
                return View(model);
            }

            var position = new Position
            {
                PortfolioId = model.PortfolioId,
                AssetId = model.AssetId,
                Quantity = (double)model.Quantity,
                PurchasePrice = (double)model.PurchasePrice,
                PurchaseDate = model.PurchaseDate
            };

            var created = await _api.CreatePositionAsync(position);
            if (created == null)
            {
                ModelState.AddModelError(string.Empty, "Erreur lors de l'ajout de la position.");
                var assets = await _api.GetAllAssetsAsync();
                model.Assets = assets?.Select(a => new AssetSelectItem
                {
                    Id = a.Id,
                    Ticker = a.Symbol,
                    Nom = a.Name,
                    Type = a.AssetType ?? "Stock"
                }).ToList() ?? new();
                return View(model);
            }

            TempData["SuccessMessage"] = "Position ajoutée avec succès.";
            return RedirectToAction("Details", "Portfolios", new { id = model.PortfolioId });
        }

        // ── EDIT ──────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var position = await _api.GetPositionByIdAsync(id);
            if (position == null) return NotFound();

            var portfolio = await _api.GetPortfolioByIdAsync(position.PortfolioId);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return NotFound();

            var asset = await _api.GetAssetByIdAsync(position.AssetId);

            return View(new PositionEditViewModel
            {
                Id = position.Id,
                PortfolioId = position.PortfolioId,
                PortfolioName = portfolio.Name,
                AssetTicker = asset?.Symbol ?? "—",
                AssetNom = asset?.Name ?? "—",
                Quantity = (decimal)position.Quantity,
                PurchasePrice = (decimal)position.PurchasePrice,
                PurchaseDate = position.PurchaseDate
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(PositionEditViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var position = await _api.GetPositionByIdAsync(model.Id);
            if (position == null) return NotFound();

            var portfolio = await _api.GetPortfolioByIdAsync(position.PortfolioId);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return NotFound();

            position.Quantity = (double)model.Quantity;
            position.PurchasePrice = (double)model.PurchasePrice;
            position.PurchaseDate = model.PurchaseDate;

            var ok = await _api.UpdatePositionAsync(model.Id, position);
            if (!ok)
            {
                ModelState.AddModelError(string.Empty, "Erreur lors de la mise à jour.");
                return View(model);
            }

            TempData["SuccessMessage"] = "Position mise à jour.";
            return RedirectToAction("Details", "Portfolios", new { id = model.PortfolioId });
        }

        // ── DELETE (POST) ─────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, int portfolioId)
        {
            var portfolio = await _api.GetPortfolioByIdAsync(portfolioId);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return NotFound();

            await _api.DeletePositionAsync(id);
            TempData["SuccessMessage"] = "Position supprimée.";
            return RedirectToAction("Details", "Portfolios", new { id = portfolioId });
        }
    }
}
