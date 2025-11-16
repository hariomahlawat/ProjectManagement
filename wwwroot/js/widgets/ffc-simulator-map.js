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

  function buildCountryStats(country) {
    var installed = valueOrZero(country, 'installed');
    var delivered = valueOrZero(country, 'delivered');
    var planned = valueOrZero(country, 'planned');
    var total = installed + delivered;
    return {
      installed: installed,
      delivered: delivered,
      planned: planned,
      total: total,
      displayCount: delivered > 0 ? delivered : installed
    };
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
    var size = 34 + Math.min(Math.max(digitCount - 1, 0) * 6, 12);
    var pointer = Math.round(size * 0.28);
    var height = size + pointer;
    var html = '' +
      '<div class="ffc-simulator-map__pin" style="--pin-size:' + size + 'px; --pin-pointer:' + pointer + 'px">' +
      '  <div class="ffc-simulator-map__pin-head">' +
      '    <span class="ffc-simulator-map__pin-count">' + safeCount + '</span>' +
      '  </div>' +
      '</div>';
    return L.divIcon({
      html: html,
      className: '',
      iconSize: [size, height],
      iconAnchor: [size / 2, height]
    });
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
        var tooltip = document.createElement('div');
        tooltip.className = 'ffc-simulator-map__tooltip';
        host.appendChild(tooltip);

        var hideTimeout = null;
        var activeIso = null;
        var state = {};

        function baseStyle(country) {
          var completed = completedValue(country);
          var ratio = maxCompleted > 0 ? completed / maxCompleted : 0;
          return {
            color: country ? '#312e81' : '#cbd5f5',
            weight: country ? 1.2 : 0.8,
            fillColor: country ? colorScale(ratio, highlightColor) : '#f1f5f9',
            fillOpacity: country ? 0.9 : 0.45,
            opacity: 1
          };
        }

        function renderTooltip(country) {
          var stats = buildCountryStats(country);
          var name = country.name || country.Name || '';
          var rows = [
            '<div class="ffc-simulator-map__tooltip-title">' + name + '</div>',
            '<dl class="ffc-simulator-map__tooltip-metrics">',
            '  <div class="ffc-simulator-map__tooltip-row">',
            '    <dt>Installed</dt><dd>' + stats.installed + '</dd>',
            '  </div>',
            '  <div class="ffc-simulator-map__tooltip-row">',
            '    <dt>Delivered</dt><dd>' + stats.delivered + '</dd>',
            '  </div>',
            '  <div class="ffc-simulator-map__tooltip-row">',
            '    <dt>Total</dt><dd>' + stats.total + '</dd>',
            '  </div>',
            '</dl>'
          ];
          tooltip.innerHTML = rows.join('');
        }

        function positionTooltip(anchor) {
          if (!anchor) {
            tooltip.style.opacity = '0';
            return;
          }
          tooltip.style.left = anchor.x + 'px';
          tooltip.style.top = anchor.y + 'px';
          tooltip.style.opacity = '1';
        }

        function resetActiveStyle() {
          if (activeIso && state[activeIso]) {
            var prev = state[activeIso];
            if (prev.layer) {
              prev.layer.setStyle(baseStyle(prev.country));
            }
            if (prev.markerEl) {
              prev.markerEl.classList.remove('ffc-simulator-map__pin--active');
            }
          }
        }

        function showCountryTooltip(countryIso, anchor) {
          if (!state[countryIso]) {
            return;
          }
          clearTimeout(hideTimeout);
          var item = state[countryIso];
          if (activeIso !== countryIso) {
            resetActiveStyle();
          }
          activeIso = countryIso;
          renderTooltip(item.country);
          positionTooltip(anchor || item.anchor);
          if (item.layer) {
            item.layer.setStyle({
              color: highlightColor,
              weight: 2,
              fillOpacity: 1,
              opacity: 1
            });
            if (item.layer.bringToFront) {
              item.layer.bringToFront();
            }
          }
          if (item.markerEl) {
            item.markerEl.classList.add('ffc-simulator-map__pin--active');
          }
          tooltip.setAttribute('data-visible', 'true');
        }

        function scheduleHideTooltip() {
          clearTimeout(hideTimeout);
          hideTimeout = setTimeout(function () {
            tooltip.removeAttribute('data-visible');
            tooltip.style.opacity = '0';
            resetActiveStyle();
            activeIso = null;
          }, 180);
        }

        function deriveAnchorFromEvent(e, fallbackLatLng) {
          var latLng = e && e.latlng ? e.latlng : fallbackLatLng;
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

        var shapes = L.geoJSON(geojson, {
          style: function (feature) {
            var country = lookup[getIso(feature)];
            return baseStyle(country);
          },
          onEachFeature: function (feature, layer) {
            var iso = getIso(feature);
            var country = lookup[iso];
            if (!country) {
              return;
            }
            var bounds = layer.getBounds();
            state[iso] = state[iso] || { country: country };
            state[iso].layer = layer;
            state[iso].bounds = bounds;
            state[iso].anchor = deriveAnchorFromEvent(null, bounds.getCenter());

            layer.on('mouseover', function (e) {
              showCountryTooltip(iso, deriveAnchorFromEvent(e));
            });
            layer.on('mouseout', function () {
              scheduleHideTooltip();
            });
          }
        }).addTo(map);

        shapes.eachLayer(function (layer) {
          var iso = getIso(layer.feature);
          var country = lookup[iso];
          if (!country) {
            return;
          }
          var stats = buildCountryStats(country);
          var center = layer.getBounds().getCenter();
          var marker = L.marker(center, { icon: createPin(stats.displayCount) }).addTo(map);
          var markerEl = marker.getElement();
          state[iso] = state[iso] || { country: country };
          state[iso].marker = marker;
          state[iso].markerEl = markerEl;
          state[iso].anchor = deriveAnchorFromEvent(null, center);

          marker.on('mouseover', function (e) {
            var anchor = deriveAnchorFromEvent(e, center);
            showCountryTooltip(iso, anchor);
          });
          marker.on('mouseout', function () {
            scheduleHideTooltip();
          });
        });

        var listItems = host.querySelectorAll('[data-country-chip]');
        if (listItems.length) {
          listItems.forEach(function (itemEl) {
            var iso = itemEl.getAttribute('data-country-chip');
            itemEl.addEventListener('mouseenter', function () {
              showCountryTooltip(iso);
            });
            itemEl.addEventListener('focus', function () {
              showCountryTooltip(iso);
            });
            itemEl.addEventListener('mouseleave', function () {
              scheduleHideTooltip();
            });
            itemEl.addEventListener('blur', function () {
              scheduleHideTooltip();
            });
            itemEl.addEventListener('click', function (e) {
              e.preventDefault();
              var item = state[iso];
              if (!item) {
                return;
              }
              if (item.bounds) {
                map.fitBounds(item.bounds, { maxZoom: 6 });
              } else if (item.marker) {
                map.panTo(item.marker.getLatLng());
              }
              showCountryTooltip(iso, item.anchor);
            });
          });
        }

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
