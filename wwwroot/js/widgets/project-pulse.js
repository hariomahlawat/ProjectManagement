// SECTION: Project pulse widget bootstrap
(function () {
  'use strict';

  if (typeof window === 'undefined') {
    return;
  }

  // SECTION: Utilities
  function safeParse(el, attr) {
    try {
      return JSON.parse(el.getAttribute(attr) || '[]');
    } catch (err) {
      console.warn('Project pulse chart parsing failed', err); // eslint-disable-line no-console
      return [];
    }
  }

  function safeParseObject(el, attr, fallback) {
    try {
      var raw = el.getAttribute(attr);
      if (!raw) {
        return fallback;
      }
      return JSON.parse(raw);
    } catch (err) {
      console.warn('Project pulse payload parsing failed', err); // eslint-disable-line no-console
      return fallback;
    }
  }
  // END SECTION

  // SECTION: Ongoing bucket filters
  function initOngoingBuckets(root, onKeyChange) {
    var container = root.querySelector('[data-ppulse-ongoing-buckets]');
    if (!container) {
      return;
    }

    var buckets = safeParseObject(container, 'data-buckets-json', {});
    var filters = Array.prototype.slice.call(container.querySelectorAll('[data-bucket-key]'));
    var segments = Array.prototype.slice.call(container.querySelectorAll('[data-bucket]'));
    if (!filters.length || !segments.length) {
      return;
    }

    function readNumber(value) {
      return Number(value) || 0;
    }

    function normalizeBucket(key, fallback) {
      if (key && Object.prototype.hasOwnProperty.call(buckets, key)) {
        return buckets[key];
      }
      if (fallback && Object.prototype.hasOwnProperty.call(buckets, fallback)) {
        return buckets[fallback];
      }
      return null;
    }

    function updateSegments(bucket) {
      if (!bucket) {
        return;
      }

      var total = readNumber(bucket.total || bucket.Total);
      var values = {
        apvl: readNumber(bucket.apvl || bucket.Apvl),
        aon: readNumber(bucket.aon || bucket.Aon),
        tender: readNumber(bucket.tender || bucket.Tender),
        devp: readNumber(bucket.devp || bucket.Devp),
        other: readNumber(bucket.other || bucket.Other)
      };
      var visibleTotal = values.apvl + values.aon + values.tender + values.devp;
      var percentTotal = visibleTotal > 0 ? visibleTotal : total;

      segments.forEach(function (segment) {
        var key = segment.getAttribute('data-bucket');
        var value = values[key] || 0;
        var percent = percentTotal > 0 ? (value / percentTotal) * 100 : 0;
        var label = segment.querySelector('[data-bucket-value]');
        if (label) {
          label.textContent = value.toString();
        }

        segment.style.width = percent.toFixed(2) + '%';

        if (percentTotal > 0 && value === 0) {
          segment.classList.add('is-hidden');
        } else {
          segment.classList.remove('is-hidden');
        }
      });
    }

    function setActiveFilter(filter) {
      filters.forEach(function (item) {
        var isActive = item === filter;
        item.classList.toggle('is-active', isActive);
        item.setAttribute('aria-selected', isActive ? 'true' : 'false');
      });
    }

    function applyKey(key) {
      var fallbackKey = container.getAttribute('data-default-key') || 'total';
      var bucket = normalizeBucket(key, fallbackKey);
      if (!bucket) {
        return;
      }
      updateSegments(bucket);

      if (typeof onKeyChange === 'function') {
        onKeyChange(key);
      }
    }

    filters.forEach(function (filter) {
      filter.addEventListener('click', function () {
        setActiveFilter(filter);
        applyKey(filter.getAttribute('data-bucket-key'));
      });
    });

    var initialFilter = filters.find(function (filter) {
      return filter.classList.contains('is-active');
    }) || filters[0];

    setActiveFilter(initialFilter);
    applyKey(initialFilter.getAttribute('data-bucket-key'));
  }
  // END SECTION

  // SECTION: Ongoing stage pills
  function initStagePills(root) {
    var container = root.querySelector('[data-ppulse-stage-pills]');
    if (!container) {
      return null;
    }

    var stageData = safeParseObject(container, 'data-stage-json', {});
    var stageOrder = safeParseObject(container, 'data-stage-order', []);
    var row = container.querySelector('[data-stage-row]');
    var emptyState = container.querySelector('[data-stage-empty]');

    if (!row) {
      return null;
    }

    function normalizeStageMap(key, fallback) {
      if (key && Object.prototype.hasOwnProperty.call(stageData, key)) {
        return stageData[key];
      }
      if (fallback && Object.prototype.hasOwnProperty.call(stageData, fallback)) {
        return stageData[fallback];
      }
      return null;
    }

    function buildOrder(stageMap) {
      var order = Array.isArray(stageOrder) ? stageOrder.slice() : [];
      var seen = {};

      order.forEach(function (code) {
        if (code) {
          seen[String(code)] = true;
        }
      });

      if (stageMap) {
        Object.keys(stageMap).forEach(function (code) {
          if (!seen[code]) {
            order.push(code);
          }
        });
      }

      return order;
    }

    function setEmptyState(show) {
      if (emptyState) {
        emptyState.hidden = !show;
      }
      row.hidden = Boolean(show);
    }

    function renderPills(stageMap) {
      row.innerHTML = '';

      if (!stageMap) {
        setEmptyState(true);
        return;
      }

      var ordered = buildOrder(stageMap);
      var rendered = 0;

      ordered.forEach(function (code) {
        if (!Object.prototype.hasOwnProperty.call(stageMap, code)) {
          return;
        }

        var count = Number(stageMap[code]) || 0;
        if (count <= 0) {
          return;
        }

        var pill = document.createElement('div');
        pill.className = 'ppulse__stage-pill';
        pill.setAttribute('role', 'listitem');

        var label = document.createElement('span');
        label.textContent = code;
        var value = document.createElement('strong');
        value.textContent = count.toString();

        pill.appendChild(label);
        pill.appendChild(value);
        row.appendChild(pill);
        rendered += 1;
      });

      setEmptyState(rendered === 0);
    }

    return function update(key) {
      var fallbackKey = container.getAttribute('data-default-key') || 'total';
      var stageMap = normalizeStageMap(key, fallbackKey);
      renderPills(stageMap);
    };
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

      var tile = createTreemapTile(item, tileWidth, tileHeight);
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

  function createTreemapTile(item, tileWidth, tileHeight) {
    var tile = document.createElement(item.url ? 'button' : 'div');
    tile.className = 'ppulse__treemap-tile' + (item.url ? ' ppulse__treemap-tile--link' : '');
    tile.title = item.label + ': ' + item.count;
    if (item.url) {
      tile.type = 'button';
      tile.setAttribute('data-url', item.url);
      tile.addEventListener('click', function () {
        window.location.href = item.url;
      });
    }

    // SECTION: Treemap tile modes (height-first with width override)
    var isMicro = tileHeight < 22;
    var isOneLine = !isMicro && tileHeight < 34 && tileWidth >= 140;
    var isTiny = !isMicro && !isOneLine && tileHeight < 34;

    if (isMicro) {
      tile.classList.add('ppulse__treemap-tile--micro');
    } else if (isOneLine) {
      tile.classList.add('ppulse__treemap-tile--one-line');
    } else if (isTiny) {
      tile.classList.add('ppulse__treemap-tile--tiny');
    }
    // END SECTION

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

    var hosts = Array.prototype.slice.call(root.querySelectorAll('.ppulse__chart[data-chart="treemap"]'));

    function hydrateChart(host) {
      if (host.getAttribute('data-hydrated') === 'true') {
        return;
      }
      var panel = host.closest('[role="tabpanel"]');
      if (panel && panel.hasAttribute('hidden')) {
        return;
      }

      var series = safeParse(host, 'data-series');
      if (!series.length) {
        return;
      }

      buildTreemap(host, series);
    }

    function hydrateAll() {
      hosts.forEach(hydrateChart);
      hosts = [];
    }

    var updateStagePills = initStagePills(root);

    initTabs(root, hydrateChart);
    initOngoingBuckets(root, updateStagePills);

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
