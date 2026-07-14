(() => {
    const commandWorkspace = document.querySelector('[data-command-workspace]');
    const workspaceRail = document.querySelector('[data-workspace-rail]');
    const workspaceRailToggle = workspaceRail?.querySelector('[data-workspace-rail-toggle]');
    const desktopNavigation = window.matchMedia('(min-width: 992px)');
    const navigationPreferenceKey = 'prism.commandWorkspace.navigationExpanded';

    const readNavigationPreference = () => {
        if (!desktopNavigation.matches) return false;
        try {
            const stored = window.localStorage.getItem(navigationPreferenceKey);
            return stored === null ? true : stored === 'true';
        } catch {
            return true;
        }
    };

    const saveNavigationPreference = (expanded) => {
        try { window.localStorage.setItem(navigationPreferenceKey, String(expanded)); } catch { /* storage is optional */ }
    };

    const setWorkspaceRailExpanded = (expanded, options = {}) => {
        if (!workspaceRail || !workspaceRailToggle || !commandWorkspace) return;
        const { returnFocus = false, persist = false } = options;
        workspaceRail.classList.toggle('is-expanded', expanded);
        commandWorkspace.classList.toggle('is-nav-expanded', expanded);
        workspaceRailToggle.setAttribute('aria-expanded', String(expanded));
        workspaceRailToggle.setAttribute(
            'aria-label',
            expanded ? 'Collapse workspace navigation' : 'Expand workspace navigation');
        workspaceRailToggle.title = expanded
            ? 'Collapse workspace navigation'
            : 'Expand workspace navigation';
        if (persist) saveNavigationPreference(expanded);
        if (returnFocus) workspaceRailToggle.focus();
    };

    setWorkspaceRailExpanded(readNavigationPreference());

    workspaceRailToggle?.addEventListener('click', () => {
        setWorkspaceRailExpanded(
            !workspaceRail.classList.contains('is-expanded'),
            { persist: desktopNavigation.matches });
    });

    document.addEventListener('pointerdown', (event) => {
        if (desktopNavigation.matches) return;
        if (!workspaceRail?.classList.contains('is-expanded')) return;
        if (workspaceRail.contains(event.target)) return;
        setWorkspaceRailExpanded(false);
    });

    document.addEventListener('keydown', (event) => {
        if (event.key !== 'Escape' || !workspaceRail?.classList.contains('is-expanded')) return;
        if (desktopNavigation.matches) return;
        event.preventDefault();
        setWorkspaceRailExpanded(false, { returnFocus: true });
    });

    workspaceRail?.querySelectorAll('a').forEach((link) => {
        link.addEventListener('click', () => {
            if (!desktopNavigation.matches) setWorkspaceRailExpanded(false);
        });
    });

    desktopNavigation.addEventListener?.('change', (event) => {
        if (!event.matches) setWorkspaceRailExpanded(false);
        else setWorkspaceRailExpanded(readNavigationPreference());
    });

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

    const officerGrid = document.querySelector('[data-officer-grid]');
    const orderForm = document.querySelector('[data-officer-order-form]');
    const orderFeedback = document.querySelector('[data-officer-order-feedback]');

    const saveOfficerOrder = async (officerUserIds, successMessage = 'Order saved') => {
        if (!orderForm) return false;
        const token = orderForm.querySelector('input[name="__RequestVerificationToken"]')?.value;
        const saveUrl = orderForm.dataset.saveUrl;
        if (!token || !saveUrl) return false;
        if (orderFeedback) {
            orderFeedback.classList.remove('is-error');
            orderFeedback.textContent = 'Saving order…';
        }
        try {
            const response = await fetch(saveUrl, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    'X-CSRF-TOKEN': token
                },
                body: JSON.stringify({ officerUserIds })
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            if (orderFeedback) orderFeedback.textContent = successMessage;
            window.setTimeout(() => {
                if (orderFeedback?.textContent === successMessage) orderFeedback.textContent = '';
            }, 2200);
            return true;
        } catch (error) {
            console.error('Unable to save officer card order.', error);
            if (orderFeedback) {
                orderFeedback.classList.add('is-error');
                orderFeedback.textContent = 'Order could not be saved. Please try again.';
            }
            return false;
        }
    };

    const currentOfficerOrder = () => officerGrid
        ? [...officerGrid.querySelectorAll('[data-officer-id]')].map(card => card.dataset.officerId).filter(Boolean)
        : [];

    if (officerGrid && window.Sortable) {
        window.Sortable.create(officerGrid, {
            animation: 180,
            easing: 'cubic-bezier(.2,.8,.2,1)',
            draggable: '.cw-officer-card',
            handle: '.cw-drag-handle',
            ghostClass: 'is-sortable-ghost',
            chosenClass: 'is-sortable-chosen',
            dragClass: 'is-sortable-drag',
            forceFallback: true,
            fallbackOnBody: true,
            fallbackTolerance: 4,
            swapThreshold: .62,
            onEnd: () => saveOfficerOrder(currentOfficerOrder())
        });

        officerGrid.querySelectorAll('.cw-drag-handle').forEach((handle) => {
            handle.addEventListener('keydown', async (event) => {
                if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
                const card = handle.closest('.cw-officer-card');
                if (!card) return;
                const moveBackward = event.key === 'ArrowLeft' || event.key === 'ArrowUp';
                const sibling = moveBackward ? card.previousElementSibling : card.nextElementSibling;
                if (!sibling) return;
                event.preventDefault();
                if (moveBackward) officerGrid.insertBefore(card, sibling);
                else officerGrid.insertBefore(sibling, card);
                handle.focus();
                await saveOfficerOrder(currentOfficerOrder(), 'Order updated');
            });
        });
    }


    const initialiseStageChart = () => {
        const canvas = document.getElementById('command-stage-chart');
        if (!canvas || !window.Chart) return;

        let rows = [];
        try { rows = JSON.parse(canvas.dataset.series || '[]'); } catch { rows = []; }
        const stageNames = [...new Map(rows.map(row => [row.stageCode, row.stageName])).entries()];
        const categories = [...new Set(rows.map(row => row.categoryName))];
        const rootStyles = getComputedStyle(document.documentElement);
        const categoryColors = {
            'DCD Projects': rootStyles.getPropertyValue('--category-dcd').trim() || '#3c68e8',
            'CoE': rootStyles.getPropertyValue('--category-coe').trim() || '#52c653',
            'Other R&D Projects': rootStyles.getPropertyValue('--category-rnd').trim() || '#ef7a00'
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
    };

    const initialiseAdoptionChart = () => {
        const canvas = document.getElementById('command-adoption-chart');
        if (!canvas || !window.Chart) return;

        let rows = [];
        try { rows = JSON.parse(canvas.dataset.series || '[]'); } catch { rows = []; }
        if (!Array.isArray(rows) || rows.length === 0) return;

        const labels = rows.map(row => {
            const date = new Date(`${row.date}T00:00:00`);
            return Number.isNaN(date.getTime())
                ? row.date
                : date.toLocaleDateString('en-IN', { day: '2-digit', month: 'short' });
        });

        new Chart(canvas, {
            data: {
                labels,
                datasets: [
                    {
                        type: 'bar',
                        label: 'Active ERP users',
                        data: rows.map(row => row.usedErpUsers || 0),
                        backgroundColor: 'rgba(49, 95, 214, .80)',
                        borderWidth: 0,
                        borderRadius: 4,
                        maxBarThickness: 38,
                        order: 2
                    },
                    {
                        type: 'line',
                        label: 'Operational contributors',
                        data: rows.map(row => row.operationalContributors || 0),
                        borderColor: 'rgba(47, 126, 82, .92)',
                        backgroundColor: 'rgba(47, 126, 82, .15)',
                        borderWidth: 2,
                        pointRadius: 3,
                        pointHoverRadius: 5,
                        tension: .25,
                        fill: false,
                        order: 1
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: {
                        position: 'top',
                        align: 'end',
                        labels: { usePointStyle: true, boxWidth: 9, boxHeight: 9, padding: 14, font: { size: 11 } }
                    }
                },
                scales: {
                    x: {
                        grid: { display: false },
                        ticks: {
                            color: context => rows[context.index]?.isWorkingDay === false ? '#a0aabc' : '#5f6e83',
                            font: { size: 10 }
                        }
                    },
                    y: {
                        beginAtZero: true,
                        suggestedMax: 5,
                        ticks: { precision: 0, color: '#5f6e83', font: { size: 10 } },
                        grid: { color: 'rgba(103,119,143,.14)' }
                    }
                }
            }
        });
    };

    const initialiseUsagePatternChart = () => {
        const canvas = document.getElementById('command-usage-pattern-chart');
        if (!canvas || !window.Chart) return;

        const readEmbeddedJson = (sourceId) => {
            if (!sourceId) return [];
            const source = document.getElementById(sourceId);
            if (!source) return [];
            try { return JSON.parse(source.textContent || '[]'); } catch { return []; }
        };
        const points = readEmbeddedJson(canvas.dataset.pointsSource);
        const users = readEmbeddedJson(canvas.dataset.usersSource);
        if (!Array.isArray(points) || points.length === 0 || !Array.isArray(users) || users.length === 0) return;

        const userIndex = new Map(users.map((user, index) => [user.userId, index]));
        const toChartPoint = point => ({
            x: Number(point.timestampUtcMilliseconds),
            y: userIndex.get(point.userId),
            meta: point
        });
        const navigation = points.filter(point => point.signal === 'navigation').map(toChartPoint);
        const interactive = points.filter(point => point.signal === 'interactive').map(toChartPoint);
        const operational = points.filter(point => point.signal === 'operational').map(toChartPoint);
        const timestamps = points.map(point => Number(point.timestampUtcMilliseconds)).filter(Number.isFinite);
        const minTimestamp = timestamps.reduce((minimum, value) => Math.min(minimum, value), Number.POSITIVE_INFINITY);
        const maxTimestamp = timestamps.reduce((maximum, value) => Math.max(maximum, value), Number.NEGATIVE_INFINITY);
        const isSingleDay = maxTimestamp - minTimestamp < 24 * 60 * 60 * 1000;
        const dateFormatter = new Intl.DateTimeFormat('en-IN', {
            timeZone: 'Asia/Kolkata',
            day: '2-digit',
            month: 'short'
        });
        const timeFormatter = new Intl.DateTimeFormat('en-IN', {
            timeZone: 'Asia/Kolkata',
            hour: '2-digit',
            minute: '2-digit',
            hour12: false
        });

        const datasets = [
            {
                label: 'Navigation / read-only',
                data: navigation,
                pointStyle: 'circle',
                pointRadius: 3,
                pointHoverRadius: 6,
                backgroundColor: 'rgba(119, 136, 164, .55)',
                borderColor: 'rgba(92, 111, 143, .78)',
                borderWidth: 1
            },
            {
                label: 'Interactive activity',
                data: interactive,
                pointStyle: 'circle',
                pointRadius: 4,
                pointHoverRadius: 7,
                backgroundColor: 'rgba(46, 99, 196, .82)',
                borderColor: 'rgba(35, 78, 158, .96)',
                borderWidth: 1
            },
            {
                label: 'Operational action',
                data: operational,
                pointStyle: 'rectRot',
                pointRadius: 5,
                pointHoverRadius: 8,
                backgroundColor: 'rgba(48, 132, 87, .90)',
                borderColor: 'rgba(35, 101, 65, 1)',
                borderWidth: 1
            }
        ].filter(dataset => dataset.data.length > 0);

        new Chart(canvas, {
            type: 'scatter',
            data: { datasets },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                parsing: false,
                normalized: true,
                animation: points.length > 1000 ? false : { duration: 260 },
                interaction: { mode: 'nearest', intersect: true },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        displayColors: false,
                        callbacks: {
                            title: items => items[0]?.raw?.meta?.displayName || '',
                            label: context => {
                                const point = context.raw?.meta;
                                if (!point) return '';
                                const signalLabel = point.signal === 'operational'
                                    ? 'Operational action recorded'
                                    : point.signal === 'interactive'
                                        ? 'Interactive activity recorded'
                                        : 'Navigation or read-only activity';
                                return [point.timestampIstLabel, signalLabel];
                            },
                            afterLabel: context => {
                                const point = context.raw?.meta;
                                if (!point) return '';
                                const details = [];
                                if (Array.isArray(point.modules) && point.modules.length > 0) {
                                    details.push(`Modules: ${point.modules.join(', ')}`);
                                }
                                if ((point.operationalActionCount || 0) > 0) {
                                    details.push(`Operational actions: ${point.operationalActionCount}`);
                                }
                                return details;
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        type: 'linear',
                        min: minTimestamp - (isSingleDay ? 20 * 60 * 1000 : 60 * 60 * 1000),
                        max: maxTimestamp + (isSingleDay ? 20 * 60 * 1000 : 60 * 60 * 1000),
                        grid: { color: 'rgba(103,119,143,.12)' },
                        ticks: {
                            maxTicksLimit: isSingleDay ? 10 : 12,
                            color: '#687890',
                            font: { size: 10 },
                            callback: value => {
                                const date = new Date(Number(value));
                                if (Number.isNaN(date.getTime())) return '';
                                return isSingleDay
                                    ? timeFormatter.format(date)
                                    : [dateFormatter.format(date), timeFormatter.format(date)];
                            }
                        },
                        title: { display: true, text: 'Date and time (IST)', color: '#52647d', font: { size: 11, weight: '600' } }
                    },
                    y: {
                        type: 'linear',
                        min: -.5,
                        max: Math.max(.5, users.length - .5),
                        reverse: true,
                        grid: { color: 'rgba(103,119,143,.10)' },
                        ticks: {
                            stepSize: 1,
                            autoSkip: false,
                            color: '#42546d',
                            font: { size: 10, weight: '600' },
                            callback: value => Number.isInteger(Number(value))
                                ? users[Number(value)]?.displayName || ''
                                : ''
                        }
                    }
                }
            }
        });
    };

    initialiseStageChart();
    initialiseAdoptionChart();
    initialiseUsagePatternChart();
})();
