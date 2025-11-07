import { initAsyncMultiselect } from '../../widgets/async-multiselect.js';

function ensureBootstrapModal() {
  if (typeof window === 'undefined') {
    return null;
  }

  const { bootstrap } = window;
  return bootstrap && typeof bootstrap.Modal === 'function' ? bootstrap : null;
}

function showModalElement(modalEl) {
  if (!modalEl) {
    return;
  }

  const bootstrap = ensureBootstrapModal();
  if (bootstrap) {
    bootstrap.Modal.getOrCreateInstance(modalEl).show();
    return;
  }

  modalEl.classList.add('show');
  modalEl.style.display = 'block';
  modalEl.removeAttribute('aria-hidden');
  modalEl.setAttribute('aria-modal', 'true');
}

function hideModalElement(modalEl) {
  if (!modalEl) {
    return;
  }

  const bootstrap = ensureBootstrapModal();
  if (bootstrap) {
    const instance = bootstrap.Modal.getOrCreateInstance(modalEl);
    instance.hide();
    return;
  }

  modalEl.classList.remove('show');
  modalEl.style.removeProperty('display');
  modalEl.setAttribute('aria-hidden', 'true');
  modalEl.removeAttribute('aria-modal');
}

function updateButtonToBusyState(button) {
  const busyLabel = button.getAttribute('data-training-busy-label');
  if (!busyLabel) {
    return;
  }

  button.dataset.trainingOriginalHtml = button.innerHTML;

  const spinner = document.createElement('span');
  spinner.className = 'spinner-border spinner-border-sm me-2';
  spinner.setAttribute('role', 'status');
  spinner.setAttribute('aria-hidden', 'true');

  const label = document.createElement('span');
  label.textContent = busyLabel;

  button.innerHTML = '';
  button.append(spinner, label);
}

function disableFormOnSubmit(form) {
  form.addEventListener('submit', event => {
    if (event.defaultPrevented) {
      return;
    }

    const submitter = event.submitter || form.querySelector('[type="submit"]');
    if (!submitter || submitter.disabled) {
      return;
    }

    submitter.disabled = true;
    updateButtonToBusyState(submitter);

    const modalEl = form.closest('.modal');
    if (modalEl) {
      hideModalElement(modalEl);
    }
  });
}

function initAutoShowExportModal() {
  const modals = document.querySelectorAll('[data-training-export-auto-show="true"]');
  modals.forEach(showModalElement);
}

function initTrainingExportForms() {
  const forms = document.querySelectorAll('.training-export-form');
  forms.forEach(disableFormOnSubmit);
}

function initTrainingYearChart() {
  const host = document.querySelector('[data-training-year]');
  if (!host) {
    return;
  }

  const raw = host.dataset.trainingYear;
  if (!raw) {
    return;
  }

  let rows;
  try {
    rows = JSON.parse(raw);
  } catch (err) {
    console.warn('training-year: invalid json', err);
    return;
  }

  if (!Array.isArray(rows) || rows.length === 0) {
    return;
  }

  const ChartCtor = window.Chart;
  if (typeof ChartCtor !== 'function') {
    console.warn('training-year: Chart.js not available');
    return;
  }

  const canvas = document.getElementById('training-year-trend-canvas');
  if (!canvas) {
    return;
  }

  const labels = rows.map(row => row.trainingYearLabel);
  const simulatorData = rows.map(row => row.simulatorTrainings ?? 0);
  const droneData = rows.map(row => row.droneTrainings ?? 0);

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
          stack: 'trainings'
        },
        {
          label: 'Drone',
          data: droneData,
          backgroundColor: 'rgba(14,165,233,0.85)',
          borderRadius: 6,
          stack: 'trainings'
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
            label: context => `${context.dataset.label}: ${context.parsed.y}`
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
          grid: {
            display: false
          }
        },
        y: {
          stacked: true,
          beginAtZero: true,
          ticks: {
            precision: 0
          },
          grid: {
            color: 'rgba(148,163,184,0.2)'
          }
        }
      }
    }
  });
}

function initDownloadButtons() {
  document.querySelectorAll('[data-action="download-png"][data-target]').forEach(button => {
    button.addEventListener('click', () => {
      const targetId = button.dataset.target;
      if (!targetId) {
        return;
      }

      const canvas = document.getElementById(targetId);
      if (!canvas) {
        return;
      }

      const link = document.createElement('a');
      link.href = canvas.toDataURL('image/png');
      link.download = `${targetId}.png`;
      link.click();
    });
  });
}

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
