(function () {
    function toggleSection(button, container) {
        var expanded = button.getAttribute('aria-expanded') === 'true';
        if (expanded) {
            container.classList.add('d-none');
            button.setAttribute('aria-expanded', 'false');
            button.querySelector('[aria-hidden="true"]').textContent = '▸';
        } else {
            container.classList.remove('d-none');
            button.setAttribute('aria-expanded', 'true');
            button.querySelector('[aria-hidden="true"]').textContent = '▾';
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-behavior="category-tree"]').forEach(function (tree) {
            tree.addEventListener('click', function (event) {
                var button = event.target.closest('.toggle-children');
                if (!button || !tree.contains(button)) {
                    return;
                }

                var selector = button.getAttribute('data-target');
                if (!selector) {
                    return;
                }

                var container = tree.querySelector(selector);
                if (!container) {
                    return;
                }

                toggleSection(button, container);
            });
        });
    });
})();
