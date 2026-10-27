(() => {
    const commandWorkspace = document.querySelector('[data-command-workspace]');
    const workspaceRail = document.querySelector('[data-workspace-rail]');
    const workspaceRailToggle = workspaceRail?.querySelector('[data-workspace-rail-toggle]');
    const desktopNavigation = window.matchMedia('(min-width: 992px)');
    const navigationPreferenceKey = 'prism.workspace.navigationExpanded';

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

        const intervalMinutes = Math.max(1, Number.parseInt(canvas.dataset.intervalMinutes || '15', 10) || 15);
        const minuteMs = 60 * 1000;
        const hourMs = 60 * minuteMs;
        const dayMs = 24 * hourMs;
        const intervalMs = intervalMinutes * minuteMs;
        const istOffsetMs = 330 * minuteMs;
        const userIndex = new Map(users.map((user, index) => [user.userId, index]));
        const alignDownIst = (value, step) => Math.floor((value + istOffsetMs) / step) * step - istOffsetMs;
        const alignUpIst = (value, step) => Math.ceil((value + istOffsetMs) / step) * step - istOffsetMs;
        const localDayKey = value => Math.floor((value + istOffsetMs) / dayMs);

        const toChartPoint = point => {
            const intervalStart = Number(point.timestampUtcMilliseconds);
            return {
                x: intervalStart + (intervalMs / 2),
                xStart: intervalStart,
                xEnd: intervalStart + intervalMs,
                y: userIndex.get(point.userId),
                meta: point
            };
        };

        const datasets = [
            {
                signal: 'navigation',
                label: 'Navigation / read-only interval',
                data: points.filter(point => point.signal === 'navigation').map(toChartPoint),
                segmentColor: 'rgba(112, 130, 159, .70)',
                segmentBorderColor: 'rgba(80, 101, 136, .90)',
                segmentWidth: 6
            },
            {
                signal: 'interactive',
                label: 'Interactive interval',
                data: points.filter(point => point.signal === 'interactive').map(toChartPoint),
                segmentColor: 'rgba(46, 99, 196, .88)',
                segmentBorderColor: 'rgba(35, 78, 158, 1)',
                segmentWidth: 7
            },
            {
                signal: 'operational',
                label: 'Operational action recorded',
                data: points.filter(point => point.signal === 'operational').map(toChartPoint),
                segmentColor: 'rgba(48, 132, 87, .92)',
                segmentBorderColor: 'rgba(35, 101, 65, 1)',
                segmentWidth: 7
            }
        ]
            .filter(dataset => dataset.data.length > 0)
            .map(dataset => ({
                ...dataset,
                pointRadius: 0,
                pointHoverRadius: 0,
                pointHitRadius: 12,
                borderWidth: 0,
                backgroundColor: 'rgba(0,0,0,0)'
            }));

        const intervalTimelinePlugin = {
            id: 'commandUsageIntervalTimeline',
            afterDatasetsDraw(chart) {
                const { ctx, chartArea, scales } = chart;
                if (!chartArea || !scales?.x || !scales?.y) return;

                ctx.save();
                ctx.beginPath();
                ctx.rect(chartArea.left, chartArea.top, chartArea.right - chartArea.left, chartArea.bottom - chartArea.top);
                ctx.clip();

                chart.data.datasets.forEach((dataset, datasetIndex) => {
                    const meta = chart.getDatasetMeta(datasetIndex);
                    if (meta.hidden) return;

                    dataset.data.forEach(point => {
                        const startX = scales.x.getPixelForValue(point.xStart);
                        const endX = scales.x.getPixelForValue(point.xEnd);
                        const rowY = scales.y.getPixelForValue(point.y);
                        if (![startX, endX, rowY].every(Number.isFinite)) return;

                        ctx.lineCap = 'round';
                        ctx.lineWidth = dataset.segmentWidth + 2;
                        ctx.strokeStyle = dataset.segmentBorderColor;
                        ctx.beginPath();
                        ctx.moveTo(startX, rowY);
                        ctx.lineTo(Math.max(startX + 8, endX), rowY);
                        ctx.stroke();

                        ctx.lineWidth = dataset.segmentWidth;
                        ctx.strokeStyle = dataset.segmentColor;
                        ctx.beginPath();
                        ctx.moveTo(startX, rowY);
                        ctx.lineTo(Math.max(startX + 8, endX), rowY);
                        ctx.stroke();

                        if (dataset.signal === 'operational') {
                            const markerX = startX + ((Math.max(startX + 8, endX) - startX) / 2);
                            const markerSize = 5;
                            ctx.fillStyle = '#f7fff9';
                            ctx.strokeStyle = dataset.segmentBorderColor;
                            ctx.lineWidth = 1.5;
                            ctx.beginPath();
                            ctx.moveTo(markerX, rowY - markerSize);
                            ctx.lineTo(markerX + markerSize, rowY);
                            ctx.lineTo(markerX, rowY + markerSize);
                            ctx.lineTo(markerX - markerSize, rowY);
                            ctx.closePath();
                            ctx.fill();
                            ctx.stroke();
                        }
                    });
                });

                ctx.restore();
            }
        };

        const minTimestamp = datasets.reduce(
            (minimum, dataset) => dataset.data.reduce(
                (datasetMinimum, point) => Math.min(datasetMinimum, point.xStart),
                minimum),
            Number.POSITIVE_INFINITY);
        const maxTimestamp = datasets.reduce(
            (maximum, dataset) => dataset.data.reduce(
                (datasetMaximum, point) => Math.max(datasetMaximum, point.xEnd),
                maximum),
            Number.NEGATIVE_INFINITY);
        const isSingleLocalDay = localDayKey(minTimestamp) === localDayKey(maxTimestamp - 1);
        const visibleSpan = Math.max(intervalMs, maxTimestamp - minTimestamp);
        const tickStep = isSingleLocalDay
            ? intervalMs
            : visibleSpan <= 3 * dayMs
                ? 3 * hourMs
                : visibleSpan <= 7 * dayMs
                    ? 6 * hourMs
                    : visibleSpan <= 14 * dayMs
                        ? 12 * hourMs
                        : dayMs;
        const scaleMin = alignDownIst(minTimestamp - tickStep, tickStep);
        const scaleMax = alignUpIst(maxTimestamp + tickStep, tickStep);

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

        new Chart(canvas, {
            type: 'scatter',
            data: { datasets },
            plugins: [intervalTimelinePlugin],
            options: {
                responsive: true,
                maintainAspectRatio: false,
                parsing: false,
                normalized: true,
                animation: points.length > 1000 ? false : { duration: 220 },
                interaction: { mode: 'nearest', intersect: true },
                layout: { padding: { top: 4, right: 8, bottom: 0, left: 2 } },
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
                                        ? 'Interactive interval'
                                        : 'Navigation or read-only interval';
                                return [point.timestampIstLabel, signalLabel];
                            },
                            afterLabel: context => {
                                const point = context.raw?.meta;
                                if (!point) return '';
                                const details = [];
                                if (Array.isArray(point.modules) && point.modules.length > 0) {
                                    details.push(`Modules visited: ${point.modules.join(', ')}`);
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
                        min: scaleMin,
                        max: scaleMax,
                        bounds: 'ticks',
                        grid: { color: 'rgba(103,119,143,.12)' },
                        ticks: {
                            stepSize: tickStep,
                            autoSkip: true,
                            autoSkipPadding: 14,
                            maxTicksLimit: isSingleLocalDay ? 12 : 14,
                            color: '#687890',
                            font: { size: 10 },
                            callback: value => {
                                const date = new Date(Number(value));
                                if (Number.isNaN(date.getTime())) return '';
                                return isSingleLocalDay
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
                            padding: 8,
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

    initialiseAdoptionChart();
    initialiseUsagePatternChart();
})();
