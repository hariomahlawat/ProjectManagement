// SECTION: FFC simulator map modal loader
(function () {
  var modal = document.querySelector('[data-ffc-map-modal]');
  if (!modal) {
    return;
  }

  var frame = modal.querySelector('[data-ffc-map-frame]');
  if (!frame) {
    return;
  }

  var frameSrc = frame.getAttribute('data-src');
  if (!frameSrc) {
    return;
  }

  var hydrateFrame = function () {
    if (frame.getAttribute('src')) {
      return;
    }
    frame.setAttribute('src', frameSrc);
  };

  modal.addEventListener('show.bs.modal', hydrateFrame);

  var resetOnClose = modal.getAttribute('data-reset-on-close') === 'true';
  if (resetOnClose) {
    modal.addEventListener('hidden.bs.modal', function () {
      frame.removeAttribute('src');
    });
  }
})();
// END SECTION
