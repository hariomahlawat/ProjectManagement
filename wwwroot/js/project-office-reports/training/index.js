// wwwroot/js/project-office-reports/training/index.js

import { initAsyncMultiselect } from '../../widgets/async-multiselect.js';

// ================================================================
// bootstrap modal helpers
// ================================================================
function ensureBootstrapModal() {
    if (typeof window === 'undefined') return null;
    const { bootstrap } = window;
    return bootstrap && typeof bootstrap.Modal === 'function' ? bootstrap : null;
}

function showModalElement(modalEl) {
    if (!modalEl) return;
    const bs = ensureBootstrapModal();
    if (bs) {
        bs.Modal.getOrCreateInstance(modalEl).show();
    } else {
        modalEl.classList.add('show');
        modalEl.style.display = 'block';
        modalEl.removeAttribute('aria-hidden');
        modalEl.setAttribute('aria-modal', 'true');
    }
}

function hideModalElement(modalEl) {
    if (!modalEl) return;
    const bs = ensureBootstrapModal();
    if (bs) {
        bs.Modal.getOrCreateInstance(modalEl).hide();
    } else {
        modalEl.classList.remove('show');
        modalEl.style.removeProperty('display');
        modalEl.setAttribute('aria-hidden', 'true');
        modalEl.removeAttribute('aria-modal');
    }
}

// ================================================================
// searchable project picker used by the export modal
// ================================================================
function normalizePickerText(value) {
    return (value || '')
        .toLocaleLowerCase()
        .normalize('NFKD')
        .replace(/[\u0300-\u036f]/g, '')
        .trim();
}

function initTrainingExportProjectPicker(picker) {
    if (!picker || picker.dataset.trainingExportProjectPickerReady === 'true') return;
    picker.dataset.trainingExportProjectPickerReady = 'true';

    const search = picker.querySelector('[data-training-export-project-search]');
    const hidden = picker.querySelector('[data-training-export-project-id]');
    const panel = picker.querySelector('[data-training-export-project-options]');
    const clear = picker.querySelector('[data-training-export-project-clear]');
    const empty = picker.querySelector('[data-training-export-project-empty]');
    const status = picker.querySelector('[data-training-export-project-status]');
    const options = Array.from(picker.querySelectorAll('[data-training-export-project-option]'));
    if (!search || !hidden || !panel || options.length === 0) return;

    let visibleOptions = [];
    let activeIndex = -1;
    search.dataset.selectedLabel = search.value.trim();

    const close = () => {
        panel.classList.add('d-none');
        search.setAttribute('aria-expanded', 'false');
        search.removeAttribute('aria-activedescendant');
        activeIndex = -1;
        options.forEach((option) => option.classList.remove('is-active'));
    };

    const setActive = (index) => {
        if (visibleOptions.length === 0) {
            activeIndex = -1;
            return;
        }

        activeIndex = Math.max(0, Math.min(index, visibleOptions.length - 1));
        visibleOptions.forEach((option, optionIndex) => {
            const active = optionIndex === activeIndex;
            option.classList.toggle('is-active', active);
            option.setAttribute('aria-selected', active ? 'true' : 'false');
            if (active) {
                if (!option.id) option.id = `training-export-project-option-${optionIndex}`;
                search.setAttribute('aria-activedescendant', option.id);
                option.scrollIntoView({ block: 'nearest' });
            }
        });
    };

    const select = (option) => {
        const id = option.dataset.projectId || '';
        const label = option.dataset.projectLabel || '';
        hidden.value = id;
        hidden.dispatchEvent(new Event('change', { bubbles: true }));
        search.value = id ? label : '';
        search.dataset.selectedLabel = search.value;
        clear?.classList.toggle('d-none', !id);
        options.forEach((candidate) => candidate.setAttribute(
            'aria-selected',
            candidate === option ? 'true' : 'false'));
        if (status) status.textContent = id ? `${label} selected.` : 'All projects selected.';
        close();
    };

    const render = () => {
        const query = normalizePickerText(search.value);
        visibleOptions = options.filter((option) => {
            const searchable = normalizePickerText(option.dataset.projectSearch || option.dataset.projectLabel);
            const visible = !query || searchable.includes(query);
            option.classList.toggle('d-none', !visible);
            option.classList.remove('is-active');
            option.setAttribute('aria-selected', 'false');
            return visible;
        });

        empty?.classList.toggle('d-none', visibleOptions.length > 0);
        panel.classList.remove('d-none');
        search.setAttribute('aria-expanded', 'true');
        activeIndex = -1;
        if (status) {
            status.textContent = visibleOptions.length === 0
                ? 'No matching project found.'
                : `${visibleOptions.length} matching ${visibleOptions.length === 1 ? 'project' : 'projects'}.`;
        }
    };

    search.addEventListener('input', () => {
        if (search.value.trim() !== (search.dataset.selectedLabel || '')) {
            hidden.value = '';
            clear?.classList.add('d-none');
        }
        render();
    });
    search.addEventListener('focus', render);
    search.addEventListener('keydown', (event) => {
        if (event.key === 'ArrowDown') {
            event.preventDefault();
            if (panel.classList.contains('d-none')) render();
            setActive(activeIndex + 1);
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            if (panel.classList.contains('d-none')) render();
            setActive(activeIndex <= 0 ? visibleOptions.length - 1 : activeIndex - 1);
        } else if (event.key === 'Enter' && activeIndex >= 0) {
            event.preventDefault();
            select(visibleOptions[activeIndex]);
        } else if (event.key === 'Escape') {
            close();
        }
    });

    options.forEach((option) => {
        option.addEventListener('mousedown', (event) => event.preventDefault());
        option.addEventListener('click', () => select(option));
    });

    clear?.addEventListener('click', () => {
        hidden.value = '';
        hidden.dispatchEvent(new Event('change', { bubbles: true }));
        search.value = '';
        search.dataset.selectedLabel = '';
        clear.classList.add('d-none');
        if (status) status.textContent = 'Project selection cleared.';
        search.focus();
        render();
    });

    document.addEventListener('pointerdown', (event) => {
        if (!picker.contains(event.target)) close();
    });
    picker.closest('.modal')?.addEventListener('hidden.bs.modal', close);
}

function validateTrainingExportProjectPicker(form) {
    const search = form.querySelector('[data-training-export-project-search]');
    const hidden = form.querySelector('[data-training-export-project-id]');
    if (!search || !hidden) return true;

    if (search.value.trim() && !hidden.value) {
        showExportErrors(form, ['Select a project from the matching list, or clear the Project field to export all projects.']);
        search.focus();
        return false;
    }

    return true;
}

// ================================================================
// reliable training export download lifecycle
// ================================================================
function setExportButtonBusy(button, busy) {
    if (!button) return;

    if (!button.dataset.trainingOriginalHtml) {
        button.dataset.trainingOriginalHtml = button.innerHTML;
    }

    button.disabled = busy;

    if (!busy) {
        button.innerHTML = button.dataset.trainingOriginalHtml;
        return;
    }

    const spinner = document.createElement('span');
    spinner.className = 'spinner-border spinner-border-sm me-2';
    spinner.setAttribute('role', 'status');
    spinner.setAttribute('aria-hidden', 'true');

    const label = document.createElement('span');
    label.textContent = button.getAttribute('data-training-busy-label') || 'Preparing…';

    button.replaceChildren(spinner, label);
}

function clearExportErrors(form) {
    const host = form.querySelector('[data-training-export-errors]');
    if (!host) return;

    host.replaceChildren();
    host.classList.add('d-none');
}

function showExportErrors(form, errors) {
    const host = form.querySelector('[data-training-export-errors]');
    if (!host) return;

    const normalized = Array.isArray(errors) && errors.length > 0
        ? errors.filter((error) => typeof error === 'string' && error.trim().length > 0)
        : ['The export could not be generated. Please try again.'];

    host.replaceChildren();
    if (normalized.length === 1) {
        host.textContent = normalized[0];
    } else {
        const list = document.createElement('ul');
        list.className = 'mb-0 ps-3';
        normalized.forEach((message) => {
            const item = document.createElement('li');
            item.textContent = message;
            list.appendChild(item);
        });
        host.appendChild(list);
    }

    host.classList.remove('d-none');
    host.focus({ preventScroll: false });
}

function parseDownloadFileName(contentDisposition) {
    if (!contentDisposition) return 'training-tracker.xlsx';

    const encodedMatch = contentDisposition.match(/filename\*=UTF-8''([^;]+)/i);
    if (encodedMatch?.[1]) {
        try {
            return decodeURIComponent(encodedMatch[1].trim().replace(/^"|"$/g, ''));
        } catch {
            // Fall through to the standard filename form.
        }
    }

    const standardMatch = contentDisposition.match(/filename=(?:"([^"]+)"|([^;]+))/i);
    return (standardMatch?.[1] || standardMatch?.[2] || 'training-tracker.xlsx').trim();
}

async function readExportErrors(response) {
    const contentType = response.headers.get('content-type') || '';
    if (contentType.includes('application/json')) {
        try {
            const payload = await response.json();
            if (Array.isArray(payload?.errors)) return payload.errors;
            if (typeof payload?.error === 'string') return [payload.error];
            if (typeof payload?.message === 'string') return [payload.message];
        } catch {
            return ['The server returned an unreadable export error.'];
        }
    }

    if (response.status === 401) {
        return ['Your session has expired. Sign in again and retry the export.'];
    }

    if (response.status === 403) {
        return ['Training exports are currently unavailable for this request.'];
    }

    return [`The export could not be generated (HTTP ${response.status}).`];
}

function triggerBlobDownload(blob, fileName) {
    const objectUrl = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = objectUrl;
    link.download = fileName;
    link.style.display = 'none';
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 30000);
}

function syncRosterScope(form) {
    const includeRoster = form.querySelector('[data-training-export-include-roster]');
    const category = form.querySelector('[data-training-export-category]');
    const scope = form.querySelector('[data-training-export-roster-scope]');
    const help = form.querySelector('[data-training-export-roster-scope-help]');
    if (!includeRoster || !scope) return;

    const selectedOnlyOption = scope.querySelector('option[value="1"]');
    const rosterEnabled = includeRoster.checked;
    const categorySelected = Boolean(category?.value);

    scope.disabled = !rosterEnabled;
    if (selectedOnlyOption) selectedOnlyOption.disabled = !categorySelected;

    if (!rosterEnabled || (!categorySelected && scope.value === '1')) {
        scope.value = '0';
    }

    if (help) {
        help.textContent = !rosterEnabled
            ? 'Enable roster details to choose which trainee rows are included.'
            : categorySelected
                ? 'Event totals remain complete; this option only controls rows on the Roster sheet.'
                : 'Select a trainee category above to limit roster rows.';
    }
}

function initRosterScope(form) {
    const includeRoster = form.querySelector('[data-training-export-include-roster]');
    const category = form.querySelector('[data-training-export-category]');
    includeRoster?.addEventListener('change', () => syncRosterScope(form));
    category?.addEventListener('change', () => syncRosterScope(form));
    syncRosterScope(form);
}

function initTrainingExportForm(form) {
    initRosterScope(form);
    form.querySelectorAll('[data-training-export-project-picker]').forEach(initTrainingExportProjectPicker);

    form.addEventListener('submit', async (event) => {
        event.preventDefault();
        if (form.dataset.trainingExportBusy === 'true') return;
        clearExportErrors(form);
        if (!form.reportValidity()) return;
        if (!validateTrainingExportProjectPicker(form)) return;

        const submitter = event.submitter || form.querySelector('[type="submit"]');
        if (!submitter) return;

        form.dataset.trainingExportBusy = 'true';
        form.setAttribute('aria-busy', 'true');
        const formData = new FormData(form);
        setExportButtonBusy(submitter, true);

        const timeoutMs = Number.parseInt(form.dataset.trainingExportTimeoutMs || '120000', 10);
        const controller = new AbortController();
        const timeoutId = window.setTimeout(() => controller.abort(), Number.isFinite(timeoutMs) ? timeoutMs : 120000);

        try {
            const response = await fetch(form.action, {
                method: (form.method || 'POST').toUpperCase(),
                body: formData,
                credentials: 'same-origin',
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                },
                signal: controller.signal
            });

            if (!response.ok) {
                showExportErrors(form, await readExportErrors(response));
                return;
            }

            const contentType = response.headers.get('content-type') || '';
            if (!contentType.includes('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')) {
                showExportErrors(form, ['The server did not return an Excel workbook. Please retry.']);
                return;
            }

            const blob = await response.blob();
            const fileName = parseDownloadFileName(response.headers.get('content-disposition'));
            triggerBlobDownload(blob, fileName);

            const modalEl = form.closest('.modal');
            if (modalEl) hideModalElement(modalEl);
        } catch (error) {
            if (error?.name === 'AbortError') {
                showExportErrors(form, ['The export timed out. Narrow the date range or other filters and try again.']);
            } else {
                console.error('training-export: request failed', error);
                showExportErrors(form, ['The export could not be downloaded. Check the connection and try again.']);
            }
        } finally {
            window.clearTimeout(timeoutId);
            delete form.dataset.trainingExportBusy;
            form.removeAttribute('aria-busy');
            setExportButtonBusy(submitter, false);
            syncRosterScope(form);
        }
    });
}

function initTrainingExportForms() {
    document.querySelectorAll('.training-export-form').forEach(initTrainingExportForm);
}

function initAutoShowExportModal() {
    document
        .querySelectorAll('[data-training-export-auto-show="true"]')
        .forEach(showModalElement);
}

// ================================================================
// chart colours: read from the site and training tokens so the
// chart follows Light and Dark (--pm-chart-*, --training-type-*)
// ================================================================
function readChartTheme() {
    const styles = getComputedStyle(document.documentElement);
    const read = (name, fallback) => styles.getPropertyValue(name).trim() || fallback;
    const palette = window.PMTheme && typeof window.PMTheme.getChartPalette === 'function'
        ? window.PMTheme.getChartPalette()
        : null;

    return {
        axisColor: palette?.axisColor || read('--pm-chart-axis', '#4b5563'),
        gridColor: palette?.gridColor || read('--pm-chart-grid', '#e5e7eb'),
        surface: read('--pm-card', '#ffffff'),
        simulator: read('--training-type-simulator', '#2563eb'),
        drone: read('--training-type-drone', '#14b8a6')
    };
}

function onThemeChange(callback) {
    const observer = new MutationObserver(callback);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-bs-theme'] });
}

// ================================================================
// chart: trainees per training year, Simulator and Drone stacked
// ================================================================
function initTrainingYearChart() {
    const host = document.querySelector('[data-training-year]');
    if (!host) return;

    const raw = host.dataset.trainingYear;
    if (!raw) return;

    let rows;
    try {
        rows = JSON.parse(raw);
    } catch (err) {
        console.warn('training-year: invalid json', err);
        return;
    }

    if (!Array.isArray(rows) || rows.length === 0) return;

    const ChartCtor = window.Chart;
    if (typeof ChartCtor !== 'function') {
        console.warn('training-year: Chart.js not available');
        return;
    }

    const canvas = document.getElementById('training-year-trend-canvas');
    if (!canvas) return;

    const labels = rows.map(
        (row) =>
            row.trainingYearLabel ||
            row.label ||
            row.trainingYear ||
            row.year ||
            ''
    );

    // The server sends trainee counts under the *Trainings names; the older *Trainees names are a fallback.
    const simulatorData = rows.map((row) => row.simulatorTrainings ?? row.simulatorTrainees ?? 0);
    const droneData = rows.map((row) => row.droneTrainings ?? row.droneTrainees ?? 0);

    let theme = readChartTheme();

    const build = () => new ChartCtor(canvas, {
        type: 'bar',
        data: {
            labels,
            datasets: [
                {
                    label: 'Simulator',
                    data: simulatorData,
                    backgroundColor: theme.simulator,
                    borderColor: theme.surface,
                    borderWidth: { top: 2 },
                    borderSkipped: 'bottom',
                    borderRadius: 4,
                    maxBarThickness: 64,
                    stack: 'values'
                },
                {
                    label: 'Drone',
                    data: droneData,
                    backgroundColor: theme.drone,
                    borderColor: theme.surface,
                    borderWidth: { top: 2 },
                    borderSkipped: 'bottom',
                    borderRadius: 4,
                    maxBarThickness: 64,
                    stack: 'values'
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    position: 'top',
                    align: 'end',
                    labels: { usePointStyle: true, pointStyle: 'rectRounded', boxWidth: 10, color: theme.axisColor }
                },
                tooltip: {
                    callbacks: {
                        label: (ctx) => `${ctx.dataset.label}: ${ctx.parsed.y.toLocaleString()} trainees`,
                        footer: (items) => `Total: ${items.reduce((sum, item) => sum + item.parsed.y, 0).toLocaleString()} trainees`
                    }
                }
            },
            interaction: {
                mode: 'index',
                intersect: false
            },
            scales: {
                x: {
                    stacked: true,
                    grid: { display: false },
                    ticks: { color: theme.axisColor },
                    border: { color: theme.gridColor }
                },
                y: {
                    stacked: true,
                    beginAtZero: true,
                    ticks: { precision: 0, color: theme.axisColor },
                    title: {
                        display: true,
                        text: 'Trainees',
                        color: theme.axisColor
                    },
                    grid: { color: theme.gridColor },
                    border: { display: false }
                }
            }
        }
    });

    let chart = build();

    // Rebuild with the new colours when the user switches Light / Dark.
    onThemeChange(() => {
        theme = readChartTheme();
        chart.destroy();
        chart = build();
    });
}


// ================================================================
// chart download buttons: a PNG on the card colour, so it reads
// the same when pasted into a document
// ================================================================
function initDownloadButtons() {
    document
        .querySelectorAll('[data-action="download-png"][data-target]')
        .forEach((button) => {
            button.addEventListener('click', () => {
                const targetId = button.dataset.target;
                if (!targetId) return;

                const source = document.getElementById(targetId);
                if (!source) return;

                const copy = document.createElement('canvas');
                copy.width = source.width;
                copy.height = source.height;
                const context = copy.getContext('2d');
                context.fillStyle = readChartTheme().surface;
                context.fillRect(0, 0, copy.width, copy.height);
                context.drawImage(source, 0, 0);

                const link = document.createElement('a');
                link.href = copy.toDataURL('image/png');
                link.download = `${targetId}.png`;
                link.click();
            });
        });
}

// ================================================================
// register: a click anywhere on a row opens the training
// (Ctrl/Cmd-click opens a new tab); sort and rows apply at once
// ================================================================
function initRegisterRows() {
    document.querySelectorAll('tr[data-row-href]').forEach((row) => {
        row.addEventListener('click', (event) => {
            if (event.defaultPrevented || event.target.closest('a, button, input, select, textarea, label')) {
                return;
            }
            if (window.getSelection && String(window.getSelection()).length > 0) {
                return;
            }
            const href = row.getAttribute('data-row-href');
            if (!href) return;

            if (event.ctrlKey || event.metaKey) {
                window.open(href, '_blank', 'noopener');
                return;
            }
            window.location.assign(href);
        });
    });
}

function initAutoSubmitControls() {
    document.querySelectorAll('[data-training-autosubmit]').forEach((control) => {
        control.addEventListener('change', () => {
            if (control.form) {
                control.form.submit();
            }
        });
    });
}

// ================================================================
// init
// ================================================================
function init() {
    initAsyncMultiselect();
    initAutoShowExportModal();
    initTrainingExportForms();
    initTrainingYearChart();
    initDownloadButtons();
    initRegisterRows();
    initAutoSubmitControls();
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init, { once: true });
} else {
    init();
}
