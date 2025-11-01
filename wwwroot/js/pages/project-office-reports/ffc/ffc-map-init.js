(function (window, document) {
  'use strict';

  var mapElement = document.getElementById('ffcMap');

  if (!mapElement || !window.FfcMap || typeof window.FfcMap.init !== 'function') {
    return;
  }

  window.FfcMap.init({
    mapId: 'ffcMap',
    dataUrl: mapElement.dataset.dataUrl,
    worldGeoJsonUrl: mapElement.dataset.worldGeojsonUrl,
    ffcIndexUrlBase: mapElement.dataset.ffcIndexUrlBase
  });
})(window, document);
