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
        surface: styles.getPropertyValue('--pm-card').trim() || '#ffffff'
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

        typeData.forEach(t => {
            const val = range === 'lastYear' ? t.countLastYear : t.count;
            if (val > 0) {
                labels.push(t.name);
                values.push(val);
            }
        });

        return { labels, values };
    }

    function showTypeEmptyState(isEmpty) {
        typeCanvas.hidden = isEmpty;
        if (typeEmpty) {
            typeEmpty.hidden = !isEmpty;
        }
    }

    function typeColours(count) {
        return Array.from({ length: count }, (_, index) => theme.accents[index % theme.accents.length]);
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
                        label: 'People',
                        data: monthlyData.map(x => x.strength),
                        type: 'line',
                        borderColor: theme.accents[1],
                        backgroundColor: theme.accents[1],
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
                        title: { display: true, text: 'People', color: theme.axisColor }
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
                    backgroundColor: typeColours(dataset.values.length),
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
            typeChart.data.datasets[0].backgroundColor = typeColours(next.values.length);
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
// VISIT PHOTO VIEWER
// ----------------------------------------------------

function initVisitPhotoViewer() {
    const gallery = document.querySelector('[data-visit-gallery]');
    const viewerEl = document.querySelector('[data-visit-photo-viewer]');
    const bootstrap = ensureBootstrapModal();

    // Progressive enhancement: without Bootstrap/JS, the existing href continues
    // to serve the secured XL image directly.
    if (!gallery || !viewerEl || !bootstrap) {
        return;
    }

    const items = Array.from(gallery.querySelectorAll('[data-visit-gallery-item]'));
    if (items.length === 0) {
        return;
    }

    const image = viewerEl.querySelector('[data-visit-photo-viewer-image]');
    const counter = viewerEl.querySelector('[data-visit-photo-viewer-counter]');
    const caption = viewerEl.querySelector('[data-visit-photo-viewer-caption]');
    const coverBadge = viewerEl.querySelector('[data-visit-photo-viewer-cover]');
    const previousButton = viewerEl.querySelector('[data-visit-photo-viewer-prev]');
    const nextButton = viewerEl.querySelector('[data-visit-photo-viewer-next]');
    const stage = viewerEl.querySelector('[data-visit-photo-viewer-stage]');
    const loading = viewerEl.querySelector('[data-visit-photo-viewer-loading]');
    const error = viewerEl.querySelector('[data-visit-photo-viewer-error]');

    if (!image || !counter || !caption || !coverBadge || !previousButton || !nextButton || !stage) {
        return;
    }

    const modal = bootstrap.Modal.getOrCreateInstance(viewerEl, {
        keyboard: true,
        focus: true
    });

    let activeIndex = 0;
    let loadSequence = 0;
    let lastTrigger = null;
    let touchStartX = null;

    const readItem = index => {
        const item = items[index];
        if (!item) {
            return null;
        }

        return {
            url: item.getAttribute('data-photo-url') || item.getAttribute('href') || '',
            caption: item.getAttribute('data-photo-caption') || '',
            isCover: item.getAttribute('data-photo-cover') === 'true'
        };
    };

    const setLoadingState = isLoading => {
        viewerEl.classList.toggle('is-loading', isLoading);
        if (loading) {
            loading.hidden = !isLoading;
        }
        if (isLoading && error) {
            error.hidden = true;
        }
    };

    const setErrorState = hasError => {
        viewerEl.classList.toggle('has-error', hasError);
        if (error) {
            error.hidden = !hasError;
        }
        if (hasError && loading) {
            loading.hidden = true;
        }
    };

    const preloadAdjacent = index => {
        [index - 1, index + 1].forEach(adjacentIndex => {
            const adjacent = readItem(adjacentIndex);
            if (!adjacent?.url) {
                return;
            }

            const preload = new Image();
            preload.decoding = 'async';
            preload.src = adjacent.url;
        });
    };

    const render = index => {
        const item = readItem(index);
        if (!item?.url) {
            return;
        }

        activeIndex = index;
        const sequence = ++loadSequence;
        const position = index + 1;

        counter.textContent = `${position} of ${items.length}`;
        previousButton.disabled = index === 0;
        nextButton.disabled = index === items.length - 1;

        coverBadge.hidden = !item.isCover;
        caption.textContent = item.caption;
        caption.hidden = !item.caption;

        image.alt = item.caption || `Visit photo ${position} of ${items.length}`;
        image.dataset.activeUrl = item.url;
        image.classList.remove('is-ready');
        setErrorState(false);
        setLoadingState(true);

        const handleLoad = () => {
            if (sequence !== loadSequence) {
                return;
            }

            setLoadingState(false);
            image.classList.add('is-ready');
            preloadAdjacent(index);
        };

        const handleError = () => {
            if (sequence !== loadSequence) {
                return;
            }

            setLoadingState(false);
            setErrorState(true);
        };

        image.addEventListener('load', handleLoad, { once: true });
        image.addEventListener('error', handleError, { once: true });
        image.src = item.url;
    };

    const move = delta => {
        const nextIndex = activeIndex + delta;
        if (nextIndex < 0 || nextIndex >= items.length) {
            return;
        }

        render(nextIndex);
    };

    const handleKeydown = event => {
        if (event.key === 'ArrowLeft') {
            event.preventDefault();
            move(-1);
        } else if (event.key === 'ArrowRight') {
            event.preventDefault();
            move(1);
        }
    };

    previousButton.addEventListener('click', () => move(-1));
    nextButton.addEventListener('click', () => move(1));

    stage.addEventListener('click', event => {
        if (event.target === stage) {
            modal.hide();
        }
    });

    stage.addEventListener('touchstart', event => {
        if (event.touches.length === 1) {
            touchStartX = event.touches[0].clientX;
        }
    }, { passive: true });

    stage.addEventListener('touchend', event => {
        if (touchStartX === null || event.changedTouches.length === 0) {
            touchStartX = null;
            return;
        }

        const deltaX = event.changedTouches[0].clientX - touchStartX;
        touchStartX = null;

        if (Math.abs(deltaX) < 48) {
            return;
        }

        move(deltaX > 0 ? -1 : 1);
    }, { passive: true });

    items.forEach((item, index) => {
        item.addEventListener('click', event => {
            event.preventDefault();
            lastTrigger = item;
            render(index);
            modal.show();
        });
    });

    viewerEl.addEventListener('shown.bs.modal', () => {
        document.addEventListener('keydown', handleKeydown);
        stage.focus({ preventScroll: true });
    });

    viewerEl.addEventListener('hidden.bs.modal', () => {
        document.removeEventListener('keydown', handleKeydown);
        loadSequence += 1;
        image.removeAttribute('src');
        image.classList.remove('is-ready');
        setLoadingState(false);
        setErrorState(false);

        if (lastTrigger && typeof lastTrigger.focus === 'function') {
            lastTrigger.focus({ preventScroll: true });
        }
    });
}

// ----------------------------------------------------
// PHOTO UPLOAD GUARDRAILS
// ----------------------------------------------------

// SECTION: Formatting helpers
function formatMb(bytes) {
    return (bytes / (1024 * 1024)).toFixed(1);
}

// SECTION: Photo upload interactions
function initPhotoUploadInputs() {
    const inputs = document.querySelectorAll('[data-photo-upload-input]');
    inputs.forEach(input => {
        const summaryId = input.getAttribute('aria-describedby')
            ?.split(' ')
            .map(id => id.trim())
            .find(id => id.endsWith('photo-summary'));
        const summaryEl = summaryId ? document.getElementById(summaryId) : null;
        const maxFiles = Number(input.getAttribute('data-max-files') || '0');
        const maxBytes = Number(input.getAttribute('data-max-bytes') || '0');

        input.addEventListener('change', () => {
            const files = Array.from(input.files || []);
            if (!summaryEl) {
                return;
            }

            // SECTION: Count guardrail
            if (maxFiles > 0 && files.length > maxFiles) {
                showToast(`You can upload up to ${maxFiles} photos at a time.`, 'danger');
            }

            // SECTION: Summary rendering
            const totalBytes = files.reduce((sum, file) => sum + (file.size || 0), 0);
            summaryEl.textContent = files.length === 0
                ? ''
                : `Selected: ${files.length} file(s), total ${formatMb(totalBytes)} MB.`;

            // SECTION: Size guardrail
            if (maxBytes > 0) {
                const oversized = files.find(file => (file.size || 0) > maxBytes);
                if (oversized) {
                    showToast(`\"${oversized.name}\" exceeds the per-photo limit. Please choose a smaller file.`, 'danger');
                }
            }
        });
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

// ----------------------------------------------------
// INITIALISE EVERYTHING
// ----------------------------------------------------

function init() {
    initConfirmations();
    initDisableOnSubmit();
    initVisitsCharts();
    initVisitPhotoViewer();
    initGalleryFallbacks();
    initPhotoUploadInputs();
    initVisitRegisterPageSize();
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init, { once: true });
} else {
    init();
}
