// SECTION: Project pulse widget bootstrap
(function () {
  'use strict';

  if (typeof window === 'undefined' || typeof window.Chart === 'undefined') {
    return;
  }

  // SECTION: Utilities
  var ChartCtor = window.Chart;
  var palette = (window.PMTheme && typeof window.PMTheme.getChartPalette === 'function')
    ? window.PMTheme.getChartPalette()
    : {
        accentPrimary: '#2563eb',
        accentMuted: '#cbd5f5',
        neutralStrong: '#64748b',
        neutralSoft: 'rgba(148, 163, 184, 0.2)',
        axisColor: '#64748b',
        gridColor: 'rgba(148, 163, 184, 0.2)'
      };

  var textColor = getCssVar('--pm-text', '#0b1220');
  var textSecondary = getCssVar('--pm-text-secondary', '#4b5563');

  function safeParse(el, attr) {
    try {
      return JSON.parse(el.getAttribute(attr) || '[]');
    } catch (err) {
      console.warn('Project pulse chart parsing failed', err); // eslint-disable-line no-console
      return [];
    }
  }

  function getCssVar(name, fallback) {
    if (typeof window === 'undefined' || !window.getComputedStyle) {
      return fallback;
    }
    var styles = getComputedStyle(document.documentElement);
    return styles.getPropertyValue(name).trim() || fallback;
  }
  // END SECTION

  // SECTION: Plugins
  var ongoingCenterLabelPlugin = {
    id: 'ongoingCenterLabel',
    afterDraw: function (chart) {
      var dataset = chart.data && chart.data.datasets && chart.data.datasets[0];
      if (!dataset || !dataset.data || !dataset.data.length) {
        return;
      }

      var totalOngoing = dataset.data[0];
      var area = chart.chartArea;
      var ctx = chart.ctx;

      if (!area) {
        return;
      }

      var centerX = (area.left + area.right) / 2;
      var centerY = (area.top + area.bottom) / 2;

      ctx.save();
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';

      ctx.fillStyle = textColor;
      ctx.font = '600 18px system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif';
      ctx.fillText(totalOngoing, centerX, centerY - 6);

      ctx.fillStyle = textSecondary;
      ctx.font = '400 10px system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif';
      ctx.fillText('ongoing projects', centerX, centerY + 12);

      ctx.restore();
    }
  };
  // END SECTION

  // SECTION: Chart builders
  function buildDonut(ctx, series) {
    var labels = series.map(function (s) { return s && (s.label || s.Label) ? (s.label || s.Label) : ''; });
    var data = series.map(function (s) {
      var value = s && (typeof s.count !== 'undefined' ? s.count : (typeof s.Count !== 'undefined' ? s.Count : 0));
      return Number(value) || 0;
    });
    return new ChartCtor(ctx, {
      type: 'doughnut',
      data: {
        labels: labels,
        datasets: [{
          data: data,
          backgroundColor: [
            palette.accentPrimary || '#2563eb',
            palette.neutralSoft || 'rgba(148, 163, 184, 0.18)'
          ],
          hoverBackgroundColor: [
            palette.accentPrimary || '#2563eb',
            palette.neutralSoft || 'rgba(148, 163, 184, 0.18)'
          ],
          borderWidth: 0,
          cutout: '60%'
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        plugins: {
          legend: { display: false },
          tooltip: {
            enabled: true,
            callbacks: {
              label: function (context) {
                var value = context.parsed || 0;
                var label = context.label ? context.label + ': ' : '';
                return label + value + ' projects';
              }
            }
          }
        }
      },
      plugins: [ongoingCenterLabelPlugin]
    });
  }

  function buildLine(ctx, series) {
    var labels = series.map(function (s) { return s && (s.stage || s.Stage) ? (s.stage || s.Stage) : ''; });
    var data = series.map(function (s) {
      var value = s && (typeof s.count !== 'undefined' ? s.count : (typeof s.Count !== 'undefined' ? s.Count : 0));
      return Number(value) || 0;
    });
    return new ChartCtor(ctx, {
      type: 'line',
      data: {
        labels: labels,
        datasets: [{
          data: data,
          tension: 0.35,
          pointRadius: 2,
          pointBackgroundColor: palette.accentPrimary || '#2563eb',
          pointHoverRadius: 4,
          fill: false,
          borderColor: palette.accentPrimary || '#2563eb'
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        plugins: { legend: { display: false } },
        scales: {
          x: {
            grid: { display: true, color: palette.neutralSoft || 'rgba(148, 163, 184, 0.18)' },
            ticks: {
              color: palette.neutralStrong || '#6b7280',
              font: { size: 10 }
            },
            title: { display: true, text: 'Stages' }
          },
          y: {
            beginAtZero: true,
            ticks: {
              precision: 0,
              color: palette.neutralStrong || '#9ca3af',
              font: { size: 10 }
            },
            grid: { display: true, color: palette.neutralSoft || 'rgba(148, 163, 184, 0.18)' }
          }
        }
      }
    });
  }

  function buildBar(ctx, series, axisLabels) {
    axisLabels = axisLabels || {};
    var xLabel = axisLabels.x || axisLabels.X || '';
    var yLabel = axisLabels.y || axisLabels.Y || '';
    var labels = series.map(function (s) { return s && (s.label || s.Label) ? (s.label || s.Label) : ''; });
    var data = series.map(function (s) {
      var value = s && (typeof s.count !== 'undefined' ? s.count : (typeof s.Count !== 'undefined' ? s.Count : 0));
      return Number(value) || 0;
    });
    return new ChartCtor(ctx, {
      type: 'bar',
      data: {
        labels: labels,
        datasets: [{
          data: data,
          borderRadius: 4,
          maxBarThickness: 22,
          backgroundColor: palette.accentPrimary || '#2563eb',
          borderColor: palette.accentPrimary || '#2563eb',
          borderWidth: 1
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        plugins: { legend: { display: false } },
        scales: {
          x: {
            grid: { display: true, color: palette.neutralSoft || 'rgba(148, 163, 184, 0.18)' },
            ticks: {
              display: true,
              color: palette.neutralStrong || '#6b7280',
              font: { size: 10 }
            },
            title: { display: Boolean(xLabel), text: xLabel }
          },
          y: {
            beginAtZero: true,
            ticks: {
              precision: 0,
              color: palette.neutralStrong || '#9ca3af',
              font: { size: 10 }
            },
            grid: { display: true, color: palette.neutralSoft || 'rgba(148, 163, 184, 0.18)' },
            title: { display: Boolean(yLabel), text: yLabel }
          }
        }
      }
    });
  }
  // END SECTION

  // SECTION: Initializer
  function init(root) {
    if (!root) {
      return;
    }

    var charts = [];
    var hosts = Array.prototype.slice.call(root.querySelectorAll('.ppulse__chart'));

    function hydrateChart(host) {
      var canvas = host.querySelector('canvas');
      if (!canvas) {
        return;
      }
      var kind = host.getAttribute('data-chart');
      var series = safeParse(host, 'data-series');
      if (!series.length) {
        return;
      }
      var chart;
      if (kind === 'donut') {
        chart = buildDonut(canvas.getContext('2d'), series);
      } else if (kind === 'line') {
        chart = buildLine(canvas.getContext('2d'), series);
      } else if (kind === 'bar') {
        chart = buildBar(canvas.getContext('2d'), series, {
          x: host.getAttribute('data-x-label') || '',
          y: host.getAttribute('data-y-label') || ''
        });
      }
      if (chart) {
        charts.push(chart);
      }
    }

    function hydrateAll() {
      hosts.forEach(hydrateChart);
      hosts = [];
    }

    if ('IntersectionObserver' in window) {
      var observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
          if (!entry.isIntersecting) {
            return;
          }
          observer.unobserve(entry.target);
          hydrateAll();
        });
      }, { rootMargin: '80px' });
      observer.observe(root);
    } else {
      hydrateAll();
    }
  }
  // END SECTION

  document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('[data-project-pulse]').forEach(init);
  });
})();
