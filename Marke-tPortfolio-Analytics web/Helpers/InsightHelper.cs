namespace Marke_tPortfolio_Analytics_web.Helpers
{

    // Traduit les métriques financières en niveaux qualitatifs lisibles par l'user.
    // Chaque méthode prend un nombre et retourne un InsightResult avec :
    // -> Niveau       : "Excellent", "Bon", "Insuffisant", "Danger"
    // -> Couleur      : couleur CSS selon le niveau
    // -> Signification : ce que ça veut dire
    // -> Cause        : pourquoi c'est ce niveau
    // -> Action       : ce que l'user devrait faire
    public static class InsightHelper
    {
        public record InsightResult(
            string Niveau,
            string Couleur,
            string Signification,
            string Cause,
            string Action
        );

        // Interprète le Sharpe Ratio
        // < 0  -> Danger      : le portefeuille perd de l'argent après ajustement du risque
        // 0-1  -> Insuffisant : rendement trop faible pour le risque pris
        // 1-2  -> Bon         : bon compromis rendement/risque
        // > 2  -> Excellent   : performance institutionnelle
        public static InsightResult Sharpe(double v) => v switch
        {
            < 0 => new("Danger", "var(--red)",
                        "Performance ajustée au risque défavorable",
                        "Pertes constatées ou volatilité excessive",
                        "Réduire l'exposition aux actifs les plus volatils"),

            < 1 => new("Insuffisant", "var(--amber)",
                        "Rendement insuffisant au regard du risque pris",
                        "Diversification limitée ou allocation peu efficiente",
                        "Renforcer la diversification avec des actifs défensifs ou décorrélés"),

            < 2 => new("Solide", "var(--green)",
                        "Performance ajustée au risque satisfaisante",
                        "Allocation globalement équilibrée",
                        "Ajuster les pondérations pour améliorer encore l'efficience"),

            _ => new("Excellent", "var(--green)",
                        "Performance ajustée au risque de très haut niveau",
                        "Allocation efficiente et diversification maîtrisée",
                        "Maintenir la structure actuelle sous surveillance")
        };

        
        // Interprète la volatilité annualisée (en %)
        // > 25% -> Danger      : portefeuille très risqué
        // 15-25% -> Insuffisant : volatilité élevée
        // 8-15%  -> Bon         : volatilité maîtrisée
        // < 8%   -> Excellent   : très faible volatilité
        public static InsightResult Volatilite(double v) => v switch
        {
            > 25 => new("Danger", "var(--red)",
                        "Niveau de risque très élevé",
                        "Concentration importante sur des actifs fortement volatils",
                        "Réallouer une partie du portefeuille vers des actifs défensifs"),

            > 15 => new("Insuffisant", "var(--amber)",
                        "Volatilité supérieure au niveau souhaitable",
                        "Exposition excessive aux segments cycliques ou de croissance",
                        "Mieux répartir l'allocation entre secteurs et styles d'investissement"),

            > 8 => new("Solide", "var(--green)",
                        "Volatilité maîtrisée",
                        "Diversification globalement efficace",
                        "Surveiller les corrélations lors des phases de stress de marché"),

            _ => new("Excellent", "var(--green)",
                        "Volatilité particulièrement faible",
                        "Construction défensive et bien équilibrée",
                        "Vérifier que ce profil prudent reste cohérent avec l'objectif de rendement")
        };
        
        // Interprète le Max Drawdown (en %)
        // Math.Abs -> on travaille sur la valeur absolue (MaxDrawdown est négatif)
        // > 35% -> Danger      : risque de perte majeure
        // 20-35% -> Insuffisant : drawdown préoccupant
        // 10-20% -> Bon         : drawdown acceptable
        // < 10%  -> Excellent   : très bonne résistance aux baisses
        public static InsightResult MaxDrawdown(double v) => Math.Abs(v) switch
        {
            > 35 => new("Danger", "var(--red)",
                        "Risque de perte sévère en phase baissière",
                        "Exposition marquée à des actifs cycliques ou insuffisamment couverts",
                        "Réduire rapidement le risque et mettre en place un rééquilibrage"),

            > 20 => new("Insuffisant", "var(--amber)",
                        "Drawdown élevé et préoccupant",
                        "Protection insuffisante en marché défavorable",
                        "Introduire davantage d'actifs refuges ou défensifs"),

            > 10 => new("Solide", "var(--green)",
                        "Drawdown contenu à un niveau acceptable",
                        "Diversification partiellement protectrice",
                        "Renforcer la résilience avec davantage de couverture ou d'actifs stabilisateurs"),

            _ => new("Excellent", "var(--green)",
                        "Très bonne résistance aux phases de baisse",
                        "Portefeuille bien structuré face au risque de repli",
                        "Maintenir la discipline d'allocation actuelle")
        };

        // Interprète le CAGR = rendement annualisé (en %)
        // < 0%  -> Danger      : portefeuille en perte
        // 0-4%  -> Insuffisant : sous-performance (en dessous de l'inflation)
        // 4-12% -> Bon         : rendement solide
        // > 12% -> Excellent   : rendement exceptionnel
        public static InsightResult Cagr(double v) => v switch
        {
            < 0 => new("Danger", "var(--red)",
                        "Performance annualisée négative",
                        "Actifs sous-performants ou allocation inadaptée",
                        "Réexaminer l'allocation et réduire les positions les moins efficientes"),

            < 4 => new("Insuffisant", "var(--amber)",
                        "Rendement annualisé modeste",
                        "Performance inférieure au niveau attendu à long terme",
                        "Revoir l'allocation pour améliorer le potentiel de croissance"),

            < 12 => new("Solide", "var(--green)",
                        "Rendement annualisé satisfaisant",
                        "Sélection d'actifs globalement cohérente",
                        "Optimiser les pondérations pour améliorer le couple rendement/risque"),

            _ => new("Excellent", "var(--green)",
                        "Rendement annualisé élevé",
                        "Exposition efficace à des actifs porteurs",
                        "Vérifier que ce niveau de performance reste soutenable dans la durée")
        };


        // Interprète la probabilité de gain du Monte Carlo (en %)
        // < 50%  -> Danger      : plus de chances de perdre que de gagner
        // 50-65% -> Insuffisant : probabilité insuffisante
        // 65-80% -> Bon         : bonne probabilité
        // > 80%  -> Excellent   : très haute probabilité de gain
        public static InsightResult ProbGain(double v) => v switch
        {
            < 50 => new("Danger", "var(--red)",
                        "Probabilité de gain inférieure au risque de perte",
                        "Allocation trop exposée à l'incertitude sur l'horizon retenu",
                        "Réduire le risque global ou revoir l'horizon d'investissement"),

            < 65 => new("Insuffisant", "var(--amber)",
                        "Probabilité de gain encore limitée",
                        "Niveau de volatilité trop élevé au regard du scénario projeté",
                        "Ajouter des actifs plus stables pour améliorer la robustesse"),

            < 80 => new("Solide", "var(--green)",
                        "Probabilité de gain favorable",
                        "Portefeuille globalement adapté à l'horizon de projection",
                        "Affiner l'allocation pour renforcer encore la probabilité de succès"),

            _ => new("Excellent", "var(--green)",
                        "Probabilité de gain très élevée",
                        "Profil rendement/risque particulièrement favorable",
                        "Maintenir la stratégie actuelle tout en surveillant les conditions de marché")
        };
    }
}
