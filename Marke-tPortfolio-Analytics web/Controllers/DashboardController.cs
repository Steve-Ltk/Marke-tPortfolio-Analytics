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
            // GetUserId() vient de BaseController -> lit la session
            int userId = GetUserId() ?? 0;

            // Sécurité : si pas d'userId en session -> redirect Login
            if (userId == 0) return RedirectToAction("Login", "Auth");

            // Charge les portefeuilles et le taux EUR/USD en parallèle
            var portfolios = await ApiService.GetPortfoliosByUserAsync(userId);
            var taux = await ApiService.GetExchangeRateAsync("EUR", "USD");

            var vm = new DashboardViewModel
            {
                UserName = GetUserName(), // depuis la session via BaseController
                TauxEurUsd = taux,
                Portfolios = portfolios
            };

            // Pas de portefeuilles -> affiche l'écran d'onboarding vide
            if (!portfolios.Any())
            {
                vm.ScoreInvestisseur = 0;
                vm.ScoreNiveau = "Vide";
                vm.ScoreMessage = "Créez votre premier portefeuille pour commencer.";
                return View(vm);
            }

            var allPositions = new List<PositionDashboard>();
            decimal valeurTotale = 0m;

            // Parcourt chaque portefeuille et chaque position pour calculer la valeur totale
            foreach (var portfolio in portfolios)
            {
                var positions = await ApiService.GetPositionsByPortfolioAsync(portfolio.Id);
                if (positions == null) continue;

                foreach (var pos in positions)
                {
                    // Récupère l'actif pour avoir sa devise (USD ou EUR)
                    var asset = await ApiService.GetAssetByIdAsync(pos.AssetId);
                    if (asset == null) continue;

                    // Prix actuel -> si FMP ne répond pas, on utilise le prix d'achat
                    var latestPrice = await ApiService.GetLatestPriceAsync(asset.Ticker);
                    decimal prixActuel = latestPrice ?? pos.AvgBuyPrice;

                    // IsUsd -> true si l'actif est coté en USD (via AssetHelper)
                    bool isUsd = AssetHelper.IsUsd(asset);
                    // Valeur en devise native (ex: 10 actions AAPL × 182$ = 1820$)
                    decimal valDev = prixActuel * pos.Quantity;
                    // Conversion en EUR si l'actif est en USD
                    decimal valEur = isUsd && taux > 0 ? valDev / taux : valDev;
                    // Coût d'achat converti en EUR pour calculer le P&L
                    decimal coutEur = isUsd && taux > 0
                        ? pos.AvgBuyPrice * pos.Quantity / taux
                        : pos.AvgBuyPrice * pos.Quantity;

                    // P&L en % -> (valeur actuelle / coût - 1) × 100
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
                        TypeActif = AssetHelper.GetTypeLabel(asset) // "Stock" ou "Bond"
                    });
                }
            }

            vm.ValeurTotaleEur = Math.Round(valeurTotale, 2);
            // Conversion valeur totale en USD pour l'affichage dual-devise
            vm.ValeurTotaleUsd = taux > 0 ? Math.Round(valeurTotale * taux, 2) : valeurTotale;

            // Calcule le poids de chaque position dans le portefeuille total
            foreach (var p in allPositions)
                p.Poids = valeurTotale > 0
                    ? Math.Round(p.ValeurEur / valeurTotale * 100, 1) : 0;

            vm.Positions = allPositions;

            // Coût total d'achat (toutes positions confondues, en EUR)
            decimal coutTotal = allPositions.Sum(p =>
                p.Devise == "USD" && taux > 0
                    ? p.Quantite * p.AvgBuyPrice / taux
                    : p.Quantite * p.AvgBuyPrice);

            // Rendement total depuis l'achat en %
            vm.RendementTotal = coutTotal > 0
                ? Math.Round((valeurTotale / coutTotal - 1) * 100, 2) : 0;

            // Allocation -> top 6 positions par valeur pour le graphique donut
            vm.Allocation = allPositions
                .OrderByDescending(p => p.ValeurEur)
                .Take(6)
                .Select((p, i) => new AllocationItem
                {
                    Ticker = p.Ticker,
                    Poids = p.Poids,
                    Couleur = DonutColors[i % DonutColors.Length]
                }).ToList();

            // Sharpe et MaxDrawdown réels depuis le backend Analytics
            // Moyenne pondérée par valeur de marché de chaque portefeuille
            await ChargerMetriquesAnalytiques(vm, portfolios, valeurTotale);

            // Score investisseur
            int score = 20;
            var pills = new List<string>(); // badges affichés sous le score

            if (vm.SharpeRatio > 1)
            { score += 25; pills.Add($"Sharpe Ratio: {vm.SharpeRatio:F2} • Efficient"); }
            else
                pills.Add($"Sharpe Ratio: {vm.SharpeRatio:F2} • Sous-optimal");

            if (allPositions.Count >= 4)
            { score += 15; pills.Add($"{allPositions.Count} actifs • Diversifié"); }
            else
                pills.Add($"{allPositions.Count} actif(s) • Concentré");

            if (allPositions.Select(p => p.TypeActif).Distinct().Count() >= 2)
            { score += 10; pills.Add("Allocation • Multi-actifs"); }
            else
                pills.Add("Allocation • Mono-actif")

            if (allPositions.Any(p => p.TypeActif == "Bond"))
            { score += 10; pills.Add("Allocation • Exposition obligataire"); }
            else
                pills.Add("Allocation • Sans obligataire");

            if (vm.RendementTotal > 4)
            { score += 20; pills.Add($"+{vm.RendementTotal:F1}% • Au-dessus de l’inflation"); }
            else
                pills.Add($"{vm.RendementTotal:F1}% • Modéré");

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

        // Sharpe et MaxDrawdown pondérés depuis le backend
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
            // Rien à calculer si pas de valeur ou pas de portefeuilles
            if (valeurTotale <= 0 || !portfolios.Any())
            {
                vm.SharpeRatio = 0;
                vm.MaxDrawdown = 0;
                return;
            }

            var dateFin = DateTime.UtcNow;
            var dateDebut = dateFin.AddYears(-1); // analyse sur 1 an glissant

            double sharpeePondere = 0;
            double maxDrawdownPondere = 0;
            decimal valeurAnalysee = 0;

            foreach (var portfolio in portfolios)
            {
                try
                {
                    // Appelle le backend Analytics pour ce portefeuille
                    var analyse = await ApiService.AnalyzePortfolioAsync(
                        portfolio.Id, dateDebut, dateFin, riskFreeRate: 0.045);

                    if (analyse == null) continue;

                    // Valeur du portefeuille -> utilisée comme poids dans la moyenne
                    decimal poidsPortfolio = analyse.TotalCurrentValue;
                    if (poidsPortfolio <= 0) continue;

                    // Accumule les métriques pondérées par valeur de marché
                    sharpeePondere += analyse.SharpeRatio * (double)poidsPortfolio;
                    maxDrawdownPondere += analyse.MaxDrawdown * (double)poidsPortfolio;
                    valeurAnalysee += poidsPortfolio;
                }
                catch (Exception ex)
                {
                    // Si un portefeuille plante -> on l'ignore et on continue
                    Logger.LogWarning(ex,
                        "Impossible de charger les métriques analytiques pour le portefeuille {Id}",
                        portfolio.Id);
                }
            }

            if (valeurAnalysee > 0)
            {
                // Moyenne pondérée = somme(métrique × poids) / somme(poids)
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
