// SECTION: Project pulse widget bootstrap
(function () {
  'use strict';

  if (typeof window === 'undefined' || typeof window.Chart === 'undefined') {
    return;
  }

  // SECTION: Utilities
  var ChartCtor = window.Chart;
  var palette = ['#475569', '#94a3b8', '#cbd5f5', '#d4d4d8', '#e2e8f0', '#c4b5fd'];
  var accent = '#2d6cdf';

  function safeParse(el, attr) {
    try {
      return JSON.parse(el.getAttribute(attr) || '[]');
    } catch (err) {
      console.warn('Project pulse chart parsing failed', err); // eslint-disable-line no-console
      return [];
    }
  }
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
          backgroundColor: labels.map(function (_, idx) { return palette[idx % palette.length]; }),
          hoverBackgroundColor: labels.map(function () { return accent; }),
          cutout: '60%'
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        plugins: { legend: { display: false }, tooltip: { enabled: true } }
      }
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
          pointBackgroundColor: accent,
          pointHoverRadius: 4,
          fill: false,
          borderColor: palette[0]
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        plugins: { legend: { display: false } },
        scales: {
          x: {
            grid: { display: false },
            ticks: { display: false },
            title: { display: true, text: 'Stages' }
          },
          y: { beginAtZero: true, ticks: { precision: 0 } }
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
          borderRadius: 6,
          maxBarThickness: 22,
          backgroundColor: palette[1],
          hoverBackgroundColor: accent
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        plugins: { legend: { display: false } },
        scales: {
          x: {
            grid: { display: false },
            ticks: { display: false },
            title: { display: Boolean(xLabel), text: xLabel }
          },
          y: {
            beginAtZero: true,
            ticks: { precision: 0 },
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
