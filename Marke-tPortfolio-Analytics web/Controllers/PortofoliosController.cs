using Marke_tPortfolio_Analytics_web.Helpers;
using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    public class PortfoliosController : BaseController
    {
        public PortfoliosController(IApiService api, ILogger<PortfoliosController> logger)
            : base(api, logger) { }

        // ── INDEX ─────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            int userId = GetUserId() ?? 0;
            var portfolios = await ApiService.GetPortfoliosByUserAsync(userId);
            var taux = await ApiService.GetExchangeRateAsync("EUR", "USD");

            var cards = new List<PortfolioCard>();
            decimal total = 0m;

            foreach (var p in portfolios)
            {
                var positions = await ApiService.GetPositionsByPortfolioAsync(p.Id);
                decimal valeur = 0m;

                foreach (var pos in positions ?? new())
                {
                    var asset = await ApiService.GetAssetByIdAsync(pos.AssetId);
                    if (asset == null) continue;
                    decimal prix = await ApiService.GetLatestPriceAsync(asset.Ticker)
                                    ?? pos.AvgBuyPrice;
                    bool isUsd = AssetHelper.IsUsd(asset);
                    decimal val = prix * pos.Quantity;
                    valeur += isUsd && taux > 0 ? val / taux : val;
                }

                total += valeur;
                cards.Add(new PortfolioCard
                {
                    Portfolio = p,
                    ValeurEur = Math.Round(valeur, 2),
                    NbPositions = positions?.Count ?? 0
                });
            }

            return View(new PortfolioIndexViewModel
            {
                Portfolios = cards,
                ValeurTotaleEur = Math.Round(total, 2)
            });
        }

        // ── DETAILS ───────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var portfolio = await ApiService.GetPortfolioByIdAsync(id);

            // ✅ FIX : vérification d'ownership — empêche l'accès aux portefeuilles d'autres users
            if (portfolio == null)
                return RedirectToAction("Index");

            if (portfolio.UserId != GetUserId())
                return NotFound();

            var positions = await ApiService.GetPositionsByPortfolioAsync(id);
            var taux = await ApiService.GetExchangeRateAsync("EUR", "USD");

            decimal total = 0m;
            var details = new List<PositionDetail>();

            foreach (var pos in positions ?? new())
            {
                var asset = await ApiService.GetAssetByIdAsync(pos.AssetId);
                if (asset == null) continue;

                decimal prix = await ApiService.GetLatestPriceAsync(asset.Ticker)
                                 ?? pos.AvgBuyPrice;
                bool isUsd = AssetHelper.IsUsd(asset);
                decimal valDev = prix * pos.Quantity;
                decimal valEur = isUsd && taux > 0 ? valDev / taux : valDev;
                decimal coutEur = isUsd && taux > 0
                    ? pos.AvgBuyPrice * pos.Quantity / taux
                    : pos.AvgBuyPrice * pos.Quantity;
                decimal pnlEur = valEur - coutEur;
                decimal pnlPct = coutEur > 0 ? pnlEur / coutEur * 100 : 0;

                total += valEur;
                details.Add(new PositionDetail
                {
                    Position = pos,
                    Ticker = asset.Ticker,
                    NomActif = asset.Name,
                    TypeActif = AssetHelper.GetTypeLabel(asset),
                    PrixActuel = Math.Round(prix, 2),
                    ValeurEur = Math.Round(valEur, 2),
                    PnlPct = Math.Round(pnlPct, 2),
                    PnlEur = Math.Round(pnlEur, 2),
                    Devise = isUsd ? "USD" : "EUR"
                });
            }

            foreach (var d in details)
                d.Poids = total > 0 ? Math.Round(d.ValeurEur / total * 100, 1) : 0;

            return View(new PortfolioDetailsViewModel
            {
                Portfolio = portfolio,
                Positions = details,
                ValeurTotaleEur = Math.Round(total, 2),
                TauxEurUsd = taux
            });
        }

        // ── CREATE ────────────────────────────────────────────────────────────

        [HttpGet]
        public IActionResult Create()
            => View(new PortfolioCreateViewModel());

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PortfolioCreateViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var created = await ApiService.CreatePortfolioAsync(
                model.Name, model.Currency, GetUserId() ?? 0);

            if (created == null)
            {
                ModelState.AddModelError(string.Empty, "Erreur lors de la création.");
                return View(model);
            }

            SetSuccess($"Portefeuille « {created.Name} » créé !");
            return RedirectToAction(nameof(Details), new { id = created.Id });
        }

        // ── EDIT ──────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var p = await ApiService.GetPortfolioByIdAsync(id);
            if (p == null || p.UserId != GetUserId()) return NotFound();

            return View(new PortfolioEditViewModel
            {
                Id = p.Id,
                Name = p.Name,
                Currency = p.Currency
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(PortfolioEditViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var p = await ApiService.GetPortfolioByIdAsync(model.Id);
            if (p == null || p.UserId != GetUserId()) return NotFound();

            if (!await ApiService.UpdatePortfolioAsync(model.Id, model.Name, model.Currency))
            {
                ModelState.AddModelError(string.Empty, "Erreur lors de la mise à jour.");
                return View(model);
            }

            SetSuccess("Portefeuille mis à jour.");
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        // ── DELETE ────────────────────────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var p = await ApiService.GetPortfolioByIdAsync(id);
            if (p == null || p.UserId != GetUserId()) return NotFound();

            var ok = await ApiService.DeletePortfolioAsync(id);
            if (!ok)
            {
                SetError($"Impossible de supprimer « {p.Name} » : retirez d'abord toutes ses positions.");
                return RedirectToAction(nameof(Details), new { id });
            }
            SetSuccess($"Portefeuille « {p.Name} » supprimé.");
            return RedirectToAction(nameof(Index));
        }
    }
}