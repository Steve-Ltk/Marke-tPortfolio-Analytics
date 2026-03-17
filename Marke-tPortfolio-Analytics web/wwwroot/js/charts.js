/**
 * charts.js — Catalis
 * Wrappers Chart.js pour les 4 modules Analytics.
 * Fonctions appelées par analytics.js après réception des données JSON.
 *
 * API publique :
 *   charts.initFanChart(canvasId, data)
 *   charts.initBacktestChart(canvasId, data)
 *   charts.initEfficientFrontier(canvasId, data)
 *   charts.initMonthlyHeatmap(canvasId, monthlyReturns)
 *   charts.initProbDonut(canvasId, probGain)
 *   charts.destroy(id)
 */

const charts = (function () {

    const C = {
        green: '#00d084',
        red: '#f43f5e',
        blue: '#3b82f6',
        amber: '#f59e0b',
        grid: 'rgba(255,255,255,.04)',
        text: '#475569',
        legend: '#94a3b8'
    };

    const _instances = {};

    function destroy(id) {
        if (_instances[id]) {
            _instances[id].destroy();
            delete _instances[id];
        }
    }

    const baseScaleX = {
        ticks: { color: C.text, maxTicksLimit: 8, font: { size: 11 } },
        grid: { color: C.grid }
    };
    const baseScaleY = {
        ticks: { color: C.text, font: { size: 11 } },
        grid: { color: C.grid }
    };
    const baseLegend = {
        labels: { color: C.legend, font: { size: 11 }, usePointStyle: true }
    };

    // ── Fan Chart Monte Carlo (P5 / Médiane / P95) ──────────────────────
    function initFanChart(canvasId, data) {
        destroy(canvasId);
        var ctx = document.getElementById(canvasId);
        if (!ctx) return;

        var labels = data.labels || data.fanMed.map(function (_, i) { return 'J+' + i; });

        _instances[canvasId] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [
                    {
                        label: 'P95 (optimiste)',
                        data: data.fanP95,
                        borderColor: 'rgba(0,208,132,.3)',
                        borderDash: [5, 4],
                        borderWidth: 1.5,
                        fill: false,
                        pointRadius: 0,
                        tension: 0.4
                    },
                    {
                        label: 'Médiane',
                        data: data.fanMed,
                        borderColor: C.green,
                        borderWidth: 2.5,
                        fill: false,
                        pointRadius: 0,
                        tension: 0.4
                    },
                    {
                        label: 'P5 (pessimiste)',
                        data: data.fanP5,
                        borderColor: 'rgba(244,63,94,.3)',
                        borderDash: [5, 4],
                        borderWidth: 1.5,
                        fill: false,
                        pointRadius: 0,
                        tension: 0.4
                    }
                ]
            },
            options: {
                responsive: true,
                plugins: { legend: baseLegend },
                scales: {
                    x: baseScaleX,
                    y: {
                        ticks: {
                            color: C.text,
                            font: { size: 11 },
                            callback: function (v) { return '€' + Math.round(v).toLocaleString('fr-FR'); }
                        },
                        grid: { color: C.grid }
                    }
                }
            }
        });
    }

    // ── Backtest Chart — portfolio vs benchmark ─────────────────────────
    function initBacktestChart(canvasId, data) {
        destroy(canvasId);
        var ctx = document.getElementById(canvasId);
        if (!ctx) return;

        var datasets = [
            {
                label: 'Portefeuille',
                data: data.portCurve,
                borderColor: C.green,
                borderWidth: 2,
                fill: false,
                pointRadius: 0,
                tension: 0.3
            }
        ];

        if (data.benchCurve && data.benchCurve.length > 0) {
            datasets.push({
                label: 'Benchmark',
                data: data.benchCurve,
                borderColor: C.blue,
                borderWidth: 1.5,
                borderDash: [4, 3],
                fill: false,
                pointRadius: 0,
                tension: 0.3
            });
        }

        _instances[canvasId] = new Chart(ctx, {
            type: 'line',
            data: { labels: data.labels || [], datasets: datasets },
            options: {
                responsive: true,
                plugins: { legend: baseLegend },
                scales: {
                    x: baseScaleX,
                    y: {
                        ticks: {
                            color: C.text,
                            font: { size: 11 },
                            callback: function (v) { return v.toFixed(0) + '%'; }
                        },
                        grid: { color: C.grid }
                    }
                }
            }
        });
    }

    // ── Efficient Frontier — scatter Markowitz ──────────────────────────
    function initEfficientFrontier(canvasId, data) {
        destroy(canvasId);
        var ctx = document.getElementById(canvasId);
        if (!ctx) return;

        var frontier = (data.efficientFrontier || []).map(function (p) {
            return { x: p.volatility * 100, y: p.expectedReturn * 100 };
        });

        _instances[canvasId] = new Chart(ctx, {
            type: 'scatter',
            data: {
                datasets: [
                    {
                        label: 'Frontière efficiente',
                        data: frontier,
                        backgroundColor: 'rgba(0,208,132,.5)',
                        pointRadius: 3
                    },
                    {
                        label: 'Optimal ★',
                        data: [{ x: data.expectedVolatility, y: data.expectedReturn }],
                        backgroundColor: C.green,
                        pointRadius: 10,
                        pointStyle: 'star'
                    },
                    {
                        label: 'Actuel',
                        data: [{ x: data.currentVolatility, y: data.currentReturn }],
                        backgroundColor: C.blue,
                        pointRadius: 7,
                        pointStyle: 'circle'
                    }
                ]
            },
            options: {
                responsive: true,
                plugins: { legend: baseLegend },
                scales: {
                    x: {
                        ticks: { color: C.text, font: { size: 11 }, callback: function (v) { return v + '%'; } },
                        grid: { color: C.grid },
                        title: { display: true, text: 'Volatilité (%)', color: C.text, font: { size: 11 } }
                    },
                    y: {
                        ticks: { color: C.text, font: { size: 11 }, callback: function (v) { return v + '%'; } },
                        grid: { color: C.grid },
                        title: { display: true, text: 'Rendement (%)', color: C.text, font: { size: 11 } }
                    }
                }
            }
        });
    }

    // ── Monthly Returns Heatmap (canvas 2D) ─────────────────────────────
    function initMonthlyHeatmap(canvasId, monthlyReturns) {
        var canvas = document.getElementById(canvasId);
        if (!canvas || !monthlyReturns || monthlyReturns.length === 0) return;

        var byYear = {};
        monthlyReturns.forEach(function (m) {
            if (!byYear[m.year]) byYear[m.year] = {};
            byYear[m.year][m.month] = m.returnPct;
        });

        var years = Object.keys(byYear).sort();
        var months = ['Jan', 'Fév', 'Mar', 'Avr', 'Mai', 'Jun', 'Jul', 'Aoû', 'Sep', 'Oct', 'Nov', 'Déc'];
        var cellW = 48, cellH = 28, padL = 44, padT = 28;

        canvas.width = padL + months.length * cellW + 8;
        canvas.height = padT + years.length * cellH + 8;

        var c = canvas.getContext('2d');
        c.fillStyle = '#111827';
        c.fillRect(0, 0, canvas.width, canvas.height);

        c.font = '10px DM Sans, sans-serif';
        c.fillStyle = '#475569';
        months.forEach(function (m, i) {
            c.fillText(m, padL + i * cellW + 14, 18);
        });

        years.forEach(function (y, yi) {
            c.fillStyle = '#475569';
            c.fillText(y, 4, padT + yi * cellH + 18);

            months.forEach(function (_, mi) {
                var v = byYear[y][mi + 1];
                if (v === undefined) return;

                var intensity = Math.min(Math.abs(v) / 10, 1);
                c.fillStyle = v >= 0
                    ? 'rgba(0,208,132,' + (0.1 + intensity * 0.6) + ')'
                    : 'rgba(244,63,94,' + (0.1 + intensity * 0.6) + ')';

                c.fillRect(padL + mi * cellW + 2, padT + yi * cellH + 2, cellW - 4, cellH - 4);

                c.fillStyle = intensity > 0.5 ? '#fff' : '#94a3b8';
                c.font = '9px DM Sans, sans-serif';
                c.fillText(
                    (v >= 0 ? '+' : '') + v.toFixed(1) + '%',
                    padL + mi * cellW + 5,
                    padT + yi * cellH + 17
                );
            });
        });
    }

    // ── Donut probabilité de gain ───────────────────────────────────────
    function initProbDonut(canvasId, probGain) {
        destroy(canvasId);
        var ctx = document.getElementById(canvasId);
        if (!ctx) return;

        var loss = Math.max(0, 100 - probGain);
        var color = probGain >= 80 ? C.green : probGain >= 65 ? C.amber : C.red;

        _instances[canvasId] = new Chart(ctx, {
            type: 'doughnut',
            data: {
                datasets: [{
                    data: [probGain, loss],
                    backgroundColor: [color, 'rgba(255,255,255,.06)'],
                    borderWidth: 0
                }]
            },
            options: {
                responsive: false,
                cutout: '72%',
                plugins: {
                    legend: { display: false },
                    tooltip: { enabled: false }
                }
            }
        });
    }

    return {
        initFanChart,
        initBacktestChart,
        initEfficientFrontier,
        initMonthlyHeatmap,
        initProbDonut,
        destroy
    };

}());
