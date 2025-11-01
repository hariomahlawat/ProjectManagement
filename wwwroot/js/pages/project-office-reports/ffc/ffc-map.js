(function (window, document) {
  'use strict';

  var DEFAULT_ICON_URL = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABkAAAApCAYAAADAk4LOAAAFgUlEQVR4Aa1XA5BjWRTN2oW17d3YaZtr2962HUzbDNpjszW24mRt28p47v7zq/bXZtrp/lWnXr337j3nPCe85NcypgSFdugCpW5YoDAMRaIMqRi6aKq5E3YqDQO3qAwjVWrD8Ncq/RBpykd8oZUb/kaJutow8r1aP9II0WmLKLIsJyv1w/kqw9Ch2MYdB++12Onxee/QMwvf4/Dk/Lfp/i4nxTXtOoQ4pW5Aj7wpici1A9erdAN2OH64x8OSP9j3Ft3b7aWkTg/Fm91siTra0f9on5sQr9INejH6CUUUpavjFNq1B+Oadhxmnfa8RfEmN8VNAsQhPqF55xHkMzz3jSmChWU6f7/XZKNH+9+hBLOHYozuKQPxyMPUKkrX/K0uWnfFaJGS1QPRtZsOPtr3NsW0uyh6NNCOkU3Yz+bXbT3I8G3xE5EXLXtCXbbqwCO9zPQYPRTZ5vIDXD7U+w7rFDEoUUf7ibHIR4y6bLVPXrz8JVZEql13trxwue/uDivd3fkWRbS6/IA2bID4uk0UpF1N8qLlbBlXs4Ee7HLTfV1j54APvODnSfOWBqtKVvjgLKzF5YdEk5ewRkGlK0i33Eofffc7HT56jD7/6U+qH3Cx7SBLNntH5YIPvODnyfIXZYRVDPqgHtLs5ABHD3YzLuespb7t79FY34DjMwrVrcTuwlT55YMPvOBnRrJ4VXTdNnYug5ucHLBjEpt30701A3Ts+HEa73u6dT3FNWwflY86eMHPk+Yu+i6pzUpRrW7SNDg5JHR4KapmM5Wv2E8Tfcb1HoqqHMHU+uWDD7zg54mz5/2BSnizi9T1Dg4QQXLToGNCkb6tb1NU+QAlGr1++eADrzhn/u8Q2YZhQVlZ5+CAOtqfbhmaUCS1ezNFVm2imDbPmPng5wmz+gwh+oHDce0eUtQ6OGDIyR0uUhUsoO3vfDmmgOezH0mZN59x7MBi++WDL1g/eEiU3avlidO671bkLfwbw5XV2P8Pzo0ydy4t2/0eu33xYSOMOD8hTf4CrBtGMSoXfPLchX+J0ruSePw3LZeK0juPJbYzrhkH0io7B3k164hiGvawhOKMLkrQLyVpZg8rHFW7E2uHOL888IBPlNZ1FPzstSJM694fWr6RwpvcJK60+0HCILTBzZLFNdtAzJaohze60T8qBzyh5ZuOg5e7uwQppofEmf2++DYvmySqGBuKaicF1blQjhuHdvCIMvp8whTTfZzI7RldpwtSzL+F1+wkdZ2TBOW2gIF88PBTzD/gpeREAMEbxnJcaJHNHrpzji0gQCS6hdkEeYt9DF/2qPcEC8RM28Hwmr3sdNyht00byAut2k3gufWNtgtOEOFGUwcXWNDbdNbpgBGxEvKkOQsxivJx33iow0Vw5S6SVTrpVq11ysA2Rp7gTfPfktc6zhtXBBC+adRLshf6sG2RfHPZ5EAc4sVZ83yCN00Fk/4kggu40ZTvIEm5g24qtU4KjBrx/BTTH8ifVASAG7gKrnWxJDcU7x8X6Ecczhm3o6YicvsLXWfh3Ch1W0k8x0nXF+0fFxgt4phz8QvypiwCCFKMqXCnqXExjq10beH+UUA7+nG6mdG/Pu0f3LgFcGrl2s0kNNjpmoJ9o4B29CMO8dMT4Q5ox8uitF6fqsrJOr8qnwNbRzv6hSnG5wP+64C7h9lp30hKNtKdWjtdkbuPA19nJ7Tz3zR/ibgARbhb4AlhavcBebmTHcFl2fvYEnW0ox9xMxKBS8btJ+KiEbq9zA4RthQXDhPa0T9TEe69gWupwc6uBUphquXgf+/FrIjweHQS4/pduMe5ERUMHUd9xv8ZR98CxkS4F2n3EUrUZ10EYNw7BWm9x1GiPssi3GgiGRDKWRYZfXlON+dfNbM+GgIwYdwAAAAASUVORK5CYII=';
  var DEFAULT_ICON_RETINA_URL = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAADIAAABSCAMAAAAhFXfZAAAC91BMVEVMaXEzeak2f7I4g7g3g7cua5gzeKg8hJo3grY4g7c3grU0gLI2frE0daAubJc2gbQwd6QzeKk2gLMtd5sxdKIua5g1frA2f7IydaM0e6w2fq41fK01eqo3grgubJgta5cxdKI1f7AydaQydaMxc6EubJgvbJkwcZ4ubZkwcJwubZgubJcydqUydKIxapgubJctbJcubZcubJcvbJYubJcvbZkubJctbJctbZcubJg2f7AubJcrbZcubJcubJcua5g3grY0fq8ubJcubJdEkdEwhsw6i88vhswuhcsuhMtBjMgthMsrg8srgss6is8qgcs8i9A9iMYtg8spgcoogMo7hcMngMonf8olfso4gr8kfck5iM8jfMk4iM8he8k1fro7itAgesk2hs8eecgzfLcofssdeMg0hc4cd8g2hcsxeLQbdsgZdcgxeLImfcszhM0vda4xgckzhM4xg84wf8Yxgs4udKsvfcQucqhUndROmdM1fK0wcZ8vb5w0eqpQm9MzeKhXoNVcpdYydKNWn9VZotVKltJFjsIwcJ1Rms9OlslLmtH///8+kc9epdYzd6dbo9VHkMM2f7FHmNBClM8ydqVcpNY9hro3gLM9hLczealQmcw3fa46f7A8gLMxc6I3eagyc6FIldJMl9JSnNRSntNNl9JPnNJFi75UnM9ZodVKksg8kM45jc09e6ZHltFBk883gbRBh7pDk9EwcaBzn784g7dKkcY2i81Om9M7j85Llc81is09g7Q4grY/j9A0eqxKmdFFltBEjcXf6fFImdBCiLxJl9FGlNFBi78yiMxVndEvbpo6js74+vx+psPP3+o/ks5HkcpGmNCjwdZCkNDM3ehYoNJEls+lxNkxh8xHks0+jdC1zd5Lg6r+/v/H2ufz9/o3jM3t8/edvdM/k89Th61OiLBSjbZklbaTt9BfptdjmL1AicBHj8hGk9FAgK1dkLNTjLRekrdClc/k7fM0icy0y9tgp9c4jc2NtM9Dlc8zicxeXZn3AAAAQ3RSTlMAHDdTb4yPA+LtnEQmC4L2EmHqB7XA0d0sr478x4/Yd5i1zOfyPkf1sLVq4Nh3FvjxopQ2/STNuFzUwFIwxKaejILpIBEV9wAABhVJREFUeF6s1NdyFEcY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';
  var DEFAULT_SHADOW_URL = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACkAAAApCAQAAAACach9AAACMUlEQVR4Ae3ShY7jQBAE0Aoz/f9/HTMzhg1zrdKUrJbdx+Kd2nD8VNudfsL/Th///dyQN2TH6f3y/BGpC379rV+S+qqetBOxImNQXL8JCAr2V4iMQXHGNJxeCfZXhSRBcQMfvkOWUdtfzlLgAENmZDcmo2TVmt8OSM2eXxBp3DjHSMFutqS7SbmemzBiR+xpKCNUIRkdkkYxhAkyGoBvyQFEJEefwSmmvBfJuJ6aKqKWnAkvGZOaZXTUgFqYULWNSHUckZuR1HIIimUExutRxwzOLROIG4vKmCKQt364mIlhSyzAf1m9lHZHJZrlAOMMztRRiKimp/rpdJDc9Awry5xTZCte7FHtuS8wJgeYGrex28xNTd086Dik7vUMscQOa8y4DoGtCCSkAKlNwpgNtphjrC6MIHUkR6YWxxs6Sc5xqn222mmCRFzIt8lEdKx+ikCtg91qS2WpwVfBelJCiQJwvzixfI9cxZQWgiSJelKnwBElKYtDOb2MFbhmUigbReQBV0Cg4+qMXSxXSyGUn4UbF8l+7qdSGnTC0XLCmahIgUHLhLOhpVCtw4CzYXvLQWQbJNmxoCsOKAxSgBJno75avolkRw8iIAFcsdc02e9iyCd8tHwmeSSoKTowIgvscSGZUOA7PuCN5b2BX9mQM7S0wYhMNU74zgsPBj3HU7wguAfnxxjFQGBE6pwN+GjME9zHY7zGp8wVxMShYX9NXvEWD3HbwJf4giO4CFIQxXScH1/TM+04kkBiAAAAAElFTkSuQmCC';
  function ensureArray(value) {
    if (Array.isArray(value)) {
      return value;
    }

    return [];
  }

  function formatNumber(value) {
    return Number(value || 0).toLocaleString(undefined);
  }

  function buildColorScale(maxValue) {
    var safeMax = Math.max(1, maxValue);
    var stops = [
      0,
      Math.ceil(safeMax * 0.05),
      Math.ceil(safeMax * 0.15),
      Math.ceil(safeMax * 0.35),
      Math.ceil(safeMax * 0.65),
      safeMax
    ];
    var colors = ['#eef2ff', '#c7d2fe', '#a5b4fc', '#818cf8', '#6366f1', '#4f46e5'];

    function colorFor(value) {
      var index = 0;
      while (index < stops.length - 1 && value > stops[index]) {
        index += 1;
      }

      return colors[index];
    }

    return {
      stops: stops,
      colors: colors,
      colorFor: colorFor
    };
  }

  function getIsoCodeFromProperties(props) {
    if (!props) {
      return '';
    }

    var value = props.iso_a3 ||
      props.ISO_A3 ||
      props.iso3 ||
      props['ISO3166-1-Alpha-3'] ||
      props['iso3166-1-alpha-3'] ||
      '';

    return value.toString().toUpperCase();
  }

  function buildPopupHtml(record, fallbackName, cfg) {
    var delivered = record ? Number(record.delivered || 0) : 0;
    var name = record && record.name ? record.name : fallbackName || 'Unknown';
    var perYear = record ? ensureArray(record.perYear) : [];

    var yearsHtml = perYear
      .slice()
      .sort(function (a, b) { return Number(b.year) - Number(a.year); })
      .slice(0, 5)
      .map(function (item) {
        return '<div><span class="text-muted">' + item.year + '</span> — <strong>' + formatNumber(item.count) + '</strong></div>';
      })
      .join('');

    var linkHtml = record
      ? '<a class="btn btn-sm btn-primary mt-2" href="' + cfg.ffcIndexUrlBase + '?countryId=' + encodeURIComponent(record.countryId) + '&delivery=completed">View in list</a>'
      : '';

    return (
      '<div class="ffc-tip">' +
      '<div class="ffc-tip__title">' + name + '</div>' +
      '<div class="ffc-tip__metric"><span>Delivered</span><strong>' + formatNumber(delivered) + '</strong></div>' +
      (yearsHtml ? '<div class="ffc-tip__years">' + yearsHtml + '</div>' : '') +
      linkHtml +
      '</div>'
    );
  }

  function renderLegend(scale) {
    var rampElements = document.querySelectorAll('.legend-ramp');
    var labelLists = document.querySelectorAll('.legend-labels');

    rampElements.forEach(function (element) {
      element.innerHTML = scale.colors
        .map(function (color) { return '<span style="background:' + color + '"></span>'; })
        .join('');
    });

    labelLists.forEach(function (list) {
      var steps = scale.stops;
      list.innerHTML = '' +
        '<li>0</li>' +
        '<li>' + formatNumber(steps[2]) + '</li>' +
        '<li>' + formatNumber(steps[3]) + '</li>' +
        '<li>' + formatNumber(steps[steps.length - 1]) + '+</li>';
    });
  }

  function attachZoomControls(map, layer) {
    var buttons = document.querySelectorAll('[data-zoom]');
    if (!buttons || buttons.length === 0) {
      return;
    }

    var worldBounds = layer && typeof layer.getBounds === 'function' ? layer.getBounds() : null;

    buttons.forEach(function (button) {
      button.addEventListener('click', function () {
        var target = button.getAttribute('data-zoom');
        if (!map) {
          return;
        }

        if (target === 'southasia') {
          map.fitBounds([[0, 55], [35, 100]], { padding: [10, 10] });
        } else if (target === 'africa') {
          map.fitBounds([[-35, -20], [38, 55]], { padding: [10, 10] });
        } else if (worldBounds) {
          map.fitBounds(worldBounds, { padding: [20, 20] });
        }
      });
    });
  }

  function init(cfg) {
    if (!cfg || typeof L === 'undefined') {
      return;
    }

    var mapElement = document.getElementById(cfg.mapId);
    if (!mapElement) {
      return;
    }

    Promise.all([
      fetch(cfg.worldGeoJsonUrl, { cache: 'no-store' }).then(function (response) { return response.json(); }),
      fetch(cfg.dataUrl, { cache: 'no-store' }).then(function (response) { return response.json(); })
    ])
      .then(function (results) {
        if (L.Icon && L.Icon.Default && typeof L.Icon.Default.mergeOptions === 'function') {
          L.Icon.Default.mergeOptions({
            iconUrl: DEFAULT_ICON_URL,
            iconRetinaUrl: DEFAULT_ICON_RETINA_URL,
            shadowUrl: DEFAULT_SHADOW_URL
          });
        }

        var world = results[0];
        var data = ensureArray(results[1]);

        var byIso = new Map();
        var maxDelivered = 0;
        data.forEach(function (item) {
          var iso = (item.iso3 || '').toString().toUpperCase();
          byIso.set(iso, item);
          if (item.delivered > maxDelivered) {
            maxDelivered = item.delivered;
          }
        });

        var scale = buildColorScale(maxDelivered);
        var map = L.map(cfg.mapId, { zoomControl: true, attributionControl: false }).setView([20, 20], 2);

        var legendControlElement = document.createElement('div');
        legendControlElement.className = 'ffc-map-legend card shadow-sm';

        var legendControlBody = document.createElement('div');
        legendControlBody.className = 'card-body';

        var legendTitle = document.createElement('h2');
        legendTitle.className = 'h6 mb-3';
        legendTitle.textContent = 'Legend';

        var legendRamp = document.createElement('div');
        legendRamp.className = 'legend-ramp';

        var legendLabels = document.createElement('ul');
        legendLabels.className = 'legend-labels list-unstyled small mb-0';

        legendControlBody.appendChild(legendTitle);
        legendControlBody.appendChild(legendRamp);
        legendControlBody.appendChild(legendLabels);
        legendControlElement.appendChild(legendControlBody);

        var LegendControl = L.Control.extend({
          options: { position: 'bottomright' },
          onAdd: function () {
            return legendControlElement;
          }
        });

        map.addControl(new LegendControl());

        L.rectangle([[-85, -179.9], [85, 179.9]], {
          color: '#f1f5f9',
          weight: 1,
          fillOpacity: 1
        }).addTo(map);

        function styleFeature(feature) {
          var props = feature && feature.properties ? feature.properties : {};
          var iso = getIsoCodeFromProperties(props);
          var record = byIso.get(iso);
          var delivered = record ? Number(record.delivered || 0) : 0;

          return {
            weight: 0.6,
            color: '#94a3b8',
            fillColor: scale.colorFor(delivered),
            fillOpacity: delivered > 0 ? 0.9 : 0.35
          };
        }

        function onEachFeature(feature, layer) {
          var props = feature && feature.properties ? feature.properties : {};
          var iso = getIsoCodeFromProperties(props);
          var record = byIso.get(iso);
          var fallbackName = props.name || props.ADMIN || '';
          var popupHtml = buildPopupHtml(record, fallbackName, cfg);

          layer.bindPopup(popupHtml, { maxWidth: 260, closeButton: true });
          layer.on({
            mouseover: function (event) {
              event.target.setStyle({ weight: 1.2, color: '#475569' });
            },
            mouseout: function (event) {
              event.target.setStyle({ weight: 0.6, color: '#94a3b8' });
            }
          });
        }

        var geoLayer = L.geoJSON(world, {
          style: styleFeature,
          onEachFeature: onEachFeature
        }).addTo(map);

        var geoBounds = geoLayer.getBounds();
        if (geoBounds && geoBounds.isValid()) {
          map.fitBounds(geoBounds, { padding: [20, 20] });
        } else {
          map.setView([10, 40], 3);
        }

        attachZoomControls(map, geoLayer);
        renderLegend(scale);
      })
      .catch(function (error) {
        console.error('Failed to initialise FFC map', error);
      });
  }

  window.FfcMap = window.FfcMap || {};
  window.FfcMap.init = init;
})(window, document);
