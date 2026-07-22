const fallbackPalette = {
  axisColor: '#64748b',
  gridColor: '#e2e8f0',
  neutral: '#94a3b8',
  accents: ['#315efb', '#d97706', '#dc2626', '#16a34a']
};

function palette() {
  const configured = window.PMTheme?.getChartPalette?.();
  return configured && Array.isArray(configured.accents) && configured.accents.length >= 3
    ? configured
    : fallbackPalette;
}

function alpha(hex, opacity) {
  const value = String(hex || '').trim();
  const match = value.match(/^#([\da-f]{6})$/i);
  if (!match) return value;
  const number = Number.parseInt(match[1], 16);
  return `rgba(${(number >> 16) & 255}, ${(number >> 8) & 255}, ${number & 255}, ${opacity})`;
}

function parseData(element, attribute) {
  try {
    return JSON.parse(element.dataset[attribute] || '[]');
  } catch (error) {
    console.error(`Unable to parse ${attribute}.`, error);
    return [];
  }
}

function formatMinutes(value) {
  const minutes = Math.max(0, Math.min(1439, Number(value) || 0));
  return `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
}

const timeBandPlugin = {
  id: 'adminLoginTimeBand',
  beforeDatasetsDraw(chart, _args, options) {
    const y = chart.scales.y;
    const area = chart.chartArea;
    if (!y || !area) return;

    const top = y.getPixelForValue(Number(options.workEnd));
    const bottom = y.getPixelForValue(Number(options.workStart));
    const colors = palette();
    chart.ctx.save();
    chart.ctx.fillStyle = alpha(colors.accents[3] || '#16a34a', 0.09);
    chart.ctx.fillRect(area.left, top, area.right - area.left, bottom - top);
    chart.ctx.restore();
  },
  afterDatasetsDraw(chart, _args, options) {
    const y = chart.scales.y;
    const area = chart.chartArea;
    if (!y || !area) return;

    const colors = palette();
    const lines = [
      { value: Number(options.median), label: 'Median' },
      { value: Number(options.p90), label: 'P90' }
    ].filter(item => Number.isFinite(item.value) && item.value > 0);

    chart.ctx.save();
    chart.ctx.font = '11px system-ui, -apple-system, Segoe UI, sans-serif';
    chart.ctx.textBaseline = 'bottom';
    for (const line of lines) {
      const pixel = y.getPixelForValue(line.value);
      chart.ctx.setLineDash([5, 4]);
      chart.ctx.strokeStyle = alpha(colors.axisColor || '#64748b', 0.62);
      chart.ctx.beginPath();
      chart.ctx.moveTo(area.left, pixel);
      chart.ctx.lineTo(area.right, pixel);
      chart.ctx.stroke();
      chart.ctx.setLineDash([]);
      chart.ctx.fillStyle = colors.axisColor || '#64748b';
      chart.ctx.fillText(line.label, area.right - 42, pixel - 3);
    }
    chart.ctx.restore();
  }
};

function initialiseFilters() {
  const form = document.querySelector('.admin-filter-toolbar--monitoring');
  if (!form) return;

  let submitTimer;
  for (const control of form.querySelectorAll('[data-admin-monitoring-submit]')) {
    control.addEventListener('change', () => {
      window.clearTimeout(submitTimer);
      submitTimer = window.setTimeout(() => form.requestSubmit(), 120);
    });
  }
}

function initialiseTrendChart() {
  const canvas = document.getElementById('adminLoginTrend');
  if (!canvas || !window.Chart) return;

  const colors = palette();
  const rows = parseData(canvas, 'series');
  new window.Chart(canvas, {
    type: 'line',
    data: {
      labels: rows.map(row => row.date),
      datasets: [
        {
          label: 'Successful',
          data: rows.map(row => row.successful),
          borderColor: colors.accents[0] || '#315efb',
          backgroundColor: alpha(colors.accents[0] || '#315efb', 0.11),
          fill: true,
          tension: 0.28,
          pointRadius: rows.length > 60 ? 0 : 2,
          borderWidth: 2
        },
        {
          label: 'Failed',
          data: rows.map(row => row.failed),
          borderColor: colors.accents[1] || '#d97706',
          backgroundColor: alpha(colors.accents[1] || '#d97706', 0.08),
          tension: 0.28,
          pointRadius: rows.length > 60 ? 0 : 2,
          borderWidth: 2
        },
        {
          label: 'Locked out',
          data: rows.map(row => row.lockedOut),
          borderColor: colors.accents[2] || '#dc2626',
          backgroundColor: alpha(colors.accents[2] || '#dc2626', 0.08),
          tension: 0.28,
          pointRadius: rows.length > 60 ? 0 : 2,
          borderWidth: 2
        }
      ]
    },
    options: {
      maintainAspectRatio: false,
      interaction: { mode: 'index', intersect: false },
      plugins: {
        legend: { position: 'bottom', labels: { usePointStyle: true, boxWidth: 8 } },
        tooltip: { callbacks: { title: items => items[0]?.label || '' } }
      },
      scales: {
        x: { grid: { display: false }, ticks: { color: colors.axisColor, maxTicksLimit: 10 } },
        y: { beginAtZero: true, ticks: { precision: 0, color: colors.axisColor }, grid: { color: colors.gridColor } }
      }
    }
  });
}

function initialisePatternChart() {
  const canvas = document.getElementById('adminLoginPattern');
  if (!canvas || !window.Chart) return;

  const rows = parseData(canvas, 'points');
  const colors = palette();
  const categories = [
    {
      label: 'Expected pattern',
      rows: rows.filter(row => !row.review && row.tone === 'success'),
      color: colors.accents[0] || '#315efb'
    },
    {
      label: 'Requires review',
      rows: rows.filter(row => row.review && row.outcome === 'Successful'),
      color: colors.accents[1] || '#d97706'
    },
    {
      label: 'Failed or locked',
      rows: rows.filter(row => row.outcome === 'Failed' || row.outcome === 'Locked out'),
      color: colors.accents[2] || '#dc2626'
    }
  ];

  const datasets = categories.map(category => ({
    label: category.label,
    data: category.rows.map(row => ({ x: Date.parse(`${row.date}T00:00:00Z`), y: row.minutes, source: row })),
    parsing: false,
    pointRadius: category.rows.length > 750 ? 2 : 3.5,
    pointHoverRadius: 6,
    backgroundColor: alpha(category.color, 0.82),
    borderColor: category.color,
    borderWidth: 1
  }));

  new window.Chart(canvas, {
    type: 'scatter',
    data: { datasets },
    plugins: [timeBandPlugin],
    options: {
      maintainAspectRatio: false,
      animation: rows.length < 1000,
      plugins: {
        legend: { display: false },
        adminLoginTimeBand: {
          workStart: Number(canvas.dataset.workStart),
          workEnd: Number(canvas.dataset.workEnd),
          median: Number(canvas.dataset.median),
          p90: Number(canvas.dataset.p90)
        },
        tooltip: {
          callbacks: {
            title: items => {
              const source = items[0]?.raw?.source;
              if (!source) return '';
              return source.when || `${source.date} · ${formatMinutes(source.minutes)} IST`;
            },
            label: item => {
              const source = item.raw?.source;
              if (!source) return '';
              return `${source.user} (@${source.login}) · ${source.outcome}`;
            },
            afterLabel: item => item.raw?.source?.reason || ''
          }
        }
      },
      scales: {
        x: {
          type: 'linear',
          grid: { color: colors.gridColor },
          ticks: {
            color: colors.axisColor,
            maxTicksLimit: 12,
            callback: value => new Intl.DateTimeFormat('en-IN', { day: '2-digit', month: 'short', timeZone: 'UTC' }).format(new Date(value))
          }
        },
        y: {
          min: 0,
          max: 1440,
          grid: { color: colors.gridColor },
          ticks: { color: colors.axisColor, stepSize: 120, callback: value => formatMinutes(value) },
          title: { display: true, text: 'Time of day (IST)', color: colors.axisColor }
        }
      }
    }
  });
}

function initialise() {
  initialiseFilters();
  initialiseTrendChart();
  initialisePatternChart();
}

document.readyState === 'loading'
  ? document.addEventListener('DOMContentLoaded', initialise, { once: true })
  : initialise();
