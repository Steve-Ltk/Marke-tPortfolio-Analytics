using Marke_tPortfolio_Analytics_web.Helpers;
using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    public class DashboardController : BaseController
    {
        private static readonly string[] DonutColors =
            { "#00d084", "#3b82f6", "#f59e0b", "#8b5cf6", "#f43f5e", "#06b6d4" };

        public DashboardController(IApiService api, ILogger<DashboardController> logger)
            : base(api, logger) { }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            int userId = GetUserId() ?? 0;
            if (userId == 0) return RedirectToAction("Login", "Auth");

            var portfolios = await ApiService.GetPortfoliosByUserAsync(userId);
            var taux = await ApiService.GetExchangeRateAsync("EUR", "USD");

            var vm = new DashboardViewModel
            {
                UserName = GetUserName(),
                TauxEurUsd = taux,
                Portfolios = portfolios
            };

            if (!portfolios.Any())
            {
                vm.ScoreInvestisseur = 0;
                vm.ScoreNiveau = "Vide";
                vm.ScoreMessage = "Créez votre premier portefeuille pour commencer.";
                return View(vm);
            }

            var allPositions = new List<PositionDashboard>();
            decimal valeurTotale = 0m;

            foreach (var portfolio in portfolios)
            {
                var positions = await ApiService.GetPositionsByPortfolioAsync(portfolio.Id);
                if (positions == null) continue;

                foreach (var pos in positions)
                {
                    var asset = await ApiService.GetAssetByIdAsync(pos.AssetId);
                    if (asset == null) continue;

                    var latestPrice = await ApiService.GetLatestPriceAsync(asset.Ticker);
                    decimal prixActuel = latestPrice ?? pos.AvgBuyPrice;
                    bool isUsd = AssetHelper.IsUsd(asset);

                    decimal valDev = prixActuel * pos.Quantity;
                    decimal valEur = isUsd && taux > 0 ? valDev / taux : valDev;
                    decimal coutEur = isUsd && taux > 0
                        ? pos.AvgBuyPrice * pos.Quantity / taux
                        : pos.AvgBuyPrice * pos.Quantity;
                    decimal pnlPct = coutEur > 0 ? (valEur / coutEur - 1) * 100 : 0;

                    valeurTotale += valEur;

                    allPositions.Add(new PositionDashboard
                    {
                        Ticker = asset.Ticker,
                        Nom = asset.Name,
                        Quantite = pos.Quantity,
                        AvgBuyPrice = pos.AvgBuyPrice,
                        PrixActuel = Math.Round(prixActuel, 2),
                        ValeurEur = Math.Round(valEur, 2),
                        PnlPct = Math.Round(pnlPct, 2),
                        Devise = isUsd ? "USD" : "EUR",
                        TypeActif = AssetHelper.GetTypeLabel(asset)
                    });
                }
            }

            vm.ValeurTotaleEur = Math.Round(valeurTotale, 2);
            vm.ValeurTotaleUsd = taux > 0 ? Math.Round(valeurTotale * taux, 2) : valeurTotale;

            foreach (var p in allPositions)
                p.Poids = valeurTotale > 0
                    ? Math.Round(p.ValeurEur / valeurTotale * 100, 1) : 0;

            vm.Positions = allPositions;

            decimal coutTotal = allPositions.Sum(p =>
                p.Devise == "USD" && taux > 0
                    ? p.Quantite * p.AvgBuyPrice / taux
                    : p.Quantite * p.AvgBuyPrice);

            vm.RendementTotal = coutTotal > 0
                ? Math.Round((valeurTotale / coutTotal - 1) * 100, 2) : 0;

            vm.Allocation = allPositions
                .OrderByDescending(p => p.ValeurEur)
                .Take(6)
                .Select((p, i) => new AllocationItem
                {
                    Ticker = p.Ticker,
                    Poids = p.Poids,
                    Couleur = DonutColors[i % DonutColors.Length]
                }).ToList();

            // ── FIX : Sharpe et MaxDrawdown réels depuis le backend ───────────
            // On analyse chaque portefeuille sur 1 an et on calcule
            // une moyenne pondérée par valeur de marché.
            await ChargerMetriquesAnalytiques(vm, portfolios, valeurTotale);

            // ── Score investisseur ────────────────────────────────────────────
            int score = 20;
            var pills = new List<string>();

            if (vm.SharpeRatio > 1)
            { score += 25; pills.Add($"Sharpe {vm.SharpeRatio:F2} ✓"); }
            else
                pills.Add($"Sharpe {vm.SharpeRatio:F2} ✗");

            if (allPositions.Count >= 4)
            { score += 15; pills.Add($"{allPositions.Count} actifs ✓"); }
            else
                pills.Add($"{allPositions.Count} actif(s) ✗");

            if (allPositions.Select(p => p.TypeActif).Distinct().Count() >= 2)
            { score += 10; pills.Add("Diversifié ✓"); }

            if (allPositions.Any(p => p.TypeActif == "Bond"))
            { score += 10; pills.Add("Obligations ✓"); }
            else
                pills.Add("Ajouter obligations");

            if (vm.RendementTotal > 4)
            { score += 20; pills.Add($"+{vm.RendementTotal:F1}% ✓"); }
            else
                pills.Add($"{vm.RendementTotal:F1}% rend.");

            vm.ScoreInvestisseur = Math.Min(score, 100);
            vm.ScorePills = pills.Take(4).ToList();

            vm.ScoreNiveau = vm.ScoreInvestisseur switch
            {
                < 40 => "Insuffisant 🔴",
                < 70 => "Moyen 🟡",
                < 85 => "Bon 🟢",
                _ => "Excellent 🏆"
            };
            vm.ScoreMessage = vm.ScoreInvestisseur switch
            {
                < 40 => "Diversifiez votre portefeuille pour améliorer votre score.",
                < 70 => "Bonne base. Ajoutez des obligations pour réduire le risque.",
                < 85 => "Portefeuille solide. Optimisez l'allocation pour atteindre Excellent.",
                _ => "Portefeuille exemplaire. Maintenez cette allocation équilibrée."
            };

            return View(vm);
        }

        // ── Sharpe et MaxDrawdown pondérés depuis le backend ──────────────────
        // Pour chaque portefeuille, on appelle l'API Analytics sur 1 an.
        // On pondère Sharpe et MaxDrawdown par la valeur de marché de chaque
        // portefeuille, pour obtenir des métriques globales cohérentes.
        //
        // Si l'API ne retourne rien (pas assez d'historique), on affiche 0
        // plutôt qu'une valeur inventée.
        private async Task ChargerMetriquesAnalytiques(
            DashboardViewModel vm,
            List<MarketPortfolioAnalytics.Models.Portfolio> portfolios,
            decimal valeurTotale)
        {
            if (valeurTotale <= 0 || !portfolios.Any())
            {
                vm.SharpeRatio = 0;
                vm.MaxDrawdown = 0;
                return;
            }

            var dateFin = DateTime.UtcNow;
            var dateDebut = dateFin.AddYears(-1);

            double sharpeePondere = 0;
            double maxDrawdownPondere = 0;
            decimal valeurAnalysee = 0;

            foreach (var portfolio in portfolios)
            {
                try
                {
                    var analyse = await ApiService.AnalyzePortfolioAsync(
                        portfolio.Id, dateDebut, dateFin, riskFreeRate: 0.03);

                    if (analyse == null) continue;

                    // Valeur du portefeuille comme poids
                    decimal poidsPortfolio = analyse.TotalCurrentValue;
                    if (poidsPortfolio <= 0) continue;

                    sharpeePondere += analyse.SharpeRatio * (double)poidsPortfolio;
                    maxDrawdownPondere += analyse.MaxDrawdown * (double)poidsPortfolio;
                    valeurAnalysee += poidsPortfolio;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex,
                        "Impossible de charger les métriques analytiques pour le portefeuille {Id}",
                        portfolio.Id);
                }
            }

            if (valeurAnalysee > 0)
            {
                // Moyenne pondérée par valeur de marché
                vm.SharpeRatio = Math.Round(
                    (decimal)(sharpeePondere / (double)valeurAnalysee), 2);
                vm.MaxDrawdown = Math.Round(
                    (decimal)(maxDrawdownPondere / (double)valeurAnalysee), 2);
            }
            else
            {
                // Pas assez d'historique — on affiche 0 plutôt qu'une valeur inventée
                vm.SharpeRatio = 0;
                vm.MaxDrawdown = 0;
            }
        }
    }
}