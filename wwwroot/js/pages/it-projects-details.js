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

    // ---- remarks: open and close the inline editor ---------------------------------------------
    // Without script the Edit link jumps to the <details> and :target shows it; with script we
    // toggle the details element in place so the page does not scroll.

    page.addEventListener('click', function (event) {
        var edit = event.target.closest('[data-itp-remark-edit]');
        if (edit) {
            var editor = document.getElementById(edit.getAttribute('href').slice(1));
            if (editor) {
                event.preventDefault();
                editor.open = true;
                var text = editor.querySelector('textarea');
                if (text) {
                    text.focus();
                }
            }
            return;
        }

        var cancel = event.target.closest('[data-itp-remark-cancel]');
        if (cancel) {
            var details = cancel.closest('details');
            if (details) {
                event.preventDefault();
                details.open = false;
            }
            return;
        }

        // ---- Record card: a year's editor opens in its row, one at a time -----------------------

        var yearEdit = event.target.closest('[data-itp-year-edit]');
        if (yearEdit) {
            var yearEditor = document.getElementById(yearEdit.getAttribute('href').slice(1));
            if (yearEditor) {
                event.preventDefault();
                Array.prototype.forEach.call(page.querySelectorAll('.itp-years__editor[open]'), function (other) {
                    other.open = false;
                });
                yearEditor.open = true;
                var first = yearEditor.querySelector('input[name="yearReference"]');
                if (first) {
                    first.focus();
                }
            }
            return;
        }

        var yearCancel = event.target.closest('[data-itp-year-cancel]');
        if (yearCancel) {
            var yearDetails = yearCancel.closest('details');
            if (yearDetails) {
                event.preventDefault();
                var yearForm = yearDetails.querySelector('form');
                if (yearForm) {
                    yearForm.reset(); // Cancel puts back what the row shows
                }
                yearDetails.open = false;
                var opener = page.querySelector('[data-itp-year-edit][href="#' + yearDetails.id + '"]');
                if (opener) {
                    opener.focus();
                }
            }
        }
    });

    // ---- remarks: the new-remark composer stays open once something is typed ---------------------
    // CSS opens the closed composer while it has focus. With text in it, it must not shut when
    // focus moves away, so the closed class comes off; emptied again, it goes back on.

    var composer = page.querySelector('[data-itp-composer]');
    if (composer) {
        var composerText = composer.querySelector('textarea');
        if (composerText) {
            composerText.addEventListener('input', function () {
                composer.classList.toggle('itp-composer--closed', composerText.value.trim() === '');
            });
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
    // Gestures a failed save carried back are already in the field; start from them.
    var reopenQueue = reopenInput.value ? reopenInput.value.split(',').filter(Boolean) : [];
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
            note.classList.toggle('itp-stage-note--reopen', !!(preview.reopened && preview.reopened.length));
        }
    }

    function setReopenField() {
        reopenInput.value = reopenQueue.join(',');
    }

    // A tick or a date on a stage supersedes any queued reopen of that stage or an earlier one:
    // the newest gesture wins. Rows are in stage order, so position stands in for order here.
    function supersedeReopens(rowIndex) {
        var keep = reopenQueue.filter(function (code) {
            var row = rowByCode(code);
            return row && rows.indexOf(row) > rowIndex;
        });
        if (keep.length !== reopenQueue.length) {
            reopenQueue = keep;
            setReopenField();
        }
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

    rows.forEach(function (row, rowIndex) {
        var code = row.getAttribute('data-itp-code');
        var tick = row.querySelector('[data-itp-completed]');
        var date = row.querySelector('[data-itp-date]');
        var include = row.querySelector('[data-itp-included]');
        var cost = row.querySelector('[data-itp-cost]');

        if (tick) {
            tick.addEventListener('change', function () {
                touched[code] = true;
                if (tick.checked) {
                    supersedeReopens(rowIndex);
                } else if (reopenQueue.indexOf(code) === -1) {
                    reopenQueue.push(code);
                    setReopenField();
                }
                preview();
            });
        }
        if (date) {
            date.addEventListener('change', function () {
                touched[code] = true;
                if (date.value) {
                    supersedeReopens(rowIndex); // a date means done
                }
                preview();
            });
        }
        if (include) {
            include.addEventListener('change', function () {
                touched[code] = true;
                preview();
            });
        }
        if (cost) {
            // The SO value does not move the chain, but the server checks it, so a bad value
            // is reported here before Save rather than only after it.
            cost.addEventListener('change', function () {
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
        // Never submit again from inside this submit event: browsers ignore a submit started while
        // one is being dispatched, so a save that needs no confirm would silently do nothing. A
        // task (not a microtask, which still runs inside the dispatch) starts after it is over.
        var pending = inFlight;
        window.setTimeout(function () {
            if (pending) {
                pending.then(decide);
            } else {
                decide();
            }
        }, 0);
    });
})();
