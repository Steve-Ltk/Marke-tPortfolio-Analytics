namespace MarketPortfolioAnalytics.Services
{
    /// <summary>
    /// Bibliothèque statique de calculs financiers.
    /// Aucune dépendance externe — tout est calculé à partir des rendements journaliers.
    ///
    /// Conventions :
    ///   - Les rendements en entrée sont des rendements SIMPLES journaliers
    ///     (ex: 0.012 = +1.2% ce jour-là, -0.005 = -0.5%)
    ///   - Les résultats de rendement/volatilité sont en DÉCIMAL
    ///     (ex: 0.12 = 12% — le contrôleur multiplie par 100 pour l'affichage)
    ///   - 252 jours de trading par an (convention financière standard)
    /// </summary>
    public static class FinancialMath
    {
        /// <summary>Nombre de jours de trading dans une année (convention standard).</summary>
        public const int TradingDaysPerYear = 252;

        // ═══════════════════════════════════════════════════════════════════════
        // RENDEMENTS
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Calcule les rendements simples journaliers à partir d'une série de prix.
        /// r_t = (P_t - P_{t-1}) / P_{t-1}
        ///
        /// Si on a N prix, on obtient N-1 rendements.
        /// Retourne un tableau vide si moins de 2 prix.
        /// </summary>
        public static double[] SimpleReturns(double[] prices)
        {
            if (prices.Length < 2)
                return Array.Empty<double>();

            var returns = new double[prices.Length - 1];

            for (int i = 1; i < prices.Length; i++)
                returns[i - 1] = (prices[i] - prices[i - 1]) / prices[i - 1];

            return returns;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // STATISTIQUES DE BASE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Moyenne arithmétique d'un tableau de valeurs.</summary>
        public static double Mean(double[] values)
            => values.Length == 0 ? 0.0 : values.Average();

        /// <summary>
        /// Écart-type échantillon (ddof = 1, dénominateur N-1).
        /// On utilise N-1 et non N car on travaille sur un échantillon
        /// de rendements historiques, pas sur la population entière.
        /// Retourne 0 si moins de 2 valeurs.
        /// </summary>
        public static double StdDev(double[] values)
        {
            if (values.Length < 2)
                return 0.0;

            double mean = Mean(values);
            double variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);

            return Math.Sqrt(variance);
        }

        /// <summary>
        /// Covariance échantillon entre deux séries de même longueur.
        /// Cov(X, Y) = Σ(xi - x̄)(yi - ȳ) / (n-1)
        /// Tronque à la longueur de la plus courte série si elles diffèrent.
        /// </summary>
        public static double Covariance(double[] x, double[] y)
        {
            int n = Math.Min(x.Length, y.Length);
            if (n < 2) return 0.0;

            double mx = x.Take(n).Average();
            double my = y.Take(n).Average();

            return x.Take(n)
                    .Zip(y.Take(n), (xi, yi) => (xi - mx) * (yi - my))
                    .Sum() / (n - 1);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MÉTRIQUES ANNUALISÉES
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Volatilité annualisée : σ_daily × √252.
        /// Résultat en décimal (ex: 0.18 = 18% de volatilité annuelle).
        /// </summary>
        public static double AnnualizedVolatility(double[] dailyReturns)
            => StdDev(dailyReturns) * Math.Sqrt(TradingDaysPerYear);

        /// <summary>
        /// Rendement annualisé géométrique.
        /// Formule : (1 + R_cumulé)^(252 / n_jours) − 1
        ///
        /// On utilise la formule géométrique (et non arithmétique) car elle
        /// tient compte de l'effet de capitalisation — c'est la méthode correcte
        /// pour annualiser un rendement sur plusieurs années.
        ///
        /// Résultat en décimal (ex: 0.12 = 12% par an).
        /// </summary>
        public static double AnnualizedReturn(double[] dailyReturns)
        {
            if (dailyReturns.Length == 0)
                return 0.0;

            // Rendement cumulé : produit de tous les (1 + r_t)
            double cumulative = dailyReturns
                .Aggregate(1.0, (acc, r) => acc * (1.0 + r)) - 1.0;

            // Nombre d'années couvertes par la série
            double years = dailyReturns.Length / (double)TradingDaysPerYear;

            // Annualisation géométrique
            return Math.Pow(1.0 + cumulative, 1.0 / years) - 1.0;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // RATIOS DE PERFORMANCE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ratio de Sharpe annualisé.
        /// Mesure le rendement excédentaire par unité de risque total.
        /// Sharpe = (R_p − R_f) / σ_p
        ///
        /// R_p = rendement annualisé du portefeuille
        /// R_f = taux sans risque (défaut 3%)
        /// σ_p = volatilité annualisée
        ///
        /// Plus le ratio est élevé, meilleur est le compromis rendement/risque.
        /// Un ratio > 1 est généralement considéré comme bon.
        /// </summary>
        public static double SharpeRatio(double[] dailyReturns, double riskFreeRate = 0.03)
        {
            double vol = AnnualizedVolatility(dailyReturns);

            if (vol == 0.0)
                return 0.0;

            return (AnnualizedReturn(dailyReturns) - riskFreeRate) / vol;
        }

        /// <summary>
        /// Ratio de Sortino annualisé.
        /// Comme le Sharpe mais pénalise uniquement la volatilité à la baisse.
        /// C'est plus pertinent car les investisseurs ne se plaignent pas
        /// des rendements positifs élevés — seule la baisse est problématique.
        ///
        /// Sortino = (R_p − R_f) / σ_downside
        /// où σ_downside = écart-type des rendements négatifs uniquement.
        /// </summary>
        public static double SortinoRatio(double[] dailyReturns, double riskFreeRate = 0.03)
        {
            // On ne garde que les rendements négatifs pour calculer σ_downside
            var negativeReturns = dailyReturns.Where(r => r < 0).ToArray();

            double downsideVol = StdDev(negativeReturns) * Math.Sqrt(TradingDaysPerYear);

            if (downsideVol == 0.0)
                return 0.0;

            return (AnnualizedReturn(dailyReturns) - riskFreeRate) / downsideVol;
        }

        /// <summary>
        /// Ratio de Calmar.
        /// Mesure le rendement annualisé par unité de drawdown maximum.
        /// Calmar = rendement annualisé / |max drawdown|
        ///
        /// Utile pour évaluer si le rendement justifie les pertes maximales subies.
        /// </summary>
        public static double CalmarRatio(double[] dailyReturns, double[] portfolioValues)
        {
            double maxDD = Math.Abs(MaxDrawdown(portfolioValues));

            if (maxDD == 0.0)
                return 0.0;

            return AnnualizedReturn(dailyReturns) / maxDD;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MÉTRIQUES DE RISQUE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Maximum Drawdown sur une série de valeurs.
        /// Mesure la pire perte subie depuis un sommet jusqu'au creux suivant.
        ///
        /// Formule : min((V_t − V_pic) / V_pic) pour tout t
        /// Résultat toujours négatif ou nul (ex: -0.25 = perte maximale de 25%).
        /// </summary>
        public static double MaxDrawdown(double[] values)
        {
            if (values.Length < 2)
                return 0.0;

            double peak = values[0];
            double maxDrawdown = 0.0;

            foreach (double v in values)
            {
                if (v > peak)
                    peak = v;   // nouveau sommet

                double drawdown = (v - peak) / peak;

                if (drawdown < maxDrawdown)
                    maxDrawdown = drawdown;   // pire drawdown observé jusqu'ici
            }

            return maxDrawdown;
        }

        /// <summary>
        /// Série de drawdown jour par jour.
        /// Pour chaque date, retourne le recul en % depuis le dernier sommet.
        /// Valeurs toujours ≤ 0. Utilisé pour le graphique de drawdown.
        /// </summary>
        public static double[] DrawdownSeries(double[] values)
        {
            if (values.Length == 0)
                return Array.Empty<double>();

            double peak = values[0];

            return values.Select(v =>
            {
                if (v > peak) peak = v;
                return (v - peak) / peak;
            }).ToArray();
        }

        /// <summary>
        /// VaR historique (Value at Risk).
        /// Répond à : "Dans (1 − confidenceLevel)% des cas, ma perte dépassera X."
        /// Ex: VaR à 95% = quantile 5% des rendements = perte dépassée 5% du temps.
        ///
        /// Utilise la même méthode Percentile() que le reste du fichier
        /// (interpolation linéaire, cohérente avec CVaR et Monte Carlo).
        ///
        /// Correction v2 : l'ancienne formule floor((1-conf)×n) donnait un index
        /// décalé d'un rang — avec n=100 et conf=0.95, elle retournait sorted[5]
        /// (premier rendement positif) au lieu de sorted[4] (dernier rendement négatif).
        ///
        /// Résultat en décimal — valeur négative (ex: -0.032 = -3.2%).
        /// </summary>
        public static double VaR(double[] dailyReturns, double confidenceLevel = 0.95)
        {
            if (dailyReturns.Length == 0)
                return 0.0;

            var sorted = dailyReturns.OrderBy(r => r).ToArray();

            // Utilise Percentile() — cohérent avec CVaR, Monte Carlo et tous les autres appels
            return Percentile(sorted, 1.0 - confidenceLevel);
        }

        /// <summary>
        /// CVaR (Conditional Value at Risk), aussi appelé Expected Shortfall.
        /// Répond à : "En moyenne, quelle est ma perte dans les pires scénarios ?"
        /// C'est la moyenne des rendements en dessous du VaR.
        ///
        /// Plus conservateur que le VaR car il mesure la sévérité des pertes extrêmes.
        /// Résultat en décimal — valeur négative.
        /// </summary>
        public static double CVaR(double[] dailyReturns, double confidenceLevel = 0.95)
        {
            double varThreshold = VaR(dailyReturns, confidenceLevel);

            // On garde uniquement les rendements en dessous du seuil VaR
            var tail = dailyReturns.Where(r => r <= varThreshold).ToArray();

            return tail.Length > 0 ? tail.Average() : varThreshold;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // BÊTA ET ALPHA (métriques relatives au benchmark)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Bêta du portefeuille par rapport au benchmark.
        /// Mesure la sensibilité du portefeuille aux mouvements du marché.
        ///
        /// β = Cov(R_p, R_b) / Var(R_b)
        ///
        /// β = 1   : le portefeuille suit exactement le marché
        /// β > 1   : plus volatil que le marché (amplifie les mouvements)
        /// β < 1   : moins volatil que le marché (amortit les mouvements)
        /// β négatif : évolue en sens inverse du marché
        /// </summary>
        public static double Beta(double[] portfolioReturns, double[] benchmarkReturns)
        {
            double varBenchmark = Covariance(benchmarkReturns, benchmarkReturns);

            // Si la variance du benchmark est nulle (marché immobile), β = 1 par convention
            if (varBenchmark == 0.0)
                return 1.0;

            return Covariance(portfolioReturns, benchmarkReturns) / varBenchmark;
        }

        /// <summary>
        /// Alpha de Jensen (surperformance ajustée du risque).
        /// Mesure si le portefeuille fait mieux que ce que prédit le CAPM.
        ///
        /// α = R_p − [R_f + β × (R_b − R_f)]
        ///
        /// α > 0 : le portefeuille surperforme son benchmark une fois le risque pris en compte
        /// α < 0 : le portefeuille sous-performe
        /// α = 0 : performance conforme au CAPM
        ///
        /// Résultat en décimal (ex: 0.02 = +2% d'alpha annualisé).
        /// </summary>
        public static double Alpha(
            double[] portfolioReturns,
            double[] benchmarkReturns,
            double riskFreeRate = 0.03)
        {
            double beta = Beta(portfolioReturns, benchmarkReturns);
            double portfolioReturn = AnnualizedReturn(portfolioReturns);
            double benchmarkReturn = AnnualizedReturn(benchmarkReturns);

            return portfolioReturn - (riskFreeRate + beta * (benchmarkReturn - riskFreeRate));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // MARKOWITZ — MATHÉMATIQUES DE PORTEFEUILLE
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Matrice de covariance annualisée n × n des rendements d'actifs.
        /// returnsMatrix[i] = tableau des rendements journaliers de l'actif i.
        ///
        /// On multiplie par 252 pour annualiser (les rendements sont journaliers).
        /// La matrice est symétrique : cov[i,j] = cov[j,i].
        /// </summary>
        public static double[,] CovarianceMatrix(double[][] returnsMatrix)
        {
            int n = returnsMatrix.Length;
            var cov = new double[n, n];

            for (int i = 0; i < n; i++)
            {
                for (int j = i; j < n; j++)
                {
                    // Covariance annualisée
                    double c = Covariance(returnsMatrix[i], returnsMatrix[j]) * TradingDaysPerYear;
                    cov[i, j] = c;
                    cov[j, i] = c;   // symétrie
                }
            }

            return cov;
        }

        /// <summary>
        /// Rendement attendu d'un portefeuille pondéré.
        /// R_p = Σ w_i × μ_i
        ///
        /// weights[i]         = poids de l'actif i (somme = 1)
        /// expectedReturns[i] = rendement annualisé de l'actif i
        /// </summary>
        public static double PortfolioReturn(double[] weights, double[] expectedReturns)
        {
            double result = 0.0;

            for (int i = 0; i < weights.Length; i++)
                result += weights[i] * expectedReturns[i];

            return result;
        }

        /// <summary>
        /// Volatilité d'un portefeuille pondéré.
        /// σ_p = √(wᵀ Σ w)
        ///
        /// weights    = vecteur des poids (somme = 1)
        /// covMatrix  = matrice de covariance annualisée n × n
        ///
        /// C'est la formule exacte qui tient compte des corrélations entre actifs.
        /// Un portefeuille diversifié aura une volatilité inférieure à la moyenne
        /// pondérée des volatilités individuelles.
        /// </summary>
        public static double PortfolioVolatility(double[] weights, double[,] covMatrix)
        {
            int n = weights.Length;
            double variance = 0.0;

            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    variance += weights[i] * weights[j] * covMatrix[i, j];

            // Math.Max pour éviter un sqrt de nombre légèrement négatif dû aux arrondis
            return Math.Sqrt(Math.Max(0.0, variance));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // UTILITAIRES
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Normalise une série de valeurs en base 100.
        /// La première valeur devient 100, les suivantes sont proportionnelles.
        /// Utilisé pour comparer portefeuille et benchmark sur le même graphique.
        /// </summary>
        public static double[] NormalizeToBase100(double[] values)
        {
            if (values.Length == 0 || values[0] == 0.0)
                return values.Select(_ => 100.0).ToArray();

            double base_ = values[0];

            return values.Select(v => v / base_ * 100.0).ToArray();
        }

        /// <summary>
        /// Percentile d'un tableau déjà trié par ordre croissant.
        /// p = 0.05 → 5e percentile, p = 0.50 → médiane, p = 0.95 → 95e percentile.
        /// Utilise une interpolation linéaire entre les deux valeurs encadrantes.
        /// </summary>
        public static double Percentile(double[] sortedValues, double p)
        {
            if (sortedValues.Length == 0)
                return 0.0;

            double idx = p * (sortedValues.Length - 1);
            int lo = (int)Math.Floor(idx);
            int hi = Math.Min(lo + 1, sortedValues.Length - 1);
            double frac = idx - lo;

            return sortedValues[lo] * (1.0 - frac) + sortedValues[hi] * frac;
        }

        /// <summary>
        /// Génère une variable aléatoire normale standard N(0,1)
        /// via la transformation de Box-Muller.
        ///
        /// Utilisé dans la simulation Monte Carlo pour simuler les chocs journaliers.
        /// Box-Muller transforme deux variables uniformes [0,1] en une variable normale.
        /// </summary>
        public static double NormalRandom(Random rng)
        {
            // Évite log(0) en excluant 0 strict
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();

            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }
    }
}
