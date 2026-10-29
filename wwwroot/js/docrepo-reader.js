// Document Repository reader: opens the PDF in the pdf.js viewer (js/docrepo/viewer.mjs), shows
// where the reader left off, and folds the details panel. Favourites, the action dialog and
// quick search come from docrepo-ui.js. If the viewer cannot start, the page offers the
// browser's own viewer and the download instead.
(() => {
    "use strict";

    const reader = document.querySelector("[data-reader]");
    if (!reader) return;

    // SECTION: Details panel (beside the pages on wide screens, a drawer on narrow ones)
    const infoToggle = reader.querySelector("[data-reader-info-toggle]");
    const wide = window.matchMedia("(min-width: 1200px)");
    const infoKey = "prism.docrepo.reader.info";

    function setInfo(open, remember) {
        reader.classList.toggle("is-info-open", open);
        infoToggle?.setAttribute("aria-expanded", open ? "true" : "false");
        if (remember && wide.matches) {
            try { window.localStorage.setItem(infoKey, open ? "1" : "0"); } catch { /* not remembered */ }
        }
    }

    let stored = null;
    try { stored = window.localStorage.getItem(infoKey); } catch { stored = null; }
    setInfo(wide.matches && stored !== "0", false);
    wide.addEventListener("change", event => setInfo(event.matches && stored !== "0", false));
    infoToggle?.addEventListener("click", () => setInfo(!reader.classList.contains("is-info-open"), true));
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && !wide.matches && reader.classList.contains("is-info-open") && !document.querySelector(".modal.show")) {
            setInfo(false, false);
            infoToggle?.focus();
        }
    });

    // Narrow screens start without thumbnails; there is no room beside the page.
    if (window.matchMedia("(max-width: 991.98px)").matches) reader.classList.add("is-thumbs-hidden");

    // SECTION: The viewer
    if (reader.classList.contains("is-missing")) return;

    const resume = reader.querySelector("[data-reader-resume]");
    reader.addEventListener("viewer:resumed", event => {
        if (!resume) return;
        resume.querySelector("[data-reader-resume-page]").textContent = String(event.detail.page);
        resume.hidden = false;
        window.setTimeout(() => { resume.hidden = true; }, 9000);
    });
    reader.addEventListener("click", event => {
        if (resume && event.target.closest("[data-viewer-action='start']")) resume.hidden = true;
    });

    import(reader.dataset.viewerModule)
        .then(module => module.mountViewer(reader, {
            url: reader.dataset.pdfUrl,
            libraryBase: reader.dataset.pdfjsBase,
            documentId: reader.dataset.docId,
            userKey: reader.dataset.userKey,
            query: reader.dataset.query || "",
            page: reader.dataset.page || null,
            matchPages: (reader.dataset.matchPages || "").split(",").map(Number).filter(Boolean),
            textMatches: reader.dataset.textMatches === "true"
        }))
        .then(() => reader.querySelector("[data-viewer-stage]")?.focus({ preventScroll: true }))
        .catch(() => reader.classList.add("is-failed"));
})();
