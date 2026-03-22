namespace Marke_tPortfolio_Analytics_web.Helpers
{
    // Définit les 4 templates de portefeuille selon le profil de risque (score 0-10).
    // Utilisés par :
    // -> GoalController.Wizard (étape 3 -> affiche le template suggéré)
    // -> GoalController.Result (simulation log-normale avec CagrEstime et VolatiliteEstimee)
    // -> GoalController.CreateFromGoal (crée les positions automatiquement)
    public static class GoalTemplates
    {
        // PoidsActif = un actif dans l'allocation avec son poids et sa couleur graphique
        // "record" -> classe immutable légère -> parfait pour des données fixes
        public record PoidsActif(string Ticker, decimal Poids, string Couleur);

        public record Template(
            string Nom,
            string Description,
            int ScoreMin,
            int ScoreMax,
            decimal CagrEstime,    
            decimal VolatiliteEstimee, 
            string Profil,
            string ClasseRisque,
            string Positionnement,
            string Couleur,
            List<PoidsActif> Allocation
        );

        // Retourne le template correspondant au score de risque (0-10)
        // "switch expression" -> syntaxe moderne équivalente à if/else if
        public static Template GetTemplate(int scoreRisque) => scoreRisque switch
        {
            <= 3 => Prudent, // score 0-3 -> profil prudent
            <= 6 => Equilibre, // score 4-6 -> profil équilibré
            <= 8 => Dynamique, // score 7-8 -> profil dynamique
            _ => Agressif // score 9-10 -> profil agressif
        };
        
        // PRUDENT (score 0-3)
        // Préservation du capital -> 50% JNJ (défensif santé) comme substitut obligataire
        // CAGR estimé faible (4.5%) en échange d'une volatilité très basse (6%)
        // OAT remplacé par JNJ -> ETF obligataires bloqués sur plan FMP gratuit
        public static readonly Template Prudent = new(
            Nom: "Portefeuille Prudent",
            Description: "Allocation défensive visant la préservation du capital avec une volatilité minimale.",
            Profil: "Défensif",
            ClasseRisque: "Faible",
            Couleur: "var(--blue)",
            ScoreMin: 0, ScoreMax: 3, 
            CagrEstime: 4.5m, VolatiliteEstimee: 6m,
            Positionnement: "Préservation du capital",
            Allocation: new()
            {
                new("AAPL",   15m, "#00d084"),
                new("MSFT",   15m, "#3b82f6"),
                new("OR.PA",  10m, "#8b5cf6"),
                new("TTE.PA", 10m, "#f59e0b"),
                new("JNJ",    50m, "#94a3b8"),
            }
        );

        // ÉQUILIBRÉ (score 4-6)
        // Mix croissance / défensif -> 25% JNJ pour stabiliser le portefeuille
        // CAGR estimé modéré (7%) avec volatilité acceptable (12%)
        public static readonly Template Equilibre = new(
            Nom: "Portefeuille Équilibré",
            Profil: "Équilibré",
            ClasseRisque: "Modéré",
            ScoreMin: 4, ScoreMax: 6, CagrEstime: 7m, VolatiliteEstimee: 12m,
            Description: "Équilibre rendement/risque. Convient pour un horizon moyen terme (5-10 ans) avec une tolérance modérée.",
            Positionnement: "Allocation diversifiée", Couleur: "var(--green)",
            Allocation: new()
            {
                new("MSFT",   25m, "#3b82f6"),
                new("AAPL",   20m, "#00d084"),
                new("OR.PA",  15m, "#8b5cf6"),
                new("TTE.PA", 15m, "#f59e0b"),
                new("JNJ",    25m, "#94a3b8"),
            }
        );

        // DYNAMIQUE (score 7-8) 
        // Fort biais tech/croissance -> pas de JNJ -> tolérance haute à la volatilité
        // CAGR estimé élevé (10%) mais volatilité significative (18%)
        public static readonly Template Dynamique = new(
            Nom: "Portefeuille Dynamique",
            Description: "Croissance à long terme. Fort biais actions tech. Horizon 10+ ans conseillé.",
            Profil: "Croissance",
            ClasseRisque: "Élevé",
            Positionnement: "Orientation actions",
            ScoreMin: 7, ScoreMax: 8,
            CagrEstime: 10m, VolatiliteEstimee: 18m,
            Couleur: "var(--amber)",
            Allocation: new()
            {
                new("AAPL",   30m, "#00d084"),
                new("MSFT",   25m, "#3b82f6"),
                new("NVDA",   20m, "#f59e0b"),
                new("OR.PA",  15m, "#8b5cf6"),
                new("TTE.PA", 10m, "#f43f5e"),
            }
        );

        // AGRESSIF (score 9-10) 
        // Maximisation du rendement -> 100% actions tech/croissance
        // CAGR estimé très élevé (14%) mais volatilité extrême (26%)
        public static readonly Template Agressif = new(
            Nom: "Portefeuille Agressif",
            Description: "Maximisation du rendement. Très forte vollatilité.",
            Profil: "Offensif",
            ClasseRisque: "Très élevé",
            Positionnement: "Maximisation du rendement",
            ScoreMin: 9, ScoreMax: 10,
            CagrEstime: 14m, VolatiliteEstimee: 26m,
            Couleur: "var(--red)",
            Allocation: new()
            {
                new("NVDA",   35m, "#f59e0b"),
                new("AAPL",   30m, "#00d084"),
                new("MSFT",   25m, "#3b82f6"),
                new("AMZN",   10m, "#f43f5e"),
            }
        );

        // Liste de tous les templates -> utilisé dans les vues qui affichent tous les profils
        public static List<Template> Tous => new() { Prudent, Equilibre, Dynamique, Agressif };
    }
}
