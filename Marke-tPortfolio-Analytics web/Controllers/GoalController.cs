using Marke_tPortfolio_Analytics_web.Helpers;
using Marke_tPortfolio_Analytics_web.Services;
using Marke_tPortfolio_Analytics_web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Marke_tPortfolio_Analytics_web.Controllers
{
    /// <summary>
    /// Wizard Mon Objectif — 4 étapes dans UNE seule vue (Wizard.cshtml).
    /// Résultat Monte Carlo dans Result.cshtml.
    ///
    /// Flux :
    ///   GET  /Goal/Wizard?step=1   → Étape 1 (objectif)
    ///   POST /Goal/Wizard?step=1   → Valide + redirige step=2
    ///   GET  /Goal/Wizard?step=2   → Étape 2 (profil)
    ///   POST /Goal/Wizard?step=2   → Valide + redirige step=3
    ///   GET  /Goal/Wizard?step=3   → Étape 3 (template)
    ///   POST /Goal/Wizard?step=3   → Calcule simulation → Result.cshtml
    ///   POST /Goal/CreateFromGoal  → Crée le portefeuille → Portfolios/Details
    /// </summary>
    public class GoalController : BaseController
    {
        public GoalController(IApiService api, ILogger<GoalController> logger)
            : base(api, logger) { }

        // ── GET Wizard (toutes étapes) ────────────────────────────────────

        [HttpGet]
        public IActionResult Wizard(int step = 1)
        {
            step = Math.Clamp(step, 1, 4);

            var vm = BuildWizardVm(step);
            return View(vm);
        }

        // ── POST Étape 1 — Objectif ───────────────────────────────────────

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
                TempData.Keep();
                return RedirectToAction(nameof(Result));
            }

            return RedirectToAction(nameof(Wizard), new { step = 1 });
        }

        // ── GET Result — simulation Monte Carlo ───────────────────────────

        [HttpGet]
        public IActionResult Result()
        {
            if (!TryGetWizardData(out var obj, out var score, out var horizon, out var capital))
                return RedirectToAction(nameof(Wizard), new { step = 1 });

            var template = GoalTemplates.GetTemplate(score);
            var vm = SimulerLogNormale(capital, template, horizon);
            vm.Objectif = obj;
            vm.ScoreRisque = score;
            vm.HorizonAns = horizon;
            vm.CapitalInitial = capital;
            vm.Template = template;

            return View(vm);
        }

        // ── POST CreateFromGoal — crée le portefeuille ────────────────────

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

        // ── Helpers ───────────────────────────────────────────────────────

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
                Template = step >= 3 ? GoalTemplates.GetTemplate(score) : null
            };
            return vm;
        }

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

        private static GoalResultViewModel SimulerLogNormale(
            decimal capital, GoalTemplates.Template t, int n)
        {
            double mu = (double)(t.CagrEstime / 100);
            double sigma = (double)(t.VolatiliteEstimee / 100);
            double cap = (double)capital;
            int pts = 20;

            double mediane = cap * Math.Exp(mu * n);
            double p5 = cap * Math.Exp((mu - 1.645 * sigma) * Math.Sqrt(n));
            double p95 = cap * Math.Exp((mu + 1.645 * sigma) * Math.Sqrt(n));
            double prob = Phi(mu * Math.Sqrt(n) / sigma) * 100;

            var labels = new List<string>();
            var med = new List<decimal>();
            var low = new List<decimal>();
            var high = new List<decimal>();

            for (int i = 0; i <= pts; i++)
            {
                double t_ = (double)n * i / pts;
                labels.Add($"An {t_:F1}");
                med.Add((decimal)(cap * Math.Exp(mu * t_)));
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
