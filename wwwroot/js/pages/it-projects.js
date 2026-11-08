// IT projects board (docs/ux/it-projects.md): filters the rows already on the page as the user
// types, so the search box never needs a round trip. The GET form stays the no-script fallback,
// and the financial year select submits it through data-auto-submit.
(function () {
    'use strict';

    var board = document.querySelector('[data-itp-board]');
    if (!board) {
        return;
    }

    var input = board.querySelector('[data-itp-search]');
    var rows = Array.prototype.slice.call(board.querySelectorAll('[data-itp-row]'));
    var bands = Array.prototype.slice.call(board.querySelectorAll('[data-itp-band]'));
    var noMatch = board.querySelector('[data-itp-no-match]');
    if (!input || rows.length === 0) {
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

    // Enter in the search box would submit the form and reload with the same result; keep the
    // in-place filter instead.
    input.addEventListener('keydown', function (event) {
        if (event.key === 'Enter') {
            event.preventDefault();
            apply();
        }
    });

    if (input.value) {
        apply();
    }
})();
