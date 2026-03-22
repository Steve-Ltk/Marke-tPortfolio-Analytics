/**
 * analytics.js — Catalis
 *
 * Orchestre les 4 modules de la page Analytics :
 *   1. Onglets (data-tab-trigger / data-tab-panel)
 *      -> affiche/masque les panneaux quand l'user clique sur un onglet
 *   2. Appels AJAX POST vers AnalyticsController
 *      -> envoie les paramètres au serveur sans recharger la page
 *   3. Spinners chargement
 *      -> affiche un loader pendant que le backend calcule
 *   4. Injection résultats dans les partials déjà présents dans le DOM
 *      -> met à jour les éléments HTML avec les données reçues du serveur
 *   5. Appels charts.js pour les graphiques
 *      -> délègue le dessin des graphiques au fichier charts.js
 */

// IIFE = Immediately Invoked Function Expression
// La fonction s'exécute immédiatement dès que le fichier est chargé
// Pourquoi : tout ce qui est déclaré ici reste privé -> ne pollue pas le scope global
// Sans ça : "portfolioId", "show", "text"... seraient accessibles depuis n'importe quel autre script
(function () {
    'use strict';
    // Mode strict JavaScript -> détecte plus d'erreurs à l'exécution
    // -> interdit les variables non déclarées, etc.
    // -> bonne pratique systématique dans tout fichier JS

    // portfolioId injecté dans l'attribut data-portfolio-id de #analytics-root
    // Cet élément est rendu par Analytics/Index.cshtml :
    // <div id="analytics-root" data-portfolio-id="@Model.SelectedPortfolioId"></div>
    var root = document.getElementById('analytics-root');

    // parseInt(..., 10) -> convertit "42" en 42 (base 10)
    // root ? ... : 0 -> si l'élément n'existe pas, portfolioId = 0
    // || '0' -> si data-portfolio-id est vide, utilise '0' pour éviter NaN
    var portfolioId = root ? parseInt(root.dataset.portfolioId || '0', 10) : 0;

    // Petites fonctions réutilisées partout pour manipuler le DOM
    // Chacune vérifie que l'élément existe (if (el)) avant d'agir -> pas d'erreur si absent

    // Affiche un élément en display:block
    function show(id) {
        var el = document.getElementById(id);
        if (el) el.style.display = 'block';
    }

    // Masque un élément en display:none
    function hide(id) {
        var el = document.getElementById(id);
        if (el) el.style.display = 'none';
    }

    // Affiche un élément en display:flex
    // Utilisé pour les spinners qui utilisent flex pour centrer leur icône de chargement
    function flex(id) {
        var el = document.getElementById(id);
        if (el) el.style.display = 'flex';
    }

    // Modifie le texte visible d'un élément
    // textContent -> plus sûr que innerHTML (pas d'injection HTML possible)
    function text(id, val) {
        var el = document.getElementById(id);
        if (el) el.textContent = val;
    }

    // Injecte du HTML dans un élément
    // innerHTML -> utilisé uniquement pour insightHtml qui génère du HTML contrôlé côté JS
    // Attention : ne jamais utiliser innerHTML avec des données utilisateur non filtrées
    function html(id, val) {
        var el = document.getElementById(id);
        if (el) el.innerHTML = val;
    }

    // Change la couleur CSS d'un élément
    // Utilisé pour colorer les métriques en vert/rouge/amber selon leur valeur
    function color(id, c) {
        var el = document.getElementById(id);
        if (el) el.style.color = c;
    }

    // Retourne "+" si la valeur est positive, "" si négative
    // Utilisé pour afficher "+5.2%" au lieu de "5.2%" pour les valeurs positives
    function sign(v) { return v >= 0 ? '+' : ''; }

    // Génère le HTML d'un badge insight : niveau coloré + séparateur + message
    // Ex : "<strong style='color:var(--green)'>Bon</strong> — Rendement solide"
    // Appelé par html('mc-insight', insightHtml(...)) pour injecter dans le DOM
    function insightHtml(niveau, couleur, message) {
        return '<strong style="color:' + couleur + '">' + niveau + '</strong> — ' + message;
    }

    // Sécurité CSRF 
    // Récupère le token anti-CSRF généré par @Html.AntiForgeryToken() dans la vue
    // CSRF = Cross-Site Request Forgery -> attaque où un site tiers soumet un formulaire à ta place
    // Le token prouve que la requête vient bien de ta page -> ASP.NET rejette les POST sans lui
    // [ValidateAntiForgeryToken] dans le controller vérifie ce token côté serveur
    function csrfToken() {
        var t = document.querySelector('input[name="__RequestVerificationToken"]');
        // Si le token n'existe pas (ex: en dev sans la directive) -> retourne '' pour ne pas bloquer
        return t ? t.value : '';
    }

    //  Helpers AJAX 

    // Envoie une requête POST avec les données encodées comme un formulaire HTML classique
    // application/x-www-form-urlencoded -> format "portfolioId=1&numSimulations=1000&horizonDays=252"
    // Utilisé pour Monte Carlo, Backtest, Optimize -> paramètres simples (strings, nombres)
    // Retourne une Promise -> .then() sera appelé quand la réponse du serveur arrive
    function postForm(url, params) {
        // Object.keys(params) -> ["portfolioId", "numSimulations", "horizonDays"]
        // .map() -> transforme chaque clé en "key=value" encodé pour l'URL
        // encodeURIComponent -> encode les caractères spéciaux (espaces, accents, &, =...)
        // .join('&') -> "portfolioId=1&numSimulations=1000&horizonDays=252"
        var body = Object.keys(params)
            .map(function (k) {
                return encodeURIComponent(k) + '=' + encodeURIComponent(params[k]);
            })
            .join('&');

        return fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                // Header CSRF -> ASP.NET le lit et compare au cookie côté serveur
                'RequestVerificationToken': csrfToken()
            },
            body: body
        // .then(r.json()) -> convertit la réponse HTTP en objet JavaScript
        // Le controller retourne return Json(new { probGain = ..., var95 = ... })
        // -> on reçoit ici { probGain: 73.2, var95: 1250.5, ... }
        }).then(function (r) { return r.json(); });
    }

    // Envoie une requête POST avec un tableau JSON dans le body
    // Utilisé uniquement pour Compare -> envoie [1, 2, 3] (liste d'ids de portefeuilles)
    // Le controller déclare [FromBody] List<int> ids -> attend ce format JSON exact
    function postBody(url, data) {
        return fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            // JSON.stringify([1,2,3]) -> '[1,2,3]' (texte JSON envoyé dans le body)
            body: JSON.stringify(data)
        }).then(function (r) { return r.json(); });
    }

    // Onglets 

    // Initialise le système d'onglets basé sur les attributs HTML data-tab-trigger / data-tab-panel
    // La vue Index.cshtml génère par exemple :
    // <button data-tab-trigger="montecarlo">Monte Carlo</button>
    // <div data-tab-panel="montecarlo" style="display:none">...</div>
    function initTabs() {
        // querySelectorAll -> retourne TOUS les éléments portant cet attribut sous forme de NodeList
        var triggers = document.querySelectorAll('[data-tab-trigger]');
        var panels   = document.querySelectorAll('[data-tab-panel]');

        // .forEach -> parcourt chaque bouton d'onglet et y attache un écouteur de clic
        triggers.forEach(function (btn) {
            btn.addEventListener('click', function () {
                // this.dataset.tabTrigger -> lit l'attribut data-tab-trigger de CE bouton
                // ex: data-tab-trigger="montecarlo" -> target = "montecarlo"
                var target = this.dataset.tabTrigger;

                // Désactive tous les boutons -> retire la classe CSS "active" de chacun
                triggers.forEach(function (b) { b.classList.remove('active'); });

                // Masque tous les panneaux -> display:none sur chacun
                panels.forEach(function (p) { p.style.display = 'none'; });

                // Active le bouton cliqué -> lui ajoute la classe "active" (surlignage CSS)
                this.classList.add('active');

                // Affiche uniquement le panneau correspondant au bouton cliqué
                // querySelector('[data-tab-panel="montecarlo"]') -> trouve le bon div
                var panel = document.querySelector('[data-tab-panel="' + target + '"]');
                if (panel) panel.style.display = 'block';
            });
        });
    }

    // MONTE CARLO 

    function lancerMonteCarlo() {
        // Lit les paramètres depuis les champs du formulaire dans le partial _MonteCarlo.cshtml
        var nb      = document.getElementById('mc-nb').value;      // ex: "1000"
        var horizon = document.getElementById('mc-horizon').value; // ex: "252" (1 an de bourse)

        // Affiche le spinner de chargement, masque les anciens résultats
        flex('mc-spinner');
        hide('mc-result');

        // Envoie la requête AJAX vers AnalyticsController.MonteCarlo()
        postForm('/Analytics/MonteCarlo', {
            portfolioId:    portfolioId, // id lu depuis #analytics-root au démarrage
            numSimulations: nb,
            horizonDays:    horizon
        })
        .then(function (d) {
            // d = objet JavaScript construit depuis le JSON retourné par le controller
            // ex: { probGain: 73.2, var95: 1250.5, median: 12450, p5: 9800, p95: 16200, ... }

            hide('mc-spinner');

            // Si le backend a retourné une erreur -> alert et stop immédiat
            // ex: d.error = "Simulation impossible : pas assez d'historique de prix."
            if (d.error) { alert(d.error); return; }

            // Couleur de la probabilité de gain selon 3 seuils
            var probColor = d.probGain >= 80 ? 'var(--green)'
                : d.probGain >= 65 ? 'var(--amber)' : 'var(--red)';

            // Injecte chaque métrique dans son élément HTML correspondant dans le partial
            text('mc-prob',    d.probGain + '%');
            color('mc-prob',   probColor);
            text('mc-var95',   '€' + d.var95);
            text('mc-median',  '€' + d.median);
            text('mc-p5',      '€' + d.p5);
            text('mc-p95',     '€' + d.p95);
            text('mc-horizon-label', d.horizonDays   + ' jours de bourse');
            text('mc-sims-label',    d.nbSimulations + ' simulations');

            // Génère et injecte le badge insight (niveau coloré + message explicatif)
            html('mc-insight', insightHtml(d.insightNiveau, d.insightCouleur, d.insightMessage));

            // Dessine le fan chart (trajectoires simulées) si des données de série temporelle existent
            // d.fanMed = tableau des médianes jour par jour -> ex: [10000, 10150, 10320, ...]
            if (d.fanMed && d.fanMed.length > 0)
                charts.initFanChart('mc-chart', d);

            // Dessine le donut de probabilité de gain (arc vert proportionnel à probGain)
            charts.initProbDonut('mc-donut', d.probGain);

            // Rend visible la zone de résultats
            show('mc-result');
        })
        // .catch -> si fetch échoue (réseau coupé, timeout serveur, erreur 500...)
        .catch(function (e) { hide('mc-spinner'); alert('Erreur : ' + e.message); });
    }

    //  BACKTEST

    function lancerBacktest() {
        // Lit les paramètres du formulaire du partial _Backtest.cshtml
        var debut = document.getElementById('bt-debut').value; // date de début ex: "2022-01-01"
        var fin   = document.getElementById('bt-fin').value;   // date de fin ex: "2024-12-31"
        var bench = document.getElementById('bt-bench').value; // ticker benchmark ex: "SPY"

        flex('bt-spinner');
        hide('bt-result');

        // Envoie vers AnalyticsController.Backtest()
        postForm('/Analytics/Backtest', {
            portfolioId: portfolioId,
            dateDebut:   debut,
            dateFin:     fin,
            benchmark:   bench
        })
        .then(function (d) {
            hide('bt-spinner');
            if (d.error) { alert(d.error); return; }

            // sign() ajoute "+" devant les valeurs positives pour la lisibilité
            text('bt-port-ret',  sign(d.portfolioReturn) + d.portfolioReturn + '%');
            text('bt-bench-ret', sign(d.benchmarkReturn) + d.benchmarkReturn + '%');
            text('bt-alpha',     sign(d.alpha) + d.alpha + '%');
            text('bt-beta',      d.beta);
            text('bt-sharpe',    d.sharpe);
            // || '—' -> si sortino/calmar sont absents ou 0 -> affiche un tiret
            text('bt-sortino',   d.sortino || '—');
            text('bt-calmar',    d.calmar  || '—');
            text('bt-drawdown',  d.maxDrawdown + '%');

            // Colore les métriques directionnelles en vert si positif, rouge si négatif
            color('bt-port-ret',  d.portfolioReturn  >= 0 ? 'var(--green)' : 'var(--red)');
            color('bt-bench-ret', d.benchmarkReturn  >= 0 ? 'var(--green)' : 'var(--red)');
            color('bt-alpha',     d.alpha            >= 0 ? 'var(--green)' : 'var(--red)');

            // Dessine le graphique de performance (courbe portefeuille vs benchmark)
            charts.initBacktestChart('bt-chart', d);

            // Dessine la heatmap des rendements mensuels si disponible
            // d.monthlyReturns = [{ year: 2023, month: 1, returnPct: 2.3 }, ...]
            if (d.monthlyReturns && d.monthlyReturns.length > 0)
                charts.initMonthlyHeatmap('bt-heatmap', d.monthlyReturns);

            show('bt-result');
        })
        .catch(function (e) { hide('bt-spinner'); alert('Erreur : ' + e.message); });
    }

    // OPTIMISATION MARKOWITZ 

    function lancerOptimisation() {
        // target = stratégie d'optimisation choisie par l'user
        // ex: "MaxSharpe", "MinVolatility", "MaxReturn"
        var target = document.getElementById('opt-target').value;
        var debut  = document.getElementById('opt-debut').value;
        var fin    = document.getElementById('opt-fin').value;

        flex('opt-spinner');
        hide('opt-result');

        // Envoie vers AnalyticsController.Optimize()
        postForm('/Analytics/Optimize', {
            portfolioId: portfolioId,
            target:      target,
            dateDebut:   debut,
            dateFin:     fin
        })
        .then(function (d) {
            hide('opt-spinner');
            if (d.error) { alert(d.error); return; }

            // Métriques du portefeuille optimal calculé par Markowitz
            text('opt-ret',    '+' + d.expectedReturn + '%');
            text('opt-vol',    d.expectedVolatility + '%');
            text('opt-sharpe', d.sharpeRatio);

            // Delta Sharpe = Sharpe optimal - Sharpe actuel
            // Quantifie l'amélioration possible en rééquilibrant selon les poids suggérés
            // d.currentSharpe || 0 -> si le Sharpe actuel n'est pas dispo, on part de 0
            var delta = (d.sharpeRatio - (d.currentSharpe || 0)).toFixed(3);
            text('opt-delta',  sign(parseFloat(delta)) + delta);
            color('opt-delta', parseFloat(delta) >= 0 ? 'var(--green)' : 'var(--red)');

            // Génère les barres de poids recommandés dynamiquement en HTML
            // d.weights = [{ ticker: "AAPL", poids: 35.2 }, { ticker: "MSFT", poids: 25.1 }, ...]
            var container = document.getElementById('opt-weights');
            if (container && d.weights) {
                // .map() -> transforme chaque objet poids en une ligne HTML
                // .join('') -> concatène toutes les lignes en un seul string HTML
                container.innerHTML = d.weights.map(function (w) {
                    return '<div style="display:flex;align-items:center;gap:10px;margin-bottom:8px">'
                        // Colonne ticker : label du titre
                        + '<div style="font-size:13px;font-weight:700;width:60px;color:var(--t1)">' + w.ticker + '</div>'
                        // Barre de progression : largeur proportionnelle au poids en %
                        + '<div style="flex:1;height:8px;background:var(--dark-4);border-radius:999px;overflow:hidden">'
                        + '<div style="width:' + w.poids + '%;height:100%;background:var(--green);border-radius:999px"></div>'
                        + '</div>'
                        // Valeur numérique du poids en % alignée à droite
                        + '<div style="font-size:13px;font-weight:700;width:44px;text-align:right">' + w.poids + '%</div>'
                        + '</div>';
                }).join('');
            }

            // Dessine la frontière efficiente si les données existent
            // d.efficientFrontier = [{ volatility: 12.5, expectedReturn: 8.3 }, ...]
            if (d.efficientFrontier && d.efficientFrontier.length > 0)
                charts.initEfficientFrontier('opt-frontier', d);

            show('opt-result');
        })
        .catch(function (e) { hide('opt-spinner'); alert('Erreur : ' + e.message); });
    }

    //  COMPARAISON

    function lancerComparaison() {
        // querySelectorAll('.cmp-check:checked') -> tous les checkboxes cochés par l'user
        // Array.from() -> convertit la NodeList en vrai tableau JavaScript (pour pouvoir .map)
        // .map() -> extrait la valeur (id du portefeuille) de chaque checkbox cochée
        var ids = Array.from(document.querySelectorAll('.cmp-check:checked'))
            .map(function (c) { return parseInt(c.value, 10); });

        // Validation : minimum 2 portefeuilles nécessaires pour une comparaison
        if (ids.length < 2) { alert('Sélectionnez au moins 2 portefeuilles.'); return; }

        // || {} -> si l'élément n'existe pas dans le DOM -> objet vide pour éviter l'erreur TypeError
        // || '' -> si .value est undefined -> chaîne vide (paramètre optionnel)
        var debut = (document.getElementById('cmp-debut') || {}).value || '';
        var fin   = (document.getElementById('cmp-fin')   || {}).value || '';

        flex('cmp-spinner');
        hide('cmp-result');

        // postBody car on envoie un tableau JSON [1, 2, 3] dans le body
        // Les dates sont passées en query string car [FromBody] prend déjà le tableau d'ids
        // -> on ne peut pas avoir deux [FromBody] -> workaround : dates dans l'URL
        postBody('/Analytics/Compare?dateDebut=' + debut + '&dateFin=' + fin, ids)
        .then(function (d) {
            hide('cmp-spinner');
            if (d.error) { alert(d.error); return; }

            // Construit les lignes du tableau de comparaison dynamiquement
            var tbody = document.getElementById('cmp-tbody');
            if (tbody && d.portfolios) {
                // Une ligne <tr> par portefeuille avec ses métriques
                tbody.innerHTML = d.portfolios.map(function (p) {
                    var rColor = p.annualizedReturn >= 0 ? 'var(--green)' : 'var(--red)';
                    var tColor = p.totalReturn      >= 0 ? 'var(--green)' : 'var(--red)';
                    return '<tr>'
                        + '<td><strong>' + p.name + '</strong></td>'
                        + '<td style="color:' + rColor + ';font-weight:600">' + sign(p.annualizedReturn) + p.annualizedReturn + '%</td>'
                        + '<td>' + p.volatility + '%</td>'
                        + '<td>' + p.sharpe + '</td>'
                        + '<td style="color:var(--red)">' + p.maxDrawdown + '%</td>'
                        + '<td style="color:' + tColor + ';font-weight:600">' + sign(p.totalReturn) + p.totalReturn + '%</td>'
                        + '</tr>';
                }).join('');
            }

            show('cmp-result');
        })
        .catch(function (e) { hide('cmp-spinner'); alert('Erreur : ' + e.message); });
    }

    //  Init

    // DOMContentLoaded -> attend que tout le HTML soit parsé et le DOM construit
    // avant d'exécuter le code d'initialisation
    // Sans cet événement -> document.getElementById() pourrait retourner null
    // car les éléments ciblés n'existent pas encore au moment où le script s'exécute
    document.addEventListener('DOMContentLoaded', function () {
        initTabs();

        // Helper local -> lie un bouton à sa fonction de lancement
        // Vérifie que le bouton existe dans le DOM avant d'ajouter l'écouteur
        // -> évite une erreur si un bouton est absent de la page
        function bind(id, fn) {
            var el = document.getElementById(id);
            if (el) el.addEventListener('click', fn);
        }

        // Association bouton -> fonction pour chaque module analytics
        bind('btn-mc',  lancerMonteCarlo);
        bind('btn-bt',  lancerBacktest);
        bind('btn-opt', lancerOptimisation);
        bind('btn-cmp', lancerComparaison);
    });

// Fermeture et exécution immédiate de l'IIFE
}());
