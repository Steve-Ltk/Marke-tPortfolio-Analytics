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
                // Validation : capital doit être positif
                if (model.CapitalInitial <= 0)
                {
                    ModelState.AddModelError("CapitalInitial",
                        "Le capital initial doit être supérieur à 0.");
                    model.Step = 2;
                    // Recharge le template si on est à l'étape 2
                    return View(model);
                }

                // Validation : horizon doit être positif
                if (model.HorizonAns <= 0)
                {
                    ModelState.AddModelError("HorizonAns",
                        "L'horizon doit être d'au moins 1 an.");
                    model.Step = 2;
                    return View(model);
                }

                TempData["Goal_Objectif"]       = model.Objectif;
                TempData["Goal_ScoreRisque"]    = model.ScoreRisque;
                TempData["Goal_HorizonAns"]     = model.HorizonAns;
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

        
        template.Nom, "EUR", userId);

    [HttpPost, ValidateAntiForgeryToken]
public async Task<IActionResult> CreateFromGoal(int scoreRisque)
{
    var template = GoalTemplates.GetTemplate(scoreRisque);
    int userId   = GetUserId() ?? 0;

    // Récupère le capital depuis TempData
    // Si absent → 10 000€ par défaut
    decimal capital = decimal.TryParse(
        TempData.Peek("Goal_CapitalInitial") as string,
        out var c) ? c : 10000m;

    // 1. Crée le portefeuille vide en EUR
    var portfolio = await ApiService.CreatePortfolioAsync(
        template.Nom, "EUR", userId);

    if (portfolio == null)
    {
        SetError("Impossible de créer le portefeuille.");
        return RedirectToAction(nameof(Result));
    }

    // 2. Récupère le taux EUR/USD une seule fois pour toute la boucle
    // Évite N appels FMP → un seul appel pour tous les actifs USD
    decimal tauxEurUsd = await ApiService.GetExchangeRateAsync("EUR", "USD");

    // Fallback neutre si FMP ne répond pas → pas de conversion
    if (tauxEurUsd <= 0) tauxEurUsd = 1m;

    int positionsCreees  = 0;
    decimal totalInvesti = 0m; // total réellement investi en EUR

    // 3. Pour chaque actif du template → importe + calcule quantité + crée position
    foreach (var alloc in template.Allocation)
    {
        // Vérifie si l'actif existe déjà en base
        var asset = await ApiService.GetAssetByTickerAsync(alloc.Ticker);

        // Sinon l'importe depuis FMP automatiquement
        if (asset == null)
            asset = await ApiService.ImportStockFromFmpAsync(alloc.Ticker);

        // Import échoué → on passe au suivant sans planter toute la création
        if (asset == null)
        {
            Logger.LogWarning(
                "CreateFromGoal : impossible d'importer {Ticker}",
                alloc.Ticker);
            continue;
        }

        // Récupère le prix actuel en devise native (USD ou EUR)
        var (prixNatif, _) = await ApiService.GetQuoteAsync(asset.Ticker);

        // Prix indisponible → on ne peut pas calculer de quantité → on saute
        if (prixNatif <= 0)
        {
            Logger.LogWarning(
                "CreateFromGoal : prix indisponible pour {Ticker}",
                alloc.Ticker);
            continue;
        }

        // Détermine si l'actif est en USD ou EUR
        // Ticker sans point → USD (AAPL, JNJ, MSFT...)
        // Ticker avec point → EUR (OR.PA, TTE.PA...)
        bool isUsd = AssetHelper.IsUsd(asset);

        // Convertit le prix en EUR pour calculer les quantités
        // Tous les calculs de répartition se font en EUR
        decimal prixEnEur = isUsd && tauxEurUsd > 0
            ? prixNatif / tauxEurUsd
            : prixNatif;

        // Montant alloué à cet actif selon son poids dans le template
        // Ex : capital=10 000€, poids=30% → montant=3 000€
        decimal montantAlloue = capital * (alloc.Poids / 100m);

        decimal quantite;

        // Ajustement 2 — capital insuffisant pour acheter 1 unité au poids cible
        // → on crée quand même la position avec 1 unité symbolique
        // → plutôt que de sauter l'actif et fausser la diversification
        if (montantAlloue < prixEnEur)
        {
            quantite = 1m;
            Logger.LogWarning(
                "CreateFromGoal : capital insuffisant pour {Ticker} " +
                "({Montant}€ < {Prix}€) → 1 unité symbolique",
                alloc.Ticker,
                Math.Round(montantAlloue, 2),
                Math.Round(prixEnEur, 2));
        }
        else
        {
            // Ajustement 1 — arrondi à 2 décimales
            // Ex : 3 000 / 217.6 = 13.78 actions
            // Plus réaliste qu'à 4 décimales (13.7800)
            quantite = Math.Round(montantAlloue / prixEnEur, 2);
        }

        // Sécurité → quantité nulle ou négative ne doit jamais arriver
        if (quantite <= 0) continue;

        // Crée la position en base
        // avgBuyPrice = prix NATIF (USD ou EUR selon l'actif) → convention du projet
        // On ne stocke jamais les prix convertis en base
        var position = await ApiService.CreatePositionAsync(
            portfolio.Id,
            asset.Id,
            quantity:    quantite,
            avgBuyPrice: prixNatif,
            buyDate:     DateTime.Today
        );

        if (position != null)
        {
            positionsCreees++;
            // Ajustement 3 — accumule le total réellement investi en EUR
            // quantite × prixEnEur = valeur réelle de la position en EUR
            totalInvesti += quantite * prixEnEur;
        }
    }

    // 4. Message final selon le résultat de la création
    if (positionsCreees == template.Allocation.Count)
    {
        // Toutes les positions créées → succès complet
        SetSuccess(
            $"Portefeuille « {template.Nom} » créé — " +
            $"€{totalInvesti:N0} investis sur €{capital:N0} de capital. " +
            $"Vous pouvez ajuster les quantités à tout moment.");
    }
    else if (positionsCreees > 0)
    {
        // Certaines positions échouées → succès partiel
        SetWarning(
            $"Portefeuille créé avec {positionsCreees}/{template.Allocation.Count} positions " +
            $"(€{totalInvesti:N0} investis sur €{capital:N0}). " +
            $"Certains actifs n'ont pas pu être importés depuis FMP.");
    }
    else
    {
        // Aucune position créée → portefeuille vide
        SetWarning(
            $"Portefeuille « {template.Nom} » créé mais vide. " +
            $"FMP n'a pas répondu pour tous les actifs. " +
            $"Ajoutez les positions manuellement.");
    }

    // Redirige vers le détail du portefeuille créé
    return RedirectToAction("Details", "Portfolios", new { id = portfolio.Id });
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
