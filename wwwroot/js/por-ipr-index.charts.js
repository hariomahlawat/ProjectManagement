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

  const prefersReducedMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

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
  const toggleLegendBtn = document.getElementById('iprToggleLabelsBtn');
  let statusChart = null;
  let legendHidden = false;

  if (toggleLegendBtn) {
    toggleLegendBtn.disabled = true;
    toggleLegendBtn.setAttribute('aria-pressed', 'false');
  }

  function setLegendButtonState(hidden) {
    if (!toggleLegendBtn) {
      return;
    }

    toggleLegendBtn.disabled = false;
    toggleLegendBtn.setAttribute('aria-pressed', hidden ? 'true' : 'false');
    toggleLegendBtn.innerHTML = hidden ? '<i class="bi bi-eye-slash"></i>' : '<i class="bi bi-eye"></i>';
  }

  function coerceNumber(value) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : 0;
  }

  function buildStatusChart(summary) {
    const counts = {
      filing: coerceNumber(summary.filing ?? summary.Filing),
      filed: coerceNumber(summary.filed ?? summary.Filed),
      granted: coerceNumber(summary.granted ?? summary.Granted),
      rejected: coerceNumber(summary.rejected ?? summary.Rejected),
      withdrawn: coerceNumber(summary.withdrawn ?? summary.Withdrawn)
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
            display: !legendHidden,
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
                const value = coerceNumber(ctx.parsed);
                const pct = Math.round((value * 100) / total);
                return `${ctx.label}: ${value} (${pct}%)`;
              }
            }
          }
        }
      }
    });

    setLegendButtonState(legendHidden);
  }

  if (statusCanvas) {
    const handleSummary = summary => {
      if (!summary) {
        return;
      }
      buildStatusChart(summary);
    };

    fetch('/ProjectOfficeReports/Ipr?handler=Summary', { credentials: 'same-origin' })
      .then(response => (response.ok ? response.json() : null))
      .then(summary => {
        if (summary) {
          handleSummary(summary);
        } else if (window.iprStatusCounts) {
          handleSummary(window.iprStatusCounts);
        }
      })
      .catch(() => {
        if (window.iprStatusCounts) {
          handleSummary(window.iprStatusCounts);
        }
      });

    if (toggleLegendBtn) {
      toggleLegendBtn.addEventListener('click', () => {
        if (!statusChart) {
          return;
        }

        legendHidden = !legendHidden;
        statusChart.options.plugins.legend.display = !legendHidden;
        statusChart.update();
        setLegendButtonState(legendHidden);
      });
    }
  }

  const barCanvas = document.getElementById('iprYearBar');
  const modeBtn = document.getElementById('iprBarModeBtn');
  let barChart = null;

  function getValue(source, key) {
    if (!source) {
      return 0;
    }
    if (Object.prototype.hasOwnProperty.call(source, key)) {
      return coerceNumber(source[key]);
    }
    const pascalKey = key.charAt(0).toUpperCase() + key.slice(1);
    if (Object.prototype.hasOwnProperty.call(source, pascalKey)) {
      return coerceNumber(source[pascalKey]);
    }
    return 0;
  }

  function getYearValue(source) {
    if (!source) {
      return '';
    }
    if (Object.prototype.hasOwnProperty.call(source, 'year')) {
      return source.year;
    }
    if (Object.prototype.hasOwnProperty.call(source, 'Year')) {
      return source.Year;
    }
    return '';
  }

  function buildStackedConfig(labels, ds) {
    return {
      type: 'bar',
      data: {
        labels,
        datasets: [
          { label: 'Filing', data: ds.filing, backgroundColor: palette.filing, stack: 's' },
          { label: 'Filed', data: ds.filed, backgroundColor: palette.filed, stack: 's' },
          { label: 'Granted', data: ds.granted, backgroundColor: palette.granted, stack: 's' },
          { label: 'Rejected', data: ds.rejected, backgroundColor: palette.rejected, stack: 's' },
          { label: 'Withdrawn', data: ds.withdrawn, backgroundColor: palette.withdrawn, stack: 's' }
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
    };
  }

  function buildTotalsConfig(labels, totals) {
    return {
      type: 'bar',
      data: {
        labels,
        datasets: [
          { label: 'Total', data: totals, backgroundColor: css.getPropertyValue('--bs-gray-600')?.trim() || '#75829C' }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: {
          x: { grid: { display: false } },
          y: { beginAtZero: true, ticks: { precision: 0 } }
        },
        plugins: {
          legend: { display: false },
          tooltip: { mode: 'index', intersect: false }
        }
      }
    };
  }

  function hydrateMiniTable(tableEl, yearly, overall) {
    if (!tableEl) {
      return;
    }

    const tbody = tableEl.querySelector('tbody');
    const tfoot = tableEl.querySelector('tfoot');
    if (!tbody || !tfoot) {
      return;
    }

    tbody.innerHTML = '';
    tfoot.innerHTML = '';

    yearly.forEach(entry => {
      const year = getYearValue(entry);
      const filing = getValue(entry, 'filing');
      const filed = getValue(entry, 'filed');
      const granted = getValue(entry, 'granted');
      const rejected = getValue(entry, 'rejected');
      const withdrawn = getValue(entry, 'withdrawn');
      const total = filing + filed + granted + rejected + withdrawn;

      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td>${year}</td>
        <td>${filing}</td>
        <td>${filed}</td>
        <td>${granted}</td>
        <td>${rejected}</td>
        <td>${withdrawn}</td>
        <td>${total}</td>`;
      tbody.appendChild(tr);
    });

    const totals = {
      filing: getValue(overall, 'filing'),
      filed: getValue(overall, 'filed'),
      granted: getValue(overall, 'granted'),
      rejected: getValue(overall, 'rejected'),
      withdrawn: getValue(overall, 'withdrawn'),
      total: getValue(overall, 'total')
    };

    const totalRow = document.createElement('tr');
    totalRow.classList.add('fw-semibold');
    totalRow.innerHTML = `
      <td>Total</td>
      <td>${totals.filing}</td>
      <td>${totals.filed}</td>
      <td>${totals.granted}</td>
      <td>${totals.rejected}</td>
      <td>${totals.withdrawn}</td>
      <td>${totals.total}</td>`;
    tfoot.appendChild(totalRow);
  }

  if (barCanvas) {
    let yearly = [];
    try {
      yearly = JSON.parse(barCanvas.dataset.yearly || '[]');
    } catch (error) {
      yearly = [];
    }

    let overall = {};
    try {
      overall = JSON.parse(barCanvas.dataset.totals || '{}');
    } catch (error) {
      overall = {};
    }

    const entries = Array.isArray(yearly) ? yearly : [];
    const filtered = entries.filter(item => {
      const total = getValue(item, 'filing') + getValue(item, 'filed') + getValue(item, 'granted') + getValue(item, 'rejected') + getValue(item, 'withdrawn');
      return total > 0;
    });

    const chartSource = filtered.length > 0 ? filtered : [];
    const labels = chartSource.map(item => getYearValue(item));
    const datasetSeries = {
      filing: chartSource.map(item => getValue(item, 'filing')),
      filed: chartSource.map(item => getValue(item, 'filed')),
      granted: chartSource.map(item => getValue(item, 'granted')),
      rejected: chartSource.map(item => getValue(item, 'rejected')),
      withdrawn: chartSource.map(item => getValue(item, 'withdrawn'))
    };
    const totalsSeries = chartSource.map((item, index) => datasetSeries.filing[index] + datasetSeries.filed[index] + datasetSeries.granted[index] + datasetSeries.rejected[index] + datasetSeries.withdrawn[index]);

    const tableEl = document.querySelector('.ipr-mini-table');
    hydrateMiniTable(tableEl, chartSource.length > 0 ? chartSource : entries, overall);

    if (chartSource.length > 0) {
      barChart = new Chart(barCanvas, buildStackedConfig(labels, datasetSeries));

      if (modeBtn) {
        modeBtn.disabled = false;
        modeBtn.removeAttribute('aria-disabled');
        modeBtn.dataset.mode = 'stacked';
        modeBtn.setAttribute('aria-label', 'Switch to totals');

        modeBtn.addEventListener('click', () => {
          if (!barChart) {
            return;
          }

          const mode = modeBtn.getAttribute('data-mode');
          barChart.destroy();

          if (mode === 'stacked') {
            barChart = new Chart(barCanvas, buildTotalsConfig(labels, totalsSeries));
            modeBtn.setAttribute('data-mode', 'total');
            modeBtn.setAttribute('aria-label', 'Switch to stacked');
            modeBtn.innerHTML = '<i class="bi bi-bar-chart"></i>';
          } else {
            barChart = new Chart(barCanvas, buildStackedConfig(labels, datasetSeries));
            modeBtn.setAttribute('data-mode', 'stacked');
            modeBtn.setAttribute('aria-label', 'Switch to totals');
            modeBtn.innerHTML = '<i class="bi bi-layers-half"></i>';
          }
        });
      }
    } else if (modeBtn) {
      modeBtn.disabled = true;
      modeBtn.setAttribute('aria-disabled', 'true');
    }
  }
})();
