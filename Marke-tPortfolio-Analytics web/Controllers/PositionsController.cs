using Marke_tPortfolio_Analytics_web.Helpers;
using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    public class PositionsController : BaseController
    {
        public PositionsController(IApiService api, ILogger<PositionsController> logger)
            : base(api, logger) { }

        // ── CREATE ────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Create(int portfolioId)
        {
            var portfolio = await ApiService.GetPortfolioByIdAsync(portfolioId);
            if (portfolio == null)
                return RedirectToAction("Index", "Portfolios");

            var assets = await ApiService.GetAllAssetsAsync();

            return View(new PositionCreateViewModel
            {
                PortfolioId = portfolioId,
                PortfolioName = portfolio.Name,
                BuyDate = DateTime.Today,
                Assets = assets.Select(a => new AssetSelectItem
                {
                    Id = a.Id,
                    Ticker = a.Ticker,
                    Nom = a.Name,
                    Type = AssetHelper.GetTypeLabel(a)
                }).ToList()
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PositionCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
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

            // ✅ CreatePositionAsync(portfolioId, assetId, quantity, avgBuyPrice, buyDate)
            var created = await ApiService.CreatePositionAsync(
                model.PortfolioId, model.AssetId,
                model.Quantity, model.AvgBuyPrice, model.BuyDate);

            if (created == null)
            {
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
            return RedirectToAction("Details", "Portfolios",
                new { id = model.PortfolioId });
        }

        // ── EDIT — clé composite (portfolioId + assetId) ──────────────────────

        [HttpGet]
        public async Task<IActionResult> Edit(int portfolioId, int assetId)
        {
            var portfolio = await ApiService.GetPortfolioByIdAsync(portfolioId);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return RedirectToAction("Index", "Portfolios");

            // ✅ GetPositionByKeyAsync(portfolioId, assetId)
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

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(PositionEditViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var portfolio = await ApiService.GetPortfolioByIdAsync(model.PortfolioId);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return NotFound();

            // ✅ UpdatePositionAsync(portfolioId, assetId, quantity, avgBuyPrice, buyDate)
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

        // ── DELETE ────────────────────────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int portfolioId, int assetId)
        {
            var portfolio = await ApiService.GetPortfolioByIdAsync(portfolioId);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return NotFound();

            // ✅ DeletePositionAsync(portfolioId, assetId)
            await ApiService.DeletePositionAsync(portfolioId, assetId);
            SetSuccess("Position supprimée.");
            return RedirectToAction("Details", "Portfolios",
                new { id = portfolioId });
        }
    }
}