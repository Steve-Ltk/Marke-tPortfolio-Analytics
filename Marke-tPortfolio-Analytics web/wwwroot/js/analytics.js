/**
 * analytics.js — Catalis
 * Orchestration des 4 modules analytics :
 *   1. Onglets (data-tab-trigger / data-tab-panel)
 *   2. Appels AJAX POST vers AnalyticsController
 *   3. Spinners chargement
 *   4. Injection résultats dans les partials déjà présents dans le DOM
 *   5. Appels charts.js pour les graphiques
 */

(function () {
    'use strict';

    // portfolioId injecté dans l'attribut data-portfolio-id de #analytics-root
    var root = document.getElementById('analytics-root');
    var portfolioId = root ? parseInt(root.dataset.portfolioId || '0', 10) : 0;

    // ── Helpers UI ────────────────────────────────────────────────────────

    function show(id) {
        var el = document.getElementById(id);
        if (el) el.style.display = 'block';
    }
    function hide(id) {
        var el = document.getElementById(id);
        if (el) el.style.display = 'none';
    }
    function flex(id) {
        var el = document.getElementById(id);
        if (el) el.style.display = 'flex';
    }
    function text(id, val) {
        var el = document.getElementById(id);
        if (el) el.textContent = val;
    }
    function html(id, val) {
        var el = document.getElementById(id);
        if (el) el.innerHTML = val;
    }
    function color(id, c) {
        var el = document.getElementById(id);
        if (el) el.style.color = c;
    }
    function sign(v) { return v >= 0 ? '+' : ''; }

    function insightHtml(niveau, couleur, message) {
        return '<strong style="color:' + couleur + '">' + niveau + '</strong> — ' + message;
    }

    // Récupère le token CSRF (si présent)
    function csrfToken() {
        var t = document.querySelector('input[name="__RequestVerificationToken"]');
        return t ? t.value : '';
    }

    // POST form-urlencoded
    function postForm(url, params) {
        var body = Object.keys(params)
            .map(function (k) {
                return encodeURIComponent(k) + '=' + encodeURIComponent(params[k]);
            })
            .join('&');

        return fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'RequestVerificationToken': csrfToken()
            },
            body: body
        }).then(function (r) { return r.json(); });
    }

    // POST JSON body (pour Compare)
    function postBody(url, data) {
        return fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        }).then(function (r) { return r.json(); });
    }

    // ── Onglets ───────────────────────────────────────────────────────────

    function initTabs() {
        var triggers = document.querySelectorAll('[data-tab-trigger]');
        var panels = document.querySelectorAll('[data-tab-panel]');

        triggers.forEach(function (btn) {
            btn.addEventListener('click', function () {
                var target = this.dataset.tabTrigger;
                triggers.forEach(function (b) { b.classList.remove('active'); });
                panels.forEach(function (p) { p.style.display = 'none'; });
                this.classList.add('active');
                var panel = document.querySelector('[data-tab-panel="' + target + '"]');
                if (panel) panel.style.display = 'block';
            });
        });
    }

    // ── MONTE CARLO ───────────────────────────────────────────────────────

    function lancerMonteCarlo() {
        var nb = document.getElementById('mc-nb').value;
        var horizon = document.getElementById('mc-horizon').value;

        flex('mc-spinner');
        hide('mc-result');

        postForm('/Analytics/MonteCarlo', {
            portfolioId: portfolioId,
            numSimulations: nb,
            horizonDays: horizon
        })
            .then(function (d) {
                hide('mc-spinner');
                if (d.error) { alert(d.error); return; }

                var probColor = d.probGain >= 80 ? 'var(--green)'
                    : d.probGain >= 65 ? 'var(--amber)' : 'var(--red)';

                text('mc-prob', d.probGain + '%');
                color('mc-prob', probColor);
                text('mc-var95', '€' + d.var95);
                text('mc-median', '€' + d.median);
                text('mc-p5', '€' + d.p5);
                text('mc-p95', '€' + d.p95);
                text('mc-horizon-label', d.horizonDays + ' jours de bourse');
                text('mc-sims-label', d.nbSimulations + ' simulations');
                html('mc-insight', insightHtml(d.insightNiveau, d.insightCouleur, d.insightMessage));

                if (d.fanMed && d.fanMed.length > 0)
                    charts.initFanChart('mc-chart', d);

                charts.initProbDonut('mc-donut', d.probGain);
                show('mc-result');
            })
            .catch(function (e) { hide('mc-spinner'); alert('Erreur : ' + e.message); });
    }

    // ── BACKTEST ──────────────────────────────────────────────────────────

    function lancerBacktest() {
        var debut = document.getElementById('bt-debut').value;
        var fin = document.getElementById('bt-fin').value;
        var bench = document.getElementById('bt-bench').value;

        flex('bt-spinner');
        hide('bt-result');

        postForm('/Analytics/Backtest', {
            portfolioId: portfolioId,
            dateDebut: debut,
            dateFin: fin,
            benchmark: bench
        })
            .then(function (d) {
                hide('bt-spinner');
                if (d.error) { alert(d.error); return; }

                text('bt-port-ret', sign(d.portfolioReturn) + d.portfolioReturn + '%');
                text('bt-bench-ret', sign(d.benchmarkReturn) + d.benchmarkReturn + '%');
                text('bt-alpha', sign(d.alpha) + d.alpha + '%');
                text('bt-beta', d.beta);
                text('bt-sharpe', d.sharpe);
                text('bt-sortino', d.sortino || '—');
                text('bt-calmar', d.calmar || '—');
                text('bt-drawdown', d.maxDrawdown + '%');

                color('bt-port-ret', d.portfolioReturn >= 0 ? 'var(--green)' : 'var(--red)');
                color('bt-bench-ret', d.benchmarkReturn >= 0 ? 'var(--green)' : 'var(--red)');
                color('bt-alpha', d.alpha >= 0 ? 'var(--green)' : 'var(--red)');

                charts.initBacktestChart('bt-chart', d);

                if (d.monthlyReturns && d.monthlyReturns.length > 0)
                    charts.initMonthlyHeatmap('bt-heatmap', d.monthlyReturns);

                show('bt-result');
            })
            .catch(function (e) { hide('bt-spinner'); alert('Erreur : ' + e.message); });
    }

    // ── OPTIMISATION ──────────────────────────────────────────────────────

    function lancerOptimisation() {
        var target = document.getElementById('opt-target').value;
        var debut = document.getElementById('opt-debut').value;
        var fin = document.getElementById('opt-fin').value;

        flex('opt-spinner');
        hide('opt-result');

        postForm('/Analytics/Optimize', {
            portfolioId: portfolioId,
            target: target,
            dateDebut: debut,
            dateFin: fin
        })
            .then(function (d) {
                hide('opt-spinner');
                if (d.error) { alert(d.error); return; }

                text('opt-ret', '+' + d.expectedReturn + '%');
                text('opt-vol', d.expectedVolatility + '%');
                text('opt-sharpe', d.sharpeRatio);

                // Delta vs actuel
                var delta = (d.sharpeRatio - (d.currentSharpe || 0)).toFixed(3);
                text('opt-delta', sign(parseFloat(delta)) + delta);
                color('opt-delta', parseFloat(delta) >= 0 ? 'var(--green)' : 'var(--red)');

                // Barres de poids
                var container = document.getElementById('opt-weights');
                if (container && d.weights) {
                    container.innerHTML = d.weights.map(function (w) {
                        return '<div style="display:flex;align-items:center;gap:10px;margin-bottom:8px">'
                            + '<div style="font-size:13px;font-weight:700;width:60px;color:var(--t1)">' + w.ticker + '</div>'
                            + '<div style="flex:1;height:8px;background:var(--dark-4);border-radius:999px;overflow:hidden">'
                            + '<div style="width:' + w.poids + '%;height:100%;background:var(--green);border-radius:999px"></div>'
                            + '</div>'
                            + '<div style="font-size:13px;font-weight:700;width:44px;text-align:right">' + w.poids + '%</div>'
                            + '</div>';
                    }).join('');
                }

                // Frontière efficiente
                if (d.efficientFrontier && d.efficientFrontier.length > 0)
                    charts.initEfficientFrontier('opt-frontier', d);

                show('opt-result');
            })
            .catch(function (e) { hide('opt-spinner'); alert('Erreur : ' + e.message); });
    }

    // ── COMPARAISON ───────────────────────────────────────────────────────

    function lancerComparaison() {
        var ids = Array.from(document.querySelectorAll('.cmp-check:checked'))
            .map(function (c) { return parseInt(c.value, 10); });

        if (ids.length < 2) { alert('Sélectionnez au moins 2 portefeuilles.'); return; }

        var debut = (document.getElementById('cmp-debut') || {}).value || '';
        var fin = (document.getElementById('cmp-fin') || {}).value || '';

        flex('cmp-spinner');
        hide('cmp-result');

        postBody('/Analytics/Compare?dateDebut=' + debut + '&dateFin=' + fin, ids)
            .then(function (d) {
                hide('cmp-spinner');
                if (d.error) { alert(d.error); return; }

                var tbody = document.getElementById('cmp-tbody');
                if (tbody && d.portfolios) {
                    tbody.innerHTML = d.portfolios.map(function (p) {
                        var rColor = p.annualizedReturn >= 0 ? 'var(--green)' : 'var(--red)';
                        var tColor = p.totalReturn >= 0 ? 'var(--green)' : 'var(--red)';
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

    // ── Init ──────────────────────────────────────────────────────────────

    document.addEventListener('DOMContentLoaded', function () {
        initTabs();

        function bind(id, fn) {
            var el = document.getElementById(id);
            if (el) el.addEventListener('click', fn);
        }

        bind('btn-mc', lancerMonteCarlo);
        bind('btn-bt', lancerBacktest);
        bind('btn-opt', lancerOptimisation);
        bind('btn-cmp', lancerComparaison);
    });

}());
