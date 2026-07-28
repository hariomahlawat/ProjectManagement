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

    form.addEventListener('submit', async (event) => {
        event.preventDefault();
        if (form.dataset.trainingExportBusy === 'true') return;
        if (!form.reportValidity()) return;

        const submitter = event.submitter || form.querySelector('[type="submit"]');
        if (!submitter) return;

        clearExportErrors(form);
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
// chart: always label as trainees; we now *have* trainee counts
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

    // 👇 IMPORTANT: try the names C# actually sends now, then fall back
    const simulatorData = rows.map(
        (row) =>
            row.simulatorTrainings ??   // current C# name
            row.simulatorTrainees ??   // older name, if you revert
            0
    );
    const droneData = rows.map(
        (row) =>
            row.droneTrainings ??       // current C# name
            row.droneTrainees ??       // older name, if you revert
            0
    );

    new ChartCtor(canvas, {
        type: 'bar',
        data: {
            labels,
            datasets: [
                {
                    label: 'Simulator',
                    data: simulatorData,
                    backgroundColor: 'rgba(59,130,246,0.85)',
                    borderRadius: 6,
                    stack: 'values'
                },
                {
                    label: 'Drone',
                    data: droneData,
                    backgroundColor: 'rgba(14,165,233,0.85)',
                    borderRadius: 6,
                    stack: 'values'
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    position: 'top'
                },
                tooltip: {
                    callbacks: {
                        label: (ctx) => `${ctx.dataset.label}: ${ctx.parsed.y}`
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
                    grid: { display: false }
                },
                y: {
                    stacked: true,
                    beginAtZero: true,
                    ticks: { precision: 0 },
                    title: {
                        display: true,
                        text: 'Trainees'
                    },
                    grid: {
                        color: 'rgba(148,163,184,0.2)'
                    }
                }
            }
        }
    });
}


// ================================================================
// chart download buttons
// ================================================================
function initDownloadButtons() {
    document
        .querySelectorAll('[data-action="download-png"][data-target]')
        .forEach((button) => {
            button.addEventListener('click', () => {
                const targetId = button.dataset.target;
                if (!targetId) return;

                const canvas = document.getElementById(targetId);
                if (!canvas) return;

                const link = document.createElement('a');
                link.href = canvas.toDataURL('image/png');
                link.download = `${targetId}.png`;
                link.click();
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
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init, { once: true });
} else {
    init();
}
