using Marke_tPortfolio_Analytics_web.Helpers;
using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using MarketPortfolioAnalytics.Models;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    public class AssetsController : BaseController
    {
        // Affiche la heatmap des actifs et gère l'import depuis FMP.
        // Utilise GetQuoteAsync pour avoir prix + variation journalière en temps réel.
        public AssetsController(IApiService api, ILogger<AssetsController> logger)
            : base(api, logger) { }

        // GET /Assets -> affiche tous les actifs en heatmap + tableau
        // q = recherche textuelle, type = filtre "Stock" ou "Bond"
        [HttpGet]
        public async Task<IActionResult> Index(string? q = null, string? type = null)
        {
            // Charge tous les actifs et le taux EUR/USD
            var assets = await ApiService.GetAllAssetsAsync();
            var tauxEurUsd = await ApiService.GetExchangeRateAsync("EUR", "USD");

            var cards = new List<AssetCard>();

            foreach (var asset in assets)
            {
                // Un appel FMP par actif -> prix + variation journalière
                // (0, 0) si FMP ne répond pas -> prix affiché à 0
                var (prixNatif, variation) = await ApiService.GetQuoteAsync(asset.Ticker);
                // true si l'actif est coté en USD (via AssetHelper.IsUsd)
                bool isUsd = AssetHelper.IsUsd(asset);

                // Conversion en EUR et USD pour l'affichage dual-devise
                decimal prixEur = isUsd && tauxEurUsd > 0 ? prixNatif / tauxEurUsd : prixNatif;
                decimal prixUsd = !isUsd && tauxEurUsd > 0 ? prixNatif * tauxEurUsd : prixNatif;

                cards.Add(new AssetCard
                {
                    Id = asset.Id,
                    Ticker = asset.Ticker,
                    Nom = asset.Name,
                    TypeLabel = AssetHelper.GetTypeLabel(asset), //"Stock" ou "Bond"
                    Exchange = asset.Exchange ?? string.Empty,
                    DeviseNative = isUsd ? "USD" : "EUR",
                    PrixNatif = Math.Round(prixNatif, 2),
                    PrixEur = Math.Round(prixEur, 2),
                    PrixUsd = Math.Round(prixUsd, 2),
                    VariationJour = Math.Round(variation, 2) // % journalier ex: +1.24
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

        // GET /Assets/Details/{id} -> affiche la fiche détaillée d'un actif
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

            // Cherche dans quels portefeuilles de l'user cet actif est détenu
            int userId = GetUserId() ?? 0;
            var portfolios = await ApiService.GetPortfoliosByUserAsync(userId);
            var portosAvecActif = new List<string>();

            foreach (var p in portfolios)
            {
                var positions = await ApiService.GetPositionsByPortfolioAsync(p.Id);
                // Any() -> true si au moins une position avec cet assetId
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
                // EstDansPortefeuille -> propriété calculée : PortefeuillesDetenant.Any()
            });
        }


        // POST /Assets/ImportFmp -> importe un actif depuis FMP
        // assetType = "stock" ou "bond" -> détermine l'endpoint backend appelé
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
                // Essaie d'importer comme obligation
                asset = await ApiService.ImportBondFromFmpAsync(t);

                if (asset == null)
                {
                    // FMP n'a pas trouvé comme obligation -> fallback sur stock
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

            // Redirige vers la fiche de l'actif importé
            return RedirectToAction(nameof(Details), new { id = asset.Id });
        }

        // POST /Assets/Delete/{id} -> supprime un actif non utilisé dans un portefeuille
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var ok = await ApiService.DeleteAssetAsync(id);
            if (!ok)
            {
                // false -> actif utilisé dans un portefeuille -> impossible de supprimer
                SetError("Suppression impossible. L'actif est peut-être utilisé dans un portefeuille.");
                return RedirectToAction(nameof(Details), new { id });
            }
            SetSuccess("Actif supprimé.");
            return RedirectToAction(nameof(Index));
        }
    }
}
