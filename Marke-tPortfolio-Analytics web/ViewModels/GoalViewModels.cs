using System.ComponentModel.DataAnnotations;
using static Marke_tPortfolio_Analytics_web.Helpers.GoalTemplates;

namespace Marke_tPortfolio_Analytics_web.ViewModels
{
    /// <summary>
    /// ViewModel unique pour Goal/Wizard.cshtml — toutes les 4 étapes.
    /// La propriété Step indique quelle section afficher.
    /// </summary>
    public class GoalWizardViewModel
    {
        public int Step { get; set; } = 1;

        public string Objectif { get; set; } = string.Empty;

        [Range(0, 10)]
        public int ScoreRisque { get; set; } = 5;

        [Range(1, 50)]
        public int HorizonAns { get; set; } = 10;

        [Range(1000, 10_000_000)]
        public decimal CapitalInitial { get; set; } = 10000;

        /// <summary>Renseigné à partir de l'étape 3.</summary>
        public Template? Template { get; set; }
    }

    /// <summary>
    /// ViewModel pour Goal/Result.cshtml — résultats simulation Monte Carlo.
    /// </summary>
    public class GoalResultViewModel
    {
        public string Objectif { get; set; } = string.Empty;
        public int ScoreRisque { get; set; }
        public int HorizonAns { get; set; }
        public decimal CapitalInitial { get; set; }
        public Template Template { get; set; } = null!;

        public decimal MedianeFinale { get; set; }
        public decimal P5Finale { get; set; }
        public decimal P95Finale { get; set; }
        public decimal ProbabiliteGain { get; set; }

        public List<decimal> CourbeMediane { get; set; } = new();
        public List<decimal> CourbeP5 { get; set; } = new();
        public List<decimal> CourbeP95 { get; set; } = new();
        public List<string> Labels { get; set; } = new();
    }
}
