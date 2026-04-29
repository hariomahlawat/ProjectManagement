// SECTION: Action Tracker page interactions.
(function () {
    "use strict";

    // SECTION: Re-open create-task modal after server-side validation errors.
    function openCreateTaskModalOnLoad() {
        const modalElement = document.getElementById("createTaskModal");
        if (!modalElement || typeof window.bootstrap === "undefined" || !window.bootstrap.Modal) {
            return;
        }

        const shouldOpen = modalElement.dataset.atOpenOnLoad === "true";
        if (!shouldOpen) {
            return;
        }

        const modal = window.bootstrap.Modal.getOrCreateInstance(modalElement);
        modal.show();
    }
    // SECTION: Lightweight searchable-select enhancement for offline deployment.
    function initSearchableSelects() {
        const selects = document.querySelectorAll("select[data-at-searchable-select='true']");
        selects.forEach((select) => {
            if (select.dataset.atSearchReady === "true") {
                return;
            }
            const wrapper = document.createElement("div");
            wrapper.className = "at-select-search-wrap";
            select.parentNode.insertBefore(wrapper, select);
            wrapper.appendChild(select);
            const input = document.createElement("input");
            input.type = "search";
            input.className = "form-control form-control-sm at-select-search-input";
            input.placeholder = select.dataset.atPlaceholder || "Search...";
            wrapper.insertBefore(input, select);
            input.addEventListener("input", () => {
                const term = input.value.trim().toLowerCase();
                Array.from(select.options).forEach((option, index) => {
                    if (index === 0) { option.hidden = false; return; }
                    option.hidden = !option.text.toLowerCase().includes(term);
                });
            });
            select.dataset.atSearchReady = "true";
        });
    }
    // SECTION: Disable status update when selected value does not change status.
    function initStatusUpdateGuard() {
        const select = document.querySelector("[data-at-status-select]");
        const button = document.querySelector("[data-at-status-submit]");
        if (!select || !button) {
            return;
        }
        const sync = () => {
            const current = (select.dataset.currentStatus || "").toLowerCase();
            const selected = (select.value || "").toLowerCase();
            button.disabled = current === selected;
        };
        select.addEventListener("change", sync);
        sync();
    }

    document.addEventListener("DOMContentLoaded", function () {
        openCreateTaskModalOnLoad();
        initSearchableSelects();
        initStatusUpdateGuard();
    });
})();
