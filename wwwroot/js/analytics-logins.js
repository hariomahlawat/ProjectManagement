// Chart is provided globally by Logins.cshtml
const Chart = window.Chart;
const elCanvas = document.getElementById('loginsScatter');
const elLookback = document.getElementById('lookback');
const elWeekend = document.getElementById('weekendOdd');
const elUser = document.getElementById('user');
const elExport = document.getElementById('export');
let chart;

const BandAndLines = {
  id: 'bandAndLines',
  afterDraw(chart, args, opts) {
    const { workStartMin, workEndMin, p50Min, p90Min } = opts;
    const { ctx, chartArea, scales: { x, y } } = chart;
    if (!x || !y) return;

    const yTop = y.getPixelForValue(workEndMin);
    const yBot = y.getPixelForValue(workStartMin);
    ctx.save();
    ctx.fillStyle = 'rgba(46, 204, 113, 0.12)';
    ctx.fillRect(chartArea.left, yTop, chartArea.right - chartArea.left, yBot - yTop);

    if (Number.isFinite(p50Min)) drawLine(y.getPixelForValue(p50Min), 'Median');
    if (Number.isFinite(p90Min)) drawLine(y.getPixelForValue(p90Min), 'P90');

    ctx.restore();

    function drawLine(yPix, label) {
      ctx.setLineDash([6,4]);
      ctx.strokeStyle = 'rgba(0,0,0,0.35)';
      ctx.beginPath();
      ctx.moveTo(chartArea.left, yPix);
      ctx.lineTo(chartArea.right, yPix);
      ctx.stroke();
      ctx.setLineDash([]);
      ctx.fillStyle = 'rgba(0,0,0,0.6)';
      ctx.font = '12px system-ui, -apple-system, Segoe UI, Roboto, Arial';
      ctx.textBaseline = 'bottom';
      ctx.fillText(label, chartArea.right - 48, yPix - 4);
    }
  }
};

async function load() {
  const days = parseInt(elLookback.value, 10) || 30;
  const weekendOdd = elWeekend.checked;
  const user = elUser.value;
  const res = await fetch(`?handler=Data&days=${days}&weekendOdd=${weekendOdd}&user=${encodeURIComponent(user)}`, { headers: { 'Accept':'application/json' } });
  const data = await res.json();

  const normal = [];
  const odd = [];
  for (const p of data.points) {
    const d = new Date(p.t);
    const point = { x: d.getTime(), y: p.m, reason: p.reason, user: p.user, iso: p.t };
    (p.odd ? odd : normal).push(point);
  }

  const n = data.points.length;
  const p50 = n ? data.p50Min : null;
  const p90 = n ? data.p90Min : null;

  const cfg = {
    type: 'scatter',
    data: {
      datasets: [
        { label: 'Normal', data: normal, pointRadius: 3 },
        { label: 'Odd', data: odd, pointRadius: 4, pointBackgroundColor: '#d9534f' }
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
            callback: (v) => {
              const d = new Date(v);
              return `${d.getDate()}/${d.getMonth()+1}`;
            }
          },
          title: { display: true, text: 'Date' }
        },
        y: {
          min: 0, max: 1440,
          ticks: {
            stepSize: 60,
            callback: v => `${String(Math.floor(v/60)).padStart(2,'0')}:00`
          },
          title: { display: true, text: 'Time of day' }
        }
      },
      plugins: {
        decimation: { enabled: true, algorithm: 'min-max' },
        legend: { position: 'bottom' },
        tooltip: {
          callbacks: {
            label: ctx => {
              const hh = String(Math.floor(ctx.raw.y/60)).padStart(2,'0');
              const mm = String(ctx.raw.y%60).padStart(2,'0');
              const d = new Date(ctx.raw.x);
              const date = d.toLocaleDateString();
              return `${date} ${hh}:${mm}${ctx.raw.reason ? ' — '+ctx.raw.reason : ''}`;
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
  chart = new Chart(elCanvas, cfg);

  chart.canvas.onclick = (evt) => {
    const points = chart.getElementsAtEventForMode(evt, 'nearest', { intersect: true }, true);
    if (points.length) {
      const p = chart.data.datasets[points[0].datasetIndex].data[points[0].index];
      const from = new Date(p.iso).toISOString();
      window.location.href = `/Admin/Logs?User=${encodeURIComponent(p.user)}&From=${from}&To=${from}`;
    }
  };

  renderOddTable(data.points.filter(p => p.odd));
}

function renderOddTable(rows) {
  const host = document.getElementById('oddRows');
  host.innerHTML = '';
  for (const r of rows) {
    const d = new Date(r.t);
    const hh = String(d.getHours()).padStart(2,'0');
    const mm = String(d.getMinutes()).padStart(2,'0');
    const tr = document.createElement('tr');
    tr.innerHTML = `<td>${d.toLocaleDateString()} ${hh}:${mm}</td><td>${r.user}</td><td>${r.reason || ''}</td>`;
    host.appendChild(tr);
  }
}

function exportCsv() {
  const days = parseInt(elLookback.value, 10) || 30;
  const weekendOdd = elWeekend.checked;
  const user = elUser.value;
  elExport.href = `?handler=ExportCsv&days=${days}&weekendOdd=${weekendOdd}&user=${encodeURIComponent(user)}`;
}

document.getElementById('refresh')?.addEventListener('click', () => { load(); exportCsv(); });
[elLookback, elWeekend, elUser].forEach(el => el?.addEventListener('change', () => { load(); exportCsv(); }));
exportCsv();
load();
