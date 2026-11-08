// IT project page (docs/ux/it-projects.md, "The project page"): opens the drawer named in the
// address, and keeps the stage editor in step with the server's stage rules.
//
// The browser calculates nothing about the stage chain. After every change it posts the form to
// the page's Preview handler, which runs ItProjectStageRules and answers with the normalised
// stages and a sentence saying what the save will also do; this script writes that back into
// the inputs and shows the sentence, and asks with PrismConfirm before such a save.
// The form carries data-page-handled: this script owns its confirm dialog.
(function () {
    'use strict';

    var page = document.querySelector('[data-itp-details]');
    if (!page) {
        return;
    }

    // ---- open the drawer the address asks for -------------------------------------------------

    var open = page.getAttribute('data-itp-open');
    var drawerIds = { stages: 'itpStagesDrawer', edit: 'itpEditDrawer', drop: 'itpDropDrawer' };
    if (open && drawerIds[open] && window.bootstrap && window.bootstrap.Offcanvas) {
        var drawer = document.getElementById(drawerIds[open]);
        if (drawer) {
            window.bootstrap.Offcanvas.getOrCreateInstance(drawer).show();
        }
    }

    // ---- stage editor -------------------------------------------------------------------------

    var form = page.querySelector('[data-itp-stage-editor]');
    if (!form) {
        return;
    }

    var previewUrl = form.getAttribute('data-itp-preview-url');
    var rows = Array.prototype.slice.call(form.querySelectorAll('[data-itp-stage]'));
    var note = form.querySelector('[data-itp-stage-note]');
    var tokenInput = form.querySelector('input[name="__RequestVerificationToken"]');
    if (!previewUrl || rows.length === 0) {
        return;
    }

    var touched = {};
    var latest = null;       // the last preview the server answered with
    var requestId = 0;

    function rowByCode(code) {
        for (var i = 0; i < rows.length; i++) {
            if (rows[i].getAttribute('data-itp-code') === code) {
                return rows[i];
            }
        }
        return null;
    }

    // Writes the server's state into the inputs. Presentation only.
    function render(preview) {
        preview.stages.forEach(function (state) {
            var row = rowByCode(state.code);
            if (!row) {
                return;
            }
            var tick = row.querySelector('[data-itp-completed]');
            var date = row.querySelector('[data-itp-date]');
            var include = row.querySelector('[data-itp-included]');
            if (tick) {
                tick.checked = state.completed;
                tick.disabled = state.skipped;
            }
            if (date) {
                date.value = state.completedOn || '';
                date.disabled = state.skipped;
            }
            if (include) {
                include.checked = !state.skipped;
            }
            row.classList.toggle('itp-stage-row--skipped', state.skipped);
            row.classList.toggle('itp-stage-row--done', state.completed);
        });
        if (note) {
            note.hidden = !preview.summary;
            note.textContent = preview.summary || '';
        }
    }

    function preview(reopenCode) {
        var id = ++requestId;
        var data = new FormData(form);
        if (reopenCode) {
            data.set('reopen', reopenCode);
        }
        data.set('touched', Object.keys(touched).join(','));

        var headers = {};
        if (tokenInput && tokenInput.value) {
            headers['X-CSRF-TOKEN'] = tokenInput.value;
        }

        return fetch(previewUrl, { method: 'POST', body: data, headers: headers, credentials: 'same-origin' })
            .then(function (response) { return response.ok ? response.json() : null; })
            .then(function (result) {
                if (id !== requestId || !result) {
                    return;
                }
                if (result.valid) {
                    latest = result;
                    render(result);
                } else {
                    // The server will say the same on save; keep the user's inputs as they are.
                    latest = null;
                    if (note) {
                        note.hidden = false;
                        note.textContent = (result.errors || []).join(' ');
                    }
                }
            })
            .catch(function () {
                // Offline or an error: the server still applies the rules on save.
                latest = null;
            });
    }

    rows.forEach(function (row) {
        var code = row.getAttribute('data-itp-code');
        var tick = row.querySelector('[data-itp-completed]');
        var date = row.querySelector('[data-itp-date]');
        var include = row.querySelector('[data-itp-included]');

        if (tick) {
            tick.addEventListener('change', function () {
                touched[code] = true;
                preview(tick.checked ? null : code);
            });
        }
        if (date) {
            date.addEventListener('change', function () {
                touched[code] = true;
                preview(null);
            });
        }
        if (include) {
            include.addEventListener('change', function () {
                touched[code] = true;
                preview(null);
            });
        }
    });

    var confirmed = false;
    form.addEventListener('submit', function (event) {
        if (confirmed || !latest || !latest.summary || !window.PrismConfirm) {
            return;
        }

        event.preventDefault();
        var reopening = latest.reopened && latest.reopened.length > 0;
        window.PrismConfirm.show({
            tone: reopening ? 'warning' : 'primary',
            title: reopening ? 'Reopen earlier progress?' : 'Complete the earlier stages too?',
            message: latest.summary,
            note: reopening ? 'Reopened stages lose their recorded dates.' : 'Their dates stay empty until someone enters them.',
            confirmText: 'Save stages',
            cancelText: 'Go back'
        }).then(function (ok) {
            if (!ok) {
                return;
            }
            confirmed = true;
            if (typeof form.requestSubmit === 'function') {
                form.requestSubmit();
            } else {
                form.submit();
            }
        });
    });
})();
