// SECTION: Project pulse widget bootstrap
(function () {
  'use strict';

  if (typeof window === 'undefined' || typeof window.Chart === 'undefined') {
    return;
  }

  // SECTION: Utilities
  var ChartCtor = window.Chart;

  var PulsePalette = {
    // Completed (success)
    completed: 'rgba(52, 199, 89, 0.8)',
    completedBorder: 'rgba(52, 199, 89, 1)',

    // Ongoing (in progress)
    ongoing: 'rgba(255, 159, 10, 0.85)',
    ongoingBorder: 'rgba(255, 159, 10, 1)',

    // Neutral bars / breakdowns
    neutralBar: 'rgba(148, 163, 184, 0.9)',
    neutralBorder: 'rgba(148, 163, 184, 1)',

    // Optional softer variants for cycling donut colours
    neutralSoft: 'rgba(207, 212, 228, 0.9)'
  };

  var neutralChartOptions = {
    plugins: {
      legend: {
        labels: {
          color: '#4B5563'
        }
      },
      tooltip: {
        backgroundColor: '#111827',
        titleColor: '#F9FAFB',
        bodyColor: '#E5E7EB'
      }
    },
    scales: {
      x: {
        grid: {
          color: 'rgba(15, 23, 42, 0.05)'
        },
        ticks: {
          color: '#6B7280'
        }
      },
      y: {
        grid: {
          color: 'rgba(15, 23, 42, 0.08)'
        },
        ticks: {
          color: '#6B7280'
        }
      }
    }
  };

  var textColor = getCssVar('--pm-text-main', getCssVar('--pm-text', '#0b1220'));
  var textSecondary = getCssVar('--pm-text-muted', getCssVar('--pm-text-secondary', '#4b5563'));

  function safeParse(el, attr) {
    try {
      return JSON.parse(el.getAttribute(attr) || 'null');
    } catch (err) {
      console.warn('Project pulse chart parsing failed', err); // eslint-disable-line no-console
      return null;
    }
  }

  function getCssVar(name, fallback) {
    if (typeof window === 'undefined' || !window.getComputedStyle) {
      return fallback;
    }
    var styles = getComputedStyle(document.documentElement);
    return styles.getPropertyValue(name).trim() || fallback;
  }

  function mergeOptions(base, overrides) {
    var merged = {
      plugins: Object.assign({}, base.plugins, overrides && overrides.plugins),
      scales: Object.assign({}, base.scales)
    };

    if (overrides && overrides.scales) {
      merged.scales.x = Object.assign({}, base.scales.x, overrides.scales.x);
      merged.scales.y = Object.assign({}, base.scales.y, overrides.scales.y);
    }

    Object.keys(overrides || {}).forEach(function (key) {
      if (key !== 'plugins' && key !== 'scales') {
        merged[key] = overrides[key];
      }
    });

    return merged;
  }
  // END SECTION

  // SECTION: Plugins
  function createOngoingCenterLabelPlugin(totalOngoing) {
    return {
      id: 'ongoingCenterLabel',
      afterDraw: function (chart) {
        var dataset = chart.data && chart.data.datasets && chart.data.datasets[0];
        if (!dataset || !dataset.data || dataset.data.length === 0) {
          return;
        }

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
        ctx.font = '600 16px system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif';
        ctx.fillText(totalOngoing.toString(), centerX, centerY - 6);

        ctx.fillStyle = textSecondary;
        ctx.font = '400 10px system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif';
        ctx.fillText('ongoing projects', centerX, centerY + 12);

        ctx.restore();
      }
    };
  }
  // END SECTION

  // SECTION: Chart builders
  function buildCompletedChart(ctx, series) {
    if (!series || !series.length) {
      return null;
    }

    var labels = series.map(function (s) { return s && (s.label || s.Label) ? (s.label || s.Label) : ''; });
    var data = series.map(function (s) {
      var value = s && (typeof s.count !== 'undefined' ? s.count : (typeof s.Count !== 'undefined' ? s.Count : 0));
      return Number(value) || 0;
    });

    var options = mergeOptions(neutralChartOptions, {
      plugins: Object.assign({}, neutralChartOptions.plugins, { legend: { display: false } }),
      scales: {
        x: Object.assign({}, neutralChartOptions.scales.x, {
          grid: Object.assign({}, neutralChartOptions.scales.x.grid, { display: true }),
          ticks: Object.assign({}, neutralChartOptions.scales.x.ticks, { font: { size: 10 } }),
          title: { display: true, text: 'Year' }
        }),
        y: Object.assign({}, neutralChartOptions.scales.y, {
          beginAtZero: true,
          ticks: Object.assign({}, neutralChartOptions.scales.y.ticks, { precision: 0, font: { size: 10 } }),
          title: { display: true, text: 'Projects completed' }
        })
      }
    });

    return new ChartCtor(ctx, {
      type: 'bar',
      data: {
        labels: labels,
        datasets: [{
          label: 'Completed',
          data: data,
          backgroundColor: PulsePalette.completed,
          borderColor: PulsePalette.completedBorder,
          borderWidth: 1,
          borderRadius: 4
        }]
      },
      options: options
    });
  }

  function buildOngoingDonut(ctx, vm) {
    var slices = (vm && vm.ongoingByCategory) || [];
    if (!slices.length) {
      return null;
    }

    var labels = slices.map(function (slice) { return slice.categoryName || ''; });
    var data = slices.map(function (slice) { return Number(slice.projectCount || 0); });
    var donutBaseColors = [
      PulsePalette.ongoing,
      PulsePalette.completed,
      PulsePalette.neutralBar,
      PulsePalette.neutralSoft
    ];
    var donutColors = data.map(function (_value, index) {
      return donutBaseColors[index % donutBaseColors.length];
    });
    var totalOngoing = typeof vm.totalOngoingProjects === 'number'
      ? vm.totalOngoingProjects
      : data.reduce(function (sum, value) { return sum + value; }, 0);

    return new ChartCtor(ctx, {
      type: 'doughnut',
      data: {
        labels: labels,
        datasets: [{
          data: data,
          backgroundColor: donutColors,
          borderWidth: 0,
          hoverBackgroundColor: donutColors,
          cutout: '70%'
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        plugins: {
          legend: { display: false },
          tooltip: Object.assign({}, neutralChartOptions.plugins.tooltip, {
            callbacks: {
              label: function (context) {
                var value = context.parsed || 0;
                var label = context.label ? context.label + ': ' : '';
                return label + value + ' project' + (value === 1 ? '' : 's');
              }
            }
          })
        }
      },
      plugins: [createOngoingCenterLabelPlugin(totalOngoing)]
    });
  }

  function buildAllProjectsChart(ctx, series) {
    if (!series || !series.length) {
      return null;
    }

    var labels = series.map(function (s) { return s && (s.label || s.Label) ? (s.label || s.Label) : ''; });
    var data = series.map(function (s) {
      var value = s && (typeof s.count !== 'undefined' ? s.count : (typeof s.Count !== 'undefined' ? s.Count : 0));
      return Number(value) || 0;
    });

    var options = mergeOptions(neutralChartOptions, {
      plugins: Object.assign({}, neutralChartOptions.plugins, { legend: { display: false } }),
      scales: {
        x: Object.assign({}, neutralChartOptions.scales.x, {
          grid: Object.assign({}, neutralChartOptions.scales.x.grid, { display: true }),
          ticks: Object.assign({}, neutralChartOptions.scales.x.ticks, { font: { size: 10 } }),
          title: { display: true, text: 'Technical category' }
        }),
        y: Object.assign({}, neutralChartOptions.scales.y, {
          beginAtZero: true,
          ticks: Object.assign({}, neutralChartOptions.scales.y.ticks, { precision: 0, font: { size: 10 } }),
          title: { display: true, text: 'Projects' }
        })
      }
    });

    return new ChartCtor(ctx, {
      type: 'bar',
      data: {
        labels: labels,
        datasets: [{
          label: 'Projects',
          data: data,
          backgroundColor: PulsePalette.neutralBar,
          borderColor: PulsePalette.neutralBorder,
          borderWidth: 1,
          borderRadius: 4
        }]
      },
      options: options
    });
  }
  // END SECTION

  // SECTION: Initializer
  function init(root) {
    if (!root) {
      return;
    }

    var vm = safeParse(root, 'data-project-pulse');
    if (!vm) {
      return;
    }

    var rendered = false;

    function renderCharts() {
      if (rendered) {
        return;
      }
      rendered = true;

      var completedHost = root.querySelector('[data-chart-role="completed"]');
      if (completedHost) {
        var completedCanvas = completedHost.querySelector('canvas');
        if (completedCanvas) {
          buildCompletedChart(completedCanvas.getContext('2d'), vm.completedByYear || []);
        }
      }

      var ongoingHost = root.querySelector('[data-chart-role="ongoing"]');
      if (ongoingHost) {
        var ongoingCanvas = ongoingHost.querySelector('canvas');
        if (ongoingCanvas) {
          buildOngoingDonut(ongoingCanvas.getContext('2d'), vm);
        }
      }

      var allProjectsHost = root.querySelector('[data-chart-role="all-projects"]');
      if (allProjectsHost) {
        var allProjectsCanvas = allProjectsHost.querySelector('canvas');
        if (allProjectsCanvas) {
          buildAllProjectsChart(allProjectsCanvas.getContext('2d'), vm.allByTechnicalCategoryTop || []);
        }
      }
    }

    if ('IntersectionObserver' in window) {
      var observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
          if (!entry.isIntersecting) {
            return;
          }
          observer.unobserve(entry.target);
          renderCharts();
        });
      }, { rootMargin: '80px' });
      observer.observe(root);
    } else {
      renderCharts();
    }
  }
  // END SECTION

  document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('[data-project-pulse]').forEach(init);
  });
})();
