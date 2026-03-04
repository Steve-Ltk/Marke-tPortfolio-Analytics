using System.Text.Json.Serialization;

namespace MarketPortfolioAnalytics.Models.Analytics
{
    // ═══════════════════════════════════════════════════════════════════════════
    // ANALYSE DE BASE
    // ═══════════════════════════════════════════════════════════════════════════

    public class PortfolioAnalyticsResult
    {
        public int PortfolioId { get; set; }
        public string PortfolioName { get; set; } = null!;
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public decimal TotalCurrentValue { get; set; }
        public decimal TotalCostBasis { get; set; }
        public decimal TotalPnL { get; set; }
        public decimal TotalReturnPct { get; set; }
        public double AnnualizedReturn { get; set; }
        public double Volatility { get; set; }
        public double SharpeRatio { get; set; }
        public double MaxDrawdown { get; set; }
        public List<PositionAnalyticsResult> Positions { get; set; } = new();
    }

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
    // ═══════════════════════════════════════════════════════════════════════════

    public class CompareRequest
    {
        public List<int> PortfolioIds { get; set; } = new();
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public double RiskFreeRate { get; set; } = 0.03;
    }

    public class PortfolioComparisonResult
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<PortfolioSummary> Portfolios { get; set; } = new();
    }

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
    // ═══════════════════════════════════════════════════════════════════════════

    public class OptimizationRequest
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public double RiskFreeRate { get; set; } = 0.03;
        public OptimizationTarget Target { get; set; } = OptimizationTarget.MaxSharpe;
        public int NumPortfolios { get; set; } = 500;
    }

    /// <summary>
    /// [JsonConverter] OBLIGATOIRE : permet de désérialiser "MaxSharpe" (string JSON).
    /// Sans cela → HTTP 400 avant d'atteindre le contrôleur.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OptimizationTarget
    {
        MaxSharpe,
        MinVolatility,
        MaxReturn
    }

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

    public class AssetAllocation
    {
        public int AssetId { get; set; }
        public string Ticker { get; set; } = null!;
        public string AssetName { get; set; } = null!;
        public double WeightPct { get; set; }
    }

    public class EfficientFrontierPoint
    {
        public double ExpectedReturn { get; set; }
        public double Volatility { get; set; }
        public double SharpeRatio { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MONTE CARLO
    // ═══════════════════════════════════════════════════════════════════════════

    public class MonteCarloRequest
    {
        public int HorizonDays { get; set; } = 252;
        public int NumSimulations { get; set; } = 1000;
        public DateTime? HistoryFrom { get; set; }
        public DateTime? HistoryTo { get; set; }
    }

    /// <summary>
    /// Résultat Monte Carlo.
    ///
    /// RÈGLE DE NOMMAGE JSON — VaR vs CVaR (asymétrie camelCase) :
    ///
    /// La politique camelCase d'ASP.NET Core abaisse UNIQUEMENT le premier caractère :
    ///   VaR95  → "vaR95"    Postman : jsonData.vaR95  → MATCH ✓ (pas de [JsonPropertyName])
    ///   VaR99  → "vaR99"    Postman : jsonData.vaR99  → MATCH ✓ (pas de [JsonPropertyName])
    ///   CVaR95 → "cVaR95"   Postman : jsonData.CVaR95 → MISMATCH ✗ (C ≠ c)
    ///
    /// Pour CVaR95 uniquement, on force la clé JSON à "CVaR95" (C majuscule)
    /// via [JsonPropertyName("CVaR95")] afin de correspondre à jsonData.CVaR95 dans Postman.
    ///
    /// VaR95 et VaR99 ne nécessitent PAS de [JsonPropertyName] :
    /// leur sortie camelCase "vaR95"/"vaR99" correspond déjà aux assertions Postman.
    /// </summary>
    public class MonteCarloResult
    {
        public int PortfolioId { get; set; }
        public int HorizonDays { get; set; }
        public int NumSimulations { get; set; }
        public decimal InitialValue { get; set; }
        public decimal Percentile5 { get; set; }
        public decimal Percentile25 { get; set; }
        public decimal Median { get; set; }
        public decimal Percentile75 { get; set; }
        public decimal Percentile95 { get; set; }

        // camelCase → "vaR95" : correspond à jsonData.vaR95 dans Postman ✓
        public decimal VaR95 { get; set; }

        // camelCase → "vaR99" : correspond à jsonData.vaR99 dans Postman ✓
        public decimal VaR99 { get; set; }

        // camelCase → "cVaR95" (c minuscule) ≠ jsonData.CVaR95 (C majuscule) dans Postman.
        // [JsonPropertyName("CVaR95")] force la clé JSON à "CVaR95" → MATCH ✓
        [JsonPropertyName("CVaR95")]
        public decimal CVaR95 { get; set; }

        public double ProbabilityOfLossPct { get; set; }
        public double ExpectedFinalReturnPct { get; set; }
        public List<MonteCarloTimePoint> TimeSeries { get; set; } = new();
        public List<MonteCarloHistogramBucket> FinalValueHistogram { get; set; } = new();
    }

    public class MonteCarloTimePoint
    {
        public int Day { get; set; }
        public decimal P5 { get; set; }
        public decimal P25 { get; set; }
        public decimal Median { get; set; }
        public decimal P75 { get; set; }
        public decimal P95 { get; set; }
    }

    public class MonteCarloHistogramBucket
    {
        public decimal MinValue { get; set; }
        public decimal MaxValue { get; set; }
        public int Count { get; set; }
        public double FrequencyPct { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // BACKTESTING
    // ═══════════════════════════════════════════════════════════════════════════

    public class BacktestRequest
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string? BenchmarkTicker { get; set; }
        public double RiskFreeRate { get; set; } = 0.03;
        public RebalancingFrequency Rebalancing { get; set; } = RebalancingFrequency.BuyAndHold;
    }

    /// <summary>
    /// [JsonConverter] OBLIGATOIRE : permet de désérialiser "BuyAndHold" (string JSON).
    /// Sans cela → HTTP 400 avant d'atteindre le contrôleur.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RebalancingFrequency
    {
        BuyAndHold,
        Monthly,
        Quarterly,
        Annually
    }

    public class BacktestResult
    {
        public int PortfolioId { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public RebalancingFrequency Rebalancing { get; set; }
        public double TotalReturnPct { get; set; }
        public double AnnualizedReturnPct { get; set; }
        public double VolatilityPct { get; set; }
        public double SharpeRatio { get; set; }
        public double SortinoRatio { get; set; }
        public double MaxDrawdownPct { get; set; }
        public double CalmarRatio { get; set; }
        public double Beta { get; set; }
        public double Alpha { get; set; }
        public string? BenchmarkTicker { get; set; }
        public double? BenchmarkReturnPct { get; set; }
        public double? BenchmarkVolatilityPct { get; set; }
        public List<BacktestTimePoint> PortfolioTimeSeries { get; set; } = new();
        public List<BacktestTimePoint>? BenchmarkTimeSeries { get; set; }
        public List<DrawdownPoint> DrawdownSeries { get; set; } = new();
        public List<MonthlyReturn> MonthlyReturns { get; set; } = new();
    }

    public class BacktestTimePoint
    {
        public DateTime Date { get; set; }
        public double Value { get; set; }
        public double DailyReturnPct { get; set; }
    }

    public class DrawdownPoint
    {
        public DateTime Date { get; set; }
        public double DrawdownPct { get; set; }
    }

    public class MonthlyReturn
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public double ReturnPct { get; set; }
    }
}