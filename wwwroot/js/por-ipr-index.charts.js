(function () {
  if (typeof Chart === 'undefined') {
    return;
  }

  const css = getComputedStyle(document.documentElement);
  const palette = {
    filing: css.getPropertyValue('--bs-indigo')?.trim() || '#7C4DFF',
    filed: css.getPropertyValue('--bs-blue')?.trim() || '#2E6BFF',
    granted: css.getPropertyValue('--bs-teal')?.trim() || '#2DB380',
    rejected: css.getPropertyValue('--bs-red')?.trim() || '#EF5350',
    withdrawn: css.getPropertyValue('--bs-gray-600')?.trim() || '#9E9E9E'
  };

  function downloadChartPng(canvasId, filenameBase) {
    const canvas = document.getElementById(canvasId);
    if (!canvas || typeof canvas.toDataURL !== 'function') {
      return;
    }

    const link = document.createElement('a');
    const today = new Date().toISOString().slice(0, 10);
    link.download = `${filenameBase}-${today}.png`;
    link.href = canvas.toDataURL('image/png', 1.0);
    link.click();
  }

  document.querySelectorAll('.pm-download-chart').forEach(btn => {
    btn.addEventListener('click', () => {
      const target = btn.dataset.chart;
      const fn = btn.dataset.fn || 'chart';
      if (!target) {
        return;
      }

      downloadChartPng(target, fn);
    });
  });

  const statusCanvas = document.getElementById('iprStatusChart');
  const toggleBtn = document.getElementById('iprToggleLabelsBtn');
  let statusChart = null;

  const prefersReducedMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  function buildStatusChart(summary) {
    const counts = {
      filing: Number(summary.filing ?? summary.Filing ?? 0),
      filed: Number(summary.filed ?? summary.Filed ?? 0),
      granted: Number(summary.granted ?? summary.Granted ?? 0),
      rejected: Number(summary.rejected ?? summary.Rejected ?? 0),
      withdrawn: Number(summary.withdrawn ?? summary.Withdrawn ?? 0)
    };

    const labels = ['Filing', 'Filed', 'Granted', 'Rejected', 'Withdrawn'];
    const data = [counts.filing, counts.filed, counts.granted, counts.rejected, counts.withdrawn];

    if (statusChart) {
      statusChart.destroy();
    }

    statusChart = new Chart(statusCanvas, {
      type: 'doughnut',
      data: {
        labels,
        datasets: [
          {
            data,
            backgroundColor: [
              palette.filing,
              palette.filed,
              palette.granted,
              palette.rejected,
              palette.withdrawn
            ],
            borderWidth: 0
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        cutout: '62%',
        layout: { padding: 8 },
        animation: prefersReducedMotion ? false : { duration: 400 },
        plugins: {
          legend: {
            display: true,
            position: 'right',
            labels: {
              boxWidth: 14,
              boxHeight: 14,
              font: { size: 12 }
            }
          },
          tooltip: {
            callbacks: {
              label(ctx) {
                const total = ctx.dataset.data.reduce((acc, val) => acc + val, 0) || 1;
                const value = ctx.parsed ?? 0;
                const pct = Math.round((value * 100) / total);
                return `${ctx.label}: ${value} (${pct}%)`;
              }
            }
          }
        }
      }
    });
  }

  if (statusCanvas) {
    fetch('/ProjectOfficeReports/Ipr?handler=Summary', { credentials: 'same-origin' })
      .then(response => (response.ok ? response.json() : null))
      .then(summary => {
        if (!summary) {
          return;
        }
        buildStatusChart(summary);
      })
      .catch(() => {});

    if (toggleBtn) {
      toggleBtn.addEventListener('click', () => {
        if (!statusChart) {
          return;
        }

        const legend = statusChart.options.plugins && statusChart.options.plugins.legend;
        const shouldShow = !(legend && legend.display === false);
        statusChart.options.plugins.legend.display = !shouldShow;
        statusChart.update();
      });
    }
  }

  const barCanvas = document.getElementById('iprYearBar');
  if (barCanvas) {
    let yearly = [];
    try {
      yearly = JSON.parse(barCanvas.dataset.yearly || '[]');
    } catch (error) {
      yearly = [];
    }

    const entries = Array.isArray(yearly) ? yearly : [];
    const filtered = entries.filter(item => {
      const total = Number(item.total ?? item.Total ?? 0);
      return total > 0;
    });

    if (filtered.length > 0) {
      const labels = filtered.map(item => Number(item.year ?? item.Year ?? 0));
      const datasetValues = key => filtered.map(item => Number(item[key] ?? item[key.charAt(0).toUpperCase() + key.slice(1)] ?? 0));

      new Chart(barCanvas, {
        type: 'bar',
        data: {
          labels,
          datasets: [
            { label: 'Filing', data: datasetValues('filing'), backgroundColor: palette.filing, stack: 's' },
            { label: 'Filed', data: datasetValues('filed'), backgroundColor: palette.filed, stack: 's' },
            { label: 'Granted', data: datasetValues('granted'), backgroundColor: palette.granted, stack: 's' },
            { label: 'Rejected', data: datasetValues('rejected'), backgroundColor: palette.rejected, stack: 's' },
            { label: 'Withdrawn', data: datasetValues('withdrawn'), backgroundColor: palette.withdrawn, stack: 's' }
          ]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          scales: {
            x: { stacked: true, grid: { display: false } },
            y: { stacked: true, beginAtZero: true, ticks: { precision: 0 } }
          },
          plugins: {
            legend: {
              position: 'bottom',
              labels: {
                boxWidth: 12,
                boxHeight: 12,
                font: { size: 11 }
              }
            },
            tooltip: {
              mode: 'index',
              intersect: false
            }
          }
        }
      });
    }
  }
})();
