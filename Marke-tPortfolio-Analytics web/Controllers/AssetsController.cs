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
                var (prixNatif, variation) = await ApiService.GetQuoteAsync(asset.Ticker);
                bool isUsd = AssetHelper.IsUsd(asset);

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
                    VariationJour = Math.Round(variation, 2)
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
            var (prixNatif, variation) = await ApiService.GetQuoteAsync(asset.Ticker);
            bool isUsd = AssetHelper.IsUsd(asset);

            decimal prixEur = isUsd && tauxEurUsd > 0 ? prixNatif / tauxEurUsd : prixNatif;
            decimal prixUsd = !isUsd && tauxEurUsd > 0 ? prixNatif * tauxEurUsd : prixNatif;

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
                VariationJour = Math.Round(variation, 2),
                TauxEurUsd = tauxEurUsd,
                PortefeuillesDetenant = portosAvecActif
            });
        }

        // ── IMPORT DEPUIS FMP ─────────────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportFmp(string ticker, string assetType = "stock")
        {
            if (string.IsNullOrWhiteSpace(ticker))
            {
                SetError("Le ticker est requis.");
                return RedirectToAction(nameof(Index));
            }

            string t = ticker.Trim().ToUpper();
            Asset? asset = null;

            if (assetType == "bond")
            {
                asset = await ApiService.ImportBondFromFmpAsync(t);

                if (asset == null)
                {
                    // Fallback : FMP n'a pas trouvé comme obligation
                    // → on essaie comme action
                    asset = await ApiService.ImportStockFromFmpAsync(t);

                    if (asset != null)
                        SetSuccess($"« {t} » importé comme action (pas trouvé comme obligation).");
                    else
                    {
                        SetError($"Impossible d'importer « {t} ». Vérifiez le ticker sur FMP.");
                        return RedirectToAction(nameof(Index));
                    }
                }
                else
                {
                    SetSuccess($"Obligation « {asset.Ticker} — {asset.Name} » importée !");
                }
            }
            else
            {
                asset = await ApiService.ImportStockFromFmpAsync(t);

                if (asset == null)
                {
                    SetError($"Impossible d'importer « {t} ». Vérifiez le ticker sur FMP.");
                    return RedirectToAction(nameof(Index));
                }

                SetSuccess($"Action « {asset.Ticker} — {asset.Name} » importée !");
            }

            return RedirectToAction(nameof(Details), new { id = asset.Id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var ok = await ApiService.DeleteAssetAsync(id);
            if (!ok)
            {
                SetError("Suppression impossible. L'actif est peut-être utilisé dans un portefeuille.");
                return RedirectToAction(nameof(Details), new { id });
            }
            SetSuccess("Actif supprimé.");
            return RedirectToAction(nameof(Index));
        }
    }
}
