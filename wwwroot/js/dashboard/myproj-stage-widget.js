// SECTION: My Projects + Stage PDC interactive linking
(function () {
    document.addEventListener('DOMContentLoaded', () => {
        const widget = document.querySelector('.myproj-stage-widget');
        if (!widget) {
            return;
        }

        const projectCards = widget.querySelectorAll('[data-project-id].dashboard-project-card');
        const pdcRows = widget.querySelectorAll('.myproj-stage-widget__pdc-row[data-project-id]');

        if (!projectCards.length || !pdcRows.length) {
            return;
        }

        const pdcById = new Map();
        pdcRows.forEach((row) => {
            pdcById.set(row.dataset.projectId, row);
        });

        const cardById = new Map();
        projectCards.forEach((card) => {
            cardById.set(card.dataset.projectId, card);
        });

        function setActive(projectId, options = { scrollToCard: false }) {
            pdcRows.forEach((row) => {
                row.classList.toggle('is-active', row.dataset.projectId === projectId);
            });

            projectCards.forEach((card) => {
                card.classList.toggle('is-active', card.dataset.projectId === projectId);
            });

            if (options.scrollToCard) {
                const card = cardById.get(projectId);
                if (card) {
                    card.scrollIntoView({ behavior: 'smooth', inline: 'center', block: 'nearest' });
                }
            }
        }

        function activateFromRow(row, shouldScroll = false) {
            const id = row.dataset.projectId;
            if (!id) {
                return;
            }

            setActive(id, { scrollToCard: shouldScroll });
        }

        projectCards.forEach((card) => {
            const id = card.dataset.projectId;
            card.addEventListener('mouseenter', () => setActive(id));
            card.addEventListener('focus', () => setActive(id));
        });

        pdcRows.forEach((row) => {
            row.addEventListener('click', () => activateFromRow(row, true));
            row.addEventListener('mouseenter', () => activateFromRow(row));
            row.addEventListener('focus', () => activateFromRow(row));
            row.addEventListener('keydown', (event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    activateFromRow(row, true);
                }
            });
        });

        const initialActiveId = pdcRows[0]?.dataset.projectId;
        if (initialActiveId) {
            setActive(initialActiveId);
        }
    });
})();
