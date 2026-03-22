using Marke_tPortfolio_Analytics_web.Helpers;
using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using MarketPortfolioAnalytics.Models.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{

    // Affiche la page Analytics et gère les 5 actions AJAX :
    // Analyse, Monte Carlo, Backtest, Optimisation Markowitz, Comparaison.
    // Les actions AJAX retournent du JSON -> consommé par analytics.js dans la vue.
    public class AnalyticsController : BaseController
    {
        public AnalyticsController(IApiService api, ILogger<AnalyticsController> logger)
            : base(api, logger) { }

         // GET /Analytics -> affiche la page principale avec les résultats d'analyse
         // portfolioId, dateDebut, dateFin, riskFree -> paramètres du formulaire (query string)
        [HttpGet]
        public async Task<IActionResult> Index(
            int? portfolioId = null,
            string? dateDebut = null,
            string? dateFin = null,
            double riskFree = 4.5)
        {
            int userId = GetUserId() ?? 0;
            var portfolios = await ApiService.GetPortfoliosByUserAsync(userId);

            var vm = new AnalyticsIndexViewModel
            {
                Portfolios = portfolios,
                // Sélectionne le portefeuille demandé ou le premier par défaut
                SelectedPortfolioId = portfolioId ?? (portfolios.FirstOrDefault()?.Id ?? 0),
                TauxSansRisque = riskFree
            };

            // Parse les dates si fournies -> sinon les valeurs par défaut du ViewModel s'appliquent
            if (DateTime.TryParse(dateDebut, out var dd)) vm.DateDebut = dd;
            if (DateTime.TryParse(dateFin, out var df)) vm.DateFin = df;
            var portfolio = portfolios.FirstOrDefault(p => p.Id == vm.SelectedPortfolioId);
            if (portfolio != null) vm.SelectedPortfolioName = portfolio.Name;

            if (vm.SelectedPortfolioId > 0)
            {
                // riskFree / 100 -> convertit 3.0% en 0.03 pour le backend
                vm.Analyse = await ApiService.AnalyzePortfolioAsync(
                    vm.SelectedPortfolioId,
                    vm.DateDebut,
                    vm.DateFin,
                    riskFree / 100.0);

                if (vm.Analyse != null)
                {
                    var a = vm.Analyse;
                    // Génère les insights qualitatifs depuis les métriques
                    // InsightHelper traduit un chiffre en niveau ("Bon", "Danger"...)
                    vm.InsightSharpe = InsightHelper.Sharpe(a.SharpeRatio);
                    vm.InsightVolatilite = InsightHelper.Volatilite(a.Volatility);
                    vm.InsightDrawdown = InsightHelper.MaxDrawdown(a.MaxDrawdown);
                    vm.InsightCagr = InsightHelper.Cagr(a.AnnualizedReturn);

                    // Niveau global = le pire niveau parmi les 4 métriques
                    var niveaux = new[]
                    {
                        vm.InsightSharpe.Niveau,
                        vm.InsightVolatilite.Niveau,
                        vm.InsightDrawdown.Niveau,
                        vm.InsightCagr.Niveau
                    };
                    vm.InsightNiveau = NiveauGlobal(niveaux);
                    vm.InsightGlobal = MessageGlobal(vm.InsightNiveau, a);
                }
            }

            return View(vm);
        }

        // fan chart (graphique en éventail): un graphique qui montre plusieurs scénarios possibles dans le futur
        // avec une zone d’incertitude.

        // MONTE CARLO JSON
        // MonteCarloRequest : HorizonDays (int), NumSimulations (int)
        // MonteCarloResult  : Percentile5, Percentile95, Median, VaR95,
        //                     ProbabilityOfLossPct, TimeSeries
        // POST /Analytics/MonteCarlo -> retourne JSON pour le fan chart
        // Appelé par analytics.js quand l'user clique "Lancer la simulation"
        [HttpPost]
        public async Task<IActionResult> MonteCarlo(
            int portfolioId,
            int numSimulations = 1000,
            int horizonDays = 252)
        {
            var portfolio = await ApiService.GetPortfolioByIdAsync(portfolioId);
            // Ownership check -> 403 si pas le bon user
            if (portfolio == null || portfolio.UserId != GetUserId())
                return Forbid();

            var result = await ApiService.RunMonteCarloAsync(portfolioId,
                new MonteCarloRequest
                {
                    NumSimulations = numSimulations,  
                    HorizonDays = horizonDays       
                });

            if (result == null)
                return Json(new { error = "Simulation impossible. Vérifiez que le portefeuille contient des positions avec historique." });

            // ProbabilityOfLossPct -> % de perte -> on inverse pour avoir % de gain
            double probGain = 100.0 - result.ProbabilityOfLossPct;
            var insight = InsightHelper.ProbGain(probGain);

            // TimeSeries → fan chart (prendre un point par semaine max 26 pts)
            var ts = result.TimeSeries ?? new();
            int step = Math.Max(1, ts.Count / 26);
            var fanP5 = ts.Where((_, i) => i % step == 0).Select(t => (double)t.P5).ToList();
            var fanMed = ts.Where((_, i) => i % step == 0).Select(t => (double)t.Median).ToList();
            var fanP95 = ts.Where((_, i) => i % step == 0).Select(t => (double)t.P95).ToList();

            // Retourne le JSON consommé par analytics.js
            return Json(new
            {
                probGain = Math.Round(probGain, 1),
                var95 = Math.Round(result.VaR95, 2),         
                initialValue = Math.Round(result.InitialValue, 2),
                median = Math.Round(result.Median, 2),         
                p5 = Math.Round(result.Percentile5, 2),    
                p95 = Math.Round(result.Percentile95, 2),   
                horizonDays = result.HorizonDays,
                nbSimulations = result.NumSimulations,
                insightNiveau = insight.Niveau,
                insightCouleur = insight.Couleur,
                insightMessage = insight.Action,
                fanP5,
                fanMed,
                fanP95
            });
        }

        // BACKTEST — JSON
        // BacktestRequest : From, To (pas StartDate/EndDate), BenchmarkTicker
        // BacktestResult  : TotalReturnPct, BenchmarkReturnPct, Alpha, Beta,
        //                   SharpeRatio, MaxDrawdownPct, PortfolioTimeSeries,
        //                   BenchmarkTimeSeries

        // POST /Analytics/Backtest -> retourne JSON pour le graphique de performance
        // benchmark = ticker du benchmark (ex: "SPY" pour le S&P 500)
        [HttpPost]
        public async Task<IActionResult> Backtest(
            int portfolioId,
            string? dateDebut = null,
            string? dateFin = null,
            string benchmark = "SPY")
        {
            var portfolio = await ApiService.GetPortfolioByIdAsync(portfolioId);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return Forbid();

            // Parse les dates -> fallback sur 1 an si non fournies
            var from = DateTime.TryParse(dateDebut, out var d1) ? d1 : DateTime.UtcNow.AddYears(-1);
            var to = DateTime.TryParse(dateFin, out var d2) ? d2 : DateTime.UtcNow;

            var result = await ApiService.RunBacktestAsync(portfolioId,
                new BacktestRequest
                {
                    From = from,        
                    To = to,          
                    BenchmarkTicker = benchmark,
                    RiskFreeRate = 0.045,
                    Rebalancing = RebalancingFrequency.BuyAndHold
                });

            if (result == null)
                return Json(new { error = "Backtest impossible. Données historiques insuffisantes." });

            // Réduit la série à max 50 points pour le graphique
            int step = Math.Max(1, result.PortfolioTimeSeries.Count / 50);
            
            var portCurve = result.PortfolioTimeSeries
                .Where((_, i) => i % step == 0)
                .Select(t => t.Value).ToList();
                
            var benchCurve = result.BenchmarkTimeSeries?
                .Where((_, i) => i % step == 0)
                .Select(t => t.Value).ToList();

            // Labels de l'axe X -> format "Jan 23"
            var labels = result.PortfolioTimeSeries
                .Where((_, i) => i % step == 0)
                .Select(t => t.Date.ToString("MMM yy")).ToList();

            return Json(new
            {
                portfolioReturn = Math.Round(result.TotalReturnPct, 2),
                sortino = Math.Round(result.SortinoRatio, 3), 
                benchmarkReturn = Math.Round(result.BenchmarkReturnPct ?? 0, 2),
                alpha = Math.Round(result.Alpha, 2),                 
                beta = Math.Round(result.Beta, 3),                
                sharpe = Math.Round(result.SharpeRatio, 3),
                maxDrawdown = Math.Round(result.MaxDrawdownPct, 2),         
                portCurve,
                benchCurve,
                labels
            });
        }

        // OptimizationRequest : From, To, Target (enum OptimizationTarget), RiskFreeRate
        // OptimizationResult  : OptimalWeights (List<AssetAllocation>),
        //                       OptimalReturn, OptimalVolatility, OptimalSharpe
        // POST /Analytics/Optimize -> retourne JSON pour la frontière efficiente
        // target = "MaxSharpe", "MinVolatility" ou "MaxReturn"

        [HttpPost]
        public async Task<IActionResult> Optimize(
            int portfolioId,
            string target = "MaxSharpe",
            string? dateDebut = null,
            string? dateFin = null)
        {
            var portfolio = await ApiService.GetPortfolioByIdAsync(portfolioId);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return Forbid();

            var from = DateTime.TryParse(dateDebut, out var d1) ? d1 : DateTime.UtcNow.AddYears(-1);
            var to = DateTime.TryParse(dateFin, out var d2) ? d2 : DateTime.UtcNow;

            // Parse la cible en enum -> fallback MaxSharpe si valeur inconnue
            if (!Enum.TryParse<OptimizationTarget>(target, true, out var targetEnum))
                targetEnum = OptimizationTarget.MaxSharpe;

            var result = await ApiService.OptimizePortfolioAsync(portfolioId,
                new OptimizationRequest
                {
                    From = from,
                    To = to,
                    Target = targetEnum,  
                    RiskFreeRate = 0.045,
                    NumPortfolios = 500
                });

            if (result == null)
                return Json(new { error = "Optimisation impossible. Données insuffisantes." });

            return Json(new
            {
                // OptimalReturn, OptimalVolatility, OptimalSharpe
                expectedReturn = Math.Round(result.OptimalReturn, 2),
                expectedVolatility = Math.Round(result.OptimalVolatility, 2),
                sharpeRatio = Math.Round(result.OptimalSharpe, 3),
                
                // OptimalWeights -> liste des poids optimaux par actif
                weights = result.OptimalWeights.Select(w => new
                {
                    ticker = w.Ticker,
                    poids = Math.Round(w.WeightPct, 1)
                }).ToList(),
                
                // Comparaison avec allocation actuelle
                currentReturn = Math.Round(result.CurrentReturn * 100, 2),
                currentVolatility = Math.Round(result.CurrentVolatility * 100, 2),
                currentSharpe = Math.Round(result.CurrentSharpe, 3)
            });
        }

        // COMPARAISON — JSON
        // CompareRequest : PortfolioIds, From, To, RiskFreeRate
        // POST /Analytics/Compare -> retourne JSON pour le tableau comparatif
        // [FromBody] -> les ids arrivent en JSON dans le body (pas en query string)

        [HttpPost]
        public async Task<IActionResult> Compare(
            [FromBody] List<int> portfolioIds,
            string? dateDebut = null,
            string? dateFin = null)
        {
            int userId = GetUserId() ?? 0;

            // Ownership check sur chaque portefeuille -> 403 si un seul n'appartient pas à l'user
            foreach (var pid in portfolioIds)
            {
                var p = await ApiService.GetPortfolioByIdAsync(pid);
                if (p == null || p.UserId != userId) return Forbid();
            }

            var from = DateTime.TryParse(dateDebut, out var d1) ? d1 : DateTime.UtcNow.AddYears(-1);
            var to = DateTime.TryParse(dateFin, out var d2) ? d2 : DateTime.UtcNow;

            var result = await ApiService.ComparePortfoliosAsync(new CompareRequest
            {
                PortfolioIds = portfolioIds,
                From = from,
                To = to,
                RiskFreeRate = 0.045
            });

            if (result == null)
                return Json(new { error = "Comparaison impossible." });

            return Json(new
            {
                from = result.From.ToString("dd/MM/yyyy"),
                to = result.To.ToString("dd/MM/yyyy"),
                // PortfolioSummary : PortfolioName, AnnualizedReturn, Volatility, SharpeRatio, MaxDrawdown
                portfolios = result.Portfolios.Select(p => new
                {
                    name = p.PortfolioName,
                    annualizedReturn = Math.Round(p.AnnualizedReturn, 2),
                    volatility = Math.Round(p.Volatility, 2),
                    sharpe = Math.Round(p.SharpeRatio, 3),
                    maxDrawdown = Math.Round(p.MaxDrawdown, 2),
                    totalReturn = Math.Round((double)p.TotalReturnPct, 2)
                }).ToList()
            });
        }

        // Retourne le niveau global = le pire parmi les 4 niveaux individuels
        // Ordre de gravité : Danger > Insuffisant > Bon > Excellent
        private static string NiveauGlobal(string[] niveaux)
        {
            if (niveaux.Contains("Danger")) return "Danger";
            if (niveaux.Contains("Insuffisant")) return "Insuffisant";
            if (niveaux.Contains("Solide")) return "Solide";
            return "Excellent";
        }

        // Génère le message global affiché en haut de la page Analytics
        // Adapte le message selon le niveau et les métriques réelles
        private static string MessageGlobal(string niveau, PortfolioAnalyticsResult a)
            => niveau switch
            {
                "Danger" => $"Profil de risque élevé — Sharpe {a.SharpeRatio:F2}, drawdown {a.MaxDrawdown:F1}%. Réallocation urgente recommandée.",
                "Insuffisant" => $"Performance en dessous des attentes — Sharpe {a.SharpeRatio:F2}. Une diversification accrue permettrait d'améliorer le profil rendement.",
                "Solide" => $"Portefeuille équilibré — Sharpe {a.SharpeRatio:F2}, rendement {a.AnnualizedReturn:F1}%/an. Des optimisations ciblées peuvent améliorer la performance.",
                _ => $"Allocation performante — Sharpe {a.SharpeRatio:F2}, rendement {a.AnnualizedReturn:F1}%/an. Profil efficient et maîtrisé."
            };
    }
}
