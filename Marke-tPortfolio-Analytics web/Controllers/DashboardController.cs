using Marke_tPortfolio_Analytics_web.Helpers;
using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    // Affiche le tableau de bord principal de l'utilisateur connecté.
    // Calcule la valeur totale, le rendement, l'allocation et le score investisseur.
    // Appelle aussi AnalyzePortfolioAsync pour avoir Sharpe et MaxDrawdown réels.
    public class DashboardController : BaseController
    {
        // Couleurs fixes pour le graphique donut d'allocation
        // Utilisées dans l'ordre -> premier actif = vert, deuxième = bleu, etc.
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
            vm.ValeurTotaleUsd = taux > 0
                ? Math.Round(valeurTotale * taux, 2)
                : valeurTotale;

            foreach (var p in allPositions)
                p.Poids = valeurTotale > 0
                    ? Math.Round(p.ValeurEur / valeurTotale * 100, 1)
                    : 0;

            vm.Positions = allPositions;

            decimal coutTotal = allPositions.Sum(p =>
                p.Devise == "USD" && taux > 0
                    ? p.Quantite * p.AvgBuyPrice / taux
                    : p.Quantite * p.AvgBuyPrice);

            vm.RendementTotal = coutTotal > 0
                ? Math.Round((valeurTotale / coutTotal - 1) * 100, 2)
                : 0;

            // CORRECTION 1 -> regroupe par ticker avant de construire le donut
            // Sans ça : si AAPL est dans 2 portefeuilles, il apparaît 2 fois
            // dans la légende et les arcs se superposent -> donut incorrect
            vm.Allocation = allPositions
                .GroupBy(p => p.Ticker)
                .Select(g => new
                {
                    Ticker = g.Key,
                    ValeurEur = g.Sum(p => p.ValeurEur)
                })
                .OrderByDescending(g => g.ValeurEur)
                .Take(6)
                .Select((g, i) => new AllocationItem
                {
                    Ticker = g.Ticker,
                    Poids = valeurTotale > 0
                        ? Math.Round(g.ValeurEur / valeurTotale * 100, 1)
                        : 0,
                    Couleur = DonutColors[i % DonutColors.Length]
                }).ToList();

            await ChargerMetriquesAnalytiques(vm, portfolios, valeurTotale);

            // ── Score investisseur ────────────────────────────────────────────
            int score = 20;
            var pills = new List<string>();

            if (vm.SharpeRatio > 1)
            { score += 25; pills.Add($"Sharpe Ratio: {vm.SharpeRatio:F2} · Efficient"); }
            else
                pills.Add($"Sharpe Ratio: {vm.SharpeRatio:F2} · Sous-optimal");

            if (allPositions.Count >= 4)
            { score += 15; pills.Add($"{allPositions.Count} actifs · Diversifié"); }
            else
                pills.Add($"{allPositions.Count} actif(s) · Concentré");

            if (allPositions.Select(p => p.TypeActif).Distinct().Count() >= 2)
            { score += 10; pills.Add("Allocation · Multi-actifs"); }
            else
                // CORRECTION 2 -> renommé "Mono-actif" en "Actions uniquement"
                // "Mono-actif" était trompeur avec "5 actifs · Diversifié" juste à côté
                // "Actions uniquement" décrit mieux la réalité : une seule classe d'actifs
                pills.Add("Allocation · Actions uniquement");

            // CORRECTION 3 -> vérification obligataire élargie
            // JNJ, KO, XOM jouent le rôle d'ancre défensive dans les templates
            // mais sont importés comme Stock via FMP (pas comme Bond)
            // -> on les reconnaît explicitement comme actifs défensifs
            if (allPositions.Any(p => p.TypeActif == "Bond"
                || p.Ticker == "JNJ"
                || p.Ticker == "KO"
                || p.Ticker == "XOM"))
            { score += 10; pills.Add("Allocation · Exposition défensive"); }
            else
                pills.Add("Allocation · Sans actif défensif");

            if (vm.RendementTotal > 4)
            { score += 20; pills.Add($"+{vm.RendementTotal:F1}% · Au-dessus de l'inflation"); }
            else
                pills.Add($"{vm.RendementTotal:F1}% · Modéré");

            vm.ScoreInvestisseur = Math.Min(score, 100);
            vm.ScorePills = pills.Take(4).ToList();

            vm.ScoreNiveau = vm.ScoreInvestisseur switch
            {
                < 40 => "Sous-performant",
                < 70 => "En progression",
                < 85 => "Solide",
                _ => "Exemplaire"
            };

            vm.ScoreMessage = vm.ScoreInvestisseur switch
            {
                < 40 => "Le portefeuille présente une diversification insuffisante et une efficacité risque/rendement limitée.",
                < 70 => "Structure correcte. Une diversification accrue permettrait d'améliorer le profil de risque global.",
                < 85 => "Portefeuille solide et relativement bien équilibré. Des optimisations ciblées peuvent encore améliorer la performance.",
                _ => "Portefeuille performant, avec une gestion du risque maîtrisée et une allocation efficiente."
            };

            return View(vm);
        }

        // Sharpe et MaxDrawdown pondérés depuis le backend.
        // Pour chaque portefeuille, on appelle l'API Analytics sur 1 an.
        // On pondère par la valeur de marché -> métriques globales cohérentes.
        // Si l'API ne retourne rien (pas assez d'historique) -> on affiche 0.
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

            double sharpePondere = 0;
            double maxDrawdownPondere = 0;
            decimal valeurAnalysee = 0;

            foreach (var portfolio in portfolios)
            {
                try
                {
                    var analyse = await ApiService.AnalyzePortfolioAsync(
                        portfolio.Id, dateDebut, dateFin, riskFreeRate: 0.045);

                    if (analyse == null) continue;

                    decimal poidsPortfolio = analyse.TotalCurrentValue;
                    if (poidsPortfolio <= 0) continue;

                    sharpePondere += analyse.SharpeRatio * (double)poidsPortfolio;
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
                vm.SharpeRatio = Math.Round(
                    (decimal)(sharpePondere / (double)valeurAnalysee), 2);
                vm.MaxDrawdown = Math.Round(
                    (decimal)(maxDrawdownPondere / (double)valeurAnalysee), 2);
            }
            else
            {
                vm.SharpeRatio = 0;
                vm.MaxDrawdown = 0;
            }
        }
    }
}