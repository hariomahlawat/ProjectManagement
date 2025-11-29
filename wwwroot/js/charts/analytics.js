// SECTION: Palette helpers
function getPalette() {
  const fallback = {
    axisColor: '#4b5563',
    gridColor: '#e5e7eb',
    accents: ['#2563eb', '#f97316', '#22c55e', '#a855f7']
  };

  if (window.PMTheme && typeof window.PMTheme.getChartPalette === 'function') {
    return window.PMTheme.getChartPalette();
  }

  return fallback;
}
// END SECTION

// SECTION: Chart initialiser
let chartInstance = null;

function initCharts() {
  const el = document.getElementById('loginsPerDayChart');
  if (!el || !window.Chart) return;

  let labels = [];
  let values = [];
  try {
    labels = JSON.parse(el.dataset.labels || '[]');
    values = JSON.parse(el.dataset.values || '[]');
  } catch {
    // ignore JSON parsing errors and keep empty arrays
  }

  if (labels.length === 1) {
    labels = [labels[0], labels[0]];
    values = [values[0], values[0]];
  }

  const palette = getPalette();
  const lineColor = palette.accents[0] || '#2563eb';

  if (chartInstance) {
    chartInstance.destroy();
  }

  chartInstance = new window.Chart(el.getContext('2d'), {
    type: 'line',
    data: {
      labels,
      datasets: [{
        label: 'Logins',
        data: values,
        borderWidth: 2,
        tension: 0.3,
        pointRadius: 2,
        pointHoverRadius: 3,
        fill: false,
        borderColor: lineColor,
        pointBackgroundColor: lineColor
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: { mode: 'index', intersect: false }
      },
      scales: {
        x: {
          grid: { display: false },
          ticks: { color: palette.axisColor }
        },
        y: {
          beginAtZero: true,
          grid: { color: palette.gridColor },
          ticks: { precision: 0, color: palette.axisColor }
        }
      }
    }
  });
}
// END SECTION

// SECTION: Bootstrap
function boot() {
  initCharts();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
// END SECTION
