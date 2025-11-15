const palette = [
  'rgba(13, 110, 253, 0.85)',
  'rgba(25, 135, 84, 0.9)',
  'rgba(255, 193, 7, 0.85)',
  'rgba(220, 53, 69, 0.8)',
  'rgba(13, 202, 240, 0.85)',
  'rgba(111, 66, 193, 0.8)'
];

function parseSeries(dataset) {
  if (!dataset) return null;
  try {
    const parsed = JSON.parse(dataset);
    if (!parsed || !Array.isArray(parsed.labels) || !Array.isArray(parsed.values)) {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

function createCompletedChart(ChartCtor, canvas, data) {
  const cfg = {
    type: 'bar',
    data: {
      labels: data.labels,
      datasets: [
        {
          data: data.values,
          backgroundColor: 'rgba(13, 110, 253, 0.5)',
          borderRadius: 4,
          maxBarThickness: 12
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: { legend: { display: false }, tooltip: { intersect: false } },
      scales: {
        x: { ticks: { maxRotation: 0, minRotation: 0 }, grid: { display: false } },
        y: { beginAtZero: true, ticks: { precision: 0 }, grid: { display: false } }
      }
    }
  };
  return new ChartCtor(canvas, cfg);
}

function createStageChart(ChartCtor, canvas, data) {
  const colors = data.labels.map((_, idx) => palette[idx % palette.length]);
  const cfg = {
    type: 'bar',
    data: {
      labels: data.labels,
      datasets: [
        {
          data: data.values,
          backgroundColor: colors,
          borderRadius: 6
        }
      ]
    },
    options: {
      indexAxis: 'y',
      responsive: true,
      maintainAspectRatio: false,
      plugins: { legend: { display: false }, tooltip: { intersect: false } },
      scales: {
        x: { beginAtZero: true, ticks: { precision: 0 }, grid: { display: false } },
        y: { grid: { display: false } }
      }
    }
  };
  return new ChartCtor(canvas, cfg);
}

function createRepositoryChart(ChartCtor, canvas, data) {
  const colors = data.labels.map((_, idx) => palette[idx % palette.length]);
  const cfg = {
    type: 'doughnut',
    data: {
      labels: data.labels,
      datasets: [
        {
          data: data.values,
          backgroundColor: colors,
          borderWidth: 0
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      cutout: '55%',
      plugins: { legend: { display: false }, tooltip: { intersect: false } }
    }
  };
  return new ChartCtor(canvas, cfg);
}

function wireTelemetry(root) {
  if (!root) return;
  root.querySelectorAll('[data-pulse-telemetry]').forEach((el) => {
    el.addEventListener('click', () => {
      const evt = el.dataset.pulseTelemetry;
      if (!evt) return;
      const telemetry = window.pmTelemetry;
      if (telemetry && typeof telemetry.emit === 'function') {
        try {
          telemetry.emit(evt);
        } catch {
          /* no-op */
        }
      }
    });
  });
}

export function initProjectPulse(root = document) {
  const host = root?.querySelector('[data-project-pulse]');
  if (!host) {
    return;
  }

  const ChartCtor = window.Chart;
  if (!ChartCtor) {
    wireTelemetry(host);
    return;
  }

  host.querySelectorAll('.pulse-chart').forEach((wrapper) => {
    const type = wrapper.dataset.chartType;
    const canvas = wrapper.querySelector('canvas[data-series]');
    const series = parseSeries(canvas?.dataset.series);
    if (!canvas || !series) {
      return;
    }

    if (type === 'stages') {
      createStageChart(ChartCtor, canvas, series);
    } else if (type === 'repository') {
      createRepositoryChart(ChartCtor, canvas, series);
    } else {
      createCompletedChart(ChartCtor, canvas, series);
    }
  });

  wireTelemetry(host);
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => initProjectPulse(document));
} else {
  initProjectPulse(document);
}
