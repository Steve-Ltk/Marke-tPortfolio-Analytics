using Marke_tPortfolio_Analytics_web.ViewModels;
using Marke_tPortfolio_Analytics_web.Services;
using MarketPortfolioAnalytics.Models;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    /// <summary>
    /// CRUD portefeuilles + vue détaillée avec positions.
    /// </summary>
    public class PortfoliosController : BaseController
    {
        private readonly IApiService _api;
        private readonly ILogger<PortfoliosController> _logger;

        public PortfoliosController(IApiService api, ILogger<PortfoliosController> logger)
        {
            _api = api;
            _logger = logger;
        }

        // ── INDEX — liste des portefeuilles ───────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var portfolios = await _api.GetPortfoliosByUserAsync(GetUserId());
            var tauxEurUsd = await _api.GetExchangeRateAsync("EUR", "USD");

            var cards = new List<PortfolioCard>();
            decimal valeurGlobale = 0m;

            foreach (var p in portfolios ?? new())
            {
                var positions = await _api.GetPositionsByPortfolioAsync(p.Id);
                decimal valeurEur = 0m;

                foreach (var pos in positions ?? new())
                {
                    var asset = await _api.GetAssetByIdAsync(pos.AssetId);
                    if (asset == null) continue;
                    var prix = await _api.GetLatestPriceAsync(asset.Symbol) ?? (decimal)pos.PurchasePrice;
                    bool isUsd = !asset.Symbol.EndsWith(".PA");
                    decimal val = prix * (decimal)pos.Quantity;
                    valeurEur += isUsd && tauxEurUsd > 0 ? val / tauxEurUsd : val;
                }

                valeurGlobale += valeurEur;
                cards.Add(new PortfolioCard
                {
                    Portfolio = p,
                    ValeurEur = Math.Round(valeurEur, 2),
                    NbPositions = positions?.Count ?? 0,
                    // Sharpe/Volatilité : placeholders Phase 3, calculés en Phase 5
                    SharpeRatio = 0,
                    Volatilite = 0,
                    RendementPct = 0
                });
            }

            return View(new PortfolioIndexViewModel
            {
                Portfolios = cards,
                ValeurTotaleEur = Math.Round(valeurGlobale, 2)
            });
        }

        // ── DETAILS ───────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var portfolio = await _api.GetPortfolioByIdAsync(id);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return NotFound();

            var positions = await _api.GetPositionsByPortfolioAsync(id);
            var tauxEurUsd = await _api.GetExchangeRateAsync("EUR", "USD");

            decimal valeurTotale = 0m;
            var details = new List<PositionDetail>();

            foreach (var pos in positions ?? new())
            {
                var asset = await _api.GetAssetByIdAsync(pos.AssetId);
                if (asset == null) continue;

                decimal prixActuel = await _api.GetLatestPriceAsync(asset.Symbol) ?? (decimal)pos.PurchasePrice;
                bool isUsd = !asset.Symbol.EndsWith(".PA");

                decimal valeurDevise = prixActuel * (decimal)pos.Quantity;
                decimal valeurEur = isUsd && tauxEurUsd > 0 ? valeurDevise / tauxEurUsd : valeurDevise;
                decimal coutEur = isUsd && tauxEurUsd > 0
                    ? (decimal)pos.PurchasePrice * (decimal)pos.Quantity / tauxEurUsd
                    : (decimal)pos.PurchasePrice * (decimal)pos.Quantity;

                decimal pnlEur = valeurEur - coutEur;
                decimal pnlPct = coutEur > 0 ? (pnlEur / coutEur) * 100 : 0;

                valeurTotale += valeurEur;

                details.Add(new PositionDetail
                {
                    Position = pos,
                    Ticker = asset.Symbol,
                    NomActif = asset.Name,
                    TypeActif = asset.AssetType ?? "Stock",
                    PrixActuel = Math.Round(prixActuel, 2),
                    ValeurEur = Math.Round(valeurEur, 2),
                    PnlPct = Math.Round(pnlPct, 2),
                    PnlEur = Math.Round(pnlEur, 2),
                    Devise = isUsd ? "USD" : "EUR"
                });
            }

            // Poids
            foreach (var d in details)
                d.Poids = valeurTotale > 0
                    ? Math.Round(d.ValeurEur / valeurTotale * 100, 1)
                    : 0;

            return View(new PortfolioDetailsViewModel
            {
                Portfolio = portfolio,
                Positions = details,
                ValeurTotaleEur = Math.Round(valeurTotale, 2),
                TauxEurUsd = tauxEurUsd
            });
        }

        // ── CREATE ────────────────────────────────────────────────────────

        [HttpGet]
        public IActionResult Create()
        {
            ViewData["Title"] = "Nouveau portefeuille";
            return View(new PortfolioCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PortfolioCreateViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var portfolio = new Portfolio
            {
                Name = model.Name,
                Description = model.Description,
                Currency = model.Currency,
                UserId = GetUserId(),
                CreatedAt = DateTime.UtcNow
            };

            var created = await _api.CreatePortfolioAsync(portfolio);
            if (created == null)
            {
                ModelState.AddModelError(string.Empty, "Erreur lors de la création du portefeuille.");
                return View(model);
            }

            TempData["SuccessMessage"] = $"Portefeuille « {created.Name} » créé avec succès !";
            return RedirectToAction(nameof(Details), new { id = created.Id });
        }

        // ── EDIT ──────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var portfolio = await _api.GetPortfolioByIdAsync(id);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return NotFound();

            return View(new PortfolioEditViewModel
            {
                Id = portfolio.Id,
                Name = portfolio.Name,
                Description = portfolio.Description,
                Currency = portfolio.Currency
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(PortfolioEditViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var portfolio = await _api.GetPortfolioByIdAsync(model.Id);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return NotFound();

            portfolio.Name = model.Name;
            portfolio.Description = model.Description;
            portfolio.Currency = model.Currency;

            var ok = await _api.UpdatePortfolioAsync(model.Id, portfolio);
            if (!ok)
            {
                ModelState.AddModelError(string.Empty, "Erreur lors de la mise à jour.");
                return View(model);
            }

            TempData["SuccessMessage"] = "Portefeuille mis à jour.";
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        // ── DELETE (POST) ─────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var portfolio = await _api.GetPortfolioByIdAsync(id);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return NotFound();

            await _api.DeletePortfolioAsync(id);
            TempData["SuccessMessage"] = $"Portefeuille « {portfolio.Name} » supprimé.";
            return RedirectToAction(nameof(Index));
        }
    }
}
