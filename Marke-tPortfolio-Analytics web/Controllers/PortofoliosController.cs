using Marke_tPortfolio_Analytics_web.Helpers;
using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    // Affiche et gère les portefeuilles de l'utilisateur connecté.
    // Vérifie l'ownership sur chaque action -> un user ne peut voir que ses portefeuilles.
    public class PortfoliosController : BaseController
    {
        public PortfoliosController(IApiService api, ILogger<PortfoliosController> logger)
            : base(api, logger) { }

        // GET /Portfolios -> liste tous les portefeuilles de l'user avec leur valeur actuelle
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

                    // Prix actuel -> fallback sur prix d'achat si FMP ne répond pas
                    decimal prix = await ApiService.GetLatestPriceAsync(asset.Ticker)
                                    ?? pos.AvgBuyPrice;
                    bool isUsd = AssetHelper.IsUsd(asset);
                    decimal val = prix * pos.Quantity;

                    // Convertit en EUR si l'actif est en USD
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
        
        // GET /Portfolios/Details/{id} -> affiche les positions d'un portefeuille
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var portfolio = await ApiService.GetPortfolioByIdAsync(id);

            // Portefeuille inexistant -> liste
            if (portfolio == null)
                return RedirectToAction("Index");

            // Ownership check -> un user ne peut pas voir le portefeuille d'un autre
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

                // Valeur en EUR
                decimal valEur = isUsd && taux > 0 ? valDev / taux : valDev;
                // Coût d'achat en EUR
                decimal coutEur = isUsd && taux > 0
                    ? pos.AvgBuyPrice * pos.Quantity / taux
                    : pos.AvgBuyPrice * pos.Quantity;
                decimal pnlEur = valEur - coutEur;
                // P&L en % -> (gain / coût) × 100
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

            // Calcule le poids de chaque position dans le total
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

        // GET /Portfolios/Create -> formulaire de création
        [HttpGet]
        public IActionResult Create()
            => View(new PortfolioCreateViewModel());

        // POST /Portfolios/Create -> crée le portefeuille via le backend
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PortfolioCreateViewModel model)
        {
            // ModelState.IsValid -> vérifie les [Required] du ViewModel
            if (!ModelState.IsValid) return View(model);

            var created = await ApiService.CreatePortfolioAsync(
                model.Name, model.Currency, GetUserId() ?? 0);

            if (created == null)
            {
                ModelState.AddModelError(string.Empty, "Erreur lors de la création.");
                return View(model);
            }

            // SetSuccess -> TempData -> affiché sur la page suivante après redirect
            SetSuccess($"Portefeuille « {created.Name} » créé !");
            return RedirectToAction(nameof(Details), new { id = created.Id });
        }

        // GET /Portfolios/Edit/{id} -> formulaire de modification
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var p = await ApiService.GetPortfolioByIdAsync(id);
            // Ownership check -> NotFound si pas le bon user
            if (p == null || p.UserId != GetUserId()) return NotFound();

            return View(new PortfolioEditViewModel
            {
                Id = p.Id,
                Name = p.Name,
                Currency = p.Currency
            });
        }

        // POST /Portfolios/Edit -> met à jour nom et devise
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

        // POST /Portfolios/Delete -> supprime un portefeuille vide
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
            // DeletePortfolioAsync retourne bool -> vérifier le résultat
            // Si positions présentes -> backend retourne 400 -> false
            SetSuccess($"Portefeuille « {p.Name} » supprimé.");
            return RedirectToAction(nameof(Index));
        }
    }
}
