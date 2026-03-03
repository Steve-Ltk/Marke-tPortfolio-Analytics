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
        // Valeur actuelle totale = Σ (quantité × prix de clôture le plus récent)
        public decimal TotalCurrentValue { get; set; }

        // Coût total = Σ (quantité × prix moyen d'achat)
        public decimal TotalCostBasis { get; set; }

        // Plus-value latente = valeur actuelle − coût total
        public decimal TotalPnL { get; set; }

        // Rendement total en % = (valeur actuelle − coût) / coût × 100
        public decimal TotalReturnPct { get; set; }

        // ── Métriques de performance (annualisées) ────────────────────────────
        // Exprimées en % (ex: 12.5 = 12.5% par an)
        public double AnnualizedReturn { get; set; }  // rendement géométrique annualisé
        public double Volatility { get; set; }  // écart-type des rendements × √252
        public double SharpeRatio { get; set; }  // (rendement − taux sans risque) / volatilité
        public double MaxDrawdown { get; set; }  // perte maximale depuis un pic (négatif)

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
        public decimal CurrentPrice { get; set; }   // dernier prix de clôture connu
        public decimal CurrentValue { get; set; }   // quantité × prix actuel
        public decimal CostBasis { get; set; }   // quantité × prix d'achat
        public decimal PnL { get; set; }   // valeur actuelle − coût
        public decimal ReturnPct { get; set; }   // P&L / coût × 100
        public double WeightPct { get; set; }   // % de la valeur totale du portefeuille
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
    /// Permet de comparer les performances côte à côte.
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
        // portfolioId est injecté depuis la route — pas besoin de le mettre dans le body
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public double RiskFreeRate { get; set; } = 0.03;
        public OptimizationTarget Target { get; set; } = OptimizationTarget.MaxSharpe;

        // Nombre de portefeuilles aléatoires générés pour explorer la frontière efficiente
        // Plus c'est grand, plus la frontière est précise (mais plus c'est long à calculer)
        public int NumPortfolios { get; set; } = 500;
    }

    /// <summary>
    /// Cible de l'optimisation.
    ///   MaxSharpe      : maximiser le ratio de Sharpe (meilleur compromis rendement/risque)
    ///   MinVolatility  : minimiser la volatilité (portefeuille le moins risqué)
    ///   MaxReturn      : maximiser le rendement (sans contrainte de risque)
    /// </summary>
    public enum OptimizationTarget
    {
        MaxSharpe,
        MinVolatility,
        MaxReturn
    }

    /// <summary>
    /// Résultat de l'optimisation Markowitz.
    /// Contient les poids optimaux, les métriques associées,
    /// les poids actuels pour comparaison, et la frontière efficiente.
    /// </summary>
    public class OptimizationResult
    {
        public int PortfolioId { get; set; }
        public OptimizationTarget Target { get; set; }
        public double RiskFreeRate { get; set; }

        // ── Allocation optimale ────────────────────────────────────────────────
        public List<AssetAllocation> OptimalWeights { get; set; } = new();
        public double OptimalReturn { get; set; }   // rendement en %
        public double OptimalVolatility { get; set; }   // volatilité en %
        public double OptimalSharpe { get; set; }

        // ── Allocation actuelle (pour comparaison) ────────────────────────────
        public List<AssetAllocation> CurrentWeights { get; set; } = new();
        public double CurrentReturn { get; set; }
        public double CurrentVolatility { get; set; }
        public double CurrentSharpe { get; set; }

        // ── Frontière efficiente ──────────────────────────────────────────────
        // Ensemble des portefeuilles explorés — chaque point = (volatilité, rendement)
        // Utilisé pour tracer le graphique de la frontière efficiente
        public List<EfficientFrontierPoint> EfficientFrontier { get; set; } = new();
    }

    /// <summary>Poids d'un actif dans une allocation.</summary>
    public class AssetAllocation
    {
        public int AssetId { get; set; }
        public string Ticker { get; set; } = null!;
        public string AssetName { get; set; } = null!;
        public double WeightPct { get; set; }   // 0 à 100
    }

    /// <summary>
    /// Un point sur la frontière efficiente.
    /// Chaque point représente un portefeuille exploré lors de l'optimisation.
    /// </summary>
    public class EfficientFrontierPoint
    {
        public double ExpectedReturn { get; set; }  // rendement attendu en %
        public double Volatility { get; set; }  // volatilité en %
        public double SharpeRatio { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MONTE CARLO
    // Résultat de POST /api/Analytics/portfolios/{id}/montecarlo
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Corps de la requête de simulation Monte Carlo.</summary>
    public class MonteCarloRequest
    {
        // Nombre de jours de trading à simuler (252 = 1 an, 504 = 2 ans, max 1260 = 5 ans)
        public int HorizonDays { get; set; } = 252;

        // Nombre de chemins simulés (plus c'est grand, plus c'est précis)
        public int NumSimulations { get; set; } = 1000;

        // Période historique utilisée pour estimer μ et σ du portefeuille
        // Si null : on prend les 2 dernières années disponibles
        public DateTime? HistoryFrom { get; set; }
        public DateTime? HistoryTo { get; set; }
    }

    /// <summary>
    /// Résultat de la simulation Monte Carlo.
    /// Donne une vision probabiliste de l'évolution future du portefeuille.
    /// </summary>
    public class MonteCarloResult
    {
        public int PortfolioId { get; set; }
        public int HorizonDays { get; set; }
        public int NumSimulations { get; set; }
        public decimal InitialValue { get; set; }   // valeur du portefeuille au début

        // ── Percentiles de la valeur finale ──────────────────────────────────
        // P5 = dans 5% des scénarios, le portefeuille vaudra moins que cette valeur
        // P95 = dans 95% des scénarios, le portefeuille vaudra moins que cette valeur
        public decimal Percentile5 { get; set; }
        public decimal Percentile25 { get; set; }
        public decimal Median { get; set; }
        public decimal Percentile75 { get; set; }
        public decimal Percentile95 { get; set; }

        // ── Métriques de risque ───────────────────────────────────────────────
        // VaR95 : perte maximale attendue dans 95% des cas
        // VaR99 : perte maximale attendue dans 99% des cas
        // CVaR95 (Expected Shortfall) : perte moyenne dans les 5% pires scénarios
        public decimal VaR95 { get; set; }
        public decimal VaR99 { get; set; }
        public decimal CVaR95 { get; set; }

        // Probabilité que le portefeuille perde de la valeur à l'horizon
        public double ProbabilityOfLossPct { get; set; }

        // Rendement attendu moyen à l'horizon (en %)
        public double ExpectedFinalReturnPct { get; set; }

        // ── Données pour graphiques ───────────────────────────────────────────
        // Série temporelle des percentiles (max 100 points pour ne pas surcharger)
        public List<MonteCarloTimePoint> TimeSeries { get; set; } = new();

        // Histogramme des valeurs finales (pour visualiser la distribution)
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

        // Ticker du benchmark pour comparaison (ex: "SPY" pour le S&P 500)
        // Optionnel — si null, les métriques Beta et Alpha ne sont pas calculées
        public string? BenchmarkTicker { get; set; }

        public double RiskFreeRate { get; set; } = 0.03;

        // Fréquence de rééquilibrage : Buy & Hold (défaut) ou rééquilibrage périodique
        public RebalancingFrequency Rebalancing { get; set; } = RebalancingFrequency.BuyAndHold;
    }

    /// <summary>
    /// Fréquence de rééquilibrage du portefeuille.
    ///   BuyAndHold : on ne rééquilibre jamais — les poids dérivent avec le marché
    ///   Monthly    : rééquilibrage aux poids initiaux chaque mois
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
    /// Simule la performance du portefeuille sur une période passée.
    /// </summary>
    public class BacktestResult
    {
        public int PortfolioId { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public RebalancingFrequency Rebalancing { get; set; }

        // ── Métriques de performance ──────────────────────────────────────────
        public double TotalReturnPct { get; set; }   // rendement total sur la période
        public double AnnualizedReturnPct { get; set; }   // rendement annualisé
        public double VolatilityPct { get; set; }   // volatilité annualisée
        public double SharpeRatio { get; set; }
        public double SortinoRatio { get; set; }   // comme Sharpe mais pénalise seulement la baisse
        public double MaxDrawdownPct { get; set; }   // perte maximale depuis un pic
        public double CalmarRatio { get; set; }   // rendement annualisé / |max drawdown|

        // ── Métriques relatives au benchmark ─────────────────────────────────
        // Calculées uniquement si BenchmarkTicker est fourni
        public double Beta { get; set; }   // sensibilité au marché
        public double Alpha { get; set; }   // surperformance vs benchmark
        public string? BenchmarkTicker { get; set; }
        public double? BenchmarkReturnPct { get; set; }
        public double? BenchmarkVolatilityPct { get; set; }

        // ── Données pour graphiques ───────────────────────────────────────────

        // Série temporelle du portefeuille normalisée base 100
        // (valeur initiale = 100, permet de comparer facilement)
        public List<BacktestTimePoint> PortfolioTimeSeries { get; set; } = new();

        // Série temporelle du benchmark (même format, base 100)
        public List<BacktestTimePoint>? BenchmarkTimeSeries { get; set; }

        // Drawdown jour par jour (valeurs négatives ou nulles)
        public List<DrawdownPoint> DrawdownSeries { get; set; } = new();

        // Rendement mensuel (pour heatmap année × mois)
        public List<MonthlyReturn> MonthlyReturns { get; set; } = new();
    }

    /// <summary>Valeur du portefeuille (ou benchmark) à une date donnée.</summary>
    public class BacktestTimePoint
    {
        public DateTime Date { get; set; }
        public double Value { get; set; }   // base 100
        public double DailyReturnPct { get; set; }
    }

    /// <summary>Drawdown (recul depuis le dernier pic) à une date donnée.</summary>
    public class DrawdownPoint
    {
        public DateTime Date { get; set; }
        public double DrawdownPct { get; set; }   // toujours ≤ 0
    }

    /// <summary>Rendement sur un mois calendaire.</summary>
    public class MonthlyReturn
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public double ReturnPct { get; set; }
    }
}
