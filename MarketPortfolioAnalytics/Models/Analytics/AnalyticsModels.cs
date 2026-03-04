using System.Text.Json.Serialization;

namespace MarketPortfolioAnalytics.Models.Analytics
{
    // ═══════════════════════════════════════════════════════════════════════════
    // ANALYSE DE BASE
    // Résultat de GET /api/Analytics/portfolios/{id}/analyze
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Résultat complet d'une analyse de portefeuille sur une période.
    /// Contient les métriques globales et le détail par position.
    /// </summary>
    public class PortfolioAnalyticsResult
    {
        public int PortfolioId { get; set; }
        public string PortfolioName { get; set; } = null!;
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        // ── Valeur et P&L ─────────────────────────────────────────────────────
        public decimal TotalCurrentValue { get; set; }
        public decimal TotalCostBasis { get; set; }
        public decimal TotalPnL { get; set; }
        public decimal TotalReturnPct { get; set; }

        // ── Métriques de performance (annualisées) ────────────────────────────
        public double AnnualizedReturn { get; set; }
        public double Volatility { get; set; }
        public double SharpeRatio { get; set; }
        public double MaxDrawdown { get; set; }

        // ── Détail par position ───────────────────────────────────────────────
        public List<PositionAnalyticsResult> Positions { get; set; } = new();
    }

    /// <summary>
    /// Métriques d'une position individuelle dans le portefeuille.
    /// </summary>
    public class PositionAnalyticsResult
    {
        public int AssetId { get; set; }
        public string Ticker { get; set; } = null!;
        public string AssetName { get; set; } = null!;
        public decimal Quantity { get; set; }
        public decimal AvgBuyPrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal CurrentValue { get; set; }
        public decimal CostBasis { get; set; }
        public decimal PnL { get; set; }
        public decimal ReturnPct { get; set; }
        public double WeightPct { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COMPARAISON
    // Résultat de POST /api/Analytics/portfolios/compare
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Corps de la requête de comparaison de portefeuilles.</summary>
    public class CompareRequest
    {
        public List<int> PortfolioIds { get; set; } = new();
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public double RiskFreeRate { get; set; } = 0.03;
    }

    /// <summary>
    /// Résultat de la comparaison de plusieurs portefeuilles sur une même période.
    /// </summary>
    public class PortfolioComparisonResult
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<PortfolioSummary> Portfolios { get; set; } = new();
    }

    /// <summary>Résumé des métriques d'un portefeuille pour la comparaison.</summary>
    public class PortfolioSummary
    {
        public int PortfolioId { get; set; }
        public string PortfolioName { get; set; } = null!;
        public double AnnualizedReturn { get; set; }
        public double Volatility { get; set; }
        public double SharpeRatio { get; set; }
        public double MaxDrawdown { get; set; }
        public decimal TotalReturnPct { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OPTIMISATION MARKOWITZ
    // Résultat de POST /api/Analytics/portfolios/{id}/optimize
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Corps de la requête d'optimisation Markowitz.</summary>
    public class OptimizationRequest
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public double RiskFreeRate { get; set; } = 0.03;
        public OptimizationTarget Target { get; set; } = OptimizationTarget.MaxSharpe;
        public int NumPortfolios { get; set; } = 500;
    }

    /// <summary>
    /// Cible de l'optimisation.
    ///   MaxSharpe      : maximiser le ratio de Sharpe
    ///   MinVolatility  : minimiser la volatilité
    ///   MaxReturn      : maximiser le rendement
    /// </summary>
    public enum OptimizationTarget
    {
        MaxSharpe,
        MinVolatility,
        MaxReturn
    }

    /// <summary>
    /// Résultat de l'optimisation Markowitz.
    /// </summary>
    public class OptimizationResult
    {
        public int PortfolioId { get; set; }
        public OptimizationTarget Target { get; set; }
        public double RiskFreeRate { get; set; }

        public List<AssetAllocation> OptimalWeights { get; set; } = new();
        public double OptimalReturn { get; set; }
        public double OptimalVolatility { get; set; }
        public double OptimalSharpe { get; set; }

        public List<AssetAllocation> CurrentWeights { get; set; } = new();
        public double CurrentReturn { get; set; }
        public double CurrentVolatility { get; set; }
        public double CurrentSharpe { get; set; }

        public List<EfficientFrontierPoint> EfficientFrontier { get; set; } = new();
    }

    /// <summary>Poids d'un actif dans une allocation.</summary>
    public class AssetAllocation
    {
        public int AssetId { get; set; }
        public string Ticker { get; set; } = null!;
        public string AssetName { get; set; } = null!;
        public double WeightPct { get; set; }
    }

    /// <summary>Un point sur la frontière efficiente.</summary>
    public class EfficientFrontierPoint
    {
        public double ExpectedReturn { get; set; }
        public double Volatility { get; set; }
        public double SharpeRatio { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MONTE CARLO
    // Résultat de POST /api/Analytics/portfolios/{id}/montecarlo
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Corps de la requête de simulation Monte Carlo.</summary>
    public class MonteCarloRequest
    {
        public int HorizonDays { get; set; } = 252;
        public int NumSimulations { get; set; } = 1000;
        public DateTime? HistoryFrom { get; set; }
        public DateTime? HistoryTo { get; set; }
    }

    /// <summary>
    /// Résultat de la simulation Monte Carlo.
    ///
    /// IMPORTANT — nommage des propriétés VaR / CVaR :
    ///   JsonNamingPolicy.CamelCase ne lowercase que le premier caractère.
    ///   "VaR95" deviendrait "vaR95" et "CVaR95" deviendrait "cVaR95" — noms
    ///   ambigus et inattendus pour les clients de l'API.
    ///
    ///   On force les noms exacts via [JsonPropertyName] pour garantir que le JSON
    ///   contient bien "VaR95", "VaR99" et "CVaR95" (PascalCase lisible).
    /// </summary>
    public class MonteCarloResult
    {
        public int PortfolioId { get; set; }
        public int HorizonDays { get; set; }
        public int NumSimulations { get; set; }
        public decimal InitialValue { get; set; }

        // ── Percentiles de la valeur finale ──────────────────────────────────
        public decimal Percentile5 { get; set; }
        public decimal Percentile25 { get; set; }
        public decimal Median { get; set; }
        public decimal Percentile75 { get; set; }
        public decimal Percentile95 { get; set; }

        // ── Métriques de risque ───────────────────────────────────────────────
        //
        // [JsonPropertyName] OBLIGATOIRE ici :
        //   Sans attribut, camelCase donne "vaR95" / "vaR99" / "cVaR95".
        //   Les tests Postman attendent "VaR95", "VaR99", "CVaR95".
        //   Sans correspondance : jsonData.VaR95 == undefined, undefined >= undefined → false.

        [JsonPropertyName("VaR95")]
        public decimal VaR95 { get; set; }

        [JsonPropertyName("VaR99")]
        public decimal VaR99 { get; set; }

        [JsonPropertyName("CVaR95")]
        public decimal CVaR95 { get; set; }

        public double ProbabilityOfLossPct { get; set; }
        public double ExpectedFinalReturnPct { get; set; }

        // ── Données pour graphiques ───────────────────────────────────────────
        public List<MonteCarloTimePoint> TimeSeries { get; set; } = new();
        public List<MonteCarloHistogramBucket> FinalValueHistogram { get; set; } = new();
    }

    /// <summary>Percentiles du portefeuille à un instant t de la simulation.</summary>
    public class MonteCarloTimePoint
    {
        public int Day { get; set; }
        public decimal P5 { get; set; }
        public decimal P25 { get; set; }
        public decimal Median { get; set; }
        public decimal P75 { get; set; }
        public decimal P95 { get; set; }
    }

    /// <summary>Tranche de l'histogramme des valeurs finales simulées.</summary>
    public class MonteCarloHistogramBucket
    {
        public decimal MinValue { get; set; }
        public decimal MaxValue { get; set; }
        public int Count { get; set; }
        public double FrequencyPct { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // BACKTESTING
    // Résultat de POST /api/Analytics/portfolios/{id}/backtest
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Corps de la requête de backtesting.</summary>
    public class BacktestRequest
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string? BenchmarkTicker { get; set; }
        public double RiskFreeRate { get; set; } = 0.03;
        public RebalancingFrequency Rebalancing { get; set; } = RebalancingFrequency.BuyAndHold;
    }

    /// <summary>
    /// Fréquence de rééquilibrage du portefeuille.
    ///   BuyAndHold : on ne rééquilibre jamais
    ///   Monthly    : rééquilibrage chaque mois
    ///   Quarterly  : rééquilibrage chaque trimestre
    ///   Annually   : rééquilibrage chaque année
    /// </summary>
    public enum RebalancingFrequency
    {
        BuyAndHold,
        Monthly,
        Quarterly,
        Annually
    }

    /// <summary>
    /// Résultat du backtesting historique.
    /// </summary>
    public class BacktestResult
    {
        public int PortfolioId { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public RebalancingFrequency Rebalancing { get; set; }

        // ── Métriques de performance ──────────────────────────────────────────
        public double TotalReturnPct { get; set; }
        public double AnnualizedReturnPct { get; set; }
        public double VolatilityPct { get; set; }
        public double SharpeRatio { get; set; }
        public double SortinoRatio { get; set; }
        public double MaxDrawdownPct { get; set; }
        public double CalmarRatio { get; set; }

        // ── Métriques relatives au benchmark ─────────────────────────────────
        public double Beta { get; set; }
        public double Alpha { get; set; }
        public string? BenchmarkTicker { get; set; }
        public double? BenchmarkReturnPct { get; set; }
        public double? BenchmarkVolatilityPct { get; set; }

        // ── Données pour graphiques ───────────────────────────────────────────
        public List<BacktestTimePoint> PortfolioTimeSeries { get; set; } = new();
        public List<BacktestTimePoint>? BenchmarkTimeSeries { get; set; }
        public List<DrawdownPoint> DrawdownSeries { get; set; } = new();
        public List<MonthlyReturn> MonthlyReturns { get; set; } = new();
    }

    /// <summary>Valeur du portefeuille (ou benchmark) à une date donnée.</summary>
    public class BacktestTimePoint
    {
        public DateTime Date { get; set; }
        public double Value { get; set; }
        public double DailyReturnPct { get; set; }
    }

    /// <summary>Drawdown (recul depuis le dernier pic) à une date donnée.</summary>
    public class DrawdownPoint
    {
        public DateTime Date { get; set; }
        public double DrawdownPct { get; set; }
    }

    /// <summary>Rendement sur un mois calendaire.</summary>
    public class MonthlyReturn
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public double ReturnPct { get; set; }
    }
}