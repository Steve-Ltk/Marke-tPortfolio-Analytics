using System.ComponentModel.DataAnnotations;
using static Marke_tPortfolio_Analytics_web.Helpers.GoalTemplates;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    // ViewModel unique pour Goal/Wizard.cshtml — toutes les étapes
    // Step indique quelle section afficher dans la vue
    public class GoalWizardViewModel
    {
        public int Step { get; set; } = 1;

        // Objectif choisi à l'étape 1
        public string Objectif { get; set; } = string.Empty;

        // Score de risque choisi à l'étape 2
        [Range(0, 10)]
        public int ScoreRisque { get; set; } = 5;

        // Horizon d'investissement en années
        [Range(1, 50)]
        public int HorizonAns { get; set; } = 10;

        // Capital initial en euros -> validé > 0 dans GoalController
        [Range(1000, 10_000_000)]
        public decimal CapitalInitial { get; set; } = 10000;

        // Template de portefeuille suggéré -> renseigné à partir de l'étape 3
        // Null aux étapes 1 et 2 -> la vue ne l'affiche pas
        public Template? Template { get; set; }
    }

    // ViewModel de Goal/Result.cshtml — résultats de la simulation log-normale
    public class GoalResultViewModel
    {
        // Données du wizard récupérées depuis TempData
        public string Objectif { get; set; } = string.Empty;
        public int ScoreRisque { get; set; }
        public int HorizonAns { get; set; }
        public decimal CapitalInitial { get; set; }
        public Template Template { get; set; } = null!;

        // Résultats de la simulation log-normale
        public decimal MedianeFinale { get; set; } // scénario central après HorizonAns ans
        public decimal P5Finale { get; set; } // scénario pessimiste (5% pires cas)
        public decimal P95Finale { get; set; } // scénario optimiste (5% meilleurs cas)
        public decimal ProbabiliteGain { get; set; } // % de chances que la valeur finale > capital initial
        
        // Séries de points pour le graphique fan chart
        // 21 points (0 à HorizonAns) pour chaque courbe
        public List<decimal> CourbeMediane { get; set; } = new();
        public List<decimal> CourbeP5 { get; set; } = new();
        public List<decimal> CourbeP95 { get; set; } = new();
        public List<string> Labels { get; set; } = new(); // ex: "An 0", "An 0.5", ..., "An 10"
    }
}
