(function () {
  'use strict';

  if (typeof window === 'undefined' || typeof window.Chart === 'undefined') {
    return;
  }

  var ChartCtor = window.Chart;
  var strokeColor = '#3b82f6';
  var fillColor = 'rgba(59, 130, 246, 0.06)';

  function parseValues(target) {
    try {
      var parsed = JSON.parse(target.getAttribute('data-ocr-trend') || '[]');
      return Array.isArray(parsed) ? parsed.map(function (value) { return Number(value) || 0; }) : [];
    } catch (_err) {
      return [];
    }
  }

  function buildChart(target, values) {
    if (!target || !values.length) {
      return null;
    }

    var canvas = target.querySelector('canvas');
    if (!canvas) {
      return null;
    }

    var min = Math.min.apply(null, values);
    var max = Math.max.apply(null, values);
    var paddedMin = Math.max(0, min - 1);
    var paddedMax = Math.max(paddedMin + 1, max + 1);

    return new ChartCtor(canvas.getContext('2d'), {
      type: 'line',
      data: {
        labels: values.map(function (_, idx) { return idx + 1; }),
        datasets: [{
          data: values,
          tension: 0.35,
          pointRadius: 0,
          borderWidth: 2,
          borderColor: strokeColor,
          backgroundColor: fillColor,
          fill: true
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        scales: {
          x: { display: false },
          y: { display: false, min: paddedMin, max: paddedMax }
        },
        plugins: {
          legend: { display: false },
          tooltip: { enabled: false }
        }
      }
    });
  }

  function hydrate(target) {
    var values = parseValues(target);
    if (!values.length) {
      return;
    }

    var chart = buildChart(target, values);

    if ('ResizeObserver' in window && chart) {
      var ro = new ResizeObserver(function () {
        chart.resize();
      });
      ro.observe(target);
    }
  }

  function init() {
    var trendHolders = document.querySelectorAll('[data-ocr-trend]');
    if (!trendHolders.length) {
      return;
    }

    var observer = 'IntersectionObserver' in window ? new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) {
          return;
        }
        observer.unobserve(entry.target);
        hydrate(entry.target);
      });
    }, { rootMargin: '80px' }) : null;

    trendHolders.forEach(function (holder) {
      if (observer) {
        observer.observe(holder);
      } else {
        hydrate(holder);
      }
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init, { once: true });
  } else {
    init();
  }
})();
// END SECTION
