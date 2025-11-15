// SECTION: Project pulse micro-charts
(function () {
  if (typeof Chart === 'undefined') {
    return;
  }

  const selectAll = (selector, root = document) => Array.from(root.querySelectorAll(selector));

  function sparkline(canvas, series) {
    const ctx = canvas.getContext('2d');
    new Chart(ctx, {
      type: 'line',
      data: {
        labels: series.map((_, index) => index + 1),
        datasets: [
          {
            data: series,
            borderWidth: 2,
            borderColor: '#2563eb',
            backgroundColor: 'rgba(37, 99, 235, 0.15)',
            fill: true,
            tension: 0.3,
            pointRadius: 0
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: { enabled: false }
        },
        scales: {
          x: { display: false },
          y: { display: false }
        }
      }
    });
  }

  function stackedBar(canvas, buckets) {
    const labels = buckets.map((_, index) => index + 1);
    const completed = buckets.map((bucket) => bucket.completed ?? bucket.Completed ?? 0);
    const ongoing = buckets.map((bucket) => bucket.ongoing ?? bucket.Ongoing ?? 0);
    const ctx = canvas.getContext('2d');

    new Chart(ctx, {
      type: 'bar',
      data: {
        labels,
        datasets: [
          { data: completed, borderWidth: 0, backgroundColor: '#16a34a', stack: 'ppulse' },
          { data: ongoing, borderWidth: 0, backgroundColor: '#2563eb', stack: 'ppulse' }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: { enabled: false }
        },
        scales: {
          x: { display: false, stacked: true },
          y: { display: false, stacked: true }
        }
      }
    });
  }

  function initCard(selector, type) {
    selectAll(selector).forEach((card) => {
      const canvas = card.querySelector('canvas[data-chart]');
      if (!canvas) {
        return;
      }

      const attr = type === 'stackedbar' ? card.getAttribute('data-weekly') : card.getAttribute('data-series');
      if (!attr) {
        return;
      }

      let payload;
      try {
        payload = JSON.parse(attr);
      } catch {
        payload = null;
      }

      if (!payload || (Array.isArray(payload) && payload.length === 0)) {
        return;
      }

      if (type === 'stackedbar') {
        stackedBar(canvas, Array.isArray(payload) ? payload : []);
      } else {
        sparkline(canvas, Array.isArray(payload) ? payload : []);
      }
    });
  }

  function init() {
    const root = document.querySelector('[data-ppulse]');
    if (!root) {
      return;
    }

    initCard('[data-pp-all]', 'stackedbar');
    initCard('[data-pp-done]', 'sparkline');
    initCard('[data-pp-doing]', 'sparkline');
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init, { once: true });
  } else {
    init();
  }
})();
