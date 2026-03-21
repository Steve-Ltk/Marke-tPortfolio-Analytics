using Marke_tPortfolio_Analytics_web.Helpers;
using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using MarketPortfolioAnalytics.Models.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    public class AnalyticsController : BaseController
    {
        public AnalyticsController(IApiService api, ILogger<AnalyticsController> logger)
            : base(api, logger) { }

        // ── INDEX — analyse principale ────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Index(
            int? portfolioId = null,
            string? dateDebut = null,
            string? dateFin = null,
            double riskFree = 3.0)
        {
            int userId = GetUserId() ?? 0;
            var portfolios = await ApiService.GetPortfoliosByUserAsync(userId);

            var vm = new AnalyticsIndexViewModel
            {
                Portfolios = portfolios,
                SelectedPortfolioId = portfolioId ?? (portfolios.FirstOrDefault()?.Id ?? 0),
                TauxSansRisque = riskFree
            };

            if (DateTime.TryParse(dateDebut, out var dd)) vm.DateDebut = dd;
            if (DateTime.TryParse(dateFin, out var df)) vm.DateFin = df;

            var portfolio = portfolios.FirstOrDefault(p => p.Id == vm.SelectedPortfolioId);
            if (portfolio != null) vm.SelectedPortfolioName = portfolio.Name;

            if (vm.SelectedPortfolioId > 0)
            {
                vm.Analyse = await ApiService.AnalyzePortfolioAsync(
                    vm.SelectedPortfolioId,
                    vm.DateDebut,
                    vm.DateFin,
                    riskFree / 100.0);

                if (vm.Analyse != null)
                {
                    var a = vm.Analyse;
                    // ✅ Utilise les vrais champs : Volatility (pas AnnualizedVolatility)
                    vm.InsightSharpe = InsightHelper.Sharpe(a.SharpeRatio);
                    vm.InsightVolatilite = InsightHelper.Volatilite(a.Volatility);
                    vm.InsightDrawdown = InsightHelper.MaxDrawdown(a.MaxDrawdown);
                    vm.InsightCagr = InsightHelper.Cagr(a.AnnualizedReturn);

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

        // ── MONTE CARLO — JSON ────────────────────────────────────────────────
        // MonteCarloRequest : HorizonDays (int), NumSimulations (int)
        // MonteCarloResult  : Percentile5, Percentile95, Median, VaR95,
        //                     ProbabilityOfLossPct, TimeSeries

        [HttpPost]
        public async Task<IActionResult> MonteCarlo(
            int portfolioId,
            int numSimulations = 1000,
            int horizonDays = 252)
        {
            var portfolio = await ApiService.GetPortfolioByIdAsync(portfolioId);
            if (portfolio == null || portfolio.UserId != GetUserId())
                return Forbid();

            var result = await ApiService.RunMonteCarloAsync(portfolioId,
                new MonteCarloRequest
                {
                    NumSimulations = numSimulations,  // ✅ pas NumberOfSimulations
                    HorizonDays = horizonDays       // ✅ pas TimeHorizonYears
                });

            if (result == null)
                return Json(new { error = "Simulation impossible. Vérifiez que le portefeuille contient des positions avec historique." });

            // ProbabilityOfLossPct → on inverse pour obtenir probGain
            double probGain = 100.0 - result.ProbabilityOfLossPct;
            var insight = InsightHelper.ProbGain(probGain);

            // TimeSeries → fan chart (prendre un point par semaine max 26 pts)
            var ts = result.TimeSeries ?? new();
            int step = Math.Max(1, ts.Count / 26);
            var fanP5 = ts.Where((_, i) => i % step == 0).Select(t => (double)t.P5).ToList();
            var fanMed = ts.Where((_, i) => i % step == 0).Select(t => (double)t.Median).ToList();
            var fanP95 = ts.Where((_, i) => i % step == 0).Select(t => (double)t.P95).ToList();

            return Json(new
            {
                probGain = Math.Round(probGain, 1),
                var95 = Math.Round(result.VaR95, 2),         // ✅ VaR95
                initialValue = Math.Round(result.InitialValue, 2),
                median = Math.Round(result.Median, 2),         // ✅ Median
                p5 = Math.Round(result.Percentile5, 2),    // ✅ Percentile5
                p95 = Math.Round(result.Percentile95, 2),   // ✅ Percentile95
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

        // ── BACKTEST — JSON ───────────────────────────────────────────────────
        // BacktestRequest : From, To (pas StartDate/EndDate), BenchmarkTicker
        // BacktestResult  : TotalReturnPct, BenchmarkReturnPct, Alpha, Beta,
        //                   SharpeRatio, MaxDrawdownPct, PortfolioTimeSeries,
        //                   BenchmarkTimeSeries

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

            var from = DateTime.TryParse(dateDebut, out var d1) ? d1 : DateTime.UtcNow.AddYears(-1);
            var to = DateTime.TryParse(dateFin, out var d2) ? d2 : DateTime.UtcNow;

            var result = await ApiService.RunBacktestAsync(portfolioId,
                new BacktestRequest
                {
                    From = from,        // ✅ From (pas StartDate)
                    To = to,          // ✅ To   (pas EndDate)
                    BenchmarkTicker = benchmark,
                    RiskFreeRate = 0.03,
                    Rebalancing = RebalancingFrequency.BuyAndHold
                });

            if (result == null)
                return Json(new { error = "Backtest impossible. Données historiques insuffisantes." });

            // Points pour graphique (max 50)
            int step = Math.Max(1, result.PortfolioTimeSeries.Count / 50);
            var portCurve = result.PortfolioTimeSeries
                .Where((_, i) => i % step == 0)
                .Select(t => t.Value).ToList();
            var benchCurve = result.BenchmarkTimeSeries?
                .Where((_, i) => i % step == 0)
                .Select(t => t.Value).ToList();
            var labels = result.PortfolioTimeSeries
                .Where((_, i) => i % step == 0)
                .Select(t => t.Date.ToString("MMM yy")).ToList();

            return Json(new
            {
                portfolioReturn = Math.Round(result.TotalReturnPct, 2),
                sortino = Math.Round(result.SortinoRatio, 3), // ✅
                benchmarkReturn = Math.Round(result.BenchmarkReturnPct ?? 0, 2),// ✅
                alpha = Math.Round(result.Alpha, 2),                  // ✅
                beta = Math.Round(result.Beta, 3),                  // ✅
                sharpe = Math.Round(result.SharpeRatio, 3),
                maxDrawdown = Math.Round(result.MaxDrawdownPct, 2),         // ✅
                portCurve,
                benchCurve,
                labels
            });
        }

        // ── OPTIMISATION — JSON ───────────────────────────────────────────────
        // OptimizationRequest : From, To, Target (enum OptimizationTarget), RiskFreeRate
        // OptimizationResult  : OptimalWeights (List<AssetAllocation>),
        //                       OptimalReturn, OptimalVolatility, OptimalSharpe

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

            // ✅ Target est un enum OptimizationTarget (pas une string "Objective")
            if (!Enum.TryParse<OptimizationTarget>(target, true, out var targetEnum))
                targetEnum = OptimizationTarget.MaxSharpe;

            var result = await ApiService.OptimizePortfolioAsync(portfolioId,
                new OptimizationRequest
                {
                    From = from,
                    To = to,
                    Target = targetEnum,   // ✅ Target (pas Objective)
                    RiskFreeRate = 0.03,
                    NumPortfolios = 500
                });

            if (result == null)
                return Json(new { error = "Optimisation impossible. Données insuffisantes." });

            return Json(new
            {
                // ✅ OptimalReturn, OptimalVolatility, OptimalSharpe
                expectedReturn = Math.Round(result.OptimalReturn * 100, 2),
                expectedVolatility = Math.Round(result.OptimalVolatility * 100, 2),
                sharpeRatio = Math.Round(result.OptimalSharpe, 3),
                // ✅ OptimalWeights est List<AssetAllocation> avec .Ticker et .WeightPct
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

        // ── COMPARAISON — JSON ────────────────────────────────────────────────
        // CompareRequest : PortfolioIds, From, To, RiskFreeRate

        [HttpPost]
        public async Task<IActionResult> Compare(
            [FromBody] List<int> portfolioIds,
            string? dateDebut = null,
            string? dateFin = null)
        {
            int userId = GetUserId() ?? 0;
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
                RiskFreeRate = 0.03
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

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string NiveauGlobal(string[] niveaux)
        {
            if (niveaux.Contains("Danger")) return "Danger";
            if (niveaux.Contains("Insuffisant")) return "Insuffisant";
            if (niveaux.Contains("Bon")) return "Bon";
            return "Excellent";
        }

        private static string MessageGlobal(string niveau, PortfolioAnalyticsResult a)
            => niveau switch
            {
                "Danger" => $"Portefeuille à risque élevé — Sharpe {a.SharpeRatio:F2}, drawdown {a.MaxDrawdown:F1}%. Action immédiate recommandée.",
                "Insuffisant" => $"Performance en dessous des attentes — Sharpe {a.SharpeRatio:F2}. Diversification à améliorer.",
                "Bon" => $"Portefeuille solide — Sharpe {a.SharpeRatio:F2}, rendement {a.AnnualizedReturn:F1}%/an. Optimisation possible.",
                _ => $"Performance excellente — Sharpe {a.SharpeRatio:F2}, rendement {a.AnnualizedReturn:F1}%/an. Maintenez cette stratégie."
            };
    }
}
