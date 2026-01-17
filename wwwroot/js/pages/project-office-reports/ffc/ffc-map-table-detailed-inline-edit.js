/* SECTION: Inline edit controller */
(() => {
    "use strict";

    // SECTION: Constants
    const selectors = {
        editableCell: "td[data-editable=\"true\"][data-kind]",
        display: ".display-text",
        editor: ".editor",
        textarea: "textarea",
        saveButton: ".js-inline-save",
        cancelButton: ".js-inline-cancel",
        error: ".inline-error",
        status: ".inline-status",
        tokenInput: "#ffc-inline-edit-token input[name=\"__RequestVerificationToken\"]"
    };

    const endpoints = {
        overall: "?handler=UpdateOverallRemarks",
        progress: "?handler=UpdateProgress"
    };

    // SECTION: State
    let activeCell = null;

    // SECTION: Utilities
    const getToken = () => {
        const input = document.querySelector(selectors.tokenInput);
        return input ? input.value : "";
    };

    const normalizeDisplayText = (value) => (value || "").trim();

    // SECTION: Identifier parsing
    const parseNullableId = (value) => {
        if (value === undefined || value === null) {
            return null;
        }

        const numberValue = Number(value);
        return Number.isFinite(numberValue) && numberValue > 0 ? numberValue : null;
    };

    const setDisplayValue = (cell, text) => {
        const displayValue = cell.querySelector(".display-value");
        if (!displayValue) {
            return;
        }

        const emptyText = cell.dataset.emptyText || "";
        const normalized = normalizeDisplayText(text);

        if (!normalized) {
            displayValue.textContent = emptyText;
            displayValue.classList.add("text-muted", "fst-italic");
        } else {
            displayValue.textContent = normalized;
            displayValue.classList.remove("text-muted", "fst-italic");
        }
    };

    const showStatus = (cell, message) => {
        const status = cell.querySelector(selectors.status);
        if (!status) {
            return;
        }

        status.textContent = message;
        status.classList.remove("d-none");
        window.setTimeout(() => {
            status.classList.add("d-none");
        }, 2000);
    };

    const setError = (cell, message) => {
        const error = cell.querySelector(selectors.error);
        if (!error) {
            return;
        }

        if (message) {
            error.textContent = message;
            error.classList.remove("d-none");
        } else {
            error.textContent = "";
            error.classList.add("d-none");
        }
    };

    const setEditorState = (cell, isEditing) => {
        const display = cell.querySelector(selectors.display);
        const editor = cell.querySelector(selectors.editor);

        if (display) {
            display.classList.toggle("d-none", isEditing);
        }

        if (editor) {
            editor.classList.toggle("d-none", !isEditing);
        }
    };

    const resetCell = (cell) => {
        if (!cell) {
            return;
        }

        const textarea = cell.querySelector(selectors.textarea);
        if (textarea) {
            textarea.value = cell.dataset.originalValue || "";
        }

        setError(cell, "");
        setEditorState(cell, false);
    };

    const beginEdit = (cell) => {
        if (activeCell && activeCell !== cell) {
            resetCell(activeCell);
            activeCell = null;
        }

        if (activeCell === cell) {
            return;
        }

        const textarea = cell.querySelector(selectors.textarea);
        if (textarea) {
            cell.dataset.originalValue = textarea.value;
            textarea.focus();
            textarea.setSelectionRange(textarea.value.length, textarea.value.length);
        }

        setError(cell, "");
        setEditorState(cell, true);
        activeCell = cell;
    };

    const endEdit = (cell, rawValue, displayValue) => {
        if (!cell) {
            return;
        }

        const textarea = cell.querySelector(selectors.textarea);
        if (textarea) {
            textarea.value = rawValue || "";
            cell.dataset.originalValue = textarea.value;
        }

        setDisplayValue(cell, displayValue);
        setEditorState(cell, false);
        showStatus(cell, "Saved");
        setError(cell, "");
        activeCell = null;
    };

    const setBusy = (cell, isBusy) => {
        const textarea = cell.querySelector(selectors.textarea);
        const saveButton = cell.querySelector(selectors.saveButton);
        const cancelButton = cell.querySelector(selectors.cancelButton);

        if (textarea) {
            textarea.disabled = isBusy;
        }

        if (saveButton) {
            saveButton.disabled = isBusy;
            saveButton.textContent = isBusy ? "Saving..." : "Save";
        }

        if (cancelButton) {
            cancelButton.disabled = isBusy;
        }
    };

    // SECTION: Network
    const postUpdate = async (cell) => {
        const kind = cell.dataset.kind;
        const textarea = cell.querySelector(selectors.textarea);
        const payload = textarea ? textarea.value : "";
        const token = getToken();
        const linkedProjectId = parseNullableId(cell.dataset.linkedProjectId);
        const externalRemarkId = parseNullableId(cell.dataset.externalRemarkId);

        const response = await fetch(endpoints[kind], {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "RequestVerificationToken": token
            },
            body: JSON.stringify(
                kind === "overall"
                    ? { ffcRecordId: Number(cell.dataset.ffcRecordId), overallRemarks: payload }
                    : {
                        ffcProjectId: Number(cell.dataset.ffcProjectId),
                        progressText: payload,
                        linkedProjectId,
                        externalRemarkId
                    }
            )
        });

        return response;
    };

    // SECTION: Event handlers
    const handleDisplayClick = (event) => {
        const cell = event.currentTarget.closest("td");
        if (!cell) {
            return;
        }

        beginEdit(cell);
    };

    const handleSaveClick = async (event) => {
        const cell = event.currentTarget.closest("td");
        if (!cell) {
            return;
        }

        setBusy(cell, true);
        setError(cell, "");

        try {
            const response = await postUpdate(cell);

            if (response.ok) {
                const data = await response.json();
                const rawValue = cell.dataset.kind === "overall" ? data.overallRemarks : data.progressText;
                const displayValue = cell.dataset.kind === "overall"
                    ? data.renderedOverallRemarks ?? data.overallRemarks
                    : data.renderedProgressText ?? data.progressText;

                endEdit(cell, rawValue, displayValue);
                if (cell.dataset.kind === "progress" && data.externalRemarkId) {
                    cell.dataset.externalRemarkId = String(data.externalRemarkId);
                }
                return;
            }

            if (response.status === 403) {
                setError(cell, "Not authorised to update this value.");
                return;
            }

            if (response.status === 404) {
                setError(cell, "Record not found.");
                return;
            }

            const errorPayload = await response.json().catch(() => null);
            const message = errorPayload && errorPayload.message
                ? errorPayload.message
                : "Unable to save changes. Please try again.";

            setError(cell, message);
        } catch (error) {
            setError(cell, "Unable to save changes. Please try again.");
        } finally {
            setBusy(cell, false);
        }
    };

    const handleCancelClick = (event) => {
        const cell = event.currentTarget.closest("td");
        if (!cell) {
            return;
        }

        resetCell(cell);
        activeCell = null;
    };

    // SECTION: Initialisation
    const init = () => {
        const cells = Array.from(document.querySelectorAll(selectors.editableCell));
        if (cells.length === 0) {
            return;
        }

        cells.forEach((cell) => {
            const display = cell.querySelector(selectors.display);
            const saveButton = cell.querySelector(selectors.saveButton);
            const cancelButton = cell.querySelector(selectors.cancelButton);

            if (display) {
                display.addEventListener("click", handleDisplayClick);
            }

            if (saveButton) {
                saveButton.addEventListener("click", handleSaveClick);
            }

            if (cancelButton) {
                cancelButton.addEventListener("click", handleCancelClick);
            }
        });
    };

    init();
})();
