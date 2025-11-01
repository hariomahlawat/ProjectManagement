(function () {
  const el = document.getElementById('iprStatusChart');
  if (!el || typeof Chart === 'undefined') return;

  const css = getComputedStyle(document.documentElement);
  const colors = {
    filing: css.getPropertyValue('--bs-indigo')?.trim() || '#4c6ef5',
    filed: css.getPropertyValue('--bs-blue')?.trim() || '#0d6efd',
    granted: css.getPropertyValue('--bs-teal')?.trim() || '#20c997',
    rejected: css.getPropertyValue('--bs-red')?.trim() || '#dc3545',
    withdrawn: css.getPropertyValue('--bs-gray-600')?.trim() || '#6c757d'
  };

  const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  const centerTextPlugin = {
    id: 'centerText',
    afterDraw(chart) {
      const ds = chart.data.datasets[0];
      if (!ds || !Array.isArray(ds.data)) return;
      const total = ds.data.reduce((a, b) => a + b, 0);
      const meta = chart.getDatasetMeta(0);
      if (!meta || !meta.data || meta.data.length === 0) return;
      const center = meta.data[0];
      if (!center) return;

      const { ctx } = chart;
      ctx.save();
      ctx.font = '600 16px system-ui,-apple-system,Segoe UI,Roboto';
      ctx.fillStyle = getComputedStyle(document.body).getPropertyValue('--bs-body-color') || '#212529';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(String(total), center.x, center.y);
      ctx.restore();
    }
  };

  let showLabels = false;
  const arcLabelsPlugin = {
    id: 'arcLabels',
    afterDatasetsDraw(chart) {
      if (!showLabels) return;
      const { ctx } = chart;
      const meta = chart.getDatasetMeta(0);
      const ds = chart.data.datasets[0];
      const labels = chart.data.labels;
      if (!meta || !meta.data || !ds || !Array.isArray(ds.data) || !Array.isArray(labels)) return;
      const total = ds.data.reduce((a, b) => a + b, 0) || 1;

      ctx.save();
      ctx.font = '500 11px system-ui,-apple-system,Segoe UI,Roboto';
      ctx.fillStyle = '#111';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';

      meta.data.forEach((arc, i) => {
        const value = ds.data[i];
        if (!value) return;
        const p = Math.round((value * 100) / total);
        const r = (arc.outerRadius + arc.innerRadius) / 2;
        const angle = (arc.startAngle + arc.endAngle) / 2;
        const x = arc.x + Math.cos(angle) * r;
        const y = arc.y + Math.sin(angle) * r;
        ctx.fillText(`${labels[i]}: ${value} (${p}%)`, x, y);
      });

      ctx.restore();
    }
  };

  Chart.register(centerTextPlugin, arcLabelsPlugin);

  fetch('/ProjectOfficeReports/Ipr?handler=Summary', { credentials: 'same-origin' })
    .then(r => (r.ok ? r.json() : null))
    .then(d => {
      if (!d) return;

      const data = [d.filing, d.filed, d.granted, d.rejected, d.withdrawn];
      const labels = ['Filing', 'Filed', 'Granted', 'Rejected', 'Withdrawn'];

      const chart = new Chart(el, {
        type: 'doughnut',
        data: {
          labels,
          datasets: [
            {
              data,
              backgroundColor: [colors.filing, colors.filed, colors.granted, colors.rejected, colors.withdrawn],
              borderWidth: 0
            }
          ]
        },
        options: {
          responsive: true,
          maintainAspectRatio: true,
          aspectRatio: 2.0,
          cutout: '66%',
          plugins: {
            legend: { display: false },
            tooltip: {
              callbacks: {
                label: ctx => {
                  const total = ctx.dataset.data.reduce((a, b) => a + b, 0) || 1;
                  const val = ctx.parsed;
                  const pct = Math.round((val * 100) / total);
                  return `${ctx.label}: ${val} (${pct}%)`;
                }
              }
            }
          },
          layout: { padding: 8 },
          animation: prefersReducedMotion ? false : { duration: 400 }
        }
      });

      const toggleBtn = document.getElementById('iprToggleLabelsBtn');
      if (toggleBtn) {
        toggleBtn.addEventListener('click', () => {
          showLabels = !showLabels;
          chart.update();
        });
      }

      const dlBtn = document.getElementById('iprDownloadBtn');
      if (dlBtn) {
        dlBtn.addEventListener('click', () => {
          const a = document.createElement('a');
          const now = new Date();
          const stamp = now.toISOString().replace(/[:.]/g, '-');
          a.download = `ipr-status-${stamp}.png`;
          a.href = chart.toBase64Image('image/png', 1);
          a.click();
        });
      }
    })
    .catch(() => {});
})();
