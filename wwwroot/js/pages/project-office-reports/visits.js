// wwwroot/js/pages/project-office-reports/visits.js

// SECTION: Client-side notices
// Server messages come from the shared layout (_Toasts.cshtml, TempData "ToastSuccess" and
// "ToastError"). This only covers checks made in the browser, such as an oversized photo,
// and uses the same app-toast look so both kinds read as one system.
function ensureBootstrap() {
    return typeof window !== 'undefined'
        && window.bootstrap
        && typeof window.bootstrap.Toast === 'function'
        ? window.bootstrap
        : null;
}

function ensureToastHost() {
    let host = document.querySelector('.app-toast-container');
    if (host) {
        return host;
    }

    host = document.createElement('div');
    host.className = 'toast-container position-fixed top-0 end-0 p-3 app-toast-container';
    host.setAttribute('aria-live', 'polite');
    host.setAttribute('aria-atomic', 'true');
    document.body.appendChild(host);
    return host;
}

const toastTitles = { success: 'Success', danger: 'Action required', warning: 'Attention' };

function createToastElement(message, variant) {
    const toast = document.createElement('div');
    toast.className = `toast app-toast app-toast-${variant}`;
    toast.setAttribute('role', variant === 'danger' ? 'alert' : 'status');
    toast.setAttribute('aria-atomic', 'true');

    const header = document.createElement('div');
    header.className = 'toast-header';

    const indicator = document.createElement('span');
    indicator.className = 'app-toast-indicator';
    indicator.setAttribute('aria-hidden', 'true');

    const title = document.createElement('strong');
    title.className = 'me-auto';
    title.textContent = toastTitles[variant] || 'Update';

    const dismiss = document.createElement('button');
    dismiss.type = 'button';
    dismiss.className = 'btn-close';
    dismiss.setAttribute('data-bs-dismiss', 'toast');
    dismiss.setAttribute('aria-label', 'Close');

    const body = document.createElement('div');
    body.className = 'toast-body';
    body.textContent = message;

    header.append(indicator, title, dismiss);
    toast.append(header, body);
    return toast;
}

function showToast(message, variant) {
    const bootstrap = ensureBootstrap();
    if (!bootstrap || !message) {
        return;
    }

    const toastEl = createToastElement(message, variant);
    ensureToastHost().appendChild(toastEl);

    const toastInstance = bootstrap.Toast.getOrCreateInstance(toastEl, { autohide: true, delay: 6000 });
    toastEl.addEventListener(
        'hidden.bs.toast',
        () => {
            toastInstance.dispose();
            toastEl.remove();
        },
        { once: true }
    );

    toastInstance.show();
}

function updateButtonToBusyState(button) {
    const busyLabel = button.getAttribute('data-visits-busy-label');
    if (!busyLabel) {
        return;
    }

    button.dataset.visitsOriginalHtml = button.innerHTML;
    const spinner = document.createElement('span');
    spinner.className = 'spinner-border spinner-border-sm me-2';
    spinner.setAttribute('role', 'status');
    spinner.setAttribute('aria-hidden', 'true');

    const label = document.createElement('span');
    label.textContent = busyLabel;

    button.innerHTML = '';
    button.append(spinner, label);
}

/**
 * Submit locking helpers
 */
function resolveSubmitter(form, event) {
    if (event.submitter instanceof HTMLElement) {
        return event.submitter;
    }

    if (document.activeElement instanceof HTMLElement && form.contains(document.activeElement)) {
        const activeElement = document.activeElement;
        if (activeElement.matches('button[type="submit"], input[type="submit"], input[type="image"]')) {
            return activeElement;
        }
    }

    return form.querySelector('[type="submit"]');
}

function preserveSubmitterValue(form, submitter) {
    const name = submitter.getAttribute('name');
    if (!name) {
        return;
    }

    const hidden = document.createElement('input');
    hidden.type = 'hidden';
    hidden.name = name;
    hidden.value = submitter.getAttribute('value') || '';
    hidden.dataset.visitsSubmitterValue = 'true';
    form.appendChild(hidden);
}

/**
 * Prevent locking the form when client-side validation fails.
 */
function disableFormOnSubmit(form) {
    form.addEventListener('submit', event => {
        const submitter = resolveSubmitter(form, event);

        setTimeout(() => {
            if (event.defaultPrevented) {
                return;
            }

            const hasClientErrors = form.querySelector('.input-validation-error');
            if (hasClientErrors) {
                return;
            }

            if (!submitter || submitter.disabled) {
                return;
            }

            preserveSubmitterValue(form, submitter);
            submitter.disabled = true;
            updateButtonToBusyState(submitter);
        }, 0);
    });
}

function initDisableOnSubmit() {
    const forms = document.querySelectorAll('form[data-visits-disable-on-submit]');
    forms.forEach(disableFormOnSubmit);
}

const confirmModalId = 'visitsConfirmModal';

function ensureBootstrapModal() {
    const bootstrap = ensureBootstrap();
    return bootstrap && typeof bootstrap.Modal === 'function' ? bootstrap : null;
}

function ensureConfirmModalElement() {
    let modal = document.getElementById(confirmModalId);
    if (modal) {
        return modal;
    }

    modal = document.createElement('div');
    modal.id = confirmModalId;
    modal.className = 'modal fade visits-confirm-modal';
    modal.tabIndex = -1;
    modal.setAttribute('aria-hidden', 'true');
    modal.innerHTML = `
    <div class="modal-dialog modal-dialog-centered">
      <div class="modal-content border-0 shadow-lg">
        <button type="button" class="btn-close visits-confirm-modal__close" data-bs-dismiss="modal" aria-label="Cancel"></button>
        <div class="modal-body p-4">
          <div class="d-flex align-items-start gap-3">
            <div class="visits-confirm-modal__icon" aria-hidden="true">!</div>
            <div class="flex-grow-1">
              <h5 class="visits-confirm-modal__title mb-1">Review before deleting</h5>
              <p class="visits-confirm-modal__message mb-3" data-visits-confirm-message></p>
              <p class="visits-confirm-modal__subtitle mb-0">This action can't be undone.</p>
            </div>
          </div>
        </div>
        <div class="modal-footer border-0 pt-0">
          <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal" data-visits-confirm-cancel>Keep record</button>
          <button type="button" class="btn btn-danger" data-visits-confirm-accept>Delete</button>
        </div>
      </div>
    </div>
  `;

    document.body.appendChild(modal);
    return modal;
}

function showConfirmModal(message) {
    const bootstrap = ensureBootstrapModal();
    if (!bootstrap) {
        return Promise.resolve(window.confirm(message));
    }

    const modalEl = ensureConfirmModalElement();
    const instance = bootstrap.Modal.getOrCreateInstance(modalEl, {
        backdrop: 'static',
        keyboard: true,
        focus: true
    });

    const messageEl = modalEl.querySelector('[data-visits-confirm-message]');
    if (messageEl) {
        messageEl.textContent = message;
    }

    const confirmButton = modalEl.querySelector('[data-visits-confirm-accept]');
    const cancelButton = modalEl.querySelector('[data-visits-confirm-cancel]');

    return new Promise(resolve => {
        let handled = false;

        const cleanup = () => {
            confirmButton?.removeEventListener('click', handleConfirm);
            cancelButton?.removeEventListener('click', handleCancel);
            modalEl.removeEventListener('hidden.bs.modal', handleHidden);
        };

        const handleConfirm = () => {
            handled = true;
            cleanup();
            resolve(true);
            instance.hide();
        };

        const handleCancel = () => {
            handled = true;
            cleanup();
            resolve(false);
            instance.hide();
        };

        const handleHidden = () => {
            if (!handled) {
                resolve(false);
            }
            cleanup();
        };

        confirmButton?.addEventListener('click', handleConfirm);
        cancelButton?.addEventListener('click', handleCancel);
        modalEl.addEventListener('hidden.bs.modal', handleHidden, { once: true });

        instance.show();
    });
}

function attachConfirmDialog(form) {
    const message = form.getAttribute('data-confirm');
    if (!message) {
        return;
    }

    form.addEventListener('submit', event => {
        if (form.dataset.visitsConfirmBypassed === 'true') {
            delete form.dataset.visitsConfirmBypassed;
            return;
        }

        event.preventDefault();

        const submitter = event.submitter;

        showConfirmModal(message).then(confirmed => {
            if (!confirmed) {
                return;
            }

            form.dataset.visitsConfirmBypassed = 'true';

            if (typeof form.requestSubmit === 'function') {
                form.requestSubmit(submitter);
                return;
            }

            if (submitter && typeof submitter.click === 'function') {
                submitter.click();
                return;
            }

            form.submit();
        });
    });
}

function initConfirmations() {
    const forms = document.querySelectorAll('form[data-confirm]');
    forms.forEach(attachConfirmDialog);
}

// ----------------------------------------------------
// CHARTS
// ----------------------------------------------------

// SECTION: Chart colours
// Charts read the site's chart tokens (--pm-chart-*) so they follow Light and Dark.
function readChartTheme() {
    const fallback = {
        axisColor: '#4b5563',
        gridColor: '#e5e7eb',
        accents: ['#2563eb', '#f97316', '#22c55e', '#a855f7', '#ec4899', '#14b8a6']
    };
    const palette = window.PMTheme && typeof window.PMTheme.getChartPalette === 'function'
        ? window.PMTheme.getChartPalette()
        : null;
    const styles = getComputedStyle(document.documentElement);
    return {
        axisColor: palette?.axisColor || fallback.axisColor,
        gridColor: palette?.gridColor || fallback.gridColor,
        accents: palette?.accents?.length ? palette.accents : fallback.accents,
        surface: styles.getPropertyValue('--pm-card').trim() || '#ffffff',
        // The visitors line is neutral ink, so it never shares a colour with a visit type.
        ink: styles.getPropertyValue('--pm-text-strong').trim() || '#1f2937'
    };
}

function withAlpha(color, alpha) {
    const hex = /^#([0-9a-f]{6})$/i.exec(color || '');
    if (!hex) {
        return color;
    }
    const value = parseInt(hex[1], 16);
    return `rgba(${(value >> 16) & 255}, ${(value >> 8) & 255}, ${value & 255}, ${alpha})`;
}

function onThemeChange(callback) {
    const observer = new MutationObserver(callback);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-bs-theme'] });
}

function initVisitsCharts() {
    const monthlyHost = document.getElementById('visits-monthly-chart');
    const typeHost = document.getElementById('visits-type-chart');

    if (!monthlyHost || !typeHost) return;

    if (typeof Chart === 'undefined') {
        console.warn('Chart.js not found for visits page.');
        return;
    }

    const monthlyData = JSON.parse(monthlyHost.dataset.monthly || '[]');
    const typeData = JSON.parse(typeHost.dataset.types || '[]');
    const monthlyCanvas = document.getElementById('visits-monthly-canvas');
    const typeCanvas = document.getElementById('visits-type-canvas');
    const typeEmpty = typeHost.querySelector('[data-visits-type-empty]');
    const rangeSelect = document.getElementById('visits-type-range');

    function buildTypeDataset(range) {
        const labels = [];
        const values = [];
        const slots = [];

        typeData.forEach(t => {
            const val = range === 'lastYear' ? t.countLastYear : t.count;
            if (val > 0) {
                labels.push(t.name);
                values.push(val);
                slots.push(t.colourSlot || labels.length);
            }
        });

        return { labels, values, slots };
    }

    function showTypeEmptyState(isEmpty) {
        typeCanvas.hidden = isEmpty;
        if (typeEmpty) {
            typeEmpty.hidden = !isEmpty;
        }
    }

    // Each type keeps the colour slot it has in the register's type tags.
    function typeColours(slots) {
        return slots.map(slot => theme.accents[(slot - 1) % theme.accents.length]);
    }

    function buildMonthlyChart() {
        return new Chart(monthlyCanvas.getContext('2d'), {
            type: 'bar',
            data: {
                labels: monthlyData.map(x => x.label),
                datasets: [
                    {
                        label: 'Visits',
                        data: monthlyData.map(x => x.visits),
                        backgroundColor: withAlpha(theme.accents[0], 0.45),
                        hoverBackgroundColor: withAlpha(theme.accents[0], 0.7),
                        borderRadius: 6,
                        maxBarThickness: 28,
                        order: 2
                    },
                    {
                        label: 'Visitors',
                        data: monthlyData.map(x => x.strength),
                        type: 'line',
                        borderColor: theme.ink,
                        backgroundColor: theme.ink,
                        borderWidth: 2,
                        // Straight segments: a curve would suggest values between months.
                        tension: 0,
                        pointRadius: 3,
                        pointBackgroundColor: theme.surface,
                        yAxisID: 'y1',
                        order: 1
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                devicePixelRatio: 2,
                scales: {
                    x: {
                        ticks: { color: theme.axisColor },
                        grid: { display: false },
                        border: { color: theme.gridColor }
                    },
                    y: {
                        beginAtZero: true,
                        ticks: { precision: 0, color: theme.axisColor },
                        grid: { color: theme.gridColor },
                        border: { display: false },
                        title: { display: true, text: 'Visits', color: theme.axisColor }
                    },
                    y1: {
                        beginAtZero: true,
                        position: 'right',
                        grid: { drawOnChartArea: false },
                        ticks: { precision: 0, color: theme.axisColor },
                        border: { display: false },
                        title: { display: true, text: 'Visitors', color: theme.axisColor }
                    }
                },
                plugins: {
                    legend: {
                        position: 'top',
                        labels: { usePointStyle: true, boxWidth: 8, color: theme.axisColor }
                    },
                    tooltip: { mode: 'index', intersect: false }
                }
            }
        });
    }

    function buildTypeChart(range) {
        const dataset = buildTypeDataset(range);
        showTypeEmptyState(dataset.values.length === 0);
        return new Chart(typeCanvas.getContext('2d'), {
            type: 'doughnut',
            data: {
                labels: dataset.labels,
                datasets: [{
                    data: dataset.values,
                    backgroundColor: typeColours(dataset.slots),
                    borderColor: theme.surface,
                    borderWidth: 2
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                devicePixelRatio: 2,
                cutout: '58%',
                plugins: {
                    legend: {
                        position: 'bottom',
                        labels: { usePointStyle: true, boxWidth: 8, color: theme.axisColor }
                    }
                }
            }
        });
    }

    let theme = readChartTheme();
    let monthlyChart = buildMonthlyChart();
    let typeChart = buildTypeChart(rangeSelect?.value || 'lastYear');

    if (rangeSelect) {
        rangeSelect.addEventListener('change', () => {
            const next = buildTypeDataset(rangeSelect.value);
            showTypeEmptyState(next.values.length === 0);
            typeChart.data.labels = next.labels;
            typeChart.data.datasets[0].data = next.values;
            typeChart.data.datasets[0].backgroundColor = typeColours(next.slots);
            typeChart.update();
        });
    }

    // Rebuild with the new colours when the user switches Light / Dark.
    onThemeChange(() => {
        theme = readChartTheme();
        monthlyChart.destroy();
        typeChart.destroy();
        monthlyChart = buildMonthlyChart();
        typeChart = buildTypeChart(rangeSelect?.value || 'lastYear');
    });

    // SECTION: Chart downloads
    // A PNG on the card colour, so it reads the same when pasted into a document.
    document.querySelectorAll('[data-chart-download]').forEach(btn => {
        btn.addEventListener('click', () => {
            const target = btn.getAttribute('data-chart-download');
            const chart = target === 'visits-monthly' ? monthlyChart : target === 'visits-type' ? typeChart : null;
            if (!chart) {
                return;
            }

            const source = chart.canvas;
            const copy = document.createElement('canvas');
            copy.width = source.width;
            copy.height = source.height;
            const context = copy.getContext('2d');
            context.fillStyle = theme.surface;
            context.fillRect(0, 0, copy.width, copy.height);
            context.drawImage(source, 0, 0);

            const now = new Date();
            const stamp = `${now.getFullYear()}${String(now.getMonth() + 1).padStart(2, '0')}${String(now.getDate()).padStart(2, '0')}`;
            const link = document.createElement('a');
            link.href = copy.toDataURL('image/png');
            link.download = `${target}-${stamp}.png`;
            link.click();
        });
    });
}

// ----------------------------------------------------
// PHOTO UPLOAD GUARDRAILS
// ----------------------------------------------------

// SECTION: Formatting helpers
function formatMb(bytes) {
    return (bytes / (1024 * 1024)).toFixed(1);
}

// A file size people can read: KB under a megabyte, otherwise MB to one decimal.
function formatSize(bytes) {
    return bytes < 1024 * 1024
        ? `${Math.max(1, Math.round(bytes / 1024))} KB`
        : `${formatMb(bytes)} MB`;
}

// A configured limit, as the help text states it (20 MB, not 20.0 MB).
function formatLimit(bytes) {
    return `${Math.round(bytes / (1024 * 1024))} MB`;
}

// SECTION: Photo upload interactions
function initPhotoUploadInputs() {
    const inputs = document.querySelectorAll('[data-photo-upload-input]');
    inputs.forEach(input => {
        const picker = input.closest('[data-photo-picker]');
        const summaryEl = picker?.querySelector('[data-photo-upload-summary]');
        const previewEl = picker?.querySelector('[data-photo-upload-preview]');
        const dropZone = input.closest('.visit-photo-picker__drop');
        const maxFiles = Number(input.getAttribute('data-max-files') || '0');
        const maxBytes = Number(input.getAttribute('data-max-bytes') || '0');
        const maxBatchBytes = Number(input.getAttribute('data-max-batch-bytes') || '0');
        let previewUrls = [];
        let problem = null;

        const clearPreviews = () => {
            previewUrls.forEach(url => URL.revokeObjectURL(url));
            previewUrls = [];
            if (previewEl) {
                previewEl.replaceChildren();
            }
        };

        // SECTION: Selection check and previews
        const render = () => {
            const files = Array.from(input.files || []);
            const totalBytes = files.reduce((sum, file) => sum + (file.size || 0), 0);
            const oversized = files.filter(file => maxBytes > 0 && (file.size || 0) > maxBytes);

            problem = null;
            if (maxFiles > 0 && files.length > maxFiles) {
                problem = `Choose up to ${maxFiles} photos at a time; ${files.length} are selected.`;
            } else if (oversized.length > 0) {
                problem = oversized.length === 1
                    ? `"${oversized[0].name}" is larger than ${formatLimit(maxBytes)}.`
                    : `${oversized.length} photos are larger than ${formatLimit(maxBytes)} each.`;
            } else if (maxBatchBytes > 0 && totalBytes > maxBatchBytes) {
                problem = `Together these photos are ${formatSize(totalBytes)}; the limit is ${formatLimit(maxBatchBytes)}.`;
            }

            if (summaryEl) {
                summaryEl.classList.toggle('is-invalid', problem !== null);
                summaryEl.textContent = files.length === 0
                    ? ''
                    : problem ?? `${files.length} ${files.length === 1 ? 'photo' : 'photos'} selected · ${formatSize(totalBytes)}`;
            }
            input.setCustomValidity(problem ?? '');

            clearPreviews();
            if (!previewEl) {
                return;
            }

            files.slice(0, 48).forEach(file => {
                const item = document.createElement('li');
                item.className = 'visit-photo-preview';
                if (maxBytes > 0 && (file.size || 0) > maxBytes) {
                    item.classList.add('is-too-large');
                }

                const img = document.createElement('img');
                img.alt = '';
                if (file.type && file.type.startsWith('image/')) {
                    const url = URL.createObjectURL(file);
                    previewUrls.push(url);
                    img.src = url;
                }

                const name = document.createElement('span');
                name.className = 'visit-photo-preview__name';
                name.textContent = file.name;
                name.title = file.name;

                const size = document.createElement('span');
                size.className = 'visit-photo-preview__size';
                size.textContent = formatSize(file.size || 0);

                item.append(img, name, size);
                previewEl.append(item);
            });
        };

        input.addEventListener('change', render);

        // Stop the form being sent while the selection breaks the limits; the server checks again.
        input.form?.addEventListener('submit', event => {
            if (problem) {
                event.preventDefault();
                showToast(problem, 'danger');
                input.focus();
            }
        });

        // SECTION: Drag highlight
        if (dropZone) {
            ['dragenter', 'dragover'].forEach(type => input.addEventListener(type, () => dropZone.classList.add('is-dragging')));
            ['dragleave', 'drop'].forEach(type => input.addEventListener(type, () => dropZone.classList.remove('is-dragging')));
        }

        window.addEventListener('pagehide', clearPreviews);
    });
}

// SECTION: Character counters
// <span data-char-counter-for="fieldId"> shows how much of a maxlength field is left.
function initCharacterCounters() {
    document.querySelectorAll('[data-char-counter-for]').forEach(counter => {
        const field = document.getElementById(counter.getAttribute('data-char-counter-for') || '');
        const limit = Number(field?.getAttribute('maxlength') || '0');
        if (!field || limit <= 0) {
            return;
        }

        const update = () => {
            const used = field.value.length;
            counter.textContent = `${used.toLocaleString()} / ${limit.toLocaleString()}`;
            counter.classList.toggle('is-near-limit', used >= limit * 0.9);
        };

        field.addEventListener('input', update);
        update();
    });
}


// ----------------------------------------------------
// VISIT REGISTER TOOLBAR
// ----------------------------------------------------

// SECTION: Page-size auto-submit
function initVisitRegisterPageSize() {
    const pageSizeControls = document.querySelectorAll('[data-visit-page-size-submit]');

    pageSizeControls.forEach(control => {
        control.addEventListener('change', () => {
            if (control.form) {
                control.form.submit();
            }
        });
    });
}

// SECTION: Missing photo files
// If a stored photo cannot be served, show a quiet placeholder instead of a dark tile with alt text.
function markMissingPhoto(img) {
    const frame = img.closest('.visit-gallery-card__frame');
    if (frame) {
        frame.classList.add('is-missing');
        frame.setAttribute('data-missing-label', 'Photo unavailable');
    }
}

function initGalleryFallbacks() {
    document.querySelectorAll('.visit-gallery-card__frame img').forEach(img => {
        if (img.complete && img.naturalWidth === 0) {
            markMissingPhoto(img);
            return;
        }
        img.addEventListener('error', () => markMissingPhoto(img), { once: true });
    });
}

// SECTION: Register rows
// The visitor name is the real link; a click anywhere else on the row opens the same visit.
function initRegisterRows() {
    document.querySelectorAll('tr[data-row-href]').forEach(row => {
        row.addEventListener('click', event => {
            if (event.defaultPrevented || event.target.closest('a, button, input, select, textarea, label')) {
                return;
            }
            if (window.getSelection && String(window.getSelection()).length > 0) {
                return;
            }
            const href = row.getAttribute('data-row-href');
            if (!href) {
                return;
            }
            if (event.ctrlKey || event.metaKey) {
                window.open(href, '_blank', 'noopener');
                return;
            }
            window.location.assign(href);
        });
    });

    // A thumbnail whose file cannot be served falls back to the no-photo tile.
    document.querySelectorAll('.visit-entry__thumb img').forEach(img => {
        const fallback = () => {
            const thumb = img.closest('.visit-entry__thumb');
            if (!thumb || thumb.classList.contains('is-missing')) {
                return;
            }
            thumb.classList.add('is-missing', 'visit-entry__thumb--none');
            const icon = document.createElement('i');
            icon.className = 'bi bi-image';
            icon.setAttribute('aria-hidden', 'true');
            thumb.prepend(icon);
        };
        if (img.complete && img.naturalWidth === 0) {
            fallback();
        } else {
            img.addEventListener('error', fallback, { once: true });
        }
    });
}

// SECTION: Cover photo on the record page
// The cover opens the shared viewer through its data-media-viewer-open attributes; here it only
// falls back to a quiet placeholder when the stored file cannot be served.
function initCoverPhoto() {
    document.querySelectorAll('[data-visit-cover]').forEach(button => {
        const cover = button.closest('.visit-cover');
        const img = button.querySelector('img');
        const markMissing = () => {
            cover?.classList.add('is-missing');
            button.disabled = true;
        };
        if (!img) {
            return;
        }
        if (img.complete && img.naturalWidth === 0) {
            markMissing();
        } else {
            img.addEventListener('error', markMissing, { once: true });
        }
    });
}

// ----------------------------------------------------
// INITIALISE EVERYTHING
// ----------------------------------------------------

function init() {
    initConfirmations();
    initDisableOnSubmit();
    initVisitsCharts();
    initGalleryFallbacks();
    initRegisterRows();
    initCoverPhoto();
    initPhotoUploadInputs();
    initCharacterCounters();
    initVisitRegisterPageSize();
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init, { once: true });
} else {
    init();
}
