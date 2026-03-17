using Marke_tPortfolio_Analytics_web.ViewModels;
using Marke_tPortfolio_Analytics_web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    /// <summary>
    /// Dashboard principal — vue consolidée de tous les portefeuilles.
    /// Calcule le Score Investisseur et les KPIs globaux côté serveur.
    /// </summary>
    public class DashboardController : BaseController
    {
        private readonly IApiService _api;
        private readonly ILogger<DashboardController> _logger;

        // Couleurs d'allocation (donut)
        private static readonly string[] DonutColors =
            { "#00d084", "#3b82f6", "#f59e0b", "#8b5cf6", "#f43f5e", "#06b6d4" };

        public DashboardController(IApiService api, ILogger<DashboardController> logger)
        {
            _api = api;
            _logger = logger;
        }

        // GET /Dashboard
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            int userId = GetUserId();

            // Récupérer les portefeuilles de l'utilisateur
            var portfolios = await _api.GetPortfoliosByUserAsync(userId);
            var tauxEurUsd = await _api.GetExchangeRateAsync("EUR", "USD");

            var vm = new DashboardViewModel
            {
                UserName = GetUserName(),
                TauxEurUsd = tauxEurUsd,
                Portfolios = portfolios ?? new()
            };

            if (portfolios == null || !portfolios.Any())
            {
                // Pas de portefeuille — onboarding
                vm.ScoreInvestisseur = 0;
                vm.ScoreNiveau = "Vide";
                vm.ScoreMessage = "Créez votre premier portefeuille pour commencer l'analyse.";
                return View(vm);
            }

            // Positions de tous les portefeuilles
            var allPositions = new List<PositionDashboard>();
            decimal valeurTotaleEur = 0m;

            foreach (var portfolio in portfolios)
            {
                var positions = await _api.GetPositionsByPortfolioAsync(portfolio.Id);
                if (positions == null) continue;

                foreach (var pos in positions)
                {
                    // Récupérer le prix actuel via FMP
                    var asset = await _api.GetAssetByIdAsync(pos.AssetId);
                    if (asset == null) continue;

                    var latestPrice = await _api.GetLatestPriceAsync(asset.Symbol);
                    decimal prixActuel = latestPrice ?? (decimal)pos.PurchasePrice;

                    // Conversion en EUR si nécessaire
                    bool isUsd = !asset.Symbol.EndsWith(".PA");
                    decimal valeurDevise = prixActuel * (decimal)pos.Quantity;
                    decimal valeurEur = isUsd && tauxEurUsd > 0
                        ? valeurDevise / tauxEurUsd
                        : valeurDevise;

                    decimal prixMoyenEur = isUsd && tauxEurUsd > 0
                        ? (decimal)pos.PurchasePrice / tauxEurUsd
                        : (decimal)pos.PurchasePrice;

                    decimal pnlPct = prixMoyenEur > 0
                        ? (valeurEur / ((decimal)pos.Quantity * prixMoyenEur) - 1) * 100
                        : 0;

                    valeurTotaleEur += valeurEur;

                    allPositions.Add(new PositionDashboard
                    {
                        Ticker = asset.Symbol,
                        Nom = asset.Name,
                        Quantite = (decimal)pos.Quantity,
                        PrixMoyen = (decimal)pos.PurchasePrice,
                        PrixActuel = prixActuel,
                        ValeurEur = Math.Round(valeurEur, 2),
                        PnlPct = Math.Round(pnlPct, 2),
                        Devise = isUsd ? "USD" : "EUR",
                        Type = asset.AssetType ?? "Stock"
                    });
                }
            }

            vm.ValeurTotaleEur = Math.Round(valeurTotaleEur, 2);
            vm.ValeurTotaleUsd = tauxEurUsd > 0
                ? Math.Round(valeurTotaleEur * tauxEurUsd, 2)
                : vm.ValeurTotaleEur;

            // Poids de chaque position
            foreach (var p in allPositions)
                p.Poids = valeurTotaleEur > 0
                    ? Math.Round(p.ValeurEur / valeurTotaleEur * 100, 1)
                    : 0;

            vm.Positions = allPositions;

            // Rendement global approximatif
            decimal coutTotal = allPositions.Sum(p =>
                p.Devise == "USD" && tauxEurUsd > 0
                    ? p.Quantite * p.PrixMoyen / tauxEurUsd
                    : p.Quantite * p.PrixMoyen);

            vm.RendementTotal = coutTotal > 0
                ? Math.Round((valeurTotaleEur / coutTotal - 1) * 100, 2)
                : 0;

            // Allocation donut (top 6)
            var topPositions = allPositions
                .OrderByDescending(p => p.ValeurEur)
                .Take(6)
                .ToList();

            vm.Allocation = topPositions.Select((p, i) => new AllocationItem
            {
                Ticker = p.Ticker,
                Poids = p.Poids,
                Couleur = DonutColors[i % DonutColors.Length]
            }).ToList();

            // ── Score Investisseur ──────────────────────────────────────
            vm.SharpeRatio = CalculerSharpeApproximatif(allPositions);
            vm.MaxDrawdown = -14.2m; // Placeholder Phase 3 — calculé réellement en Phase 5 Analytics

            int score = 0;
            var pills = new List<string>();

            if (vm.SharpeRatio > 1) { score += 25; pills.Add($"Sharpe {vm.SharpeRatio:F2} ✓"); }
            else pills.Add($"Sharpe {vm.SharpeRatio:F2} ✗");

            if (allPositions.Count >= 4) { score += 15; pills.Add($"{allPositions.Count} actifs ✓"); }
            else pills.Add($"{allPositions.Count} actif(s) ✗");

            var secteurs = allPositions.Select(p => p.Type).Distinct().Count();
            if (secteurs >= 2) { score += 10; pills.Add("Diversifié ✓"); }

            var hasOblig = allPositions.Any(p => p.Type == "Bond");
            if (hasOblig) { score += 10; pills.Add("Obligations ✓"); }
            else pills.Add("Ajouter obligations");

            if (vm.RendementTotal > 4) { score += 20; pills.Add($"+{vm.RendementTotal:F1}% rend. ✓"); }
            else pills.Add($"{vm.RendementTotal:F1}% rend.");

            score = Math.Min(score + 20, 100); // Bonus base portefeuille existant

            vm.ScoreInvestisseur = score;
            vm.ScorePills = pills.Take(4).ToList();
            vm.ScoreNiveau = score switch
            {
                < 40 => "Insuffisant 🔴",
                < 70 => "Moyen 🟡",
                < 85 => "Bon 🟢",
                _ => "Excellent 🏆"
            };
            vm.ScoreMessage = score switch
            {
                < 40 => "Diversifiez votre portefeuille pour améliorer votre score.",
                < 70 => "Bonne base. Ajoutez des obligations pour réduire le risque.",
                < 85 => "Portefeuille solide. Optimisez l'allocation pour atteindre Excellent.",
                _ => "Portefeuille exemplaire. Maintenez cette allocation équilibrée."
            };

            return View(vm);
        }

        // ── Helpers privés ─────────────────────────────────────────────

        private static decimal CalculerSharpeApproximatif(List<PositionDashboard> positions)
        {
            if (!positions.Any()) return 0;
            decimal rendMoyen = positions.Average(p => p.PnlPct);
            if (rendMoyen <= 0) return 0;
            decimal volatApprox = 18m; // Placeholder Phase 3 — Vol réelle calculée en Phase 5
            decimal riskFree = 3.5m;
            return volatApprox > 0
                ? Math.Round((rendMoyen - riskFree) / volatApprox, 2)
                : 0;
        }
    }
}
