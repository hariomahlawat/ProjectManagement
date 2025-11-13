// wwwroot/js/pages/search.js
(function () {
    function wire(form) {
        if (!form) return;
        const input = form.querySelector('input[name="q"]');
        const clearBtn = form.querySelector('[data-clear]');
        if (!input || !clearBtn) return;

        // show/hide the clear button like Google
        const update = () => {
            const has = input.value && input.value.trim().length > 0;
            clearBtn.style.visibility = has ? 'visible' : 'hidden';
        };

        clearBtn.addEventListener('click', (e) => {
            e.preventDefault();
            input.value = '';
            input.focus();
            update();
        });

        input.addEventListener('input', update);
        update(); // initialize on load
    }

    document.addEventListener('DOMContentLoaded', () => {
        wire(document.querySelector('form.pm-gs-search'));      // hero form
        wire(document.querySelector('form.pm-gs-top__form'));   // results top bar form
    });
})();
