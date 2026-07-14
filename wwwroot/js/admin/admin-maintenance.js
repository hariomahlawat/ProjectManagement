(() => {
    "use strict";
    document.querySelectorAll("[data-pdf-ingestion-form]").forEach(form => {
        const button = form.querySelector("[data-pdf-ingestion-submit]");
        form.addEventListener("submit", () => {
            if (!button) return;
            button.disabled = true;
            button.setAttribute("aria-busy", "true");
            button.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Running ingestion…';
        });
    });
})();
