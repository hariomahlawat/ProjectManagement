(function (window, document) {
  'use strict';

  var mapElement = document.getElementById('ffc-map');

  if (!mapElement || !window.FfcMap || typeof window.FfcMap.init !== 'function') {
    return;
  }

  window.FfcMap.init({
    mapId: 'ffc-map',
    dataUrl: mapElement.dataset.dataUrl,
    geoJsonUrl: mapElement.dataset.geoUrl,
    ffcIndexUrlBase: mapElement.dataset.ffcIndexUrlBase
  });
})(window, document);
