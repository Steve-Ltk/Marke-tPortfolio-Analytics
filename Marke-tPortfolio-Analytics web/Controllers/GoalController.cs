using Marke_tPortfolio_Analytics_web.Helpers;
using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    // Wizard "Mon Objectif" en 3 étapes dans une seule vue Wizard.cshtml.
    // Résultat Monte Carlo simplifié dans Result.cshtml.
    //
    // Flux complet :
    // GET  /Goal/Wizard?step=1  -> étape 1 (choix objectif)
    // POST /Goal/Wizard?step=1  -> valide + redirect step=2
    // GET  /Goal/Wizard?step=2  -> étape 2 (profil de risque)
    // POST /Goal/Wizard?step=2  -> valide + redirect step=3
    // GET  /Goal/Wizard?step=3  -> étape 3 (portefeuille suggéré)
    // POST /Goal/Wizard?step=3  -> calcule simulation -> Result.cshtml
    // POST /Goal/CreateFromGoal -> crée le portefeuille -> Portfolios/Details
    public class GoalController : BaseController
    {
        public GoalController(IApiService api, ILogger<GoalController> logger)
            : base(api, logger) { }

        // GET /Goal/Wizard?step=X -> affiche l'étape X du wizard

        [HttpGet]
        public IActionResult Wizard(int step = 1)
        {
            // Clamp -> garantit que step est entre 1 et 4
            step = Math.Clamp(step, 1, 4);

            var vm = BuildWizardVm(step);
            return View(vm);
        }

        // POST /Goal/Wizard?step=X -> traite l'étape X et redirige vers la suivante

        [HttpPost, ValidateAntiForgeryToken]
        [Route("Goal/Wizard")]
        public IActionResult Wizard(GoalWizardViewModel model, [FromQuery] int step = 1)
        {
            if (step == 1)
            {
                if (string.IsNullOrWhiteSpace(model.Objectif))
                {
                    ModelState.AddModelError("Objectif", "Choisissez un objectif.");
                    model.Step = 1;
                    return View(model);
                }
                TempData["Goal_Objectif"] = model.Objectif;
                return RedirectToAction(nameof(Wizard), new { step = 2 });
            }

            // Stocke toutes les données de l'étape 2 en TempData
            if (step == 2)
            {
                TempData["Goal_Objectif"] = model.Objectif;
                TempData["Goal_ScoreRisque"] = model.ScoreRisque;
                TempData["Goal_HorizonAns"] = model.HorizonAns;
                TempData["Goal_CapitalInitial"] = model.CapitalInitial.ToString();
                return RedirectToAction(nameof(Wizard), new { step = 3 });
            }

            if (step == 3)
            {
                // Passer à Result avec la simulation
                // TempData.Keep() -> empêche TempData d'être effacé après la redirection
                TempData.Keep();
                return RedirectToAction(nameof(Result));
            }

            return RedirectToAction(nameof(Wizard), new { step = 1 });
        }

        // GET /Goal/Result -> affiche la simulation Monte Carlo simplifiée
        [HttpGet]
        public IActionResult Result()
        {
            // Si TempData manque -> wizard pas complété -> retour étape 1
            if (!TryGetWizardData(out var obj, out var score, out var horizon, out var capital))
                return RedirectToAction(nameof(Wizard), new { step = 1 });

            // Récupère le template de portefeuille selon le score de risque
            var template = GoalTemplates.GetTemplate(score);

            // Lance la simulation log-normale simplifiée (pas de GBM complet)
            var vm = SimulerLogNormale(capital, template, horizon);

            // Complète le ViewModel avec les données du wizard
            vm.Objectif = obj;
            vm.ScoreRisque = score;
            vm.HorizonAns = horizon;
            vm.CapitalInitial = capital;
            vm.Template = template;

            return View(vm);
        }

        // POST /Goal/CreateFromGoal -> crée le portefeuille recommandé
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFromGoal(int scoreRisque)
        {
            var template = GoalTemplates.GetTemplate(scoreRisque);
            int userId = GetUserId() ?? 0;

            var portfolio = await ApiService.CreatePortfolioAsync(
                template.Nom, "EUR", userId);

            if (portfolio == null)
            {
                SetError("Impossible de créer le portefeuille.");
                return RedirectToAction(nameof(Result));
            }

            SetSuccess($"Portefeuille « {template.Nom} » créé ! Ajoutez vos positions.");
            return RedirectToAction("Details", "Portfolios", new { id = portfolio.Id });
        }

        // Construit le ViewModel du wizard en lisant les données TempData existantes
        private GoalWizardViewModel BuildWizardVm(int step)
        {
            TryGetWizardData(out var obj, out var score, out var horizon, out var capital);
            var vm = new GoalWizardViewModel
            {
                Step = step,
                Objectif = obj,
                ScoreRisque = score,
                HorizonAns = horizon,
                CapitalInitial = capital,
                // Template seulement à partir de l'étape 3
                Template = step >= 3 ? GoalTemplates.GetTemplate(score) : null
            };
            return vm;
        }

        // Lit les données du wizard depuis TempData
        // TempData.Peek -> lit sans effacer (contrairement à TempData[key])
        // Retourne false si l'objectif manque -> wizard pas complété
        private bool TryGetWizardData(
            out string objectif, out int score, out int horizon, out decimal capital)
        {
            objectif = TempData.Peek("Goal_Objectif") as string ?? string.Empty;
            score = TempData.Peek("Goal_ScoreRisque") is int s ? s : 5;
            horizon = TempData.Peek("Goal_HorizonAns") is int h ? h : 10;
            capital = decimal.TryParse(
                           TempData.Peek("Goal_CapitalInitial") as string,
                           out var c) ? c : 10000m;
            return !string.IsNullOrEmpty(objectif);
        }

        // Simule l'évolution du capital avec une formule log-normale simplifiée
        // Plus rapide que Monte Carlo complet -> suffisant pour l'estimation du wizard
        // mu = rendement annuel estimé, sigma = volatilité estimée (depuis le template)
        private static GoalResultViewModel SimulerLogNormale(
            decimal capital, GoalTemplates.Template t, int n)
        {
            double mu = (double)(t.CagrEstime / 100);
            double sigma = (double)(t.VolatiliteEstimee / 100);
            double cap = (double)capital;
            int pts = 20; // nombre de points sur le graphique
            // Médiane : cap × e^(μ×n) -> scénario central sans volatilité
            double mediane = cap * Math.Exp(mu * n);
             // P5 : cap × e^((μ - 1.645σ)×√n) -> scénario pessimiste (5% pires cas)
             // 1.645 = quantile 95% de la loi normale standard
            double p5 = cap * Math.Exp((mu - 1.645 * sigma) * Math.Sqrt(n));
            // P95 : cap × e^((μ + 1.645σ)×√n) -> scénario optimiste (5% meilleurs cas)
            double p95 = cap * Math.Exp((mu + 1.645 * sigma) * Math.Sqrt(n));
            // Probabilité de gain -> Phi(μ×√n / σ) -> loi normale cumulée
            double prob = Phi(mu * Math.Sqrt(n) / sigma) * 100;

            var labels = new List<string>();
            var med = new List<decimal>();
            var low = new List<decimal>();
            var high = new List<decimal>();

            // Génère les points de la courbe pour le graphique
            for (int i = 0; i <= pts; i++)
            {
                double t_ = (double)n * i / pts;
                labels.Add($"An {t_:F1}"); fraction de l'horizon
                med.Add((decimal)(cap * Math.Exp(mu * t_)));
                // +0.001 -> évite sqrt(0) au premier point
                low.Add((decimal)(cap * Math.Exp((mu - 1.645 * sigma) * Math.Sqrt(t_ + 0.001))));
                high.Add((decimal)(cap * Math.Exp((mu + 1.645 * sigma) * Math.Sqrt(t_ + 0.001))));
            }

            return new GoalResultViewModel
            {
                MedianeFinale = Math.Round((decimal)mediane, 0),
                P5Finale = Math.Round((decimal)p5, 0),
                P95Finale = Math.Round((decimal)p95, 0),
                ProbabiliteGain = Math.Round((decimal)prob, 1),
                CourbeMediane = med,
                CourbeP5 = low,
                CourbeP95 = high,
                Labels = labels
            };
        }

        // Phi = fonction de répartition de la loi normale standard N(0,1)
        // Approximation polynomiale de Abramowitz & Stegun -> précision suffisante pour le wizard
        // Retourne la probabilité que X <= x pour X ~ N(0,1)
        private static double Phi(double x)
        {
            double t = 1.0 / (1.0 + 0.2316419 * Math.Abs(x));
            double d = 0.3989422820 * Math.Exp(-x * x / 2.0);
            double p = d * t * (0.3193815 + t * (-0.3565638
                      + t * (1.7814779 + t * (-1.8212560 + t * 1.3302744))));
            return x > 0 ? 1 - p : p;
        }
    }
}
