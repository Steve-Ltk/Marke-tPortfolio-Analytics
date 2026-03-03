using Microsoft.AspNetCore.Mvc;
using MarketPortfolioAnalytics.Models.Analytics;
using MarketPortfolioAnalytics.Services;

namespace MarketPortfolioAnalytics.Controllers
{
    /// <summary>
    /// Endpoints d'analyse financière avancée.
    ///
    /// Tous ces endpoints sont en lecture ou en calcul — aucune modification de données.
    /// Les paramètres communs (from, to, riskFreeRate) sont validés ici
    /// avant d'être transmis aux services.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class AnalyticsController : ControllerBase
    {
        private readonly PortfolioAnalyticsService _analyticsService;
        private readonly PortfolioOptimizationService _optimizationService;
        private readonly MonteCarloService _monteCarloService;
        private readonly BacktestService _backtestService;

        public AnalyticsController(
            PortfolioAnalyticsService analyticsService,
            PortfolioOptimizationService optimizationService,
            MonteCarloService monteCarloService,
            BacktestService backtestService)
        {
            _analyticsService = analyticsService;
            _optimizationService = optimizationService;
            _monteCarloService = monteCarloService;
            _backtestService = backtestService;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ANALYSE DE BASE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Analyse complète d'un portefeuille sur une période.
        /// Retourne : valeur actuelle, P&L, rendement annualisé, volatilité, Sharpe, drawdown,
        /// et le détail de chaque position (poids, P&L individuel...).
        ///
        /// Exemple :
        ///   GET /api/Analytics/portfolios/1/analyze?from=2023-01-01&amp;to=2024-01-01
        ///   GET /api/Analytics/portfolios/1/analyze?from=2023-01-01&amp;to=2024-01-01&amp;riskFreeRate=0.04
        /// </summary>
        // GET api/Analytics/portfolios/1/analyze
        [HttpGet("portfolios/{portfolioId}/analyze")]
        public async Task<ActionResult<PortfolioAnalyticsResult>> Analyze(
            int portfolioId,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to,
            [FromQuery] double riskFreeRate = 0.03)
        {
            if (from == default || to == default)
                return BadRequest("Les paramètres 'from' et 'to' sont requis. " +
                    "Exemple : ?from=2023-01-01&to=2024-01-01");

            if (from >= to)
                return BadRequest("La date 'from' doit être strictement antérieure à 'to'.");

            var result = await _analyticsService.AnalyzeAsync(
                portfolioId, from, to, riskFreeRate);

            if (result is null)
                return NotFound($"Portefeuille {portfolioId} introuvable.");

            return Ok(result);
        }

        /// <summary>
        /// Compare plusieurs portefeuilles sur une même période.
        /// Utile pour comparer différentes stratégies côte à côte.
        ///
        /// Corps JSON attendu :
        /// {
        ///   "portfolioIds": [1, 2, 3],
        ///   "from": "2023-01-01",
        ///   "to":   "2024-01-01",
        ///   "riskFreeRate": 0.03
        /// }
        /// </summary>
        // POST api/Analytics/portfolios/compare
        [HttpPost("portfolios/compare")]
        public async Task<ActionResult<PortfolioComparisonResult>> Compare(
            [FromBody] CompareRequest req)
        {
            if (req.PortfolioIds is null || req.PortfolioIds.Count == 0)
                return BadRequest("Au moins un portfolioId est requis.");

            if (req.From >= req.To)
                return BadRequest("La date 'from' doit être strictement antérieure à 'to'.");

            var result = await _analyticsService.CompareAsync(
                req.PortfolioIds, req.From, req.To, req.RiskFreeRate);

            return Ok(result);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // OPTIMISATION MARKOWITZ
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Optimisation Markowitz du portefeuille.
        /// Génère la frontière efficiente et retourne les poids optimaux
        /// selon la cible choisie.
        ///
        /// Prérequis :
        ///   - Le portefeuille doit contenir au moins 2 actifs.
        ///   - Chaque actif doit avoir au moins 30 jours de prix sur la période.
        ///
        /// Corps JSON attendu :
        /// {
        ///   "from":          "2022-01-01",
        ///   "to":            "2024-01-01",
        ///   "target":        "MaxSharpe",    ← ou "MinVolatility" ou "MaxReturn"
        ///   "riskFreeRate":  0.03,
        ///   "numPortfolios": 500             ← entre 100 et 5000
        /// }
        /// </summary>
        // POST api/Analytics/portfolios/1/optimize
        [HttpPost("portfolios/{portfolioId}/optimize")]
        public async Task<ActionResult<OptimizationResult>> Optimize(
            int portfolioId,
            [FromBody] OptimizationRequest req)
        {
            if (req.From >= req.To)
                return BadRequest("La date 'from' doit être strictement antérieure à 'to'.");

            if (req.NumPortfolios < 100 || req.NumPortfolios > 5000)
                return BadRequest("numPortfolios doit être compris entre 100 et 5000.");

            var result = await _optimizationService.OptimizeAsync(portfolioId, req);

            if (result is null)
                return BadRequest(
                    "Optimisation impossible. Vérifiez que le portefeuille existe, " +
                    "contient au moins 2 actifs et que chaque actif dispose " +
                    "d'au moins 30 jours de prix sur la période.");

            return Ok(result);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MONTE CARLO
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Simulation Monte Carlo (GBM) de l'évolution future du portefeuille.
        /// Retourne les percentiles P5/P25/P50/P75/P95, VaR 95%/99%, CVaR 95%
        /// et la probabilité de perte à l'horizon.
        ///
        /// Prérequis :
        ///   - Le portefeuille doit avoir au moins 20 jours d'historique
        ///     sur la période historyFrom → historyTo pour estimer μ et σ.
        ///
        /// Corps JSON attendu :
        /// {
        ///   "horizonDays":    252,         ← 1 à 1260 (5 ans max)
        ///   "numSimulations": 1000,        ← 100 à 10000
        ///   "historyFrom":    "2022-01-01", ← optionnel
        ///   "historyTo":      "2024-01-01"  ← optionnel
        /// }
        /// </summary>
        // POST api/Analytics/portfolios/1/montecarlo
        [HttpPost("portfolios/{portfolioId}/montecarlo")]
        public async Task<ActionResult<MonteCarloResult>> MonteCarlo(
            int portfolioId,
            [FromBody] MonteCarloRequest req)
        {
            if (req.HorizonDays < 1 || req.HorizonDays > 1260)
                return BadRequest("horizonDays doit être compris entre 1 et 1260 (5 ans).");

            if (req.NumSimulations < 100 || req.NumSimulations > 10000)
                return BadRequest("numSimulations doit être compris entre 100 et 10000.");

            if (req.HistoryFrom.HasValue && req.HistoryTo.HasValue
                && req.HistoryFrom >= req.HistoryTo)
                return BadRequest("historyFrom doit être antérieure à historyTo.");

            var result = await _monteCarloService.SimulateAsync(portfolioId, req);

            if (result is null)
                return BadRequest(
                    "Simulation impossible. Vérifiez que le portefeuille existe " +
                    "et dispose d'au moins 20 jours d'historique de prix.");

            return Ok(result);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // BACKTESTING
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Backtesting historique du portefeuille avec comparaison optionnelle à un benchmark.
        /// Retourne Sharpe, Sortino, Calmar, Alpha, Bêta, Max Drawdown,
        /// les séries temporelles normalisées base 100 et les rendements mensuels.
        ///
        /// Corps JSON attendu :
        /// {
        ///   "from":            "2022-01-01",
        ///   "to":              "2024-01-01",
        ///   "benchmarkTicker": "SPY",          ← optionnel (doit être en base)
        ///   "riskFreeRate":    0.03,
        ///   "rebalancing":     "BuyAndHold"    ← ou "Monthly", "Quarterly", "Annually"
        /// }
        ///
        /// Note sur le benchmark : le ticker doit être présent dans la table Asset
        /// et avoir des prix sur la même période. Synchronisez-le via
        /// POST /api/AssetPrices/sync/{assetId} avant d'utiliser cette route.
        /// </summary>
        // POST api/Analytics/portfolios/1/backtest
        [HttpPost("portfolios/{portfolioId}/backtest")]
        public async Task<ActionResult<BacktestResult>> Backtest(
            int portfolioId,
            [FromBody] BacktestRequest req)
        {
            if (req.From >= req.To)
                return BadRequest("La date 'from' doit être strictement antérieure à 'to'.");

            var result = await _backtestService.RunAsync(portfolioId, req);

            if (result is null)
                return BadRequest(
                    "Backtesting impossible. Vérifiez que le portefeuille existe " +
                    "et dispose de données de prix sur la période demandée " +
                    "(minimum 5 jours de trading).");

            return Ok(result);
        }
    }
}
