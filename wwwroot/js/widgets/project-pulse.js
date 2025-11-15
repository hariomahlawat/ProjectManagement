// SECTION: Project pulse widget bootstrap
(function () {
  'use strict';

  if (typeof window === 'undefined' || typeof window.Chart === 'undefined') {
    return;
  }

  // SECTION: Utilities
  var ChartCtor = window.Chart;
  var palette = ['#2d6cdf', '#0ea5e9', '#22c55e', '#f97316', '#a855f7', '#ef4444'];

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
      data: { labels: labels, datasets: [{ data: data, tension: 0.35, pointRadius: 2, fill: false, borderColor: palette[0] }] },
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

  function buildBar(ctx, series) {
    var labels = series.map(function (s) { return s && (s.label || s.Label) ? (s.label || s.Label) : ''; });
    var data = series.map(function (s) {
      var value = s && (typeof s.count !== 'undefined' ? s.count : (typeof s.Count !== 'undefined' ? s.Count : 0));
      return Number(value) || 0;
    });
    return new ChartCtor(ctx, {
      type: 'bar',
      data: { labels: labels, datasets: [{ data: data, borderRadius: 6, maxBarThickness: 22, backgroundColor: palette[1] }] },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        plugins: { legend: { display: false } },
        scales: {
          x: {
            grid: { display: false },
            ticks: { display: false },
            title: { display: true, text: 'Technical category' }
          },
          y: { beginAtZero: true, ticks: { precision: 0 } }
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
    var observer = 'IntersectionObserver' in window
      ? new IntersectionObserver(onIntersect, { rootMargin: '80px' })
      : null;

    function onIntersect(entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) {
          return;
        }
        observer && observer.unobserve(entry.target);
        hydrateChart(entry.target);
      });
    }

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
        chart = buildBar(canvas.getContext('2d'), series);
      }
      if (chart) {
        charts.push(chart);
      }
    }

    function hydrateAll() {
      root.querySelectorAll('.ppulse__chart').forEach(function (chartHost) {
        if (observer) {
          observer.observe(chartHost);
        } else {
          hydrateChart(chartHost);
        }
      });
    }

    hydrateAll();
  }
  // END SECTION

  document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('[data-project-pulse]').forEach(init);
  });
})();
