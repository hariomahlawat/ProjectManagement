// SECTION: FFC simulator map widget bootstrapper
(function () {
  'use strict';

  if (typeof window === 'undefined' || typeof window.L === 'undefined') {
    return;
  }

  // SECTION: Helpers
  function safeParse(json) {
    try {
      return JSON.parse(json || '[]');
    } catch (err) {
      console.warn('FFC simulator map payload could not be parsed', err); // eslint-disable-line no-console
      return [];
    }
  }

  function parseBounds(raw) {
    if (!raw || typeof raw !== 'string') {
      return null;
    }
    var parts = raw.split('|');
    if (parts.length !== 2) {
      return null;
    }
    var northWest = parts[0].split(',').map(Number);
    var southEast = parts[1].split(',').map(Number);
    if (northWest.length !== 2 || southEast.length !== 2 || northWest.some(isNaN) || southEast.some(isNaN)) {
      return null;
    }
    return L.latLngBounds(L.latLng(southEast[0], northWest[1]), L.latLng(northWest[0], southEast[1]));
  }

  function getIso(feature) {
    if (!feature || !feature.properties) {
      return '';
    }
    var props = feature.properties;
    return String(props.iso_a3 || props.iso3 || props.iso || props.ISO_A3 || '').toUpperCase();
  }

  function buildLookup(countries) {
    return countries.reduce(function (acc, country) {
      var iso = String(country.iso3 || country.Iso3 || '').toUpperCase();
      if (iso) {
        acc[iso] = country;
      }
      return acc;
    }, {});
  }

  function valueOrZero(country, key) {
    if (!country) {
      return 0;
    }
    var value = country[key];
    if (typeof value === 'undefined') {
      var altKey = key.charAt(0).toUpperCase() + key.slice(1);
      value = country[altKey];
    }
    return Number(value) || 0;
  }

  function completedValue(country) {
    if (!country) {
      return 0;
    }
    if (typeof country.completed !== 'undefined') {
      return Number(country.completed) || 0;
    }
    if (typeof country.Completed !== 'undefined') {
      return Number(country.Completed) || 0;
    }
    return valueOrZero(country, 'installed') + valueOrZero(country, 'delivered');
  }

  function colorScale(ratio, accent) {
    if (ratio >= 0.75) {
      return accent;
    }
    if (ratio >= 0.5) {
      return '#7c3aed';
    }
    if (ratio >= 0.25) {
      return '#a78bfa';
    }
    return '#ddd6fe';
  }

  function createPin(count) {
    var safeCount = typeof count === 'number' && !Number.isNaN(count) ? count : 0;
    var digitCount = String(Math.abs(safeCount)).length;
    var size = 38 + Math.min(Math.max(digitCount - 1, 0) * 6, 12);
    var html = '' +
      '<div class="ffc-simulator-map__pin" style="--pin-size:' + size + 'px">' +
      '  <span class="ffc-simulator-map__pin-count">' + safeCount + '</span>' +
      '</div>';
    return L.divIcon({
      html: html,
      className: '',
      iconSize: [size, size + 14],
      iconAnchor: [size / 2, size + 10]
    });
  }

  function createOverlay(host) {
    var overlay = document.createElement('div');
    overlay.className = 'ffc-simulator-map__overlay';

    var leaderLayer = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    leaderLayer.setAttribute('class', 'ffc-simulator-map__leader-layer');
    leaderLayer.setAttribute('width', '100%');
    leaderLayer.setAttribute('height', '100%');

    var calloutsLayer = document.createElement('div');
    calloutsLayer.className = 'ffc-simulator-map__callouts';

    overlay.appendChild(leaderLayer);
    overlay.appendChild(calloutsLayer);
    host.appendChild(overlay);

    return { overlay: overlay, leaderLayer: leaderLayer, calloutsLayer: calloutsLayer };
  }

  function buildCalloutContent(country, installed, delivered, planned) {
    var safeName = country.name || country.Name || 'Untitled';
    var total = installed + delivered;
    return '' +
      '<p class="ffc-simulator-map__callout-title">' + safeName + '</p>' +
      '<dl class="ffc-simulator-map__callout-stats">' +
      '  <div><dt>Installed</dt><dd>' + installed + '</dd></div>' +
      '  <div><dt>Delivered</dt><dd>' + delivered + '</dd></div>' +
      '  <div><dt>Total</dt><dd>' + total + '</dd></div>' +
      (planned > 0 ? '<div><dt>Planned</dt><dd>' + planned + '</dd></div>' : '') +
      '</dl>';
  }

  function clamp(value, min, max) {
    return Math.max(min, Math.min(value, max));
  }

  function buildCalloutEntry(options) {
    var callout = document.createElement('div');
    callout.className = 'ffc-simulator-map__callout';
    callout.innerHTML = options.content;

    var line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
    line.setAttribute('class', 'ffc-simulator-map__leader-line');
    options.leaderLayer.appendChild(line);
    options.calloutsLayer.appendChild(callout);

    return {
      callout: callout,
      line: line,
      latLng: options.latLng,
      delta: { x: 24, y: -36 },
      position: { x: 0, y: 0 }
    };
  }

  function syncDelta(entry, map) {
    var anchor = map.latLngToContainerPoint(entry.latLng);
    entry.delta.x = entry.position.x - anchor.x;
    entry.delta.y = entry.position.y - anchor.y;
  }

  function updateLine(entry, anchor, overlay, width, height) {
    var line = entry.line;
    var calloutX = entry.position.x;
    var calloutY = entry.position.y;
    var calloutMidY = calloutY + height / 2;
    var connectLeft = anchor.x <= calloutX + width / 2;
    var connectX = connectLeft ? calloutX : calloutX + width;
    line.setAttribute('x1', anchor.x);
    line.setAttribute('y1', anchor.y);
    line.setAttribute('x2', clamp(connectX, 0, overlay.clientWidth));
    line.setAttribute('y2', clamp(calloutMidY, 0, overlay.clientHeight));
  }

  function applyCalloutPosition(entry, map, overlay, override) {
    var anchor = map.latLngToContainerPoint(entry.latLng);
    var baseX = typeof override === 'object' && typeof override.x === 'number'
      ? override.x
      : anchor.x + entry.delta.x;
    var baseY = typeof override === 'object' && typeof override.y === 'number'
      ? override.y
      : anchor.y + entry.delta.y;

    var callout = entry.callout;
    var width = callout.offsetWidth || 200;
    var height = callout.offsetHeight || 100;
    var maxX = Math.max(overlay.clientWidth - width, 0);
    var maxY = Math.max(overlay.clientHeight - height, 0);
    var clampedX = clamp(baseX, 0, maxX);
    var clampedY = clamp(baseY, 0, maxY);

    entry.position.x = clampedX;
    entry.position.y = clampedY;
    callout.style.left = clampedX + 'px';
    callout.style.top = clampedY + 'px';

    updateLine(entry, anchor, overlay, width, height);
  }

  function enableCalloutDrag(entry, map, overlay) {
    var callout = entry.callout;
    var dragging = false;
    var pointerId = null;
    var origin = { x: 0, y: 0 };
    var startPosition = { x: 0, y: 0 };

    function onPointerDown(event) {
      event.preventDefault();
      event.stopPropagation();
      dragging = true;
      pointerId = event.pointerId;
      callout.setPointerCapture(pointerId);
      callout.classList.add('is-dragging');
      origin.x = event.clientX;
      origin.y = event.clientY;
      startPosition.x = entry.position.x;
      startPosition.y = entry.position.y;
    }

    function onPointerMove(event) {
      if (!dragging || event.pointerId !== pointerId) {
        return;
      }
      var deltaX = event.clientX - origin.x;
      var deltaY = event.clientY - origin.y;
      applyCalloutPosition(entry, map, overlay, {
        x: startPosition.x + deltaX,
        y: startPosition.y + deltaY
      });
    }

    function stopDragging(event) {
      if (!dragging || event.pointerId !== pointerId) {
        return;
      }
      dragging = false;
      if (typeof callout.hasPointerCapture === 'function' && callout.hasPointerCapture(pointerId)) {
        callout.releasePointerCapture(pointerId);
      }
      callout.classList.remove('is-dragging');
      syncDelta(entry, map);
    }

    callout.addEventListener('pointerdown', onPointerDown);
    callout.addEventListener('pointermove', onPointerMove);
    callout.addEventListener('pointerup', stopDragging);
    callout.addEventListener('pointercancel', stopDragging);
  }
  // END SECTION

  // SECTION: Map builder
  function hydrate(host) {
    if (!host) {
      return;
    }
    var canvas = host.querySelector('.ffc-simulator-map__canvas');
    if (!canvas) {
      return;
    }

    var countries = safeParse(host.getAttribute('data-countries'));
    if (!countries.length) {
      host.classList.add('ffc-simulator-map--empty');
      return;
    }

    var lookup = buildLookup(countries);
    var overlayElements = createOverlay(host);
    var calloutEntries = [];
    var maxCompleted = countries.reduce(function (max, country) {
      return Math.max(max, completedValue(country));
    }, 0);
    var highlightColor = host.getAttribute('data-highlight-color') || '#4338ca';
    var map = L.map(canvas, {
      attributionControl: false,
      zoomControl: false,
      scrollWheelZoom: false,
      doubleClickZoom: false,
      dragging: true
    });

    var defaultBounds = parseBounds(host.getAttribute('data-focus-bounds'));
    if (defaultBounds) {
      map.fitBounds(defaultBounds);
    } else {
      map.setView([4, 21], 3);
    }

    var geoUrl = host.getAttribute('data-geo-url');
    if (!geoUrl) {
      host.classList.add('ffc-simulator-map--error');
      return;
    }

    fetch(geoUrl)
      .then(function (response) {
        if (!response.ok) {
          throw new Error('Failed to load geo data');
        }
        return response.json();
      })
      .then(function (geojson) {
        var shapes = L.geoJSON(geojson, {
          style: function (feature) {
            var country = lookup[getIso(feature)];
            var completed = completedValue(country);
            var ratio = maxCompleted > 0 ? completed / maxCompleted : 0;
            return {
              color: country ? '#312e81' : '#cbd5f5',
              weight: country ? 1.2 : 0.8,
              fillColor: country ? colorScale(ratio, highlightColor) : '#f1f5f9',
              fillOpacity: country ? 0.9 : 0.45,
              opacity: 1
            };
          },
          onEachFeature: function (feature, layer) {
            var country = lookup[getIso(feature)];
            if (!country) {
              return;
            }
            var installed = valueOrZero(country, 'installed');
            var delivered = valueOrZero(country, 'delivered');
            var planned = valueOrZero(country, 'planned');
            var tooltip = '<strong>' + (country.name || country.Name || '') + '</strong>' +
              '<br/>Installed: ' + installed +
              '<br/>Delivered: ' + delivered +
              '<br/>Planned: ' + planned;
            layer.bindTooltip(tooltip, { sticky: true, direction: 'top' });
          }
        }).addTo(map);

        function refreshCallouts() {
          calloutEntries.forEach(function (entry) {
            applyCalloutPosition(entry, map, overlayElements.overlay);
          });
        }

        shapes.eachLayer(function (layer) {
          var country = lookup[getIso(layer.feature)];
          if (!country) {
            return;
          }
          var delivered = valueOrZero(country, 'delivered');
          var installed = valueOrZero(country, 'installed');
          var displayCount = delivered > 0 ? delivered : installed;
          var center = layer.getBounds().getCenter();
          L.marker(center, { icon: createPin(displayCount) }).addTo(map);

          var planned = valueOrZero(country, 'planned');
          var entry = buildCalloutEntry({
            content: buildCalloutContent(country, installed, delivered, planned),
            leaderLayer: overlayElements.leaderLayer,
            calloutsLayer: overlayElements.calloutsLayer,
            latLng: center
          });
          calloutEntries.push(entry);
          applyCalloutPosition(entry, map, overlayElements.overlay);
          syncDelta(entry, map);
          enableCalloutDrag(entry, map, overlayElements.overlay);
        });

        map.on('move zoom', refreshCallouts);
        map.on('resize', refreshCallouts);
        window.addEventListener('resize', refreshCallouts, { passive: true });

        host.classList.add('ffc-simulator-map--ready');
        var status = host.querySelector('[data-map-status]');
        if (status) {
          status.setAttribute('aria-hidden', 'true');
        }
      })
      .catch(function () {
        host.classList.add('ffc-simulator-map--error');
      });
  }
  // END SECTION

  // SECTION: Initialisation
  var hosts = document.querySelectorAll('[data-widget="ffc-simulator-map"]');
  if (hosts.length) {
    hosts.forEach(hydrate);
  }
  // END SECTION
})();
// END SECTION
