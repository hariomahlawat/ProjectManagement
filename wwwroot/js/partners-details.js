(function () {
    "use strict";

    // SECTION: Offcanvas auto-open
    var marker = document.querySelector("[data-partner-offcanvas]");
    if (!marker || !window.bootstrap) {
        return;
    }

    var targetId = marker.getAttribute("data-partner-offcanvas");
    if (!targetId) {
        return;
    }

    var target = document.getElementById(targetId);
    if (!target) {
        return;
    }

    var offcanvas = bootstrap.Offcanvas.getOrCreateInstance(target);
    offcanvas.show();
})();
