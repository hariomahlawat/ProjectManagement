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
    var size = 28 + Math.min(Math.max(digitCount - 1, 0) * 4, 8);
    return L.divIcon({
      html: '<div class="ffc-simulator-map__pin"><span>' + safeCount + '</span></div>',
      className: '',
      iconSize: [size, size],
      iconAnchor: [size / 2, size / 2]
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
        });

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
