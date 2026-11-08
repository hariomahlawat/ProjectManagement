// IT project page (docs/ux/it-projects.md, "The project page"): opens the drawer named in the
// address, and keeps the stage editor in step with the server's stage rules.
//
// The browser calculates nothing about the stage chain. After every change it posts the form to
// the page's Preview handler, which runs ItProjectStageRules and answers with the normalised
// stages and a sentence saying what the save will also do; this script writes that back into
// the inputs and shows the sentence, and asks with PrismConfirm before such a save.
//
// An untick is a "reopen" gesture. It is recorded in the hidden `reopen` field, which both the
// preview and the save handler apply before the rules, so a save that arrives before a preview
// has answered (or when the preview failed) still reopens what the user asked. The field is
// cleared once a preview has written the normalised state back, because that state then carries
// the gesture itself.
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
    var reopenInput = form.querySelector('[data-itp-reopen]');
    if (!previewUrl || rows.length === 0 || !reopenInput) {
        return;
    }

    var touched = {};
    var reopenQueue = [];
    var latest = null;        // the last preview the server answered with, or null
    var inFlight = null;      // the preview request currently running, or null
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

    function setReopenField() {
        reopenInput.value = reopenQueue.join(',');
    }

    function preview() {
        var id = ++requestId;
        var data = new FormData(form);
        data.set('touched', Object.keys(touched).join(','));

        var headers = {};
        if (tokenInput && tokenInput.value) {
            headers['X-CSRF-TOKEN'] = tokenInput.value;
        }

        var request = fetch(previewUrl, { method: 'POST', body: data, headers: headers, credentials: 'same-origin' })
            .then(function (response) { return response.ok ? response.json() : null; })
            .then(function (result) {
                if (id !== requestId) {
                    return; // a newer edit superseded this preview
                }
                if (result && result.valid) {
                    latest = result;
                    render(result);
                    // The written-back state now carries the reopen gestures; nothing left to replay.
                    reopenQueue = [];
                    setReopenField();
                } else {
                    latest = null;
                    if (note) {
                        var errors = result && result.errors ? result.errors : [];
                        note.hidden = errors.length === 0;
                        note.textContent = errors.join(' ');
                    }
                }
            })
            .catch(function () {
                // Offline or an error: the save handler applies the rules and the queued reopens itself.
                if (id === requestId) {
                    latest = null;
                }
            })
            .then(function () {
                if (id === requestId) {
                    inFlight = null;
                }
            });

        inFlight = request;
        return request;
    }

    rows.forEach(function (row) {
        var code = row.getAttribute('data-itp-code');
        var tick = row.querySelector('[data-itp-completed]');
        var date = row.querySelector('[data-itp-date]');
        var include = row.querySelector('[data-itp-included]');

        if (tick) {
            tick.addEventListener('change', function () {
                touched[code] = true;
                if (!tick.checked && reopenQueue.indexOf(code) === -1) {
                    reopenQueue.push(code);
                    setReopenField();
                }
                preview();
            });
        }
        if (date) {
            date.addEventListener('change', function () {
                touched[code] = true;
                preview();
            });
        }
        if (include) {
            include.addEventListener('change', function () {
                touched[code] = true;
                preview();
            });
        }
    });

    var confirmed = false;

    function submitNow() {
        confirmed = true;
        if (typeof form.requestSubmit === 'function') {
            form.requestSubmit();
        } else {
            form.submit();
        }
    }

    // Decides, from the latest server preview, whether to ask first. Called only when no preview
    // is in flight, so the decision matches what will be posted.
    function decide() {
        if (!latest || !latest.summary || !window.PrismConfirm) {
            submitNow();
            return;
        }

        var reopening = latest.reopened && latest.reopened.length > 0;
        window.PrismConfirm.show({
            tone: reopening ? 'warning' : 'primary',
            title: reopening ? 'Reopen earlier progress?' : 'Complete the earlier stages too?',
            message: latest.summary,
            note: reopening ? 'Reopened stages lose their recorded dates.' : 'Their dates stay empty until someone enters them.',
            confirmText: 'Save stages',
            cancelText: 'Go back'
        }).then(function (ok) {
            if (ok) {
                submitNow();
            }
        });
    }

    form.addEventListener('submit', function (event) {
        if (confirmed) {
            return;
        }

        event.preventDefault();
        if (inFlight) {
            inFlight.then(decide);
        } else {
            decide();
        }
    });
})();
