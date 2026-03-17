using Marke_tPortfolio_Analytics_web.Helpers;
using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using MarketPortfolioAnalytics.Models;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    public class AssetsController : BaseController
    {
        public AssetsController(IApiService api, ILogger<AssetsController> logger)
            : base(api, logger) { }

        // ── INDEX — heatmap de tous les actifs ────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Index(string? q = null, string? type = null)
        {
            var assets = await ApiService.GetAllAssetsAsync();
            var tauxEurUsd = await ApiService.GetExchangeRateAsync("EUR", "USD");

            var cards = new List<AssetCard>();

            foreach (var asset in assets)
            {
                // Prix temps réel — null si FMP indisponible
                var prix = await ApiService.GetLatestPriceAsync(asset.Ticker);
                bool isUsd = AssetHelper.IsUsd(asset);

                decimal prixNatif = prix ?? 0m;
                decimal prixEur = isUsd && tauxEurUsd > 0 ? prixNatif / tauxEurUsd : prixNatif;
                decimal prixUsd = !isUsd && tauxEurUsd > 0 ? prixNatif * tauxEurUsd : prixNatif;

                cards.Add(new AssetCard
                {
                    Id = asset.Id,
                    Ticker = asset.Ticker,
                    Nom = asset.Name,
                    TypeLabel = AssetHelper.GetTypeLabel(asset),
                    Exchange = asset.Exchange ?? string.Empty,
                    DeviseNative = isUsd ? "USD" : "EUR",
                    PrixNatif = Math.Round(prixNatif, 2),
                    PrixEur = Math.Round(prixEur, 2),
                    PrixUsd = Math.Round(prixUsd, 2),
                    VariationJour = 0m  // Placeholder — FMP variation endpoint Phase 5
                });
            }

            return View(new AssetIndexViewModel
            {
                Assets = cards,
                TauxEurUsd = tauxEurUsd,
                Recherche = q ?? string.Empty,
                FiltreType = type ?? "Tous"
            });
        }

        // ── DETAILS ───────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var asset = await ApiService.GetAssetByIdAsync(id);
            if (asset == null) return NotFound();

            var tauxEurUsd = await ApiService.GetExchangeRateAsync("EUR", "USD");
            var prix = await ApiService.GetLatestPriceAsync(asset.Ticker);
            bool isUsd = AssetHelper.IsUsd(asset);

            decimal prixNatif = prix ?? 0m;
            decimal prixEur = isUsd && tauxEurUsd > 0 ? prixNatif / tauxEurUsd : prixNatif;
            decimal prixUsd = !isUsd && tauxEurUsd > 0 ? prixNatif * tauxEurUsd : prixNatif;

            // Portefeuilles de l'utilisateur qui détiennent cet actif
            int userId = GetUserId() ?? 0;
            var portfolios = await ApiService.GetPortfoliosByUserAsync(userId);
            var portosAvecActif = new List<string>();

            foreach (var p in portfolios)
            {
                var positions = await ApiService.GetPositionsByPortfolioAsync(p.Id);
                if (positions?.Any(pos => pos.AssetId == id) == true)
                    portosAvecActif.Add(p.Name);
            }

            return View(new AssetDetailsViewModel
            {
                Asset = asset,
                TypeLabel = AssetHelper.GetTypeLabel(asset),
                PrixActuel = Math.Round(prixNatif, 2),
                PrixEur = Math.Round(prixEur, 2),
                PrixUsd = Math.Round(prixUsd, 2),
                VariationJour = 0m,
                TauxEurUsd = tauxEurUsd,
                PortefeuillesDetenant = portosAvecActif
            });
        }

        // ── IMPORT DEPUIS FMP ─────────────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportFmp(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
            {
                SetError("Le ticker est requis.");
                return RedirectToAction(nameof(Index));
            }

            var asset = await ApiService.ImportStockFromFmpAsync(ticker.Trim().ToUpper());
            if (asset == null)
            {
                SetError($"Impossible d'importer « {ticker.ToUpper()} ». Vérifiez que le ticker est valide sur FMP.");
                return RedirectToAction(nameof(Index));
            }

            SetSuccess($"Actif « {asset.Ticker} — {asset.Name} » importé avec succès !");
            return RedirectToAction(nameof(Details), new { id = asset.Id });
        }
    }
}
