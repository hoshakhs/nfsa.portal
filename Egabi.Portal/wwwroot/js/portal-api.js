/* ============================================================
   FILE: wwwroot/js/portal-api.js
   Connects the applicant screens to the portal's own endpoints
   (/portal/api/...), which forward to the egabi Platform API.

   Markup it understands:
     form[data-api="url"]            send the form as JSON, show errors, follow "redirect"
       [data-form-error]             box for the general message
       [data-error-for="field"]      message under a field (name is case-insensitive)
       data-type="bool|int|number"   value conversion (empty → null)
       [data-repeater="owners"]      repeatable rows → array of objects
         <template> + [data-rows] + [data-add-row] + [data-remove-row]
       form[data-form-config="facility-form-config"]
                                     hide / require fields from the platform's settings
                                     (fields marked with data-field="FieldName")
     select[data-lookup="name"]      options loaded from /portal/api/lookups/name
       data-depends="parentName"     reload when the parent select changes
       data-param="sectorId"         query-string name for the parent's id
       data-store="name"             submit the English name instead of the id
       data-default="Egypt"          preselect this English name
     input[type=file][data-doc-upload="url"][data-code="CODE"]
                                     upload one document, then refresh the page
     button[data-api-post="url"][data-confirm="text"]
                                     POST (no body), then follow "redirect"
   ============================================================ */
(function () {
    'use strict';

    var isAr = (document.documentElement.lang || '').toLowerCase().indexOf('ar') === 0;
    var LOOKUP_URL = '/portal/api/lookups/';

    function $(s, r) { return (r || document).querySelector(s); }
    function $$(s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); }
    function t(en, ar) { return isAr ? ar : en; }

    function antiForgery(scope) {
        var i = (scope && $('input[name="__RequestVerificationToken"]', scope)) ||
                $('input[name="__RequestVerificationToken"]');
        return i ? i.value : '';
    }

    function headers(scope, json) {
        var h = { 'X-Portal-Lang': isAr ? 'ar' : 'en', 'RequestVerificationToken': antiForgery(scope) };
        if (json) { h['Content-Type'] = 'application/json'; }
        return h;
    }

    function showToast(msg) {
        var el = $('#gp-toast');
        if (!el || !msg) { return; }
        el.textContent = msg;
        el.classList.add('gp-is-open');
        setTimeout(function () { el.classList.remove('gp-is-open'); }, 3200);
    }

    /* Reads the JSON reply; a network failure or a non-JSON reply becomes a friendly message. */
    function send(url, options) {
        return fetch(url, Object.assign({ credentials: 'same-origin' }, options))
            .then(function (r) {
                return r.json().catch(function () {
                    return { ok: false, message: r.status === 400
                        ? t('The page has expired. Please refresh it and try again.', 'انتهت صلاحية الصفحة. أعد تحميلها وحاول مرة أخرى.')
                        : t('Something went wrong. Please try again.', 'حدث خطأ. حاول مرة أخرى.') };
                });
            })
            .catch(function () {
                return { ok: false, message: t('No connection. Check your internet and try again.', 'لا يوجد اتصال. تحقق من الإنترنت وحاول مرة أخرى.') };
            });
    }

    function follow(res) {
        if (res && res.redirect) {
            setTimeout(function () { window.location.href = res.redirect; }, res.message ? 700 : 0);
        }
    }

    /* ---------------- Value reading ---------------- */
    function readValue(el) {
        var type = el.getAttribute('data-type');
        if (type === 'bool') { return el.checked; }
        var v = (el.value || '').trim();
        if (v === '') { return null; }
        if (type === 'int') { var i = parseInt(v, 10); return isNaN(i) ? null : i; }
        if (type === 'number') { var n = parseFloat(v); return isNaN(n) ? null : n; }
        return v;
    }

    function isNamedField(el) {
        return el.name && el.name !== '__RequestVerificationToken' && !el.disabled &&
               el.type !== 'file' && el.type !== 'submit' && el.type !== 'button';
    }

    function collect(form) {
        var body = {};
        $$('input, select, textarea', form).forEach(function (el) {
            if (!isNamedField(el) || el.closest('[data-repeater]') || el.closest('template')) { return; }
            if (el.type === 'radio' && !el.checked) { return; }
            body[el.name] = readValue(el);
        });
        $$('[data-repeater]', form).forEach(function (rep) {
            var items = [];
            $$('[data-row]', rep).forEach(function (row) {
                var item = {};
                var typed = false;   // rows where nothing was typed are ignored (lists have default values)
                $$('input, select, textarea', row).forEach(function (el) {
                    if (!isNamedField(el)) { return; }
                    item[el.name] = readValue(el);
                    if (el.tagName !== 'SELECT' && el.type !== 'checkbox' && item[el.name] !== null) { typed = true; }
                });
                row.removeAttribute('data-sent-index');
                if (typed) { row.setAttribute('data-sent-index', items.length); items.push(item); }
            });
            body[rep.getAttribute('data-repeater')] = items;
        });
        // Custom fields added in the platform's Form Config → { customFields: { FieldName: "text" } }
        var custom = $$('[data-custom]', form);
        if (custom.length) {
            body.customFields = {};
            custom.forEach(function (el) {
                if (el.closest('.gp-is-hidden')) { return; }   // not for the selected sector
                var v = el.type === 'checkbox' ? (el.checked ? 'true' : null) : ((el.value || '').trim() || null);
                body.customFields[el.getAttribute('data-custom')] = v;
            });
        }
        return body;
    }

    /* ---------------- Errors ---------------- */
    function clearErrors(form) {
        var box = $('[data-form-error]', form);
        if (box) { box.hidden = true; box.textContent = ''; }
        $$('[data-error-for]', form).forEach(function (e) { e.textContent = ''; });
        $$('.gp-field--invalid', form).forEach(function (e) { e.classList.remove('gp-field--invalid'); });
    }

    function findErrorTarget(form, key) {
        // "Owners[1].OwnerName" → row 1 of the "owners" repeater, field "ownername"
        var m = /^([A-Za-z]+)\[(\d+)\]\.([A-Za-z]+)$/.exec(key);
        if (m) {
            var rep = $$('[data-repeater]', form).filter(function (r) {
                return r.getAttribute('data-repeater').toLowerCase() === m[1].toLowerCase();
            })[0];
            var row = rep ? $('[data-row][data-sent-index="' + parseInt(m[2], 10) + '"]', rep) : null;
            if (row) { return matchIn(row, m[3]); }
        }
        var top = $$('[data-error-for]', form).filter(function (e) {
            return !e.closest('[data-row]') && e.getAttribute('data-error-for').toLowerCase() === key.toLowerCase();
        })[0];
        return top || null;
    }

    function matchIn(scope, name) {
        return $$('[data-error-for]', scope).filter(function (e) {
            return e.getAttribute('data-error-for').toLowerCase() === name.toLowerCase();
        })[0] || null;
    }

    function showErrors(form, res) {
        var box = $('[data-form-error]', form);
        var unplaced = [];
        var first = null;
        Object.keys(res.errors || {}).forEach(function (key) {
            var msgs = res.errors[key] || [];
            var target = findErrorTarget(form, key.replace(/^\$\./, ''));
            if (target) {
                target.textContent = msgs.join(' ');
                var field = target.closest('.gp-field');
                if (field) { field.classList.add('gp-field--invalid'); }
                first = first || target;
            } else {
                unplaced = unplaced.concat(msgs);
            }
        });
        if (box) {
            var text = [res.message].concat(unplaced).filter(Boolean).join(' • ');
            if (text) { box.textContent = text; box.hidden = false; first = first || box; }
        }
        if (first) { first.scrollIntoView({ behavior: 'smooth', block: 'center' }); }
    }

    /* ---------------- Forms ---------------- */
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!form.matches('form[data-api]')) { return; }
        e.preventDefault();
        if (form.classList.contains('gp-is-busy')) { return; }

        clearErrors(form);
        form.classList.add('gp-is-busy');
        var btn = $('button[type="submit"]', form);
        if (btn) { btn.disabled = true; }

        send(form.getAttribute('data-api'), {
            method: 'POST',
            headers: headers(form, true),
            body: JSON.stringify(collect(form))
        }).then(function (res) {
            form.classList.remove('gp-is-busy');
            if (btn) { btn.disabled = false; }
            if (res.ok) {
                showToast(res.message);
                follow(res);
            } else {
                showErrors(form, res);
                if (res.redirect) { follow(res); }
            }
        });
    });

    /* ---------------- Repeaters ---------------- */
    function addRow(rep) {
        var tpl = $('template', rep);
        var rows = $('[data-rows]', rep);
        if (!tpl || !rows) { return; }
        rows.appendChild(tpl.content.cloneNode(true));
    }

    document.addEventListener('click', function (e) {
        var add = e.target.closest('[data-add-row]');
        if (add) { addRow(add.closest('[data-repeater]')); return; }
        var rm = e.target.closest('[data-remove-row]');
        if (rm) { var row = rm.closest('[data-row]'); if (row) { row.remove(); } }
    });

    $$('[data-repeater]').forEach(function (rep) {
        if (!$('[data-row]', rep)) { addRow(rep); }   // start with one empty row
    });

    /* ---------------- Lookups (lists) ---------------- */
    function optionLabel(item) {
        return (isAr ? (item.nameAr || item.nameEn) : (item.nameEn || item.nameAr)) || '';
    }

    function resetSelect(sel) {
        var first = sel.options[0] && sel.options[0].value === '' ? sel.options[0] : null;
        sel.innerHTML = '';
        if (first) { sel.appendChild(first); }
        sel.value = '';
    }

    function parentOf(sel) {
        var name = sel.getAttribute('data-depends');
        return name ? $('[name="' + name + '"]', sel.form || document) : null;
    }

    function parentId(parent) {
        var opt = parent.options[parent.selectedIndex];
        return opt && opt.value ? (opt.getAttribute('data-id') || opt.value) : '';
    }

    function loadLookup(sel) {
        var parent = parentOf(sel);
        var query = '';
        resetSelect(sel);
        if (parent) {
            var pid = parentId(parent);
            if (!pid) { sel.dispatchEvent(new Event('change', { bubbles: true })); return; }
            query = '?' + encodeURIComponent(sel.getAttribute('data-param') || 'id') + '=' + encodeURIComponent(pid);
        }
        sel.disabled = true;
        send(LOOKUP_URL + sel.getAttribute('data-lookup') + query, { headers: headers(null, false) })
            .then(function (res) {
                sel.disabled = false;
                if (!res.ok) { if (res.redirect) { follow(res); } return; }
                var byName = sel.getAttribute('data-store') === 'name';
                var def = (sel.getAttribute('data-default') || '').toLowerCase();
                (res.data || []).forEach(function (item) {
                    var o = document.createElement('option');
                    o.value = byName ? item.nameEn : item.id;
                    o.setAttribute('data-id', item.id);
                    o.textContent = optionLabel(item);
                    if (def && (item.nameEn || '').toLowerCase() === def) { o.selected = true; }
                    sel.appendChild(o);
                });
                sel.dispatchEvent(new Event('change', { bubbles: true }));
            });
    }

    var lookups = $$('select[data-lookup]');
    lookups.forEach(function (sel) {
        if (!sel.getAttribute('data-depends')) { loadLookup(sel); }
    });
    document.addEventListener('change', function (e) {
        var parent = e.target;
        if (!parent.name || !parent.matches('select')) { return; }
        lookups.forEach(function (child) {
            if (child.getAttribute('data-depends') === parent.name && child.form === parent.form) { loadLookup(child); }
        });
    });

    /* ---------------- Custom fields (added by the admin in the platform's Form Config) ---------------- */
    function fieldLabel(cfg) { return (isAr ? (cfg.labelAr || cfg.label) : (cfg.label || cfg.labelAr)) || cfg.fieldName; }

    function buildCustomField(cfg) {
        var wrap = document.createElement('div');
        wrap.className = 'gp-field';
        wrap.setAttribute('data-custom-field', cfg.fieldName);
        var id = 'cf-' + cfg.fieldName.replace(/[^A-Za-z0-9_-]/g, '');
        var input;

        if (cfg.fieldType === 'Checkbox') {
            var box = document.createElement('label');
            box.className = 'gp-check';
            input = document.createElement('input');
            input.type = 'checkbox';
            var span = document.createElement('span');
            span.textContent = fieldLabel(cfg);
            box.appendChild(input);
            box.appendChild(span);
            wrap.appendChild(box);
        } else {
            var label = document.createElement('label');
            label.htmlFor = id;
            label.textContent = fieldLabel(cfg) + ' ';
            if (cfg.isRequired) {
                var star = document.createElement('span');
                star.className = 'gp-req';
                star.textContent = '*';
                label.appendChild(star);
            }
            wrap.appendChild(label);

            if (cfg.fieldType === 'Dropdown') {
                input = document.createElement('select');
                var empty = document.createElement('option');
                empty.value = '';
                empty.textContent = t('— Choose —', '— اختر —');
                input.appendChild(empty);
                (cfg.options || []).forEach(function (o) {
                    var opt = document.createElement('option');
                    opt.value = o;
                    opt.textContent = o;
                    input.appendChild(opt);
                });
            } else {
                input = document.createElement('input');
                input.type = { Date: 'date', Number: 'number', Email: 'email', Phone: 'tel' }[cfg.fieldType] || 'text';
                if (input.type === 'number') { input.step = 'any'; }
                if (input.type === 'text') { input.maxLength = 500; }
                if (cfg.placeholder) { input.placeholder = cfg.placeholder; }
            }
            input.id = id;
            input.required = !!cfg.isRequired;
            wrap.appendChild(input);
        }
        input.setAttribute('data-custom', cfg.fieldName);

        if (cfg.helpText) {
            var hint = document.createElement('small');
            hint.className = 'gp-hint';
            hint.textContent = cfg.helpText;
            wrap.appendChild(hint);
        }
        var err = document.createElement('small');
        err.className = 'gp-field-error';
        err.setAttribute('data-error-for', 'customFields.' + cfg.fieldName);
        wrap.appendChild(err);
        return wrap;
    }

    /* Puts each custom field in its section (TabName), right after its AfterField — like the internal form. */
    function placeCustomFields(form, configs) {
        var sections = $$('[data-section]', form);
        if (!sections.length) { return; }
        var lastAfter = {};   // keeps several fields placed after the same field in order
        configs
            .filter(function (c) { return !c.isStandard && c.isVisible; })
            .sort(function (a, b) { return (a.displayOrder || 0) - (b.displayOrder || 0); })
            .forEach(function (cfg) {
                var tab = (cfg.tabName || 'Basic').toLowerCase();
                var section = sections.filter(function (s) { return s.getAttribute('data-section').toLowerCase() === tab; })[0]
                              || sections[0];
                var el = buildCustomField(cfg);
                var anchorKey = tab + '|' + (cfg.afterField || '').toLowerCase();
                var anchor = lastAfter[anchorKey];
                if (!anchor && cfg.afterField) {
                    anchor = $$('[data-field]', section).filter(function (f) {
                        return f.getAttribute('data-field').toLowerCase() === cfg.afterField.toLowerCase();
                    })[0];
                }
                if (anchor && anchor.parentNode === section) {
                    section.insertBefore(el, anchor.nextSibling);
                } else {
                    section.appendChild(el);
                }
                lastAfter[anchorKey] = el;
            });
    }

    /* ---------------- Form settings from the platform (hide / required) ---------------- */
    $$('form[data-form-config]').forEach(function (form) {
        send(LOOKUP_URL + form.getAttribute('data-form-config'), { headers: headers(null, false) })
            .then(function (res) {
                if (!res.ok || !res.data) { return; }
                placeCustomFields(form, res.data);
                var sectorSel = $('[name="sectorID"]', form);
                function apply() {
                    var sectorId = sectorSel ? parseInt(sectorSel.value, 10) : NaN;
                    res.data.forEach(function (cfg) {
                        if (!cfg.isStandard) {
                            // a custom field for one sector only is shown when that sector is selected
                            $$('[data-custom-field]', form).forEach(function (el) {
                                if (el.getAttribute('data-custom-field') !== cfg.fieldName) { return; }
                                el.classList.toggle('gp-is-hidden', !!cfg.sectorId && cfg.sectorId !== sectorId);
                            });
                            return;
                        }
                        if (cfg.sectorId && cfg.sectorId !== sectorId) { return; }
                        $$('[data-field]', form).forEach(function (el) {
                            if (el.getAttribute('data-field').toLowerCase() !== (cfg.fieldName || '').toLowerCase()) { return; }
                            el.classList.toggle('gp-is-hidden', !cfg.isVisible);
                            var input = $('input, select, textarea', el);
                            if (input && cfg.isVisible && cfg.isRequired) {
                                input.required = true;
                                var label = $('label', el);
                                if (label && !$('.gp-req', label)) {
                                    var star = document.createElement('span');
                                    star.className = 'gp-req';
                                    star.textContent = ' *';
                                    label.appendChild(star);
                                }
                            }
                        });
                    });
                }
                apply();
                if (sectorSel) { sectorSel.addEventListener('change', apply); }
            });
    });

    /* ---------------- Document upload ---------------- */
    document.addEventListener('change', function (e) {
        var input = e.target;
        if (!input.matches('input[type="file"][data-doc-upload]') || !input.files.length) { return; }

        var row = input.closest('[data-doc-row]') || input.parentNode;
        var err = $('[data-doc-error]', row);
        var label = input.closest('label');
        var file = input.files[0];

        if (err) { err.textContent = ''; }
        if (file.size > 10 * 1024 * 1024) {
            if (err) { err.textContent = t('The file is larger than 10 MB.', 'حجم الملف أكبر من 10 ميجابايت.'); }
            input.value = '';
            return;
        }

        var data = new FormData();
        data.append('documentTypeCode', input.getAttribute('data-code'));
        data.append('file', file);

        row.classList.add('gp-is-uploading');
        if (label) { label.classList.add('gp-is-busy'); }

        send(input.getAttribute('data-doc-upload'), {
            method: 'POST',
            headers: headers(null, false),   // the browser sets the multipart Content-Type itself
            body: data
        }).then(function (res) {
            row.classList.remove('gp-is-uploading');
            if (label) { label.classList.remove('gp-is-busy'); }
            input.value = '';
            if (res.ok) {
                showToast(t('Document uploaded.', 'تم رفع المستند.'));
                setTimeout(function () { window.location.reload(); }, 600);
            } else if (res.redirect) {
                follow(res);
            } else if (err) {
                err.textContent = res.message || t('The file could not be uploaded.', 'تعذر رفع الملف.');
            }
        });
    });

    /* ---------------- Action buttons (e.g. submit for approval) ---------------- */
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('button[data-api-post]');
        if (!btn || btn.disabled) { return; }
        var question = btn.getAttribute('data-confirm');
        if (question && !window.confirm(question)) { return; }

        var scope = btn.closest('.gp-panel') || document;
        var box = $('[data-form-error]', scope);
        if (box) { box.hidden = true; box.textContent = ''; }
        btn.disabled = true;

        send(btn.getAttribute('data-api-post'), { method: 'POST', headers: headers(null, false) })
            .then(function (res) {
                btn.disabled = false;
                if (res.ok) {
                    showToast(res.message);
                    if (res.redirect) { follow(res); } else { setTimeout(function () { window.location.reload(); }, 700); }
                } else {
                    if (box) { box.textContent = res.message || ''; box.hidden = !res.message; }
                    follow(res);
                }
            });
    });
})();
