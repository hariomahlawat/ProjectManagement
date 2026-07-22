// SECTION: FFC simulator footprint map
(function () {
  'use strict';

  if (typeof window === 'undefined' || typeof window.L === 'undefined') {
    return;
  }

  // SECTION: Value and payload helpers
  function safeParse(json) {
    try {
      return JSON.parse(json || '[]');
    } catch (error) {
      console.warn('FFC simulator map payload could not be parsed.', error); // eslint-disable-line no-console
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
    if (
      northWest.length !== 2 ||
      southEast.length !== 2 ||
      northWest.some(isNaN) ||
      southEast.some(isNaN)
    ) {
      return null;
    }

    return L.latLngBounds(
      L.latLng(southEast[0], northWest[1]),
      L.latLng(northWest[0], southEast[1])
    );
  }

  function readFlag(host, name, defaultValue) {
    var raw = host.getAttribute(name);
    if (raw === null || raw === '') {
      return defaultValue;
    }

    return raw !== 'false' && raw !== '0';
  }

  function readNumber(host, name, defaultValue) {
    var raw = Number(host.getAttribute(name));
    return Number.isFinite(raw) ? raw : defaultValue;
  }

  function property(country, camelName, pascalName) {
    if (!country) {
      return undefined;
    }
    if (typeof country[camelName] !== 'undefined') {
      return country[camelName];
    }
    return country[pascalName];
  }

  function valueOrZero(country, camelName, pascalName) {
    return Number(property(country, camelName, pascalName)) || 0;
  }

  function countryIso(country) {
    return String(property(country, 'iso3', 'Iso3') || '').toUpperCase();
  }

  function countryName(country) {
    return String(property(country, 'name', 'Name') || 'Partner country');
  }

  function countryDetailsUrl(country) {
    return String(property(country, 'detailsUrl', 'DetailsUrl') || '');
  }

  function escapeHtml(value) {
    return String(value || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function clamp(value, minimum, maximum) {
    return Math.min(Math.max(value, minimum), maximum);
  }

  function getIso(feature) {
    if (!feature || !feature.properties) {
      return '';
    }

    var properties = feature.properties;
    var candidates = [
      properties.iso_a3,
      properties.iso3,
      properties.iso,
      properties.ISO_A3,
      properties.ADM0_A3,
      properties.ADM0_A3_IN,
      properties.SOV_A3
    ];

    for (var index = 0; index < candidates.length; index += 1) {
      var value = String(candidates[index] || '').trim().toUpperCase();
      if (value && value !== '-99') {
        return value;
      }
    }

    return '';
  }

  function featureLabelLatLng(feature) {
    if (!feature || !feature.properties) {
      return null;
    }

    var properties = feature.properties;
    var longitudeValue = typeof properties.LABEL_X !== 'undefined'
      ? properties.LABEL_X
      : (typeof properties.label_x !== 'undefined' ? properties.label_x : properties.labelX);
    var latitudeValue = typeof properties.LABEL_Y !== 'undefined'
      ? properties.LABEL_Y
      : (typeof properties.label_y !== 'undefined' ? properties.label_y : properties.labelY);
    var longitude = Number(longitudeValue);
    var latitude = Number(latitudeValue);

    if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) {
      return null;
    }

    return L.latLng(latitude, longitude);
  }

  function buildLookup(countries) {
    return countries.reduce(function (lookup, country) {
      var iso = countryIso(country);
      if (iso) {
        lookup[iso] = country;
      }
      return lookup;
    }, {});
  }

  function buildCountryStats(country) {
    var installed = valueOrZero(country, 'installed', 'Installed');
    var delivered = valueOrZero(country, 'delivered', 'Delivered');
    var planned = valueOrZero(country, 'planned', 'Planned');
    var completed = installed + delivered;

    return {
      installed: installed,
      delivered: delivered,
      planned: planned,
      completed: completed,
      total: completed + planned,
      displayCount: completed > 0 ? completed : planned
    };
  }

  function completedValue(country) {
    var explicit = property(country, 'completed', 'Completed');
    if (typeof explicit !== 'undefined') {
      return Number(explicit) || 0;
    }
    return buildCountryStats(country).completed;
  }

  function colorScale(ratio, accent) {
    if (ratio >= 0.75) {
      return accent;
    }
    if (ratio >= 0.5) {
      return '#7185e8';
    }
    if (ratio >= 0.25) {
      return '#aebbf4';
    }
    return '#dbe3fb';
  }

  function createPin(stats, mode) {
    var safeCount = Number.isFinite(stats.displayCount) ? stats.displayCount : 0;
    var digitCount = String(Math.abs(safeCount)).length;
    var size = 32 + Math.min(Math.max(digitCount - 1, 0) * 5, 10);
    var pointer = Math.round(size * 0.28);
    var height = size + pointer;
    var plannedBadge = mode === 'mixed' && stats.planned > 0
      ? '<span class="ffc-simulator-map__pin-planned">+' + stats.planned + '</span>'
      : '';
    var html = '' +
      '<div class="ffc-simulator-map__pin ffc-simulator-map__pin--' + mode + '" style="--pin-size:' + size + 'px;--pin-pointer:' + pointer + 'px">' +
      '  <div class="ffc-simulator-map__pin-head">' +
      '    <span class="ffc-simulator-map__pin-count">' + safeCount + '</span>' +
      '  </div>' +
      plannedBadge +
      '</div>';

    return {
      icon: L.divIcon({
        html: html,
        className: '',
        iconSize: [size, height],
        iconAnchor: [size / 2, height]
      }),
      size: size,
      pointer: pointer,
      height: height
    };
  }

  function markerMode(stats) {
    if (stats.completed === 0 && stats.planned > 0) {
      return 'planned';
    }
    if (stats.completed > 0 && stats.planned > 0) {
      return 'mixed';
    }
    return 'completed';
  }
  // END SECTION

  // SECTION: Marker collision resolution
  function buildCandidateOffsets() {
    var offsets = [[0, 0]];
    var radii = [42, 68, 94, 120];
    var directions = [
      [0, -1], [1, 0], [-1, 0], [0, 1],
      [0.72, -0.72], [-0.72, -0.72], [0.72, 0.72], [-0.72, 0.72],
      [0.38, -0.92], [-0.38, -0.92], [0.92, -0.38], [-0.92, -0.38],
      [0.92, 0.38], [-0.92, 0.38], [0.38, 0.92], [-0.38, 0.92]
    ];

    radii.forEach(function (radius) {
      directions.forEach(function (direction) {
        offsets.push([
          Math.round(direction[0] * radius),
          Math.round(direction[1] * radius)
        ]);
      });
    });

    return offsets;
  }

  var candidateOffsets = buildCandidateOffsets();

  function markerBubbleCenter(anchorPoint, item) {
    return L.point(
      anchorPoint.x,
      anchorPoint.y - item.markerMeta.pointer - (item.markerMeta.size / 2)
    );
  }

  function markerFitsCanvas(anchorPoint, item, canvas) {
    var horizontalPadding = 8;
    var verticalPadding = 8;
    var halfWidth = item.markerMeta.size / 2;
    var bubbleTop = anchorPoint.y - item.markerMeta.height;

    return anchorPoint.x - halfWidth >= horizontalPadding &&
      anchorPoint.x + halfWidth <= canvas.clientWidth - horizontalPadding &&
      bubbleTop >= verticalPadding &&
      anchorPoint.y <= canvas.clientHeight - verticalPadding;
  }

  function markerCollides(anchorPoint, item, occupied) {
    var center = markerBubbleCenter(anchorPoint, item);
    var radius = (item.markerMeta.size / 2) + 4;

    return occupied.some(function (placed) {
      return center.distanceTo(placed.center) < radius + placed.radius + 7;
    });
  }
  // END SECTION

  // SECTION: Map builder
  function hydrate(host) {
    if (!host || host.getAttribute('data-ffc-hydrated') === 'true') {
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

    host.setAttribute('data-ffc-hydrated', 'true');

    var lookup = buildLookup(countries);
    var maxCompleted = countries.reduce(function (maximum, country) {
      return Math.max(maximum, completedValue(country));
    }, 0);
    var highlightColor = host.getAttribute('data-highlight-color') || '#3255d2';
    var autoFit = readFlag(host, 'data-auto-fit', false);
    var autoFitMaxZoom = readNumber(host, 'data-auto-fit-max-zoom', 4);
    var deconflictMarkers = readFlag(host, 'data-deconflict-markers', false);
    var scope = host.closest('[data-ffc-map-scope]') || host.parentElement || host;

    var map = L.map(canvas, {
      attributionControl: false,
      boxZoom: false,
      doubleClickZoom: false,
      dragging: true,
      keyboard: false,
      scrollWheelZoom: false,
      tap: false,
      touchZoom: false,
      zoomControl: false
    });

    var defaultBounds = parseBounds(host.getAttribute('data-focus-bounds'));
    if (defaultBounds) {
      map.fitBounds(defaultBounds, { animate: false });
    } else {
      map.setView([4, 35], 3);
    }

    var geoUrl = host.getAttribute('data-geo-url');
    if (!geoUrl) {
      host.classList.add('ffc-simulator-map--error');
      return;
    }

    fetch(geoUrl, { credentials: 'same-origin' })
      .then(function (response) {
        if (!response.ok) {
          throw new Error('Failed to load FFC map geography.');
        }
        return response.json();
      })
      .then(function (geoJson) {
        var tooltip = document.createElement('div');
        tooltip.className = 'ffc-simulator-map__tooltip';
        tooltip.setAttribute('role', 'group');
        tooltip.setAttribute('aria-label', 'Country simulator summary');
        host.appendChild(tooltip);

        var hideTimeout = null;
        var activeIso = null;
        var tooltipPositionToken = 0;
        var state = {};
        var layoutFrame = 0;

        function ensureItem(iso, country) {
          if (!state[iso]) {
            state[iso] = {
              iso: iso,
              country: country,
              layers: []
            };
          }
          return state[iso];
        }

        function baseStyle(country) {
          if (!country) {
            return {
              color: '#bcc9da',
              fillColor: '#f3f5f8',
              fillOpacity: 0.62,
              opacity: 1,
              weight: 0.8
            };
          }

          var stats = buildCountryStats(country);
          if (stats.completed === 0 && stats.planned > 0) {
            return {
              color: '#c47c00',
              fillColor: '#fff3cf',
              fillOpacity: 0.86,
              opacity: 1,
              weight: 1.25
            };
          }

          var ratio = maxCompleted > 0 ? stats.completed / maxCompleted : 0;
          return {
            color: '#4054a3',
            fillColor: colorScale(ratio, highlightColor),
            fillOpacity: 0.88,
            opacity: 1,
            weight: 1.05
          };
        }

        function applyItemStyle(item, style) {
          item.layers.forEach(function (layer) {
            layer.setStyle(style);
          });
        }

        function bringItemToFront(item) {
          item.layers.forEach(function (layer) {
            if (layer.bringToFront) {
              layer.bringToFront();
            }
          });
        }

        function renderTooltip(item) {
          var stats = item.stats || buildCountryStats(item.country);
          var name = escapeHtml(countryName(item.country));
          var detailsUrl = countryDetailsUrl(item.country);
          var rows = [
            '<div class="ffc-simulator-map__tooltip-title">' + name + '</div>',
            '<dl class="ffc-simulator-map__tooltip-metrics">',
            '  <div class="ffc-simulator-map__tooltip-row"><dt>Completed</dt><dd>' + stats.completed + '</dd></div>',
            '  <div class="ffc-simulator-map__tooltip-row"><dt>Installed</dt><dd>' + stats.installed + '</dd></div>',
            '  <div class="ffc-simulator-map__tooltip-row"><dt>Delivered, awaiting installation</dt><dd>' + stats.delivered + '</dd></div>'
          ];

          if (stats.planned > 0) {
            rows.push(
              '  <div class="ffc-simulator-map__tooltip-row"><dt>Planned</dt><dd>' + stats.planned + '</dd></div>'
            );
          }

          rows.push('</dl>');
          if (detailsUrl && detailsUrl !== '#') {
            rows.push(
              '<a class="ffc-simulator-map__tooltip-action" href="' + escapeHtml(detailsUrl) + '">' +
              '<span>View FFC projects</span><span aria-hidden="true">&rarr;</span></a>'
            );
          }

          tooltip.innerHTML = rows.join('');
        }

        function hideTooltipImmediately() {
          tooltipPositionToken += 1;
          tooltip.removeAttribute('data-visible');
          tooltip.classList.remove('is-below');
          tooltip.style.removeProperty('left');
          tooltip.style.removeProperty('top');
        }

        function positionTooltip(anchor) {
          if (!anchor) {
            hideTooltipImmediately();
            return;
          }

          var token = ++tooltipPositionToken;
          tooltip.classList.remove('is-below');
          tooltip.style.left = '0px';
          tooltip.style.top = '0px';
          tooltip.setAttribute('data-visible', 'true');

          window.requestAnimationFrame(function () {
            if (token !== tooltipPositionToken || !tooltip.hasAttribute('data-visible')) {
              return;
            }

            var width = tooltip.offsetWidth;
            var height = tooltip.offsetHeight;
            var left = clamp(anchor.x - (width / 2), 8, Math.max(8, host.clientWidth - width - 8));
            var top = anchor.y - height - 17;

            if (top < 8) {
              top = anchor.y + 17;
              tooltip.classList.add('is-below');
            }

            top = clamp(top, 8, Math.max(8, host.clientHeight - height - 8));
            tooltip.style.left = Math.round(left) + 'px';
            tooltip.style.top = Math.round(top) + 'px';
          });
        }

        function deriveAnchor(latLng) {
          if (!latLng) {
            return null;
          }

          var point = map.latLngToContainerPoint(latLng);
          var canvasRect = canvas.getBoundingClientRect();
          var hostRect = host.getBoundingClientRect();
          return {
            x: point.x + canvasRect.left - hostRect.left,
            y: point.y + canvasRect.top - hostRect.top
          };
        }

        function resetActiveStyle() {
          if (!activeIso || !state[activeIso]) {
            return;
          }

          var previous = state[activeIso];
          applyItemStyle(previous, baseStyle(previous.country));
          if (previous.markerElement) {
            var previousPin = previous.markerElement.querySelector('.ffc-simulator-map__pin');
            if (previousPin) {
              previousPin.classList.remove('ffc-simulator-map__pin--active');
            }
          }
          if (previous.marker) {
            previous.marker.setZIndexOffset(previous.defaultZIndex || 0);
          }
          if (previous.triggerElements) {
            previous.triggerElements.forEach(function (element) {
              element.classList.remove('is-active');
            });
          }
        }

        function showCountryTooltip(iso, anchor) {
          var item = state[String(iso || '').toUpperCase()];
          if (!item) {
            return;
          }

          window.clearTimeout(hideTimeout);
          if (activeIso !== item.iso) {
            resetActiveStyle();
          }

          activeIso = item.iso;
          renderTooltip(item);
          applyItemStyle(item, {
            color: highlightColor,
            fillColor: baseStyle(item.country).fillColor,
            fillOpacity: 1,
            opacity: 1,
            weight: 2
          });
          bringItemToFront(item);

          if (item.markerElement) {
            var pin = item.markerElement.querySelector('.ffc-simulator-map__pin');
            if (pin) {
              pin.classList.add('ffc-simulator-map__pin--active');
            }
          }
          if (item.marker) {
            item.marker.setZIndexOffset(2000);
          }
          if (item.triggerElements) {
            item.triggerElements.forEach(function (element) {
              element.classList.add('is-active');
            });
          }

          positionTooltip(anchor || item.anchor);
        }

        function scheduleHideTooltip() {
          window.clearTimeout(hideTimeout);
          hideTimeout = window.setTimeout(function () {
            hideTooltipImmediately();
            resetActiveStyle();
            activeIso = null;
          }, 190);
        }

        function navigateToCountry(item) {
          var url = item ? countryDetailsUrl(item.country) : '';
          if (url && url !== '#') {
            window.location.assign(url);
          }
        }

        function updateLeader(item, displayLatLng, pixelDistance) {
          if (!deconflictMarkers || pixelDistance < 8) {
            if (item.leader && map.hasLayer(item.leader)) {
              map.removeLayer(item.leader);
            }
            item.leader = null;
            return;
          }

          if (!item.leader) {
            item.leader = L.polyline(
              [item.actualLatLng, displayLatLng],
              {
                className: 'ffc-simulator-map__leader',
                interactive: false,
                opacity: 0.72,
                weight: 1.1
              }
            ).addTo(map);
          } else {
            item.leader.setLatLngs([item.actualLatLng, displayLatLng]);
            if (!map.hasLayer(item.leader)) {
              item.leader.addTo(map);
            }
          }
        }

        function layoutMarkers() {
          layoutFrame = 0;
          var items = Object.keys(state)
            .map(function (iso) { return state[iso]; })
            .filter(function (item) { return item.marker && item.actualLatLng; })
            .sort(function (left, right) {
              var completedDifference = right.stats.completed - left.stats.completed;
              if (completedDifference !== 0) {
                return completedDifference;
              }
              var totalDifference = right.stats.total - left.stats.total;
              if (totalDifference !== 0) {
                return totalDifference;
              }
              return countryName(left.country).localeCompare(countryName(right.country));
            });

          var occupied = [];
          items.forEach(function (item) {
            var actualPoint = map.latLngToContainerPoint(item.actualLatLng);
            var selectedPoint = actualPoint;

            if (deconflictMarkers) {
              for (var index = 0; index < candidateOffsets.length; index += 1) {
                var offset = candidateOffsets[index];
                var candidate = L.point(actualPoint.x + offset[0], actualPoint.y + offset[1]);
                if (!markerFitsCanvas(candidate, item, canvas)) {
                  continue;
                }
                if (!markerCollides(candidate, item, occupied)) {
                  selectedPoint = candidate;
                  break;
                }
              }
            }

            var displayLatLng = map.containerPointToLatLng(selectedPoint);
            var bubbleCenter = markerBubbleCenter(selectedPoint, item);
            var radius = (item.markerMeta.size / 2) + 4;
            var distance = actualPoint.distanceTo(selectedPoint);

            item.marker.setLatLng(displayLatLng);
            item.displayLatLng = displayLatLng;
            item.anchor = deriveAnchor(displayLatLng);
            occupied.push({ center: bubbleCenter, radius: radius });
            updateLeader(item, displayLatLng, distance);
          });

          if (activeIso && state[activeIso]) {
            positionTooltip(state[activeIso].anchor);
          }
        }

        function scheduleMarkerLayout() {
          if (layoutFrame) {
            window.cancelAnimationFrame(layoutFrame);
          }
          layoutFrame = window.requestAnimationFrame(layoutMarkers);
        }

        tooltip.addEventListener('mouseenter', function () {
          window.clearTimeout(hideTimeout);
        });
        tooltip.addEventListener('mouseleave', scheduleHideTooltip);

        var shapes = L.geoJSON(geoJson, {
          style: function (feature) {
            return baseStyle(lookup[getIso(feature)]);
          },
          onEachFeature: function (feature, layer) {
            var iso = getIso(feature);
            var country = lookup[iso];
            if (!country) {
              return;
            }

            var item = ensureItem(iso, country);
            var layerBounds = layer.getBounds();
            item.layers.push(layer);
            item.labelLatLng = item.labelLatLng || featureLabelLatLng(feature);
            if (item.bounds) {
              item.bounds.extend(layerBounds);
            } else {
              item.bounds = L.latLngBounds(layerBounds);
            }

            layer.on('mouseover', function (event) {
              showCountryTooltip(iso, deriveAnchor(event.latlng || item.actualLatLng));
            });
            layer.on('mouseout', scheduleHideTooltip);
            layer.on('click', function () {
              navigateToCountry(item);
            });
          }
        }).addTo(map);

        var footprintBounds = L.latLngBounds([]);
        Object.keys(state).forEach(function (iso) {
          var item = state[iso];
          var anchor = item.labelLatLng || (item.bounds && item.bounds.isValid() ? item.bounds.getCenter() : null);
          if (anchor) {
            footprintBounds.extend(anchor);
          }
        });

        if (autoFit && footprintBounds.isValid()) {
          map.fitBounds(footprintBounds, {
            animate: false,
            maxZoom: autoFitMaxZoom,
            paddingTopLeft: [34, 26],
            paddingBottomRight: [36, 52]
          });
        }

        Object.keys(state).forEach(function (iso) {
          var item = state[iso];
          if (!item.bounds || !item.bounds.isValid()) {
            return;
          }

          item.actualLatLng = item.labelLatLng || item.bounds.getCenter();
          item.stats = buildCountryStats(item.country);
          item.markerMeta = createPin(item.stats, markerMode(item.stats));
          item.defaultZIndex = Math.min(item.stats.completed * 4, 180);

          item.marker = L.marker(item.actualLatLng, {
            alt: countryName(item.country) + ' FFC simulator summary',
            bubblingMouseEvents: false,
            icon: item.markerMeta.icon,
            keyboard: true,
            riseOnHover: true,
            riseOffset: 800,
            title: countryName(item.country),
            zIndexOffset: item.defaultZIndex
          }).addTo(map);

          item.markerElement = item.marker.getElement();
          if (item.markerElement) {
            item.markerElement.setAttribute('role', 'link');
            item.markerElement.setAttribute(
              'aria-label',
              countryName(item.country) + ': ' +
              item.stats.completed + ' completed and ' +
              item.stats.planned + ' planned units. Open FFC projects.'
            );
            item.markerElement.addEventListener('focus', function () {
              showCountryTooltip(iso, item.anchor);
            });
            item.markerElement.addEventListener('blur', scheduleHideTooltip);
            item.markerElement.addEventListener('keydown', function (event) {
              if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                navigateToCountry(item);
              }
            });
          }

          item.marker.on('mouseover', function () {
            showCountryTooltip(iso, item.anchor);
          });
          item.marker.on('mouseout', scheduleHideTooltip);
          item.marker.on('click', function () {
            navigateToCountry(item);
          });
        });

        layoutMarkers();

        var listItems = scope.querySelectorAll('[data-country-chip]');
        listItems.forEach(function (element) {
          var iso = String(element.getAttribute('data-country-chip') || '').toUpperCase();
          var item = state[iso];
          if (!item) {
            return;
          }

          item.triggerElements = item.triggerElements || [];
          item.triggerElements.push(element);

          element.addEventListener('mouseenter', function () {
            showCountryTooltip(iso, item.anchor);
          });
          element.addEventListener('focus', function () {
            showCountryTooltip(iso, item.anchor);
          });
          element.addEventListener('mouseleave', scheduleHideTooltip);
          element.addEventListener('blur', scheduleHideTooltip);

          if (!element.matches('a[href]')) {
            element.addEventListener('click', function (event) {
              event.preventDefault();
              if (item.bounds && item.bounds.isValid()) {
                map.fitBounds(item.bounds, {
                  animate: true,
                  maxZoom: 6,
                  padding: [46, 46]
                });
              }
              showCountryTooltip(iso, item.anchor);
            });
          }
        });

        map.on('moveend zoomend', scheduleMarkerLayout);

        if (typeof ResizeObserver !== 'undefined') {
          var resizeObserver = new ResizeObserver(function () {
            map.invalidateSize(false);
            scheduleMarkerLayout();
          });
          resizeObserver.observe(canvas);
          host._ffcResizeObserver = resizeObserver; // Retain the observer for the life of the map host.
        } else {
          window.addEventListener('resize', function () {
            map.invalidateSize(false);
            scheduleMarkerLayout();
          }, { passive: true });
        }

        host.classList.add('ffc-simulator-map--ready');
        var status = host.querySelector('[data-map-status]');
        if (status) {
          status.setAttribute('aria-hidden', 'true');
        }

        // Keep a reference for diagnostics and future progressive enhancement.
        host._ffcMap = map;
        host._ffcShapes = shapes;
      })
      .catch(function (error) {
        console.warn('FFC simulator map could not be initialised.', error); // eslint-disable-line no-console
        host.classList.add('ffc-simulator-map--error');
      });
  }
  // END SECTION

  // SECTION: Initialisation
  var hosts = document.querySelectorAll('[data-widget="ffc-simulator-map"]');
  hosts.forEach(hydrate);
  // END SECTION
}());
// END SECTION
