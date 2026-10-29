// A PDF shown in a page with the repository's pdf.js viewer (js/docrepo/viewer.mjs), for
// [data-pdf-preview] blocks such as the Edit page. If the viewer cannot start, the block offers
// to open the PDF instead.
(() => {
    "use strict";

    document.querySelectorAll("[data-pdf-preview]").forEach(host => {
        import(host.dataset.viewerModule)
            .then(module => module.mountViewer(host, {
                url: host.dataset.pdfUrl,
                libraryBase: host.dataset.pdfjsBase,
                documentId: host.dataset.docId,
                compact: true
            }))
            .catch(() => {
                host.classList.add("is-failed");
                const status = host.querySelector("[data-viewer-status]");
                if (!status) return;
                const link = document.createElement("a");
                link.href = host.dataset.pdfUrl;
                link.target = "_blank";
                link.rel = "noopener";
                link.textContent = "Open the PDF in a new tab";
                status.replaceChildren(document.createTextNode("The preview could not be shown. "), link);
            });
    });
})();
