/* 
   MPA — site.js  |  JavaScript global
   Chargé sur TOUTES les pages via le <script> dans _Layout.cshtml
   -> tout ce qui est ici s'exécute sur chaque page du projet
   -> uniquement des fonctionnalités transversales (sidebar, alertes, tooltips...)
   -> les fonctionnalités spécifiques à une page sont dans leurs propres fichiers
      (analytics.js pour Analytics, charts.js pour les graphiques)
  */

'use strict';
// Mode strict activé globalement ici (pas dans une IIFE comme analytics.js)
// Ce fichier n'a pas besoin d'isoler son scope car il expose des fonctions globales
// intentionnellement (ex: fmtEur, fmtPct, startPolling utilisées par d'autres scripts)

// Sidebar mobile
// Sur mobile, la sidebar est cachée par défaut et s'ouvre en superposition
// Ces deux fonctions sont appelées par les boutons du _Layout.cshtml :
// <button onclick="openSidebar()">☰</button>
// <div id="sb-overlay" onclick="closeSidebar()"></div>

// Ouvre la sidebar : ajoute la classe "open" sur la sidebar et l'overlay
// La classe "open" est définie dans mpa.css -> fait glisser la sidebar de gauche
function openSidebar() {
    document.getElementById('mpa-sidebar')?.classList.add('open');
    // ?. = "optional chaining" -> si l'élément n'existe pas -> ne rien faire (pas d'erreur)
    // Sans ?. : document.getElementById('sb-overlay').classList -> TypeError si null
    document.getElementById('sb-overlay')?.classList.add('open');
}

// Ferme la sidebar : retire la classe "open" des deux éléments
function closeSidebar() {
    document.getElementById('mpa-sidebar')?.classList.remove('open');
    document.getElementById('sb-overlay')?.classList.remove('open');
}

// Ferme aussi la sidebar quand l'user clique sur un lien de navigation (mobile)
// Sans ça : l'user navigue vers une nouvelle page avec la sidebar encore ouverte
// .sb-item -> classe des liens dans _Sidebar.cshtml
document.querySelectorAll('.sb-item').forEach(item => {
    item.addEventListener('click', closeSidebar);
});

// Onglets génériques 
// Système d'onglets différent de celui d'analytics.js :
//   - analytics.js : utilise data-tab-trigger / data-tab-panel -> onglets Analytics uniquement
//   - site.js      : utilise class="tab" + data-tab + class="tab-content" -> onglets génériques
//
// Usage dans une vue :
//   <div data-tabs>
//     <button class="tab" data-tab="tab-positions">Positions</button>
//     <button class="tab" data-tab="tab-perfs">Performances</button>
//   </div>
//   <div class="tab-content" id="tab-positions">...</div>
//   <div class="tab-content" id="tab-perfs">...</div>
//
// La distinction "actif/inactif" est gérée par la classe CSS "active" dans mpa.css
function initTabs(containerSelector) {
    // Cherche tous les conteneurs d'onglets
    // containerSelector || '[data-tabs]' -> si pas de sélecteur fourni -> cherche [data-tabs]
    // Permet d'appeler initTabs('#mon-bloc') pour initialiser un seul groupe d'onglets
    const containers = document.querySelectorAll(containerSelector || '[data-tabs]');

    containers.forEach(container => {
        // Cherche les boutons d'onglets à l'intérieur de ce conteneur uniquement
        // -> .querySelectorAll sur container (pas document) -> limité à ce bloc
        const tabs = container.querySelectorAll('.tab');

        tabs.forEach(tab => {
            tab.addEventListener('click', () => {
                // Lit l'id du panneau cible depuis data-tab="tab-positions"
                const targetId = tab.dataset.tab;

                // Désactive TOUS les onglets et panneaux du conteneur
                tabs.forEach(t => t.classList.remove('active'));
                container.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));

                // Active le bouton cliqué
                tab.classList.add('active');

                // Active le panneau correspondant
                // ?. -> si l'id pointe vers un élément inexistant -> pas d'erreur
                document.getElementById(targetId)?.classList.add('active');
            });
        });
    });
}

// Polling temps réel (30s) 
// Permet de rafraîchir des données périodiquement (ex: prix en temps réel sur le Dashboard)
// Utilisé par DashboardController -> la vue appelle startPolling(rafraichirPrix, 30000)

// Référence vers le timer actif -> null si le polling est arrêté
let _pollingTimer = null;
// Timestamp de la dernière mise à jour -> pour calculer "il y a Xs"
let _lastUpdate   = new Date();

// Lance le polling toutes les intervalMs millisecondes (30s par défaut)
// callback = fonction à appeler à chaque tick (ex: fonction qui recharge les prix)
function startPolling(callback, intervalMs = 30000) {
    // Si un polling est déjà actif -> l'annule avant d'en démarrer un nouveau
    // Évite d'avoir deux intervalles qui tournent en parallèle
    if (_pollingTimer) clearInterval(_pollingTimer);

    _pollingTimer = setInterval(() => {
        _lastUpdate = new Date(); // mémorise l'heure du dernier tick
        updateRealtimeIndicator(); // met à jour l'affichage "En direct" / "Il y a Xs"
        if (typeof callback === 'function') callback(); // appelle la fonction métier
    }, intervalMs);
}

// Arrête le polling proprement
// Appelé par exemple quand l'user quitte la page ou change de portefeuille
function stopPolling() {
    if (_pollingTimer) {
        clearInterval(_pollingTimer); // annule le setInterval
        _pollingTimer = null;         // remet le flag à null
    }
}

// Met à jour le petit indicateur "En direct" / "Il y a Xs" visible dans le header
// Lit _lastUpdate et calcule combien de secondes se sont écoulées depuis
function updateRealtimeIndicator() {
    const label = document.querySelector('.rt-label');
    if (!label) return; // si l'indicateur n'est pas présent dans la page -> ne rien faire

    // Calcule l'écart en secondes entre maintenant et le dernier polling
    const secs = Math.round((new Date() - _lastUpdate) / 1000);

    // "En direct" si moins de 5s (vient de se mettre à jour), sinon "Il y a Xs"
    label.textContent = secs < 5 ? 'En direct' : `Il y a ${secs}s`;
}

// Rafraîchit l'indicateur toutes les 5s indépendamment du polling
// -> même si le polling est à 30s, l'indicateur avance : "Il y a 5s", "Il y a 10s"...
// setInterval à ce niveau global -> tourne toujours, même si le polling est arrêté
setInterval(updateRealtimeIndicator, 5000);

// Recherche globale (ticker)
// Barre de recherche dans le header (_Layout.cshtml) -> redirige vers /Assets?search=AAPL
// Deux comportements :
//   1. Frappe -> attend 600ms sans nouvelle frappe -> redirige (debounce)
//   2. Entrée -> redirige immédiatement

const searchInput = document.getElementById('globalSearch');
if (searchInput) { // si la barre de recherche n'existe pas dans la page -> skip
    let _searchTimer; // timer du debounce

    // Événement déclenché à chaque frappe dans la barre de recherche
    searchInput.addEventListener('input', (e) => {
        // Debounce : annule le timer précédent à chaque frappe
        // -> évite une redirection à chaque lettre tapée
        // -> attend que l'user s'arrête de taper pendant 600ms
        clearTimeout(_searchTimer);

        const q = e.target.value.trim(); // valeur saisie, sans espaces au début/fin
        if (q.length < 2) return;        // minimum 2 caractères pour déclencher la recherche

        // Lance le timer : si l'user ne tape rien pendant 600ms -> redirige
        _searchTimer = setTimeout(() => {
            // encodeURIComponent -> encode les caractères spéciaux dans l'URL
            // ex: "Apple Inc" -> "Apple%20Inc"
            window.location.href = `/Assets?search=${encodeURIComponent(q)}`;
        }, 600);
    });

    // Redirection immédiate sur la touche Entrée (sans attendre le debounce)
    searchInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
            const q = e.target.value.trim();
            if (q.length > 0) {
                window.location.href = `/Assets?search=${encodeURIComponent(q)}`;
            }
        }
    });
}

// Messages flash auto-disparaître 
// Les alertes MVC (TempData success/error) sont rendues par _AlertMessages.cshtml
// avec la classe "mpa-alert" -> elles disparaissent automatiquement après 5s
document.querySelectorAll('.mpa-alert').forEach(alert => {
    setTimeout(() => {
        // 1. Fade out : rend l'élément transparent en 400ms via la transition CSS
        alert.style.opacity    = '0';
        alert.style.transition = 'opacity .4s';

        // 2. Suppression du DOM après la fin du fade (400ms plus tard)
        // -> évite un espace vide là où l'alerte était
        setTimeout(() => alert.remove(), 400);

    }, 5000); // attend 5 secondes avant de déclencher la disparition
});

// Tooltips info 
// Affiche une bulle d'info au survol de n'importe quel élément portant data-tooltip
// Usage dans une vue : <span data-tooltip="Le Sharpe mesure le rendement ajusté au risque">ⓘ</span>
// Approche : crée dynamiquement un <div> flottant par élément -> positionné avec getBoundingClientRect
document.querySelectorAll('[data-tooltip]').forEach(el => {
    // Crée un <div> invisible pour ce tooltip
    const tip = document.createElement('div');
    tip.className   = 'mpa-tooltip';
    tip.textContent = el.dataset.tooltip; // texte lu depuis l'attribut data-tooltip

    // Styles CSS injectés directement (pas de classe CSS pour garder le tooltip portable)
    Object.assign(tip.style, {
        position:      'absolute', // positionné par rapport au document (pas au parent)
        background:    '#111827',
        color:         'white',
        padding:       '6px 10px',
        borderRadius:  '6px',
        fontSize:      '11px',
        zIndex:        1000,        // au-dessus de tout le reste
        maxWidth:      '240px',
        lineHeight:    '1.5',
        display:       'none',      // caché par défaut
        pointerEvents: 'none',      // ne capture pas les événements souris -> laisse passer les clics
        whiteSpace:    'normal'     // autorise le retour à la ligne dans le tooltip
    });

    // Ajoute le tooltip dans le <body> -> pas dans le parent de l'élément
    // Raison : si le parent a overflow:hidden, le tooltip serait coupé
    document.body.appendChild(tip);

    // Affiche le tooltip au survol de l'élément
    el.addEventListener('mouseenter', (e) => {
        tip.style.display = 'block';

        // getBoundingClientRect() -> coordonnées de l'élément par rapport au viewport
        // + window.scrollX / scrollY -> ajoute le scroll pour obtenir la position absolue dans le document
        const r = el.getBoundingClientRect();
        tip.style.left = (r.left  + window.scrollX) + 'px'; // aligné à gauche de l'élément
        tip.style.top  = (r.bottom + window.scrollY + 6) + 'px'; // 6px sous l'élément
    });

    // Cache le tooltip quand la souris quitte l'élément
    el.addEventListener('mouseleave', () => { tip.style.display = 'none'; });
});

// Fonctions utilitaires de formatage
// Fonctions globales réutilisables depuis n'importe quel script ou vue inline

// Formate un nombre en euros français
// fmtEur(12450.5) -> "€12 450,50"
// toLocaleString('fr-FR') -> séparateur de milliers = espace, décimales = virgule
function fmtEur(val) {
    return '€' + parseFloat(val).toLocaleString('fr-FR', {
        minimumFractionDigits: 2, // toujours 2 décimales : "€12,00" pas "€12"
        maximumFractionDigits: 2  // jamais plus de 2 décimales : "€12,50" pas "€12,500"
    });
}

// Formate un nombre en pourcentage avec signe optionnel
// fmtPct(5.2)        -> "+5.20%"
// fmtPct(-2.1)       -> "-2.10%"
// fmtPct(5.2, false) -> "5.20%" (sans le + devant les valeurs positives)
function fmtPct(val, withSign = true) {
    const v = parseFloat(val).toFixed(2); // arrondi à 2 décimales, retourne une string
    // v > 0 et non "v >= 0" -> pas de "+" devant "0.00%"
    return (withSign && v > 0 ? '+' : '') + v + '%';
}

// ── Init global ───────────────────────────────────────────────────────────
// DOMContentLoaded -> attend que le DOM soit prêt avant d'initialiser les onglets
// Nécessaire car ce script est chargé dans le <head> (avant le <body>)
// -> sans cet événement, querySelectorAll('[data-tabs]') retournerait 0 résultat
document.addEventListener('DOMContentLoaded', () => {
    initTabs(); // initialise tous les groupes d'onglets [data-tabs] de la page
});
