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

        const immediateControls = form.querySelectorAll("select[name='FilterStatus'], select[name='FilterBucket'], select[name='FilterPriority'], input[name='FilterDueDate']");
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
            const valid = syncAssigneeSelection();
            if (valid || !assigneeInput.value.trim()) {
                form.requestSubmit();
            }
        });

        assigneeInput.addEventListener("keydown", (event) => {
            if (event.key !== "Enter") {
                return;
            }
            event.preventDefault();
            const valid = syncAssigneeSelection();
            if (valid || !assigneeInput.value.trim()) {
                form.requestSubmit();
            }
        });

        assigneeInput.addEventListener("blur", () => {
            const valid = syncAssigneeSelection();
            if (!valid) {
                assigneeInput.value = "";
                hiddenInput.value = "";
            }
        });
    }

    // SECTION: Reports filter auto-apply keeps management analysis low-friction.
    function initReportsFilters() {
        const form = document.querySelector("[data-at-reports-filter-form='true']");
        if (!form) {
            return;
        }

        let isSubmitting = false;
        const loadingIndicator = form.querySelector("[data-at-reports-filter-loading='true']");

        function showLoadingState() {
            form.classList.add("is-loading");
            form.setAttribute("aria-busy", "true");
            if (loadingIndicator) {
                loadingIndicator.classList.add("is-visible");
            }
        }

        function submitReportsFilters() {
            if (isSubmitting) {
                return;
            }

            isSubmitting = true;
            showLoadingState();
            if (typeof form.requestSubmit === "function") {
                form.requestSubmit();
                return;
            }

            form.submit();
        }

        const dropdownControls = form.querySelectorAll("select[data-at-reports-filter-control='true']");
        dropdownControls.forEach((control) => {
            control.addEventListener("change", submitReportsFilters);
        });

        const dateControls = form.querySelectorAll("input[data-at-reports-date-filter='true']");
        dateControls.forEach((control) => {
            let lastAppliedValue = control.value || "";

            function applyDateFilter() {
                const currentValue = control.value || "";
                if (currentValue === lastAppliedValue) {
                    return;
                }

                lastAppliedValue = currentValue;
                submitReportsFilters();
            }

            control.addEventListener("change", applyDateFilter);
            control.addEventListener("blur", applyDateFilter);
        });
    }

    // SECTION: Sprint selector opens selected sprint through safe GET navigation.
    function initSprintSelector() {
        const form = document.querySelector("[data-at-sprint-selector='true']");
        if (!form) {
            return;
        }

        const selector = form.querySelector("select[name='SelectedSprintId']");
        if (!selector) {
            return;
        }

        selector.addEventListener("change", () => {
            form.requestSubmit();
        });
    }

    // SECTION: Keep closure disposition choices mutually exclusive per task.
    function initSprintClosureReview() {
        const review = document.querySelector("[data-at-closure-review='true']");
        if (!review) {
            return;
        }

        review.addEventListener("change", (event) => {
            const input = event.target;
            if (!input || input.getAttribute("data-at-closure-choice") === null || !input.checked) {
                return;
            }

            const row = input.closest(".at-closure-task-row");
            if (!row) {
                return;
            }

            row.querySelectorAll("input[data-at-closure-choice]").forEach((candidate) => {
                if (candidate !== input) {
                    candidate.checked = false;
                }
            });
        });
    }

    // SECTION: Create modal segmented-control flow keeps one server-rendered form active at a time.
    function initCreateTaskChoiceFlow() {
        const group = document.querySelector("[data-at-create-choice-group='true']");
        if (!group) {
            return;
        }

        const buttons = Array.from(group.querySelectorAll("[data-at-create-mode-button]"));
        const panels = Array.from(group.querySelectorAll("[data-at-create-mode-panel]"));
        if (!buttons.length || !panels.length) {
            return;
        }

        function activateMode(mode) {
            buttons.forEach((button) => {
                const isActive = button.getAttribute("data-at-create-mode-button") === mode;
                button.classList.toggle("is-active", isActive);
                button.setAttribute("aria-selected", isActive ? "true" : "false");
            });

            panels.forEach((panel) => {
                const isActive = panel.getAttribute("data-at-create-mode-panel") === mode;
                panel.hidden = !isActive;
            });
        }

        buttons.forEach((button) => {
            button.addEventListener("click", () => {
                const mode = button.getAttribute("data-at-create-mode-button");
                if (mode) {
                    activateMode(mode);
                }
            });
        });

        const initiallySelected = buttons.find((button) => button.getAttribute("aria-selected") === "true") || buttons[0];
        activateMode(initiallySelected.getAttribute("data-at-create-mode-button"));
    }

    // SECTION: Application-styled confirmation modal for destructive Action Tracker form submissions.
    function initActionConfirmations() {
        const triggers = document.querySelectorAll("[data-at-confirm='true']");
        if (!triggers.length) {
            return;
        }

        const modalElement = ensureActionConfirmationModal();
        if (!modalElement) {
            return;
        }

        const titleElement = modalElement.querySelector("[data-at-confirm-modal-title]");
        const bodyElement = modalElement.querySelector("[data-at-confirm-modal-body]");
        const cancelButton = modalElement.querySelector("[data-at-confirm-modal-cancel]");
        const acceptButton = modalElement.querySelector("[data-at-confirm-modal-accept]");
        const modal = typeof window.bootstrap !== "undefined" && window.bootstrap.Modal
            ? window.bootstrap.Modal.getOrCreateInstance(modalElement)
            : null;
        let pendingForm = null;
        let pendingSubmitter = null;

        if (!titleElement || !bodyElement || !cancelButton || !acceptButton || !modal) {
            return;
        }

        triggers.forEach((trigger) => {
            const form = trigger.closest("form");
            if (!form || form.dataset.atConfirmReady === "true") {
                return;
            }

            form.addEventListener("submit", (event) => {
                if (form.dataset.atConfirmAccepted === "true") {
                    delete form.dataset.atConfirmAccepted;
                    return;
                }

                const submitter = event.submitter || trigger;
                if (submitter.getAttribute("data-at-confirm") !== "true") {
                    return;
                }

                event.preventDefault();
                pendingForm = form;
                pendingSubmitter = submitter;
                titleElement.textContent = submitter.getAttribute("data-at-confirm-title") || "Confirm action";
                bodyElement.textContent = submitter.getAttribute("data-at-confirm-body") || "Please confirm that you want to continue.";
                cancelButton.textContent = submitter.getAttribute("data-at-confirm-cancel-label") || "Cancel";
                acceptButton.textContent = submitter.getAttribute("data-at-confirm-accept-label") || "Continue";
                modal.show();
            });
            form.dataset.atConfirmReady = "true";
        });

        acceptButton.addEventListener("click", () => {
            if (!pendingForm) {
                return;
            }

            const formToSubmit = pendingForm;
            const submitterToUse = pendingSubmitter;
            pendingForm = null;
            pendingSubmitter = null;
            formToSubmit.dataset.atConfirmAccepted = "true";
            modal.hide();

            if (typeof formToSubmit.requestSubmit === "function" && submitterToUse) {
                formToSubmit.requestSubmit(submitterToUse);
                return;
            }

            formToSubmit.submit();
        });

        modalElement.addEventListener("hidden.bs.modal", () => {
            pendingForm = null;
            pendingSubmitter = null;
        });
    }

    // SECTION: Build the shared confirmation modal without inline scripts to preserve CSP compliance.
    function ensureActionConfirmationModal() {
        const existingModal = document.getElementById("actionTaskConfirmModal");
        if (existingModal) {
            return existingModal;
        }

        const modalElement = document.createElement("div");
        modalElement.className = "modal fade";
        modalElement.id = "actionTaskConfirmModal";
        modalElement.tabIndex = -1;
        modalElement.setAttribute("aria-labelledby", "actionTaskConfirmModalTitle");
        modalElement.setAttribute("aria-hidden", "true");

        const dialog = document.createElement("div");
        dialog.className = "modal-dialog modal-dialog-centered";

        const content = document.createElement("div");
        content.className = "modal-content";

        const header = document.createElement("div");
        header.className = "modal-header";

        const title = document.createElement("h2");
        title.className = "modal-title h5 mb-0";
        title.id = "actionTaskConfirmModalTitle";
        title.setAttribute("data-at-confirm-modal-title", "true");

        const closeButton = document.createElement("button");
        closeButton.type = "button";
        closeButton.className = "btn-close";
        closeButton.setAttribute("data-bs-dismiss", "modal");
        closeButton.setAttribute("aria-label", "Close");

        const body = document.createElement("div");
        body.className = "modal-body";

        const bodyText = document.createElement("p");
        bodyText.className = "mb-0";
        bodyText.setAttribute("data-at-confirm-modal-body", "true");

        const footer = document.createElement("div");
        footer.className = "modal-footer";

        const cancelButton = document.createElement("button");
        cancelButton.type = "button";
        cancelButton.className = "btn btn-outline-secondary";
        cancelButton.setAttribute("data-bs-dismiss", "modal");
        cancelButton.setAttribute("data-at-confirm-modal-cancel", "true");

        const acceptButton = document.createElement("button");
        acceptButton.type = "button";
        acceptButton.className = "btn btn-danger";
        acceptButton.setAttribute("data-at-confirm-modal-accept", "true");

        header.appendChild(title);
        header.appendChild(closeButton);
        body.appendChild(bodyText);
        footer.appendChild(cancelButton);
        footer.appendChild(acceptButton);
        content.appendChild(header);
        content.appendChild(body);
        content.appendChild(footer);
        dialog.appendChild(content);
        modalElement.appendChild(dialog);
        document.body.appendChild(modalElement);

        return modalElement;
    }


    // SECTION: Sticky inspector action panel orchestration and keyboard behavior.
    function initInspectorActionPanels() {
        const shell = document.querySelector("[data-at-action-shell='true']");
        if (!shell) {
            return;
        }

        const panels = Array.from(shell.querySelectorAll("[data-at-action-panel]"));
        const openButtons = shell.querySelectorAll("[data-at-open-action]");
        const closeButtons = shell.querySelectorAll("[data-at-close-action]");

        const closeAllPanels = () => {
            panels.forEach((panel) => panel.removeAttribute("open"));
        };

        const openPanel = (name) => {
            closeAllPanels();
            const panel = shell.querySelector(`[data-at-action-panel='${name}']`);
            if (!panel) {
                return;
            }
            panel.setAttribute("open", "open");
            const focusTarget = panel.querySelector("textarea, select, input, button");
            if (focusTarget) {
                focusTarget.focus();
            }
        };

        openButtons.forEach((button) => {
            button.addEventListener("click", () => {
                const target = button.getAttribute("data-at-open-action");
                openPanel(target);
            });
        });

        closeButtons.forEach((button) => {
            button.addEventListener("click", () => {
                closeAllPanels();
            });
        });

        shell.addEventListener("keydown", (event) => {
            if (event.key !== "Escape") {
                return;
            }
            const hasOpenPanel = panels.some((panel) => panel.hasAttribute("open"));
            if (!hasOpenPanel) {
                return;
            }
            event.preventDefault();
            event.stopPropagation();
            closeAllPanels();
        });
    }

    document.addEventListener("DOMContentLoaded", function () {
        openCreateTaskModalOnLoad();
        initSearchableSelects();
        initCreateTaskChoiceFlow();
        initStatusUpdateGuard();
        initTaskRegisterFilters();
        initReportsFilters();
        initSprintSelector();
        initSprintClosureReview();
        initActionConfirmations();
        initInspectorActionPanels();
    });
})();
