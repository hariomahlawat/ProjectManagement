// IT project page (docs/ux/it-projects.md, "The project page"): opens the drawer named in the
// address, and keeps the stage editor's ticks consistent with the stage chain as the user works.
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
    var Chain = window.ItProjectStageChain;
    if (!form || !Chain) {
        return;
    }

    var rows = Array.prototype.slice.call(form.querySelectorAll('[data-itp-stage]'));
    var note = form.querySelector('[data-itp-stage-note]');

    function read() {
        return rows.map(function (row) {
            var tick = row.querySelector('[data-itp-completed]');
            var date = row.querySelector('[data-itp-date]');
            var include = row.querySelector('[data-itp-included]');
            var optional = row.getAttribute('data-itp-optional') === 'true';
            return {
                code: row.getAttribute('data-itp-code'),
                label: row.getAttribute('data-itp-label'),
                completed: !!(tick && tick.checked),
                date: date && date.value ? date.value : null,
                skipped: optional && !!include && !include.checked
            };
        });
    }

    function write(states) {
        states.forEach(function (state, i) {
            var row = rows[i];
            var tick = row.querySelector('[data-itp-completed]');
            var date = row.querySelector('[data-itp-date]');
            if (tick) {
                tick.checked = state.completed;
                tick.disabled = state.skipped;
            }
            if (date) {
                if (!state.completed && date.value) {
                    date.value = '';
                }
                date.disabled = state.skipped;
            }
            row.classList.toggle('itp-stage-row--skipped', state.skipped);
            row.classList.toggle('itp-stage-row--done', state.completed);
        });
    }

    var saved = Chain.chain(read());
    var touched = {};

    function refresh(states) {
        write(states);
        if (note) {
            var text = Chain.summary(Chain.cascade(saved, states, touched), states);
            note.hidden = !text;
            note.textContent = text || '';
        }
    }

    rows.forEach(function (row, index) {
        var tick = row.querySelector('[data-itp-completed]');
        var date = row.querySelector('[data-itp-date]');
        var include = row.querySelector('[data-itp-included]');
        var code = row.getAttribute('data-itp-code');

        if (tick) {
            tick.addEventListener('change', function () {
                touched[code] = true;
                refresh(Chain.tick(read(), index, tick.checked));
            });
        }
        if (date) {
            date.addEventListener('change', function () {
                touched[code] = true;
                refresh(Chain.setDate(read(), index, date.value));
            });
        }
        if (include) {
            include.addEventListener('change', function () {
                touched[code] = true;
                refresh(Chain.setSkipped(read(), index, !include.checked));
            });
        }
    });

    refresh(Chain.chain(read()));

    var confirmed = false;
    form.addEventListener('submit', function (event) {
        if (confirmed) {
            return;
        }
        var current = Chain.chain(read());
        var cascade = Chain.cascade(saved, current, touched);
        var text = Chain.summary(cascade, current);
        if (!text || !window.PrismConfirm) {
            return;
        }

        event.preventDefault();
        var reopening = cascade.reopened.length > 0;
        window.PrismConfirm.show({
            tone: reopening ? 'warning' : 'primary',
            title: reopening ? 'Reopen earlier progress?' : 'Complete the earlier stages too?',
            message: text,
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
