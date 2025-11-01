(function (window) {
  'use strict';

  if (!window.FfcMap || typeof window.FfcMap.init !== 'function') {
    return;
  }

  window.FfcMap.init({
    mapId: 'ffcMap',
    dataUrl: '/ProjectOfficeReports/FFC/Map?handler=Data',
    worldGeoJsonUrl: '/data/world_india_view.geojson',
    ffcIndexUrlBase: '/ProjectOfficeReports/FFC/Index'
  });
})(window);
