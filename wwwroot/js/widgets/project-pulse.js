// SECTION: Project pulse widget bootstrap
(function () {
  'use strict';

  if (typeof window === 'undefined') {
    return;
  }

  // SECTION: Utilities
  var ChartCtor = window.Chart;
  var hasChart = typeof ChartCtor !== 'undefined';
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
            grid: { display: true, color: 'rgba(148, 163, 184, 0.18)' },
            ticks: {
              color: '#6b7280',
              font: { size: 10 }
            },
            title: { display: true, text: 'Stages' }
          },
          y: {
            beginAtZero: true,
            ticks: {
              precision: 0,
              color: '#9ca3af',
              font: { size: 10 }
            },
            grid: { display: true, color: 'rgba(148, 163, 184, 0.18)' }
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
            grid: { display: true, color: 'rgba(148, 163, 184, 0.18)' },
            ticks: {
              display: true,
              color: '#6b7280',
              font: { size: 10 }
            },
            title: { display: Boolean(xLabel), text: xLabel }
          },
          y: {
            beginAtZero: true,
            ticks: {
              precision: 0,
              color: '#9ca3af',
              font: { size: 10 }
            },
            grid: { display: true, color: 'rgba(148, 163, 184, 0.18)' },
            title: { display: Boolean(yLabel), text: yLabel }
          }
        }
      }
    });
  }
  // END SECTION

  // SECTION: Treemap builder
  function buildTreemap(host, series) {
    if (!host || !series.length) {
      return;
    }

    var parsed = series
      .map(function (item) {
        var label = item && (item.label || item.Label) ? (item.label || item.Label) : '';
        var count = item && (typeof item.count !== 'undefined' ? item.count : item.Count);
        var url = item && (item.url || item.Url) ? (item.url || item.Url) : null;
        return {
          label: String(label),
          count: Number(count) || 0,
          url: url ? String(url) : null
        };
      })
      .filter(function (item) { return item.count > 0; });

    if (!parsed.length) {
      return;
    }

    var total = parsed.reduce(function (sum, item) { return sum + item.count; }, 0);
    if (!total) {
      return;
    }

    host.innerHTML = '';
    host.setAttribute('data-hydrated', 'true');

    var styles = window.getComputedStyle(host);
    var paddingX = parseFloat(styles.paddingLeft) + parseFloat(styles.paddingRight);
    var paddingY = parseFloat(styles.paddingTop) + parseFloat(styles.paddingBottom);
    var width = (host.clientWidth || host.offsetWidth) - paddingX;
    var height = (host.clientHeight || host.offsetHeight) - paddingY;
    if (!width || !height) {
      return;
    }

    var horizontal = true;
    var offsetX = parseFloat(styles.paddingLeft);
    var offsetY = parseFloat(styles.paddingTop);
    var remainingWidth = width;
    var remainingHeight = height;

    parsed.forEach(function (item, idx) {
      var isLast = idx === parsed.length - 1;
      var area = Math.max(1, Math.round((item.count / total) * width * height));
      var tileWidth;
      var tileHeight;

      if (horizontal) {
        tileWidth = remainingWidth;
        tileHeight = isLast ? remainingHeight : Math.max(1, Math.round(area / remainingWidth));
      } else {
        tileHeight = remainingHeight;
        tileWidth = isLast ? remainingWidth : Math.max(1, Math.round(area / remainingHeight));
      }

      tileWidth = Math.min(tileWidth, remainingWidth);
      tileHeight = Math.min(tileHeight, remainingHeight);

      var tile = createTreemapTile(item);
      tile.style.left = offsetX + 'px';
      tile.style.top = offsetY + 'px';
      tile.style.width = tileWidth + 'px';
      tile.style.height = tileHeight + 'px';
      host.appendChild(tile);

      if (horizontal) {
        offsetY += tileHeight;
        remainingHeight -= tileHeight;
      } else {
        offsetX += tileWidth;
        remainingWidth -= tileWidth;
      }

      horizontal = !horizontal;
    });
  }

  function createTreemapTile(item) {
    var tile = document.createElement(item.url ? 'button' : 'div');
    tile.className = 'ppulse__treemap-tile' + (item.url ? ' ppulse__treemap-tile--link' : '');
    if (item.url) {
      tile.type = 'button';
      tile.setAttribute('data-url', item.url);
      tile.addEventListener('click', function () {
        window.location.href = item.url;
      });
    }

    var label = document.createElement('span');
    label.textContent = item.label;
    var count = document.createElement('strong');
    count.textContent = item.count.toString();

    tile.appendChild(label);
    tile.appendChild(count);

    return tile;
  }
  // END SECTION

  // SECTION: Tabs
  function initTabs(root, hydrateChart) {
    var containers = root.querySelectorAll('[data-ppulse-tabs]');
    if (!containers || containers.length === 0) {
      return;
    }

    function isTreemap(host) {
      return host && host.getAttribute('data-chart') === 'treemap';
    }

    function hydratePanel(panel) {
      if (!panel) {
        return;
      }
      var charts = Array.prototype.slice.call(panel.querySelectorAll('.ppulse__chart'));
      charts.forEach(function (chart) {
        if (isTreemap(chart)) {
          hydrateChart(chart);
        }
      });
    }

    containers.forEach(function (container) {
      var tabs = Array.prototype.slice.call(container.querySelectorAll('[role="tab"]'));
      var panels = Array.prototype.slice.call(container.querySelectorAll('[role="tabpanel"]'));
      if (!tabs.length || !panels.length) {
        return;
      }

      function setActiveTab(nextTab) {
        tabs.forEach(function (tab) {
          var selected = tab === nextTab;
          tab.setAttribute('aria-selected', selected ? 'true' : 'false');
          tab.tabIndex = selected ? 0 : -1;
        });

        var nextKey = nextTab.getAttribute('data-tab');
        panels.forEach(function (panel) {
          var show = panel.getAttribute('data-panel') === nextKey;
          if (show) {
            panel.removeAttribute('hidden');
          } else {
            panel.setAttribute('hidden', '');
          }
        });

        var activePanel = panels.find(function (panel) {
          return panel.getAttribute('data-panel') === nextKey;
        });
        hydratePanel(activePanel);
      }

      tabs.forEach(function (tab) {
        tab.addEventListener('click', function () {
          setActiveTab(tab);
          tab.focus();
        });

        tab.addEventListener('keydown', function (event) {
          var idx = tabs.indexOf(tab);
          if (idx < 0) {
            return;
          }

          if (event.key === 'ArrowRight' || event.key === 'Right') {
            event.preventDefault();
            var nextTab = tabs[(idx + 1) % tabs.length];
            nextTab.focus();
          }

          if (event.key === 'ArrowLeft' || event.key === 'Left') {
            event.preventDefault();
            var prevTab = tabs[(idx - 1 + tabs.length) % tabs.length];
            prevTab.focus();
          }

          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            setActiveTab(tab);
          }
        });
      });

      var initial = tabs.find(function (tab) {
        return tab.getAttribute('aria-selected') === 'true';
      }) || tabs[0];
      setActiveTab(initial);
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
      var kind = host.getAttribute('data-chart');
      if (host.getAttribute('data-hydrated') === 'true') {
        return;
      }
      if (kind === 'treemap') {
        var panel = host.closest('[role="tabpanel"]');
        if (panel && panel.hasAttribute('hidden')) {
          return;
        }
      }
      var series = safeParse(host, 'data-series');
      if (!series.length) {
        return;
      }
      if (kind === 'treemap') {
        buildTreemap(host, series);
        return;
      }
      if (!hasChart) {
        return;
      }
      var canvas = host.querySelector('canvas');
      if (!canvas) {
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

    initTabs(root, hydrateChart);

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
