// IT projects board (docs/ux/it-projects.md).
//
// Two search modes, so the rows on the page and the search box never disagree:
// - When the page holds the complete scoped board (no server-side search), the box filters the
//   rows in place as the user types, with no round trip. The box is taken out of the GET form so
//   the financial year select (data-auto-submit) and the pills never carry the typed text to the
//   server, which would narrow the rows the box can then search.
// - When the page was already narrowed by the server (a link or a no-script submit carried
//   `q`), filtering the narrowed rows cannot bring the others back, so the box submits the form
//   instead: on Enter, and when it is cleared.
(function () {
    'use strict';

    var board = document.querySelector('[data-itp-board]');
    if (!board) {
        return;
    }

    var input = board.querySelector('[data-itp-search]');
    if (!input || !input.form) {
        return;
    }

    var form = input.form;
    var serverSearched = board.getAttribute('data-itp-server-search') === 'true';

    if (serverSearched) {
        var submitTimer = null;
        input.addEventListener('input', function () {
            window.clearTimeout(submitTimer);
            if (input.value.trim() === '') {
                submitTimer = window.setTimeout(function () { form.submit(); }, 250);
            }
        });
        return;
    }

    // Client mode: the form never sends the search text.
    input.removeAttribute('name');

    var rows = Array.prototype.slice.call(board.querySelectorAll('[data-itp-row]'));
    var bands = Array.prototype.slice.call(board.querySelectorAll('[data-itp-band]'));
    var noMatch = board.querySelector('[data-itp-no-match]');
    if (rows.length === 0) {
        return;
    }

    function projects(count) {
        return count === 1 ? '1 project' : count + ' projects';
    }

    function apply() {
        var needle = input.value.trim().toLowerCase();
        var visible = 0;

        rows.forEach(function (row) {
            var text = row.getAttribute('data-itp-search-text') || '';
            var show = needle === '' || text.indexOf(needle) !== -1;
            row.hidden = !show;
            if (show) {
                visible += 1;
            }
        });

        bands.forEach(function (band) {
            var shown = band.querySelectorAll('[data-itp-row]:not([hidden])').length;
            band.hidden = shown === 0;
            var count = band.querySelector('[data-itp-band-count]');
            if (count) {
                count.textContent = projects(shown);
            }
        });

        if (noMatch) {
            noMatch.hidden = visible > 0;
        }
    }

    var timer = null;
    input.addEventListener('input', function () {
        window.clearTimeout(timer);
        timer = window.setTimeout(apply, 120);
    });

    input.addEventListener('keydown', function (event) {
        if (event.key === 'Enter') {
            event.preventDefault();
            apply();
        }
    });
})();
