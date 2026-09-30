/* ============================================================
   FILE: wwwroot/js/portal-services.js   (NEW)
   Service request screen (/account/new-request?code=XYZ).
   Draws the service's Form Builder form with the platform's own
   renderer (loaded from /portal/forms/renderer.js) and submits it.

   Batch 7: also "Correct and resubmit" (/account/new-request?ref=SRV-…):
   the form is filled with the previous answers (#gp-service-answers) and
   sent to /portal/api/services/requests/{ref}/resubmit — no facility choice.

   Every call the form makes goes to the portal (/portal/api/services/...),
   which forwards it to the egabi Platform — the browser never talks
   to the platform directly.
   ============================================================ */
(function () {
    'use strict';

    var host = document.getElementById('gp-service-form');
    var schemaEl = document.getElementById('gp-service-schema');
    if (!host || !schemaEl) return;

    var isAr = host.getAttribute('data-lang') === 'ar';
    var code = host.getAttribute('data-code');
    var resubmitRef = host.getAttribute('data-resubmit');   // Batch 7
    var base = '/portal/api/services/' + encodeURIComponent(code);
    var screen = host.closest('.gp-panel') || document;
    var errorBox = screen.querySelector('[data-form-error]');
    var submitBtn = screen.querySelector('[data-service-submit]');
    var facilityError = screen.querySelector('[data-error-for="facilityId"]');

    function t(en, ar) { return isAr ? ar : en; }
    function token() {
        var i = document.querySelector('input[name="__RequestVerificationToken"]');
        return i ? i.value : '';
    }
    function showError(msg) {
        if (!errorBox) return;
        errorBox.textContent = msg || '';
        errorBox.hidden = !msg;
        if (msg) errorBox.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
    function toast(msg) {
        var el = document.getElementById('gp-toast');
        if (!el || !msg) return;
        el.textContent = msg;
        el.classList.add('gp-is-open');
        setTimeout(function () { el.classList.remove('gp-is-open'); }, 3200);
    }

    if (!window.FBRenderer) {
        host.innerHTML = '';
        showError(t('The form could not be loaded. Please refresh the page.', 'تعذر تحميل النموذج. أعد تحميل الصفحة.'));
        if (submitBtn) submitBtn.disabled = true;
        return;
    }

    var schema;
    try { schema = JSON.parse(schemaEl.textContent || '{}'); } catch (e) { schema = { pages: [] }; }
    var settings = schema.settings || {};

    var form = window.FBRenderer.create(host, schema, {
        lang: isAr ? 'ar' : 'en',
        token: token(),
        uploadUrl: base + '/upload',
        optionsUrl: base + '/options',
        entityLookupUrl: base + '/data',
        rulesUrl: base + '/rules/evaluate'
    });

    // Batch 7: correcting a returned request — start from what was submitted
    if (resubmitRef) {
        var answersEl = document.getElementById('gp-service-answers');
        try {
            var previous = JSON.parse((answersEl && answersEl.textContent) || '{}');
            if (previous && typeof previous === 'object') form.setAnswers(previous);
        } catch (e) { /* start empty */ }
    }

    function selectedFacility() {
        var r = screen.querySelector('input[name="facilityId"]:checked');
        return r ? parseInt(r.value, 10) : 0;
    }

    if (!submitBtn) return;
    submitBtn.addEventListener('click', function () {
        showError('');
        if (facilityError) facilityError.textContent = '';

        var facilityId = resubmitRef ? 0 : selectedFacility();
        if (!resubmitRef && !facilityId) {
            if (facilityError) facilityError.textContent = t('Choose the facility.', 'اختر المنشأة.');
            showError(t('Choose the facility first.', 'اختر المنشأة أولًا.'));
            return;
        }

        var v = form.validate();
        if (!v.valid) {
            showError(t('Please correct the highlighted fields (' + v.errors.length + ').',
                        'يرجى تصحيح الحقول المحددة (' + v.errors.length + ').'));
            var first = host.querySelector('.fbr-invalid');
            if (first) first.scrollIntoView({ behavior: 'smooth', block: 'center' });
            return;
        }

        var question = submitBtn.getAttribute('data-confirm');
        if ((settings.confirmSubmit || question) && !window.confirm(question || t('Submit this request?', 'هل تريد تقديم الطلب؟'))) return;

        submitBtn.disabled = true;
        var url = resubmitRef
            ? '/portal/api/services/requests/' + encodeURIComponent(resubmitRef) + '/resubmit'
            : base + '/requests';
        var payload = resubmitRef
            ? { answers: form.getAnswers() }
            : { facilityId: facilityId, answers: form.getAnswers() };

        fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token(),
                'X-Portal-Lang': isAr ? 'ar' : 'en'
            },
            body: JSON.stringify(payload)
        })
            .then(function (r) { return r.json().catch(function () { return { ok: false }; }); })
            .catch(function () { return { ok: false, message: t('No connection. Please try again.', 'لا يوجد اتصال. حاول مرة أخرى.') }; })
            .then(function (res) {
                submitBtn.disabled = false;
                if (res.ok) {
                    toast(res.message);
                    if (res.redirect) setTimeout(function () { window.location.href = res.redirect; }, 900);
                    return;
                }
                var extra = [];
                Object.keys(res.errors || {}).forEach(function (k) {
                    var msgs = res.errors[k] || [];
                    if (k.toLowerCase() === 'facilityid' && facilityError) facilityError.textContent = msgs.join(' ');
                    else extra = extra.concat(msgs);
                });
                showError([res.message || t('The request could not be submitted.', 'تعذر تقديم الطلب.')].concat(extra).join(' • '));
                if (res.redirect) setTimeout(function () { window.location.href = res.redirect; }, 1500);
            });
    });
})();
