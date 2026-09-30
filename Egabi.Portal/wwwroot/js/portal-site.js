/* Public portal — shared page behaviour (Egabi.Portal/wwwroot/js/portal-site.js).
   State is expressed with classes only (no inline styles). */
(function () {
    'use strict';

    var $ = function (s, r) { return (r || document).querySelector(s); };
    var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };
    var isAr = (document.documentElement.lang || '').indexOf('ar') === 0;
    var toastTimer = null;

    /* ---------- Toast (message at the bottom of the screen) ---------- */
    function toast(el) {
        var msg = el.getAttribute(isAr ? 'data-toast-ar' : 'data-toast-en');
        var t = $('#gp-toast');
        if (!msg || !t) { return; }
        t.textContent = msg;
        t.classList.add('gp-is-open');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(function () { t.classList.remove('gp-is-open'); }, 3200);
    }

    function closeUserMenus() {
        $$('.gp-usermenu.gp-is-open').forEach(function (m) { m.classList.remove('gp-is-open'); });
    }

    /* ---------- New request wizard ---------- */
    var wizardStep = 0;
    function wizardRender() {
        var w = $('#gp-wizard');
        if (!w) { return; }
        var steps = $$('.gp-wstep', w);
        var marks = $$('.gp-stepper li', w);
        steps.forEach(function (s, n) { s.classList.toggle('gp-is-current', n === wizardStep); });
        marks.forEach(function (m, n) {
            m.classList.toggle('gp-is-current', n === wizardStep);
            m.classList.toggle('gp-is-done', n < wizardStep);
        });
        w.classList.toggle('gp-is-first', wizardStep === 0);
        w.classList.toggle('gp-is-last', wizardStep === steps.length - 1);
        w.classList.toggle('gp-is-done', wizardStep >= steps.length);
    }

    /* ---------- Clicks ---------- */
    document.addEventListener('click', function (e) {
        var t;

        if ((t = e.target.closest('[data-action]'))) {
            var action = t.getAttribute('data-action');
            if (action === 'menu') { document.body.classList.toggle('gp-menu-open'); return; }
            if (action === 'usermenu') {
                var menu = t.parentNode.querySelector('.gp-usermenu');
                if (menu) { menu.classList.toggle('gp-is-open'); }
                e.stopPropagation();
                return;
            }
            if (action === 'readall') {
                $$('#gp-notes .gp-is-unread').forEach(function (n) { n.classList.remove('gp-is-unread'); });
                return;
            }
        }
        if (!e.target.closest('.gp-user')) { closeUserMenus(); }

        if ((t = e.target.closest('[data-wiz]'))) {
            var total = $$('#gp-wizard .gp-wstep').length;
            var dir = t.getAttribute('data-wiz');
            if (dir === 'next' && wizardStep < total - 1) { wizardStep++; }
            if (dir === 'prev' && wizardStep > 0) { wizardStep--; }
            if (dir === 'submit') { wizardStep = total; }
            wizardRender();
            window.scrollTo(0, 0);
            return;
        }

        if ((t = e.target.closest('[data-tab]'))) {
            var set = t.closest('.gp-tabset');
            var key = t.getAttribute('data-tab');
            $$('[data-tab]', set).forEach(function (b) { b.classList.toggle('gp-is-on', b === t); });
            $$('[data-panel]', set).forEach(function (p) { p.classList.toggle('gp-is-on', p.getAttribute('data-panel') === key); });
            return;
        }

        if ((t = e.target.closest('[data-filter]'))) {
            var bar = t.closest('[data-rowfilter]');
            var f = t.getAttribute('data-filter');
            $$('[data-filter]', bar).forEach(function (b) { b.classList.toggle('gp-is-on', b === t); });
            $$('#' + bar.getAttribute('data-rowfilter') + ' tbody tr').forEach(function (r) {
                r.classList.toggle('gp-is-hidden', f !== 'all' && r.getAttribute('data-status') !== f);
            });
            return;
        }

        if ((t = e.target.closest('[data-modal-open]'))) {
            var modal = document.getElementById(t.getAttribute('data-modal-open'));
            if (modal) { modal.classList.add('gp-is-open'); }
            return;
        }
        if ((t = e.target.closest('[data-modal-close]'))) {
            t.closest('.gp-modal').classList.remove('gp-is-open');
            toast(t);
            return;
        }
        if (e.target.classList.contains('gp-modal')) { e.target.classList.remove('gp-is-open'); return; }

        if ((t = e.target.closest('[data-upload]'))) {
            var li = t.closest('.gp-uploads li');
            if (li) {
                li.classList.add('gp-is-done');
                var ic = $('i', li);
                if (ic) { ic.className = 'bi bi-check-circle-fill'; }
                t.remove();
            } else {
                t.setAttribute('data-toast-en', 'File attached');
                t.setAttribute('data-toast-ar', 'تم إرفاق الملف');
                toast(t);
            }
            return;
        }

        if ((t = e.target.closest('[data-pick] > *'))) {
            if (!t.disabled) {
                $$(':scope > *', t.parentNode).forEach(function (s) { s.classList.toggle('gp-is-on', s === t); });
                t.classList.remove('gp-is-unread');
            }
        }

        if ((t = e.target.closest('[data-href]'))) {
            window.location.href = t.getAttribute('data-href');
            return;
        }

        if ((t = e.target.closest('button[data-toast-en]')) && t.type !== 'submit') { toast(t); }
    });

    /* ---------- Keyboard ---------- */
    document.addEventListener('keydown', function (e) {
        if ((e.key === 'Enter' || e.key === ' ') && e.target.matches('[data-href]')) {
            e.preventDefault();
            window.location.href = e.target.getAttribute('data-href');
        }
        if (e.key === 'Escape') {
            $$('.gp-modal.gp-is-open').forEach(function (m) { m.classList.remove('gp-is-open'); });
            closeUserMenus();
            document.body.classList.remove('gp-menu-open');
        }
    });

    /* ---------- Forms on applicant screens (sample behaviour until the API is connected) ---------- */
    document.addEventListener('submit', function (e) {
        var f = e.target;
        if (!f.closest('.gp-screen')) { return; }      // public forms (search, track on home) submit normally
        if (f.hasAttribute('data-api')) { return; }    // real forms are handled by portal-api.js
        if (f.hasAttribute('data-native')) { return; } // real forms that submit normally (e.g. Track a request)
        e.preventDefault();
        toast(f);
        var next = f.getAttribute('data-next');
        if (next) {
            setTimeout(function () { window.location.href = next; }, f.hasAttribute('data-toast-en') ? 900 : 0);
        }
    });

    /* ---------- Init ---------- */
    $$('tr[data-href]').forEach(function (r) { r.tabIndex = 0; });
    wizardRender();
})();
