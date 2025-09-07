export function initCharts() {
  const el = document.getElementById('loginsPerDayChart');
  if (!el) return;

  // read serialized arrays from data-* attributes
  let labels = [];
  let values = [];
  try {
    labels = JSON.parse(el.dataset.labels || '[]');
    values = JSON.parse(el.dataset.values || '[]');
  } catch {}

  // pad to two points so the chart doesn’t look empty on day 1
  if (labels.length === 1) { labels = [labels[0], labels[0]]; values = [values[0], values[0]]; }

  const ctx = el.getContext('2d');
  // eslint-disable-next-line no-undef
  new Chart(ctx, {
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
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false, // use container height
      plugins: { legend: { display: false }, tooltip: { mode: 'index', intersect: false } },
      scales: {
        x: { grid: { display: false } },
        y: { beginAtZero: true, grid: { color: 'rgba(0,0,0,.06)' }, ticks: { precision: 0 } }
      },
      elements: { line: { borderColor: '#1a73e8' }, point: { backgroundColor: '#1a73e8' } }
    }
  });
}

// run once DOM is ready
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initCharts, { once: true });
} else {
  initCharts();
}
