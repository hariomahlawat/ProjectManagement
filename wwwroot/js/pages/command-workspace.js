(() => {
    const submitForm = (form) => {
        if (!form || form.classList.contains('is-submitting')) return;
        form.classList.add('is-submitting');
        const status = form.querySelector('[data-cw-filter-status]');
        if (status) status.textContent = 'Updating…';
        form.requestSubmit();
    };

    document.querySelectorAll('[data-cw-auto-filter]').forEach((form) => {
        let searchTimer = 0;
        const search = form.querySelector('input[type="search"]');
        search?.addEventListener('input', () => {
            window.clearTimeout(searchTimer);
            searchTimer = window.setTimeout(() => submitForm(form), 380);
        });

        form.querySelectorAll('select').forEach((select) => {
            select.addEventListener('change', () => submitForm(form));
        });

        form.querySelectorAll('input[type="checkbox"]').forEach((checkbox) => {
            if (checkbox.type === 'hidden') return;
            checkbox.addEventListener('change', () => {
                const delay = checkbox.name === 'ParentCategoryIds' ? 320 : 0;
                window.clearTimeout(searchTimer);
                searchTimer = window.setTimeout(() => submitForm(form), delay);
            });
        });
    });

    const trigger = document.querySelector('[data-cw-filter-trigger]');
    const popover = document.querySelector('[data-cw-filter-popover]');
    if (trigger && popover) {
        trigger.addEventListener('click', () => {
            const willOpen = popover.hidden;
            popover.hidden = !willOpen;
            trigger.setAttribute('aria-expanded', String(willOpen));
        });
        document.addEventListener('click', (event) => {
            if (!popover.hidden && !popover.contains(event.target) && !trigger.contains(event.target)) {
                popover.hidden = true;
                trigger.setAttribute('aria-expanded', 'false');
            }
        });
        document.addEventListener('keydown', (event) => {
            if (event.key === 'Escape' && !popover.hidden) {
                popover.hidden = true;
                trigger.setAttribute('aria-expanded', 'false');
                trigger.focus();
            }
        });
    }

    const controlButtonsFor = (board) => [...document.querySelectorAll(`[data-scroll-target="${CSS.escape(board.id)}"]`)];

    const updateBoardState = (board) => {
        const shell = board.closest('.cw-stage-board-shell, .cw-officer-board-shell');
        const maxScrollLeft = Math.max(0, board.scrollWidth - board.clientWidth);
        const canLeft = board.scrollLeft > 4;
        const canRight = board.scrollLeft < maxScrollLeft - 4;
        shell?.classList.toggle('can-scroll-left', canLeft);
        shell?.classList.toggle('can-scroll-right', canRight);
        controlButtonsFor(board).forEach((button) => {
            const isPrevious = button.dataset.scrollDirection === 'previous';
            button.disabled = isPrevious ? !canLeft : !canRight;
        });
    };

    const enablePressDrag = (board) => {
        let pointerId = null;
        let startX = 0;
        let startY = 0;
        let startScrollLeft = 0;
        let dragging = false;
        let suppressClick = false;
        const threshold = 6;

        board.addEventListener('pointerdown', (event) => {
            if (event.pointerType === 'mouse' && event.button !== 0) return;
            pointerId = event.pointerId;
            startX = event.clientX;
            startY = event.clientY;
            startScrollLeft = board.scrollLeft;
            dragging = false;
            suppressClick = false;
        });

        board.addEventListener('pointermove', (event) => {
            if (pointerId !== event.pointerId) return;
            const dx = event.clientX - startX;
            const dy = event.clientY - startY;
            if (!dragging) {
                if (Math.abs(dx) < threshold || Math.abs(dx) <= Math.abs(dy)) return;
                dragging = true;
                suppressClick = true;
                board.classList.add('is-dragging');
                try { board.setPointerCapture(pointerId); } catch { /* no-op */ }
            }
            event.preventDefault();
            board.scrollLeft = startScrollLeft - dx;
        }, { passive: false });

        const finish = (event) => {
            if (pointerId !== event.pointerId) return;
            if (dragging) {
                event.preventDefault();
                board.classList.remove('is-dragging');
                try { board.releasePointerCapture(pointerId); } catch { /* no-op */ }
                window.setTimeout(() => { suppressClick = false; }, 0);
            }
            dragging = false;
            pointerId = null;
            updateBoardState(board);
        };

        board.addEventListener('pointerup', finish);
        board.addEventListener('pointercancel', finish);
        board.addEventListener('lostpointercapture', () => {
            board.classList.remove('is-dragging');
            dragging = false;
            pointerId = null;
        });
        board.addEventListener('dragstart', (event) => event.preventDefault());
        board.addEventListener('click', (event) => {
            if (!suppressClick) return;
            event.preventDefault();
            event.stopPropagation();
        }, true);
    };

    document.querySelectorAll('[data-horizontal-board]').forEach((board) => {
        enablePressDrag(board);
        updateBoardState(board);
        board.addEventListener('scroll', () => updateBoardState(board), { passive: true });
        window.addEventListener('resize', () => updateBoardState(board));
        board.addEventListener('wheel', (event) => {
            if (!event.shiftKey || Math.abs(event.deltaY) <= Math.abs(event.deltaX)) return;
            event.preventDefault();
            board.scrollBy({ left: event.deltaY, behavior: 'auto' });
        }, { passive: false });
    });

    document.querySelectorAll('[data-scroll-target]').forEach((button) => {
        const board = document.getElementById(button.dataset.scrollTarget);
        if (!board) return;
        button.addEventListener('click', () => {
            const direction = button.dataset.scrollDirection === 'previous' ? -1 : 1;
            board.scrollBy({ left: Math.max(280, board.clientWidth * .78) * direction, behavior: 'smooth' });
        });
    });

    document.querySelectorAll('[data-expand-projects]').forEach((button) => {
        button.setAttribute('aria-expanded', 'false');
        button.addEventListener('click', () => {
            const container = button.closest('.cw-stage-column__body, details');
            if (!container) return;
            const items = container.querySelectorAll('.cw-collapsible-item');
            const expanded = button.getAttribute('aria-expanded') === 'true';
            items.forEach((item) => { item.hidden = expanded; });
            button.setAttribute('aria-expanded', String(!expanded));
            button.textContent = expanded ? button.dataset.collapsedLabel : button.dataset.expandedLabel;
        });
    });

    const canvas = document.getElementById('command-stage-chart');
    if (!canvas || !window.Chart) return;

    let rows = [];
    try { rows = JSON.parse(canvas.dataset.series || '[]'); } catch { rows = []; }
    const stageNames = [...new Map(rows.map(row => [row.stageCode, row.stageName])).entries()];
    const categories = [...new Set(rows.map(row => row.categoryName))];
    const categoryColors = {
        'DCD Projects': '#3c68e8',
        'Other R&D Projects': '#ef7a00',
        'CoE': '#52c653'
    };
    const fallbackPalette = ['#8f4cf0', '#15a6a6', '#d94b68'];
    const datasets = categories.map((category, index) => ({
        label: category,
        data: stageNames.map(([code]) => rows.find(row => row.stageCode === code && row.categoryName === category)?.count || 0),
        backgroundColor: categoryColors[category] || fallbackPalette[index % fallbackPalette.length],
        borderWidth: 0,
        borderRadius: 3,
        maxBarThickness: 44
    }));

    const chart = new Chart(canvas, {
        type: 'bar',
        data: { labels: stageNames.map(([code]) => code === 'UNASSIGNED' ? 'Unassigned' : code), datasets },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: { mode: 'index', intersect: false },
            onClick: (_event, elements) => {
                if (!elements.length) return;
                const stageCode = stageNames[elements[0].index]?.[0];
                const column = document.querySelector(`.cw-stage-column[data-stage-code="${CSS.escape(stageCode)}"]`);
                if (!column) return;
                column.scrollIntoView({ behavior: 'smooth', inline: 'center', block: 'nearest' });
                column.classList.add('is-highlighted');
                window.setTimeout(() => column.classList.remove('is-highlighted'), 1400);
            },
            plugins: {
                legend: { position: 'top', labels: { usePointStyle: true, pointStyle: 'rectRounded', boxWidth: 9, boxHeight: 9, padding: 13, font: { size: 11 } } },
                tooltip: { callbacks: { title: items => stageNames[items[0].dataIndex]?.[1] || items[0].label } }
            },
            scales: {
                x: { stacked: true, grid: { display: false }, ticks: { color: '#5f6e83', font: { size: 11 } } },
                y: { stacked: true, beginAtZero: true, ticks: { precision: 0, color: '#5f6e83', font: { size: 11 } }, grid: { color: 'rgba(103,119,143,.15)' } }
            }
        }
    });

    document.querySelector('[data-chart-download]')?.addEventListener('click', () => {
        const link = document.createElement('a');
        link.download = 'ongoing-projects-stage-distribution.png';
        link.href = chart.toBase64Image('image/png', 1);
        link.click();
    });
})();
