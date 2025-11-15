// SECTION: Project pulse chart bootstrap
(function () {
  'use strict';

  // SECTION: DOM helpers
  function ready(fn) {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', fn, { once: true });
    } else {
      fn();
    }
  }

  function queryAll(selector, root) {
    return Array.prototype.slice.call((root || document).querySelectorAll(selector));
  }
  // END SECTION

  // SECTION: Data helpers
  function toLabelsValues(series) {
    if (!Array.isArray(series)) {
      return { labels: [], values: [] };
    }

    var labels = series.map(function (entry) {
      if (entry && typeof entry.label === 'string') {
        return entry.label;
      }
      if (entry && typeof entry.Label === 'string') {
        return entry.Label;
      }
      return '';
    });

    var values = series.map(function (entry) {
      var raw = entry && typeof entry.value !== 'undefined'
        ? entry.value
        : (entry && typeof entry.Value !== 'undefined' ? entry.Value : 0);
      var numeric = Number(raw);
      return Number.isFinite ? (Number.isFinite(numeric) ? numeric : 0) : (isFinite(numeric) ? numeric : 0);
    });

    return { labels: labels, values: values };
  }

  var palette = [
    '#2d6cdf', '#0ea5e9', '#22c55e', '#f97316',
    '#a855f7', '#ef4444', '#14b8a6', '#f59e0b'
  ];
  // END SECTION

  // SECTION: Chart builders
  function baseDataset(kind, labels, values) {
    if (kind === 'pie') {
      return [{
        data: values,
        backgroundColor: labels.map(function (_, idx) { return palette[idx % palette.length]; })
      }];
    }

    if (kind === 'line') {
      return [{
        data: values,
        borderColor: palette[0],
        backgroundColor: 'rgba(45, 108, 223, 0.08)',
        borderWidth: 2,
        pointRadius: 2,
        tension: 0.35,
        fill: true
      }];
    }

    return [{
      data: values,
      backgroundColor: 'rgba(45, 108, 223, 0.35)',
      borderRadius: 6,
      maxBarThickness: 26
    }];
  }

  function buildChart(canvas, kind, series, customOptions) {
    var ChartCtor = window.Chart;
    if (!ChartCtor) {
      return null;
    }

    var parsed = toLabelsValues(series);
    if (parsed.labels.length === 0 || parsed.values.length === 0) {
      return null;
    }

    var hideXTickLabels = !!(customOptions && customOptions.hideXTickLabels);
    var xAxisTitle = customOptions && customOptions.xAxisTitle;

    var config = {
      type: kind === 'pie' ? 'pie' : (kind === 'line' ? 'line' : 'bar'),
      data: {
        labels: parsed.labels,
        datasets: baseDataset(kind, parsed.labels, parsed.values)
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: kind === 'pie' },
          tooltip: { mode: 'index', intersect: false }
        },
        scales: kind === 'pie' ? {} : {
          x: {
            grid: { display: false },
            ticks: {
              autoSkip: true,
              maxRotation: 0,
              display: !hideXTickLabels
            },
            title: xAxisTitle ? {
              display: true,
              text: xAxisTitle,
              font: { weight: '600' }
            } : { display: false }
          },
          y: {
            beginAtZero: true,
            ticks: { precision: 0 }
          }
        }
      }
    };

    if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      config.options.animation = false;
    }

    return new ChartCtor(canvas.getContext('2d'), config);
  }
  // END SECTION

  // SECTION: Initializer
  function init() {
    var host = document.querySelector('[data-project-pulse]');
    if (!host) {
      return;
    }

    var charts = [];

    // SECTION: Chart lifecycle helpers
    function destroyCharts() {
      if (!charts.length) {
        return;
      }

      charts.forEach(function (chart) { return chart.destroy(); });
      charts = [];
    }

    function buildCharts() {
      destroyCharts();

      queryAll('.ppulse__chart', host).forEach(function (zone) {
        var kind = zone.getAttribute('data-chart');
        var raw = zone.getAttribute('data-series') || '[]';
        var series;
        try {
          series = JSON.parse(raw);
        } catch (err) {
          series = [];
        }

        var canvas = zone.querySelector('canvas');
        if (canvas && series.length > 0) {
          var chart = buildChart(canvas, kind, series, {
            xAxisTitle: zone.getAttribute('data-x-axis-title') || '',
            hideXTickLabels: zone.getAttribute('data-x-axis-hide-labels') === 'true'
          });
          if (chart) {
            charts.push(chart);
          }
        }
      });
    }
    // END SECTION

    buildCharts();

    document.addEventListener('visibilitychange', function handleVisibility() {
      if (document.hidden) {
        destroyCharts();
        return;
      }

      if (!charts.length) {
        requestAnimationFrame(buildCharts);
      }
    });
  }
  // END SECTION

  ready(init);
})();
// END SECTION
