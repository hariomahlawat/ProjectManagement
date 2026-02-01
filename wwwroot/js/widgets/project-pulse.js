// SECTION: Project pulse widget bootstrap
(function () {
  'use strict';

  if (typeof window === 'undefined') {
    return;
  }

  // SECTION: Utilities
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
    var segmentEls = Array.prototype.slice.call(container.querySelectorAll('[data-ppulse-bucket-seg]'));
    if (!filters.length || !segmentEls.length) {
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

    var segments = segmentEls.reduce(function (acc, el) {
      var key = el.getAttribute('data-ppulse-bucket-seg');
      if (!key) {
        return acc;
      }
      acc[key] = {
        el: el,
        valueEl: el.querySelector('[data-ppulse-bucket-seg-value]'),
        percentEl: el.querySelector('[data-ppulse-bucket-seg-percent]')
      };
      return acc;
    }, {});

    function updateSegments(bucket) {
      if (!bucket) {
        return;
      }

      var values = {
        apvl: readNumber(bucket.apvl || bucket.Apvl),
        aon: readNumber(bucket.aon || bucket.Aon),
        tender: readNumber(bucket.tender || bucket.Tender),
        devp: readNumber(bucket.devp || bucket.Devp)
      };
      var other = readNumber(bucket.other || bucket.Other);
      var total = readNumber(bucket.total || bucket.Total);
      var visibleTotal = Math.max(0, total - other);

      Object.keys(segments).forEach(function (key) {
        var meta = segments[key];
        if (!meta) {
          return;
        }

        var value = values[key] || 0;

        if (value <= 0 || visibleTotal <= 0) {
          meta.el.style.display = 'none';
          meta.el.style.flexGrow = '0';
          meta.el.style.flexBasis = '0px';
          return;
        }

        meta.el.style.display = '';
        if (meta.valueEl) {
          meta.valueEl.textContent = value.toString();
        }

        var pct = Math.round((value / visibleTotal) * 100);
        if (meta.percentEl) {
          meta.percentEl.textContent = pct + '%';
        }

        meta.el.style.width = '';
        meta.el.style.flexGrow = String(value);
        meta.el.style.flexShrink = '1';
        meta.el.style.flexBasis = '0px';
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

  // SECTION: Tabs
  function initTabs(root) {
    var containers = root.querySelectorAll('[data-ppulse-tabs]');
    if (!containers || containers.length === 0) {
      return;
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

    var updateStagePills = initStagePills(root);

    initTabs(root);
    initOngoingBuckets(root, updateStagePills);
  }
  // END SECTION

  document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('[data-project-pulse]').forEach(init);
  });
})();
