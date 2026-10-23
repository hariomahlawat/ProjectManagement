// =============================
// GLASS LIGHT
// The specular highlight on glass surfaces ([data-glass-light]) follows the pointer, the way
// light moves across real glass. It only sets --lp-mx / --lp-my on the element; the CSS in
// landing.css draws the highlight. Skipped for touch-only devices and Reduce Motion.
// =============================
(function () {
  'use strict';

  var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var finePointer = window.matchMedia && window.matchMedia('(hover: hover) and (pointer: fine)').matches;
  if (reduceMotion || !finePointer) {
    return;
  }

  var surfaces = document.querySelectorAll('[data-glass-light]');
  for (var i = 0; i < surfaces.length; i++) {
    attach(surfaces[i]);
  }

  function attach(surface) {
    var frame = 0;
    var lastEvent = null;

    function paint() {
      frame = 0;
      var rect = surface.getBoundingClientRect();
      surface.style.setProperty('--lp-mx', (lastEvent.clientX - rect.left) + 'px');
      surface.style.setProperty('--lp-my', (lastEvent.clientY - rect.top) + 'px');
    }

    surface.addEventListener('pointermove', function (event) {
      lastEvent = event;
      if (!frame) {
        frame = window.requestAnimationFrame(paint);
      }
    });

    surface.addEventListener('pointerleave', function () {
      if (frame) {
        window.cancelAnimationFrame(frame);
        frame = 0;
      }
      surface.style.removeProperty('--lp-mx');
      surface.style.removeProperty('--lp-my');
    });
  }
})();
