/* ═══════════════════════════════════════════════════════════════════════════
   MPA — site.js  |  JavaScript global
   Chargé sur toutes les pages via _Layout.cshtml
   ═══════════════════════════════════════════════════════════════════════════ */

'use strict';

// ── Sidebar mobile ────────────────────────────────────────────────────────
function openSidebar() {
    document.getElementById('mpa-sidebar')?.classList.add('open');
    document.getElementById('sb-overlay')?.classList.add('open');
}

function closeSidebar() {
    document.getElementById('mpa-sidebar')?.classList.remove('open');
    document.getElementById('sb-overlay')?.classList.remove('open');
}

// Ferme sidebar en cliquant un lien (mobile)
document.querySelectorAll('.sb-item').forEach(item => {
    item.addEventListener('click', closeSidebar);
});

// ── Onglets génériques ────────────────────────────────────────────────────
// Usage : <button class="tab" data-tab="tab-id">
//         <div class="tab-content" id="tab-id">
function initTabs(containerSelector) {
    const containers = document.querySelectorAll(containerSelector || '[data-tabs]');
    containers.forEach(container => {
        const tabs = container.querySelectorAll('.tab');
        tabs.forEach(tab => {
            tab.addEventListener('click', () => {
                const targetId = tab.dataset.tab;
                // Désactiver tous
                tabs.forEach(t => t.classList.remove('active'));
                container.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
                // Activer le sélectionné
                tab.classList.add('active');
                document.getElementById(targetId)?.classList.add('active');
            });
        });
    });
}

// ── Polling temps réel (30s) ──────────────────────────────────────────────
let _pollingTimer = null;
let _lastUpdate = new Date();

function startPolling(callback, intervalMs = 30000) {
    if (_pollingTimer) clearInterval(_pollingTimer);
    _pollingTimer = setInterval(() => {
        _lastUpdate = new Date();
        updateRealtimeIndicator();
        if (typeof callback === 'function') callback();
    }, intervalMs);
}

function stopPolling() {
    if (_pollingTimer) {
        clearInterval(_pollingTimer);
        _pollingTimer = null;
    }
}

function updateRealtimeIndicator() {
    const label = document.querySelector('.rt-label');
    if (!label) return;
    const secs = Math.round((new Date() - _lastUpdate) / 1000);
    label.textContent = secs < 5 ? 'En direct' : `Il y a ${secs}s`;
}

// Mise à jour toutes les 5s de l'indicateur
setInterval(updateRealtimeIndicator, 5000);

// ── Recherche globale (ticker) ────────────────────────────────────────────
const searchInput = document.getElementById('globalSearch');
if (searchInput) {
    let _searchTimer;
    searchInput.addEventListener('input', (e) => {
        clearTimeout(_searchTimer);
        const q = e.target.value.trim();
        if (q.length < 2) return;
        _searchTimer = setTimeout(() => {
            window.location.href = `/Assets?search=${encodeURIComponent(q)}`;
        }, 600);
    });

    searchInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
            const q = e.target.value.trim();
            if (q.length > 0) {
                window.location.href = `/Assets?search=${encodeURIComponent(q)}`;
            }
        }
    });
}

// ── Messages flash auto-disparaître ──────────────────────────────────────
document.querySelectorAll('.mpa-alert').forEach(alert => {
    setTimeout(() => {
        alert.style.opacity = '0';
        alert.style.transition = 'opacity .4s';
        setTimeout(() => alert.remove(), 400);
    }, 5000);
});

// ── Tooltips info (ⓘ) ────────────────────────────────────────────────────
document.querySelectorAll('[data-tooltip]').forEach(el => {
    const tip = document.createElement('div');
    tip.className = 'mpa-tooltip';
    tip.textContent = el.dataset.tooltip;
    Object.assign(tip.style, {
        position: 'absolute', background: '#111827', color: 'white',
        padding: '6px 10px', borderRadius: '6px', fontSize: '11px',
        zIndex: 1000, maxWidth: '240px', lineHeight: '1.5',
        display: 'none', pointerEvents: 'none', whiteSpace: 'normal'
    });
    document.body.appendChild(tip);

    el.addEventListener('mouseenter', (e) => {
        tip.style.display = 'block';
        const r = el.getBoundingClientRect();
        tip.style.left = (r.left + window.scrollX) + 'px';
        tip.style.top = (r.bottom + window.scrollY + 6) + 'px';
    });
    el.addEventListener('mouseleave', () => { tip.style.display = 'none'; });
});

// ── Format nombre en EUR ──────────────────────────────────────────────────
function fmtEur(val) {
    return '€' + parseFloat(val).toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function fmtPct(val, withSign = true) {
    const v = parseFloat(val).toFixed(2);
    return (withSign && v > 0 ? '+' : '') + v + '%';
}

// ── Init global ───────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
    initTabs();
});
