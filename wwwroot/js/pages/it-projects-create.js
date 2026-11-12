// New IT project form (docs/ux/it-projects-redesign.md, "Year type"): shows the fields the chosen
// kind uses. New asks for the number and the financial year; CL or CF (a case carried over into
// this year) asks for this year's number, the year it was first listed and, if known, its first
// number. Without this script every field shows and the server uses the ones the kind needs.
// Hidden fields are skipped by the client-side validation, so a hidden year picker never blocks.
(function () {
    'use strict';

    var group = document.querySelector('[data-itp-kind]');
    if (!group) {
        return;
    }

    var form = group.closest('form');
    var options = Array.prototype.slice.call(group.querySelectorAll('[data-itp-kind-option]'));
    var parts = Array.prototype.slice.call(form.querySelectorAll('[data-itp-kind-show]'));

    function chosen() {
        for (var i = 0; i < options.length; i++) {
            if (options[i].checked) {
                return options[i].value;
            }
        }
        return 'New';
    }

    function apply() {
        var carried = chosen() !== 'New';
        parts.forEach(function (part) {
            var forCarried = part.getAttribute('data-itp-kind-show') === 'carried';
            part.hidden = forCarried !== carried;
        });
    }

    options.forEach(function (option) {
        option.addEventListener('change', apply);
    });
    apply();
})();
