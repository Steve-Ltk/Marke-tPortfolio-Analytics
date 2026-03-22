namespace Marke_tPortfolio_Analytics_web.Helpers
{
    /// <summary>
    /// Templates de portefeuille selon le profil de risque (score 0-10).
    /// Définis dans le CDC : Prudent, Équilibré, Dynamique, Agressif.
    /// </summary>
    public static class GoalTemplates
    {
        public record PoidsActif(string Ticker, decimal Poids, string Couleur);

        public record Template(
            string Nom,
            string Description,
            int ScoreMin,
            int ScoreMax,
            decimal CagrEstime,    // % annuel
            decimal VolatiliteEstimee, // %
            string Icone,
            string Couleur,
            List<PoidsActif> Allocation
        );

        public static Template GetTemplate(int scoreRisque) => scoreRisque switch
        {
            <= 3 => Prudent,
            <= 6 => Equilibre,
            <= 8 => Dynamique,
            _ => Agressif
        };

        public static readonly Template Prudent = new(
            Nom: "Portefeuille Prudent",
            Description: "Préservation du capital, faible volatilité. Idéal pour un horizon court ou une tolérance au risque minimale.",
            ScoreMin: 0, ScoreMax: 3,
            CagrEstime: 4.5m, VolatiliteEstimee: 6m,
            Icone: "🛡️", Couleur: "var(--blue)",
            Allocation: new()
            {
                new("AAPL",   15m, "#00d084"),
                new("MSFT",   15m, "#3b82f6"),
                new("OR.PA",  10m, "#8b5cf6"),
                new("TTE.PA", 10m, "#f59e0b"),
                new("JNJ",    50m, "#94a3b8"),,
            }
        );

        public static readonly Template Equilibre = new(
            Nom: "Portefeuille Équilibré",
            Description: "Équilibre rendement/risque. Convient pour un horizon moyen terme (5-10 ans) avec une tolérance modérée.",
            ScoreMin: 4, ScoreMax: 6,
            CagrEstime: 7m, VolatiliteEstimee: 12m,
            Icone: "⚖️", Couleur: "var(--green)",
            Allocation: new()
            {
                new("MSFT",   25m, "#3b82f6"),
                new("AAPL",   20m, "#00d084"),
                new("OR.PA",  15m, "#8b5cf6"),
                new("TTE.PA", 15m, "#f59e0b"),
                new("JNJ",    50m, "#94a3b8"),
            }
        );

        public static readonly Template Dynamique = new(
            Nom: "Portefeuille Dynamique",
            Description: "Croissance à long terme. Fort biais actions tech. Horizon 10+ ans conseillé.",
            ScoreMin: 7, ScoreMax: 8,
            CagrEstime: 10m, VolatiliteEstimee: 18m,
            Icone: "🚀", Couleur: "var(--amber)",
            Allocation: new()
            {
                new("AAPL",   30m, "#00d084"),
                new("MSFT",   25m, "#3b82f6"),
                new("NVDA",   20m, "#f59e0b"),
                new("OR.PA",  15m, "#8b5cf6"),
                new("TTE.PA", 10m, "#f43f5e"),
            }
        );

        public static readonly Template Agressif = new(
            Nom: "Portefeuille Agressif",
            Description: "Maximisation du rendement. Très forte volatilité. Convient uniquement aux investisseurs expérimentés.",
            ScoreMin: 9, ScoreMax: 10,
            CagrEstime: 14m, VolatiliteEstimee: 26m,
            Icone: "⚡", Couleur: "var(--red)",
            Allocation: new()
            {
                new("NVDA",   35m, "#f59e0b"),
                new("AAPL",   30m, "#00d084"),
                new("MSFT",   25m, "#3b82f6"),
                new("AMZN",   10m, "#f43f5e"),
            }
        );

        public static List<Template> Tous => new() { Prudent, Equilibre, Dynamique, Agressif };
    }
}
