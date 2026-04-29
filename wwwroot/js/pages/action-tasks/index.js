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

    // SECTION: Task register filter auto-apply and responsible-person searchable picker.
    function initTaskRegisterFilters() {
        const form = document.querySelector("[data-at-task-filter-form='true']");
        if (!form) {
            return;
        }

        const immediateControls = form.querySelectorAll("select[name='FilterStatus'], select[name='FilterPriority'], input[name='FilterDueDate']");
        immediateControls.forEach((control) => {
            control.addEventListener("change", () => {
                form.requestSubmit();
            });
        });

        const searchInput = form.querySelector("[data-at-filter-search='true']");
        if (searchInput) {
            let searchDebounceHandle;
            searchInput.addEventListener("input", () => {
                if (searchDebounceHandle) {
                    window.clearTimeout(searchDebounceHandle);
                }
                searchDebounceHandle = window.setTimeout(() => {
                    form.requestSubmit();
                }, 400);
            });
        }

        const assigneeInput = form.querySelector("[data-at-assignee-picker='true']");
        if (!assigneeInput) {
            return;
        }

        const targetId = assigneeInput.getAttribute("data-at-assignee-target");
        const hiddenInput = targetId ? form.querySelector(`#${targetId}`) : null;
        const optionsListId = assigneeInput.getAttribute("list");
        const optionsList = optionsListId ? document.getElementById(optionsListId) : null;
        if (!hiddenInput || !optionsList) {
            return;
        }

        function syncAssigneeSelection() {
            const typedName = (assigneeInput.value || "").trim();
            if (!typedName) {
                hiddenInput.value = "";
                return true;
            }

            const matchedOption = Array.from(optionsList.options).find((option) =>
                (option.value || "").trim().toLowerCase() === typedName.toLowerCase());

            if (!matchedOption) {
                hiddenInput.value = "";
                return false;
            }

            hiddenInput.value = matchedOption.dataset.userId || "";
            assigneeInput.value = matchedOption.value;
            return true;
        }

        assigneeInput.addEventListener("change", () => {
            syncAssigneeSelection();
            form.requestSubmit();
        });

        assigneeInput.addEventListener("keydown", (event) => {
            if (event.key !== "Enter") {
                return;
            }
            event.preventDefault();
            syncAssigneeSelection();
            form.requestSubmit();
        });

        assigneeInput.addEventListener("blur", () => {
            syncAssigneeSelection();
        });
    }

    document.addEventListener("DOMContentLoaded", function () {
        openCreateTaskModalOnLoad();
        initSearchableSelects();
        initStatusUpdateGuard();
        initTaskRegisterFilters();
    });
})();
