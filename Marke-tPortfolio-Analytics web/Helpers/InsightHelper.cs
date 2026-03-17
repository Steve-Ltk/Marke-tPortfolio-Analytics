namespace Marke_tPortfolio_Analytics_web.Helpers
{
    public static class InsightHelper
    {
        public record InsightResult(
            string Niveau,
            string Couleur,
            string Signification,
            string Cause,
            string Action
        );

        public static InsightResult Sharpe(double v) => v switch
        {
            < 0 => new("Danger", "var(--red)", "Rendement négatif après risque",
                        "Pertes ou volatilité excessive",
                        "Réduire l'exposition aux actifs très volatils"),
            < 1 => new("Insuffisant", "var(--amber)", "Rendement insuffisant pour le risque",
                        "Portefeuille peu diversifié",
                        "Ajouter des obligations ou actifs décorrélés"),
            < 2 => new("Bon", "var(--green)", "Bon rendement ajusté au risque",
                        "Allocation équilibrée",
                        "Optimiser les poids pour atteindre > 2"),
            _ => new("Excellent", "var(--green)", "Performance institutionnelle",
                        "Diversification optimale",
                        "Maintenir l'allocation actuelle")
        };

        public static InsightResult Volatilite(double v) => v switch
        {
            > 25 => new("Danger", "var(--red)", "Portefeuille très risqué",
                        "Concentration sur des actifs fortement volatils",
                        "Allouer 20-30% en obligations ou actifs défensifs"),
            > 15 => new("Insuffisant", "var(--amber)", "Volatilité élevée",
                        "Exposition tech ou croissance trop importante",
                        "Diversifier avec des secteurs défensifs"),
            > 8 => new("Bon", "var(--green)", "Volatilité maîtrisée",
                        "Bonne diversification sectorielle",
                        "Surveiller les corrélations en période de stress"),
            _ => new("Excellent", "var(--green)", "Très faible volatilité",
                        "Portefeuille défensif bien équilibré",
                        "Vérifier que le rendement reste suffisant")
        };

        public static InsightResult MaxDrawdown(double v) => Math.Abs(v) switch
        {
            > 35 => new("Danger", "var(--red)", "Risque de perte majeure",
                        "Forte concentration sur actifs cycliques",
                        "Stop-loss ou rebalancement immédiat requis"),
            > 20 => new("Insuffisant", "var(--amber)", "Drawdown préoccupant",
                        "Manque de couverture en période baissière",
                        "Ajouter des actifs refuge (or, obligations)"),
            > 10 => new("Bon", "var(--green)", "Drawdown acceptable",
                        "Diversification partielle efficace",
                        "Renforcer la protection via des puts ou obligations"),
            _ => new("Excellent", "var(--green)", "Très bonne résistance aux baisses",
                        "Portefeuille bien protégé",
                        "Maintenir la stratégie de couverture")
        };

        public static InsightResult Cagr(double v) => v switch
        {
            < 0 => new("Danger", "var(--red)", "Portefeuille en perte",
                        "Actifs sous-performants",
                        "Réviser l'allocation et couper les pertes"),
            < 4 => new("Insuffisant", "var(--amber)", "Sous-performance",
                        "Rendement inférieur à l'inflation",
                        "Augmenter l'exposition actions"),
            < 12 => new("Bon", "var(--green)", "Rendement solide",
                        "Bonne sélection d'actifs",
                        "Optimiser les poids pour maximiser le Sharpe"),
            _ => new("Excellent", "var(--green)", "Rendement exceptionnel",
                        "Forte exposition à des actifs en croissance",
                        "Vérifier la soutenabilité à long terme")
        };

        public static InsightResult ProbGain(double v) => v switch
        {
            < 50 => new("Danger", "var(--red)", "Plus de chance de perdre",
                        "Allocation trop risquée",
                        "Réduire l'horizon ou augmenter la diversification"),
            < 65 => new("Insuffisant", "var(--amber)", "Probabilité insuffisante",
                        "Volatilité trop élevée sur l'horizon",
                        "Ajouter des actifs à rendement stable"),
            < 80 => new("Bon", "var(--green)", "Bonne probabilité de gain",
                        "Portefeuille adapté à l'horizon",
                        "Optimiser l'allocation pour dépasser 80%"),
            _ => new("Excellent", "var(--green)", "Très haute probabilité",
                        "Excellent rapport rendement/risque",
                        "Maintenir la stratégie")
        };
    }
}
