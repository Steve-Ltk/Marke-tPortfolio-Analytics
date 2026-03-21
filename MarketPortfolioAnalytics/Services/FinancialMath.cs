namespace MarketPortfolioAnalytics.Services
{
    // Bibliothèque de calculs financiers — toutes les formules mathématiques du projet.
    // "static" = pas d'instance nécessaire → on appelle directement FinancialMath.SharpeRatio(...)
    // Aucune dépendance externe → que des calculs purs sur des tableaux de nombres.
    //
    // Convention dans tout ce fichier :
    // -> Les rendements en entrée sont des décimaux journaliers (0.012 = +1.2% ce jour)
    // -> Les résultats sont aussi en décimaux (0.12 = 12%) → le controller multiplie par 100 pour l'affichage
    // -> 252 = nombre de jours de bourse dans une année (convention financière mondiale)
    public static class FinancialMath
    {
        // Constante : 252 jours de trading par an (convention standard des marchés financiers)
        // Utilisée pour annualiser la volatilité et le rendement calculés sur des données journalières
        public const int TradingDaysPerYear = 252;

        // Transforme une série de PRIX en série de RENDEMENTS journaliers
        // Ex : [100, 103, 101] → [+0.03, -0.019]
        // Formule : r_t = (P_t - P_{t-1}) / P_{t-1}
        // Si on a N prix -> on obtient N-1 rendements (le premier jour n'a pas de "avant")
        // Retourne un tableau vide si moins de 2 prix → pas de rendement calculable
        public static double[] SimpleReturns(double[] prices)
        {
            // Moins de 2 prix → impossible de calculer un rendement -> tableau vide
            if (prices.Length < 2)
                return Array.Empty<double>();

            // Le tableau de rendements a un élément de moins que le tableau de prix
            var returns = new double[prices.Length - 1];

            // Pour chaque jour : (prix aujourd'hui - prix hier) / prix hier
            for (int i = 1; i < prices.Length; i++)
                returns[i - 1] = (prices[i] - prices[i - 1]) / prices[i - 1];

            return returns;
        }

        // Calcule la moyenne arithmétique d'un tableau de valeurs
        // Retourne 0 si le tableau est vide → évite une division par zéro
        public static double Mean(double[] values)
        {
            if (values.Length == 0)
                return 0.0;
            else
                return values.Average();
        }

        // Calcule l'écart-type échantillon (dénominateur N-1, pas N)
        // N-1 car on travaille sur un échantillon historique, pas la population entière
        // L'écart-type mesure à quel point les valeurs s'éloignent de la moyenne
        // Retourne 0 si moins de 2 valeurs -> pas de dispersion calculable
        public static double StdDev(double[] values)
        {
            // Moins de 2 valeurs → écart-type indéfini
            if (values.Length < 2)
                return 0.0;
            double mean = Mean(values);

            // Somme des carrés des écarts à la moyenne, divisée par N-1
            double variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);

            // Racine carrée de la variance = écart-type
            return Math.Sqrt(variance);
        }

        // Calcule la covariance entre deux séries de données
        // Mesure si deux actifs bougent dans le même sens (positif) ou opposé (négatif)
        // Si les séries ont des longueurs différentes → on tronque à la plus courte
        // Retourne 0 si moins de 2 points communs → covariance indéfinie
        public static double Covariance(double[] x, double[] y)
        {
            // On prend la longueur minimale des deux séries
            int n = Math.Min(x.Length, y.Length);
            if (n < 2) return 0.0;

            // Take -> Prend les n premiers éléments
            double mx = x.Take(n).Average(); // moyenne de x
            double my = y.Take(n).Average(); // moyenne de y

            // Somme de (xi - mx)(yi - my) divisée par N-1
            // Zip pour combiner les deux listes élément par élément
            return x.Take(n)
                    .Zip(y.Take(n), (xi, yi) => (xi - mx) * (yi - my))
                    .Sum() / (n - 1);
        }

        // Volatilité annualisée : écart-type journalier × √252
        // √252 car la volatilité scale avec la racine carrée du temps (propriété mathématique)
        // Ex : écart-type journalier de 1% -> volatilité annuelle de 1% × √252 ≈ 15.87%
        public static double AnnualizedVolatility(double[] dailyReturns)
            => StdDev(dailyReturns) * Math.Sqrt(TradingDaysPerYear);

        // Rendement annualisé géométrique (formule des intérêts composés)
        // Formule : (1 + R_cumulé)^(252/n) - 1
        // On utilise la formule géométrique (pas arithmétique) car elle tient compte
        // de l'effet de capitalisation sur plusieurs années
        // Ex : +50% sur 2 ans → pas 25%/an mais (1.5)^(1/2) - 1 = 22.47%/an
        public static double AnnualizedReturn(double[] dailyReturns)
        {
            // Pas de rendements → pas de calcul possible
            if (dailyReturns.Length == 0)
                return 0.0;

            // Rendement cumulé : on multiplie tous les (1 + r_t) entre eux
            //Aggregate -> part d’une valeur initiale 1.0, parcourt chaque élément r et met à jour un accumulateur acc
            // Ex : [+3%, -2%, +1%] → 1.03 × 0.98 × 1.01 - 1 = 1.98%
            double cumulative = dailyReturns
                .Aggregate(1.0, (acc, r) => acc * (1.0 + r)) - 1.0;

            // Nombre d'années couvertes par la série de rendements
            double years = dailyReturns.Length / (double)TradingDaysPerYear;

            // Annualisation géométrique : (1 + rendement_cumulé)^(1/années) - 1
            return Math.Pow(1.0 + cumulative, 1.0 / years) - 1.0;
        }

        // Ratio de Sharpe : mesure le rendement excédentaire par unité de risque
        // Formule : (Rendement_annualisé - Taux_sans_risque) / Volatilité_annualisée
        // > 2 = excellent, > 1 = bon, < 0 = le portefeuille perd de l'argent après risque
        // Taux sans risque = ce que tu gagnes sans risquer (ex: obligations d'État = 3%)
        public static double SharpeRatio(double[] dailyReturns, double riskFreeRate = 0.03)
        {
            double vol = AnnualizedVolatility(dailyReturns);

            // Volatilité nulle → impossible de calculer (division par zéro)
            if (vol == 0.0)
                return 0.0;
            
            // (Rendement - Taux sans risque) / Volatilité
            return (AnnualizedReturn(dailyReturns) - riskFreeRate) / vol;
        }

        // Ratio de Sortino : comme Sharpe mais pénalise uniquement la volatilité NÉGATIVE
        // Plus pertinent car personne ne se plaint des rendements positifs élevés
        // Formule : (Rendement - Taux_sans_risque) / Écart-type des rendements négatifs
        public static double SortinoRatio(double[] dailyReturns, double riskFreeRate = 0.03)
        {
            // On garde uniquement les jours où le portefeuille a perdu de l'argent
            var negativeReturns = dailyReturns.Where(r => r < 0).ToArray();

            // Volatilité calculée uniquement sur les jours négatifs, puis annualisée
            double downsideVol = StdDev(negativeReturns) * Math.Sqrt(TradingDaysPerYear);

            // Pas de jours négatifs -> ratio indéfini
            if (downsideVol == 0.0)
                return 0.0;

            return (AnnualizedReturn(dailyReturns) - riskFreeRate) / downsideVol;
        }

        // Ratio de Calmar : rendement annualisé divisé par la pire perte observée
        // Répond à : "est-ce que le rendement justifie les pertes maximales subies ?"
        // Plus le ratio est élevé, mieux le portefeuille se comporte par rapport à ses pires moments
        public static double CalmarRatio(double[] dailyReturns, double[] portfolioValues)
        {
            // Math.Abs car MaxDrawdown retourne une valeur négative (ex: -0.25 = -25%)
            double maxDD = Math.Abs(MaxDrawdown(portfolioValues));

            // Pas de drawdown -> ratio indéfini (division par zéro)
            if (maxDD == 0.0)
                return 0.0;

            return AnnualizedReturn(dailyReturns) / maxDD;
        }

        // Maximum Drawdown : la pire perte depuis un sommet jusqu'au creux suivant
        // Répond à : "au pire, combien j'aurais perdu si j'avais acheté au plus haut ?"
        // Ex : portefeuille à 100 → monte à 150 → redescend à 90 → MaxDrawdown = (90-150)/150 = -40%
        // Résultat toujours négatif ou nul
        public static double MaxDrawdown(double[] values)
        {
            // Moins de 2 valeurs -> pas de drawdown calculable
            if (values.Length < 2)
                return 0.0;

            // On commence avec la première valeur comme sommet initial
            double peak = values[0];
            double maxDrawdown = 0.0;

            foreach (double v in values)
            {
                // Nouveau sommet → on met à jour le pic de référence
                if (v > peak)
                    peak = v;   // nouveau sommet

                // Drawdown depuis le sommet : (valeur actuelle - sommet) / sommet
                double drawdown = (v - peak) / peak;

                // On garde le pire drawdown observé
                if (drawdown < maxDrawdown)
                    maxDrawdown = drawdown;   // pire drawdown observé jusqu'ici
            }

            return maxDrawdown; // valeur négative ex: -0.25 = -25%
        }

        // Série de drawdown jour par jour (pour le graphique drawdown dans le backtest)
        // Pour chaque jour : quel est le recul en % depuis le dernier sommet ?
        // Toutes les valeurs sont ≤ 0 (on ne peut pas être au-dessus du sommet)
        public static double[] DrawdownSeries(double[] values)
        {
            if (values.Length == 0)
                return Array.Empty<double>();

            double peak = values[0]; // sommet courant

            return values.Select(v =>
            {
                // Met à jour le sommet si on dépasse l'ancien
                if (v > peak) peak = v;
                // Recul depuis le sommet = (valeur - sommet) / sommet
                return (v - peak) / peak;
            }).ToArray();
        }

        // VaR (Value at Risk) : la perte que tu risques de dépasser dans 5% des cas (conf 95%)
        // Ex : VaR 95% = -2% -> dans 5% des jours, tu perds plus de 2%
        // Utilise Percentile() pour la cohérence avec CVaR et Monte Carlo
        // Résultat négatif (c'est une perte)
        public static double VaR(double[] dailyReturns, double confidenceLevel = 0.95)
        {
            if (dailyReturns.Length == 0)
                return 0.0;

            // Trie les rendements du plus petit (pire perte) au plus grand (meilleur gain)
            var sorted = dailyReturns.OrderBy(r => r).ToArray();

            // Le percentile 5% des rendements = la limite des 5% pires jours
            return Percentile(sorted, 1.0 - confidenceLevel);
        }

        // CVaR (Conditional VaR) = Expected Shortfall
        // Répond à : "EN MOYENNE, combien je perds dans les pires 5% des cas ?"
        // Plus prudent que VaR car mesure la SÉVÉRITÉ des pertes extrêmes, pas juste leur seuil
        public static double CVaR(double[] dailyReturns, double confidenceLevel = 0.95)
        {
            double varThreshold = VaR(dailyReturns, confidenceLevel);

            // On garde uniquement les rendements en dessous du seuil VaR
            var tail = dailyReturns.Where(r => r <= varThreshold).ToArray();

            // Moyenne de ces pires jours -> plus négatif que VaR
            return tail.Length > 0 ? tail.Average() : varThreshold;
        }

        // Bêta : mesure la sensibilité du portefeuille aux mouvements du marché
        // β = 1   → suit exactement le marché
        // β > 1   → amplifie les mouvements du marché (plus risqué)
        // β < 1   → amortit les mouvements (moins risqué)
        // β négatif → évolue en sens inverse du marché
        // Formule : Cov(portefeuille, benchmark) / Var(benchmark)
        public static double Beta(double[] portfolioReturns, double[] benchmarkReturns)
        {
            // Variance du benchmark = covariance avec lui-même
            double varBenchmark = Covariance(benchmarkReturns, benchmarkReturns);

            // Si la variance du benchmark est nulle (marché immobile), β = 1 par convention
            if (varBenchmark == 0.0)
                return 1.0;

            return Covariance(portfolioReturns, benchmarkReturns) / varBenchmark;
        }

        // Alpha de Jensen : surperformance du portefeuille vs ce que le CAPM prédit
        // α > 0 → le portefeuille fait mieux que ce que son risque (β) justifie → bonne gestion
        // α < 0 → le portefeuille sous-performe → mauvaise gestion ou malchance
        // α = 0 → performance exactement conforme au CAPM
        // Formule : Rp - [Rf + β × (Rb - Rf)]
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

        // Matrice de covariance n×n : mesure comment chaque paire d'actifs bouge ensemble
        // Taille n = nombre d'actifs dans le portefeuille
        // Annualisée (× 252) car les rendements en entrée sont journaliers
        // La matrice est symétrique : cov[i,j] = cov[j,i]
        public static double[,] CovarianceMatrix(double[][] returnsMatrix)
        {
            int n = returnsMatrix.Length; // nombre d'actifs
            var cov = new double[n, n]; // matrice n×n initialisée à zéro

            for (int i = 0; i < n; i++)
            {
                for (int j = i; j < n; j++) // j commence à i -> on calcule la moitié et on symétrise
                {
                    // Covariance annualisée entre actif i et actif j
                    double c = Covariance(returnsMatrix[i], returnsMatrix[j]) * TradingDaysPerYear;
                    cov[i, j] = c;
                    cov[j, i] = c;   // symétrie
                }
            }

            return cov;
        }

        // Rendement attendu d'un portefeuille pondéré
        // Formule : R_p = Σ (poids_i × rendement_i)
        // Ex : 60% AAPL (15%/an) + 40% MSFT (12%/an) → 0.6×0.15 + 0.4×0.12 = 13.8%/an
        public static double PortfolioReturn(double[] weights, double[] expectedReturns)
        {
            double result = 0.0;

            // Somme pondérée des rendements individuels
            for (int i = 0; i < weights.Length; i++)
                result += weights[i] * expectedReturns[i];

            return result;
        }

        // Volatilité d'un portefeuille pondéré
        // Formule : σ_p = √(wᵀ Σ w) où Σ = matrice de covariance
        // Tient compte des corrélations -> un portefeuille diversifié est moins volatile
        // que la moyenne pondérée des volatilités individuelles
        public static double PortfolioVolatility(double[] weights, double[,] covMatrix)
        {
            int n = weights.Length;
            double variance = 0.0;

            // Double boucle : calcule wᵀ Σ w
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    variance += weights[i] * weights[j] * covMatrix[i, j];

            // Math.Max(0, ...) évite un sqrt de nombre légèrement négatif dû aux arrondis flottants
            return Math.Sqrt(Math.Max(0.0, variance));
        }

        // Normalise une série de valeurs en base 100
        // La première valeur devient 100, les suivantes sont proportionnelles
        // Utilisé pour comparer portefeuille et benchmark sur le même graphique
        // Ex : [150, 165, 145] → [100, 110, 96.67]
        public static double[] NormalizeToBase100(double[] values)
        {
            // Si vide ou première valeur = 0 → tout à 100 pour éviter la division par zéro
            if (values.Length == 0 || values[0] == 0.0)
                return values.Select(_ => 100.0).ToArray();

            double base_ = values[0]; // valeur de référence (le premier point = 100)

            // Chaque valeur divisée par la valeur initiale × 100
            return values.Select(v => v / base_ * 100.0).ToArray();
        }

        // Calcule le percentile d'un tableau DÉJÀ TRIÉ par ordre croissant
        // p = 0.05 -> 5e percentile (les 5% pires valeurs)
        // p = 0.50 -> médiane (50% des valeurs sont en dessous)
        // p = 0.95 -> 95e percentile (les 5% meilleures valeurs)
        // Utilise une interpolation linéaire entre les deux valeurs encadrantes
        public static double Percentile(double[] sortedValues, double p)
        {
            if (sortedValues.Length == 0)
                return 0.0;

            // Position exacte dans le tableau (peut être un nombre décimal)
            double idx = p * (sortedValues.Length - 1);

            // Index inférieur et supérieur autour de la position exacte
            int lo = (int)Math.Floor(idx);
            int hi = Math.Min(lo + 1, sortedValues.Length - 1);

            // Fraction entre les deux -> pour interpoler
            double frac = idx - lo;

            // Interpolation linéaire : valeur_basse × (1-frac) + valeur_haute × frac
            return sortedValues[lo] * (1.0 - frac) + sortedValues[hi] * frac;
        }

        // Génère un nombre aléatoire suivant la loi normale standard N(0,1)
        // Utilisé dans Monte Carlo pour simuler les chocs journaliers du marché
        // Transformation de Box-Muller : convertit deux nombres uniformes [0,1] en normale
        public static double NormalRandom(Random rng)
        {
            //// 1.0 - NextDouble() pour éviter log(0) → NextDouble() peut retourner 0 exactement
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();

            // Formule de Box-Muller : produit une valeur N(0,1)
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }
    }
}
