const monthFormatter = new Intl.DateTimeFormat('en-GB', { month: 'short' });
const formatDisplayDate = (date) => {
  const d = date instanceof Date ? date : new Date(date);
  return `${String(d.getDate()).padStart(2,'0')} ${monthFormatter.format(d)} ${d.getFullYear()}`;
};
const formatDisplayDateTime = (date) => {
  const d = date instanceof Date ? date : new Date(date);
  const hh = String(d.getHours()).padStart(2,'0');
  const mm = String(d.getMinutes()).padStart(2,'0');
  return `${formatDisplayDate(d)} ${hh}:${mm}`;
};

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

function withAlpha(color, alpha) {
  if (!color) return '';
  const hexMatch = color.match(/^#?([a-f\d]{6})$/i);
  if (!hexMatch) {
    return color;
  }
  const hex = hexMatch[1];
  const intVal = parseInt(hex, 16);
  const r = (intVal >> 16) & 255;
  const g = (intVal >> 8) & 255;
  const b = intVal & 255;
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}
// END SECTION

const BandAndLines = {
  id: 'bandAndLines',
  afterDraw(chart, args, opts) {
    const palette = getPalette();
    const { workStartMin, workEndMin, p50Min, p90Min } = opts;
    const { ctx, chartArea, scales: { x, y } } = chart;
    if (!x || !y) return;

    const yTop = y.getPixelForValue(workEndMin);
    const yBot = y.getPixelForValue(workStartMin);
    ctx.save();
    ctx.fillStyle = withAlpha(palette.accents[2], 0.18);
    ctx.fillRect(chartArea.left, yTop, chartArea.right - chartArea.left, yBot - yTop);

    if (Number.isFinite(p50Min)) drawLine(y.getPixelForValue(p50Min), 'Median');
    if (Number.isFinite(p90Min)) drawLine(y.getPixelForValue(p90Min), 'P90');

    ctx.restore();

    function drawLine(yPix, label) {
      ctx.setLineDash([6,4]);
      ctx.strokeStyle = withAlpha(palette.axisColor, 0.6);
      ctx.beginPath();
      ctx.moveTo(chartArea.left, yPix);
      ctx.lineTo(chartArea.right, yPix);
      ctx.stroke();
      ctx.setLineDash([]);
      ctx.fillStyle = withAlpha(palette.axisColor, 0.85);
      ctx.font = '12px system-ui, -apple-system, Segoe UI, Roboto, Arial';
      ctx.textBaseline = 'bottom';
      ctx.fillText(label, chartArea.right - 48, yPix - 4);
    }
  }
};

function init() {
  const ChartCtor = window.Chart;
  const elCanvas = document.getElementById('loginsScatter');
  const elLookback = document.getElementById('lookback');
  const elWeekend = document.getElementById('weekendOdd');
  const elUser = document.getElementById('user');
  const elExport = document.getElementById('export');
  const elRefresh = document.getElementById('refresh');

  if (!ChartCtor || !elCanvas || !elLookback || !elWeekend || !elUser || !elExport) {
    return;
  }

  let chart;
  let lastData = null;

  async function load() {
    if (!ChartCtor || !elCanvas) {
      return;
    }
    const days = parseInt(elLookback.value, 10) || 30;
    const weekendOdd = elWeekend.checked;
    const user = elUser.value;
    const res = await fetch(`?handler=Data&days=${days}&weekendOdd=${weekendOdd}&user=${encodeURIComponent(user)}`, { headers: { Accept: 'application/json' } });
    const data = await res.json();
    lastData = data;
    renderChart(data);
  }

  function renderChart(data) {
    const palette = getPalette();
    const normal = [];
    const odd = [];
    for (const p of data.points) {
      const d = new Date(p.t);
      const point = { x: d.getTime(), y: p.m, reason: p.reason, userId: p.user, userName: p.userName, iso: p.t };
      (p.odd ? odd : normal).push(point);
    }

    const n = data.points.length;
    const p50 = n ? data.p50Min : null;
    const p90 = n ? data.p90Min : null;

    const cfg = {
      type: 'scatter',
      data: {
        datasets: [
          { label: 'Normal', data: normal, pointRadius: 3, pointBackgroundColor: palette.accents[0] },
          { label: 'Odd', data: odd, pointRadius: 4, pointBackgroundColor: palette.accents[1] }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        parsing: false,
        scales: {
          x: {
            type: 'linear',
            ticks: {
              callback: (v) => formatDisplayDate(new Date(v)),
              color: palette.axisColor
            },
            title: { display: true, text: 'Date', color: palette.axisColor },
            grid: { color: palette.gridColor }
          },
          y: {
            min: 0,
            max: 1440,
            ticks: {
              stepSize: 60,
              callback: v => `${String(Math.floor(v/60)).padStart(2,'0')}:00`,
              color: palette.axisColor
            },
            title: { display: true, text: 'Time of day', color: palette.axisColor },
            grid: { color: palette.gridColor }
          }
        },
        plugins: {
          decimation: { enabled: true, algorithm: 'min-max' },
          legend: { position: 'bottom', labels: { color: palette.axisColor } },
          tooltip: {
            callbacks: {
              label: ctx => {
                const hh = String(Math.floor(ctx.raw.y/60)).padStart(2,'0');
                const mm = String(ctx.raw.y%60).padStart(2,'0');
                const date = formatDisplayDate(new Date(ctx.raw.x));
                return `${date} ${hh}:${mm} — ${ctx.raw.userName}${ctx.raw.reason ? ' · '+ctx.raw.reason : ''}`;
              }
            }
          },
          bandAndLines: {
            workStartMin: data.workStartMin,
            workEndMin: data.workEndMin,
            p50Min: p50,
            p90Min: p90
          }
        }
      },
      plugins: [BandAndLines]
    };

    if (chart) chart.destroy();
    chart = new ChartCtor(elCanvas, cfg);

    chart.canvas.onclick = (evt) => {
      const points = chart.getElementsAtEventForMode(evt, 'nearest', { intersect: true }, true);
      if (points.length) {
        const p = chart.data.datasets[points[0].datasetIndex].data[points[0].index];
        const from = new Date(p.iso).toISOString();
        window.location.href = `/Admin/Logs?User=${encodeURIComponent(p.userId)}&From=${from}&To=${from}`;
      }
    };

    renderOddTable(data.points.filter(p => p.odd));
  }

  function exportCsv() {
    const days = parseInt(elLookback.value, 10) || 30;
    const weekendOdd = elWeekend.checked;
    const user = elUser.value;
    elExport.href = `?handler=ExportCsv&days=${days}&weekendOdd=${weekendOdd}&user=${encodeURIComponent(user)}`;
  }

  elRefresh?.addEventListener('click', () => {
    load();
    exportCsv();
  });
  [elLookback, elWeekend, elUser].forEach(el => el?.addEventListener('change', () => {
    load();
    exportCsv();
  }));

  exportCsv();
  load();

}

function renderOddTable(rows) {
  const host = document.getElementById('oddRows');
  if (!host) return;
  host.innerHTML = '';
  for (const r of rows) {
    const d = new Date(r.t);
    const hh = String(d.getHours()).padStart(2,'0');
    const mm = String(d.getMinutes()).padStart(2,'0');
    const tr = document.createElement('tr');
    const tdDate = document.createElement('td');
    tdDate.textContent = formatDisplayDateTime(d);
    const tdUser = document.createElement('td');
    tdUser.textContent = r.userName;
    const tdReason = document.createElement('td');
    tdReason.textContent = r.reason || '';
    tr.append(tdDate, tdUser, tdReason);
    host.appendChild(tr);
  }
}

init();
