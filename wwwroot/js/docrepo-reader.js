// Document Repository reader: hides the loading note once the PDF viewer has loaded and offers
// a new tab or a download if the browser is slow to show it. Favourites and the manage dialog
// come from docrepo-ui.js.
(() => {
    "use strict";

    const viewer = document.querySelector("[data-reader-viewer]");
    const frame = viewer?.querySelector("[data-reader-frame]");
    if (!viewer || !frame) return;

    const loading = viewer.querySelector("[data-reader-loading]");
    const slow = viewer.querySelector("[data-reader-slow]");
    const slowTimer = window.setTimeout(() => { if (slow) slow.hidden = false; }, 8000);

    frame.addEventListener("load", () => {
        window.clearTimeout(slowTimer);
        viewer.classList.add("is-loaded");
        if (loading) loading.hidden = true;
    }, { once: true });
})();
