/**
 * charts.js — Catalis
 *
 * Wrappers Chart.js pour les 4 modules Analytics.
 * Fonctions appelées par analytics.js après réception des données JSON du serveur.
 *
 * API publique (accessible via l'objet "charts" global) :
 *   charts.initFanChart(canvasId, data)        -> Fan chart Monte Carlo (P5 / Médiane / P95)
 *   charts.initBacktestChart(canvasId, data)   -> Courbe portefeuille vs benchmark
 *   charts.initEfficientFrontier(canvasId, data) -> Scatter frontière efficiente Markowitz
 *   charts.initMonthlyHeatmap(canvasId, monthlyReturns) -> Heatmap rendements mensuels
 *   charts.initProbDonut(canvasId, probGain)   -> Donut probabilité de gain
 *   charts.destroy(id)                         -> Détruit un graphique existant
 */

// "const charts = (function() { ... })()" -> IIFE qui retourne un objet public
// Même principe que analytics.js : tout ce qui est ici reste privé
// Sauf ce que le "return { ... }" expose à la fin
// -> analytics.js peut appeler charts.initFanChart() mais pas les fonctions internes
const charts = (function () {

    // Palette de couleurs centralisée -> utilisée dans tous les graphiques
    // Définit une fois ici -> facile à changer globalement
    // Correspond aux CSS variables du projet (--green, --red, etc.)
    const C = {
        green:  '#00d084',                  // vert catalis -> gains, positif
        red:    '#f43f5e',                  // rouge -> pertes, négatif
        blue:   '#3b82f6',                  // bleu -> benchmark, neutre
        amber:  '#f59e0b',                  // orange -> avertissement, risque modéré
        grid:   'rgba(255,255,255,.04)',    // lignes de grille très discrètes sur fond sombre
        text:   '#475569',                  // couleur des labels des axes
        legend: '#94a3b8'                   // couleur du texte de légende
    };

    // Registre des instances Chart.js actives
    // Clé = canvasId (string), valeur = instance Chart.js
    // Pourquoi : Chart.js ne peut pas dessiner deux fois sur le même canvas
    // -> si on relance un calcul, il faut d'abord détruire l'ancien graphique
    const _instances = {};

    // Détruit un graphique existant sur un canvas donné
    // Appelé avant chaque nouvelle création pour éviter les doublons
    function destroy(id) {
        if (_instances[id]) {
            _instances[id].destroy(); // méthode Chart.js -> libère le canvas et la mémoire
            delete _instances[id];   // retire du registre
        }
    }

    // Configurations de base réutilisées
    // Définies une fois -> passées en référence dans chaque graphique
    // Évite de répéter les mêmes options dans chaque initXxx()

    // Axe X standard : labels gris, max 8 graduations, police 11px
    const baseScaleX = {
        ticks: { color: C.text, maxTicksLimit: 8, font: { size: 11 } },
        grid:  { color: C.grid }
    };

    // Axe Y standard : labels gris, police 11px
    // Pas de maxTicksLimit -> Chart.js choisit automatiquement
    const baseScaleY = {
        ticks: { color: C.text, font: { size: 11 } },
        grid:  { color: C.grid }
    };

    // Légende standard : texte gris clair, police 11px, point carré (usePointStyle)
    // usePointStyle -> affiche le style de ligne/point plutôt qu'un rectangle plein
    const baseLegend = {
        labels: { color: C.legend, font: { size: 11 }, usePointStyle: true }
    };

    // Fan Chart Monte Carlo (P5 / Médiane / P95) 
    // Dessine 3 courbes : scénario optimiste (P95), médiane, pessimiste (P5)
    // data vient du JSON de AnalyticsController.MonteCarlo()
    function initFanChart(canvasId, data) {
        // Détruit l'ancien graphique si l'user relance une simulation
        destroy(canvasId);

        // Récupère l'élément <canvas id="mc-chart"> dans le DOM
        var ctx = document.getElementById(canvasId);
        if (!ctx) return; // si l'élément n'existe pas -> arrêt silencieux

        // Labels de l'axe X : dates réelles si fournies, sinon "J+0", "J+1", ...
        // data.labels -> fourni par le backend si disponible
        // data.fanMed.map((_, i) -> ...) -> génère des labels de remplacement
        // "_" = convention JS pour "je ne me sers pas de cette valeur" (ici la valeur, pas l'index)
        var labels = data.labels || data.fanMed.map(function (_, i) { return 'J+' + i; });

        // Création du graphique Chart.js et stockage dans le registre
        _instances[canvasId] = new Chart(ctx, {
            type: 'line', // graphique en courbes
            data: {
                labels: labels,
                datasets: [
                    {
                        label: 'P95 (optimiste)',
                        data: data.fanP95,                       // tableau des valeurs du 95e percentile
                        borderColor: 'rgba(0,208,132,.3)',       // vert très transparent -> discret
                        borderDash: [5, 4],                      // [5px trait, 4px espace] -> ligne pointillée
                        borderWidth: 1.5,
                        fill: false,                             // pas de remplissage sous la courbe
                        pointRadius: 0,                          // pas de points -> courbe fluide
                        tension: 0.4                             // courbure de la ligne (0=droites, 1=très courbé)
                    },
                    {
                        label: 'Médiane',
                        data: data.fanMed,                       // tableau des valeurs médianes
                        borderColor: C.green,                    // vert plein -> courbe principale visible
                        borderWidth: 2.5,                        // plus épaisse que les percentiles
                        fill: false,
                        pointRadius: 0,
                        tension: 0.4
                    },
                    {
                        label: 'P5 (pessimiste)',
                        data: data.fanP5,                        // tableau des valeurs du 5e percentile
                        borderColor: 'rgba(244,63,94,.3)',       // rouge très transparent -> discret
                        borderDash: [5, 4],                      // pointillé comme P95
                        borderWidth: 1.5,
                        fill: false,
                        pointRadius: 0,
                        tension: 0.4
                    }
                ]
            },
            options: {
                responsive: true, // s'adapte à la taille du conteneur CSS
                plugins: { legend: baseLegend },
                scales: {
                    x: baseScaleX,
                    y: {
                        ticks: {
                            color: C.text,
                            font: { size: 11 },
                            // callback -> formate chaque graduation de l'axe Y
                            // "€ 12 450" au lieu de "12450"
                            callback: function (v) { return '€' + Math.round(v).toLocaleString('fr-FR'); }
                        },
                        grid: { color: C.grid }
                    }
                }
            }
        });
    }

    //  Backtest Chart — portefeuille vs benchmark
    // Dessine la performance cumulée du portefeuille (et du benchmark si disponible)
    // Les valeurs sont en % de gain/perte depuis le début de la période
    // data vient du JSON de AnalyticsController.Backtest()
    function initBacktestChart(canvasId, data) {
        destroy(canvasId);
        var ctx = document.getElementById(canvasId);
        if (!ctx) return;

        // Construction des datasets -> commence avec le portefeuille seul
        var datasets = [
            {
                label: 'Portefeuille',
                data: data.portCurve,        // ex: [0, 1.2, 3.5, -0.8, ...] (rendements cumulés en %)
                borderColor: C.green,
                borderWidth: 2,
                fill: false,
                pointRadius: 0,
                tension: 0.3
            }
        ];

        // Ajoute le benchmark seulement si les données existent
        // Le benchmark peut être absent si le ticker FMP n'a pas d'historique sur la période
        if (data.benchCurve && data.benchCurve.length > 0) {
            datasets.push({
                label: 'Benchmark',
                data: data.benchCurve,
                borderColor: C.blue,
                borderWidth: 1.5,
                borderDash: [4, 3], // pointillé -> visuellement secondaire vs le portefeuille
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
                            // .toFixed(0) -> arrondit à l'entier -> "5%" au lieu de "5.0000%"
                            callback: function (v) { return v.toFixed(0) + '%'; }
                        },
                        grid: { color: C.grid }
                    }
                }
            }
        });
    }

    // Efficient Frontier — scatter Markowitz 
    // Nuage de points : chaque point = un portefeuille possible (volatilité, rendement)
    // La frontière efficiente = les portefeuilles optimaux (meilleur rendement pour chaque risque)
    // data vient du JSON de AnalyticsController.Optimize()
    function initEfficientFrontier(canvasId, data) {
        destroy(canvasId);
        var ctx = document.getElementById(canvasId);
        if (!ctx) return;

        // Transforme les points de la frontière en format {x, y} attendu par Chart.js scatter
        // * 100 -> convertit de décimal (0.12) en pourcentage (12%)
        // Attention : le backend retourne déjà des % dans certains cas
        // -> vérifier si ce *100 est cohérent avec les données de PortfolioOptimizationService
        var frontier = (data.efficientFrontier || []).map(function (p) {
            return { x: p.volatility * 100, y: p.expectedReturn * 100 };
        });

        _instances[canvasId] = new Chart(ctx, {
            type: 'scatter', // nuage de points -> chaque dataset = un ensemble de (x,y)
            data: {
                datasets: [
                    {
                        // Ensemble des portefeuilles sur la frontière efficiente
                        label: 'Frontière efficiente',
                        data: frontier,
                        backgroundColor: 'rgba(0,208,132,.5)', // vert semi-transparent
                        pointRadius: 3                          // petits points pour la frontière
                    },
                    {
                        // Point unique : le portefeuille optimal calculé
                        label: 'Optimal ★',
                        data: [{ x: data.expectedVolatility, y: data.expectedReturn }],
                        backgroundColor: C.green,
                        pointRadius: 10,           // grand point pour le mettre en valeur
                        pointStyle: 'star'         // symbole étoile Chart.js
                    },
                    {
                        // Point unique : le portefeuille actuel de l'user (pour comparaison)
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
                        grid:  { color: C.grid },
                        // title -> affiche un label sous l'axe X
                        title: { display: true, text: 'Volatilité (%)', color: C.text, font: { size: 11 } }
                    },
                    y: {
                        ticks: { color: C.text, font: { size: 11 }, callback: function (v) { return v + '%'; } },
                        grid:  { color: C.grid },
                        title: { display: true, text: 'Rendement (%)', color: C.text, font: { size: 11 } }
                    }
                }
            }
        });
    }

    // Monthly Returns Heatmap (canvas 2D natif) 
    // Dessin manuel avec l'API Canvas 2D (pas Chart.js)
    // Chart.js n'a pas de heatmap native -> on dessine cellule par cellule
    // Chaque cellule = un mois, colorée en vert/rouge selon le rendement
    // monthlyReturns = [{ year: 2023, month: 1, returnPct: 2.3 }, ...]
    function initMonthlyHeatmap(canvasId, monthlyReturns) {
        var canvas = document.getElementById(canvasId);
        if (!canvas || !monthlyReturns || monthlyReturns.length === 0) return;

        // Réorganise le tableau plat en objet imbriqué { année: { mois: valeur } }
        // Ex: byYear[2023][3] = 1.5 -> mars 2023 = +1.5%
        var byYear = {};
        monthlyReturns.forEach(function (m) {
            if (!byYear[m.year]) byYear[m.year] = {}; // crée l'entrée de l'année si absente
            byYear[m.year][m.month] = m.returnPct;    // stocke le rendement
        });

        // Récupère les années dans l'ordre chronologique
        // Object.keys() -> ["2022", "2023", "2024"]
        // .sort() -> tri lexicographique (correct pour les années numériques de même longueur)
        var years  = Object.keys(byYear).sort();

        // Labels des 12 mois en français
        var months = ['Jan', 'Fév', 'Mar', 'Avr', 'Mai', 'Jun', 'Jul', 'Aoû', 'Sep', 'Oct', 'Nov', 'Déc'];

        // Dimensions des cellules et marges intérieures
        var cellW = 48; // largeur d'une cellule (1 mois) en pixels
        var cellH = 28; // hauteur d'une cellule (1 année) en pixels
        var padL  = 44; // marge gauche pour les labels d'années
        var padT  = 28; // marge haute pour les labels de mois

        // Calcule la taille totale du canvas selon le nombre de mois/années
        canvas.width  = padL + months.length * cellW + 8; // 12 colonnes + marge
        canvas.height = padT + years.length  * cellH + 8; // N lignes + marge

        // Récupère le contexte 2D -> objet qui expose toutes les méthodes de dessin
        var c = canvas.getContext('2d');

        // Fond sombre pour tout le canvas
        c.fillStyle = '#111827';
        c.fillRect(0, 0, canvas.width, canvas.height);

        // ── Dessine les labels des mois (ligne d'en-tête) ──
        c.font      = '10px DM Sans, sans-serif';
        c.fillStyle = '#475569';
        months.forEach(function (m, i) {
            // padL + i*cellW + 14 -> centre approximatif de chaque colonne
            c.fillText(m, padL + i * cellW + 14, 18);
        });

        // ── Dessine chaque ligne (année) ──
        years.forEach(function (y, yi) {
            // Label de l'année à gauche
            c.fillStyle = '#475569';
            c.fillText(y, 4, padT + yi * cellH + 18);

            // ── Dessine chaque cellule (mois) de cette année ──
            months.forEach(function (_, mi) {
                // mi+1 -> les mois dans monthlyReturns vont de 1 à 12
                var v = byYear[y][mi + 1];
                if (v === undefined) return; // mois absent -> cellule vide

                // Intensité de couleur proportionnelle à la valeur absolue du rendement
                // Math.min(..., 1) -> plafond à 1 pour ne pas dépasser l'opacité max
                // /10 -> un rendement de 10% = intensité max
                var intensity = Math.min(Math.abs(v) / 10, 1);

                // Couleur de la cellule : vert si positif, rouge si négatif
                // Opacité entre 0.1 (presque transparent) et 0.7 (bien visible)
                c.fillStyle = v >= 0
                    ? 'rgba(0,208,132,' + (0.1 + intensity * 0.6) + ')'
                    : 'rgba(244,63,94,' + (0.1 + intensity * 0.6) + ')';

                // Dessine le rectangle de la cellule (2px de marge intérieure)
                c.fillRect(
                    padL + mi * cellW + 2,   // x gauche
                    padT + yi * cellH + 2,   // y haut
                    cellW - 4,               // largeur (-4 pour les 2 marges de chaque côté)
                    cellH - 4                // hauteur
                );

                // Texte de la valeur : blanc si cellule foncée (intensity > 0.5), gris sinon
                c.fillStyle = intensity > 0.5 ? '#fff' : '#94a3b8';
                c.font      = '9px DM Sans, sans-serif';
                c.fillText(
                    (v >= 0 ? '+' : '') + v.toFixed(1) + '%', // ex: "+2.3%" ou "-1.5%"
                    padL + mi * cellW + 5,   // x texte (légèrement indenté dans la cellule)
                    padT + yi * cellH + 17   // y texte (centré verticalement dans la cellule)
                );
            });
        });
        // Pas de destroy() ici car ce n'est pas une instance Chart.js
        // -> c'est un dessin canvas 2D natif, il n'y a rien à détruire
        // Pour "réinitialiser" -> il suffit de redessiner par-dessus (fillRect fond)
    }

    // Donut probabilité de gain 
    // Arc circulaire coloré proportionnel à la probabilité de gain Monte Carlo
    // Ex: probGain = 73 -> 73% de l'arc en vert, 27% transparent
    function initProbDonut(canvasId, probGain) {
        destroy(canvasId);
        var ctx = document.getElementById(canvasId);
        if (!ctx) return;

        // Calcule la part de perte (complément à 100%)
        // Math.max(0, ...) -> évite une valeur négative si probGain > 100 (ne devrait pas arriver)
        var loss = Math.max(0, 100 - probGain);

        // Couleur de l'arc selon les mêmes seuils que analytics.js
        var color = probGain >= 80 ? C.green : probGain >= 65 ? C.amber : C.red;

        _instances[canvasId] = new Chart(ctx, {
            type: 'doughnut', // anneau circulaire (doughnut = donut)
            data: {
                datasets: [{
                    data: [probGain, loss],                      // [part colorée, part transparente]
                    backgroundColor: [color, 'rgba(255,255,255,.06)'], // couleur, quasi-transparent
                    borderWidth: 0                               // pas de bordure entre les segments
                }]
            },
            options: {
                responsive: false, // taille fixe définie par CSS (pas d'adaptation automatique)
                cutout: '72%',    // épaisseur de l'anneau : 72% = anneau fin (28% de rayon utilisé)
                plugins: {
                    legend:  { display: false }, // pas de légende -> le pourcentage est affiché en texte
                    tooltip: { enabled: false }  // pas de tooltip au survol -> design épuré
                }
            }
        });
    }

    // API publique 
    // Seules ces fonctions sont accessibles depuis l'extérieur via "charts.xxx()"
    // Tout le reste (C, _instances, destroy interne, baseScaleX...) reste privé
    return {
        initFanChart,
        initBacktestChart,
        initEfficientFrontier,
        initMonthlyHeatmap,
        initProbDonut,
        destroy
    };

}()); // IIFE -> s'exécute immédiatement et assigne le résultat à "charts"
