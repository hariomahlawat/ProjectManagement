/* SECTION: Ongoing projects inline external remark editing */
(() => {
  "use strict";

  // SECTION: Selectors
  const selectors = {
    tableRoot: "#oprTableRoot",
    tokenInput: "#oprInlineRemarkToken input[name=\"__RequestVerificationToken\"]",
    remarkCell: ".opr-remark-cell",
    display: ".opr-remark-display",
    displayText: ".opr-remark-text",
    editor: ".opr-remark-editor",
    textarea: ".opr-remark-editor-input",
    editButton: ".opr-remark-edit",
    saveButton: ".opr-remark-save",
    cancelButton: ".opr-remark-cancel",
    error: ".opr-remark-error"
  };

  // SECTION: Bootstrap
  const root = document.querySelector(selectors.tableRoot);
  if (!root) {
    return;
  }

  const canEdit = root.dataset.canEdit === "1";
  if (!canEdit) {
    return;
  }

  const tokenInput = document.querySelector(selectors.tokenInput);
  const todayIst = root.dataset.todayIst || "";

  let activeCell = null;

  // SECTION: Utilities
  const getToken = () => (tokenInput ? tokenInput.value : "");

  const getCell = (event) => {
    const target = event.target;
    if (!(target instanceof Element)) {
      return null;
    }

    return target.closest(selectors.remarkCell);
  };

  const setEditorState = (cell, isEditing) => {
    const display = cell.querySelector(selectors.display);
    const editor = cell.querySelector(selectors.editor);

    display?.classList.toggle("d-none", isEditing);
    editor?.classList.toggle("d-none", !isEditing);
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

  const updateDisplay = (cell, value) => {
    const displayText = cell.querySelector(selectors.displayText);
    if (!displayText) {
      return;
    }

    const emptyText = cell.dataset.emptyText || "N/A";
    const normalized = (value || "").trim();

    if (!normalized) {
      displayText.textContent = emptyText;
      displayText.classList.add("text-muted", "fst-italic");
    } else {
      displayText.textContent = normalized;
      displayText.classList.remove("text-muted", "fst-italic");
    }
  };

  const beginEdit = (cell) => {
    if (activeCell && activeCell !== cell) {
      cancelEdit(activeCell);
    }

    const textarea = cell.querySelector(selectors.textarea);
    if (textarea) {
      const emptyText = cell.dataset.emptyText || "N/A";
      const displayText = cell.querySelector(selectors.displayText);
      const displayValue = displayText ? displayText.textContent || "" : "";
      const rawBody = cell.dataset.remarkBody || "";
      const normalizedDisplay = displayValue.trim();
      const initialValue = rawBody || (normalizedDisplay === emptyText ? "" : normalizedDisplay);

      textarea.value = initialValue;
      textarea.focus();
      textarea.setSelectionRange(textarea.value.length, textarea.value.length);
    }

    setError(cell, "");
    setEditorState(cell, true);
    activeCell = cell;
  };

  const cancelEdit = (cell) => {
    const textarea = cell.querySelector(selectors.textarea);
    if (textarea) {
      textarea.value = cell.dataset.remarkBody || "";
    }

    setError(cell, "");
    setEditorState(cell, false);
    if (activeCell === cell) {
      activeCell = null;
    }
  };

  const setRemarkData = (cell, data, fallbackBody) => {
    const body = typeof data?.body === "string" ? data.body : fallbackBody;
    const rowVersion = typeof data?.rowVersion === "string" ? data.rowVersion : cell.dataset.rowVersion || "";
    const remarkId = typeof data?.id === "number" ? data.id : Number(cell.dataset.remarkId || 0);
    const eventDate = typeof data?.eventDate === "string" ? data.eventDate : cell.dataset.eventDate || todayIst;
    const scope = typeof data?.scope === "string" ? data.scope : cell.dataset.scope || "General";

    cell.dataset.remarkBody = body || "";
    cell.dataset.rowVersion = rowVersion;
    cell.dataset.remarkId = String(remarkId);
    cell.dataset.eventDate = eventDate;
    cell.dataset.scope = scope;

    updateDisplay(cell, body);
  };

  // SECTION: Network
  const saveRemark = async (cell) => {
    const textarea = cell.querySelector(selectors.textarea);
    const body = textarea ? textarea.value.trim() : "";

    const projectId = Number(cell.dataset.projectId || 0);
    const remarkId = Number(cell.dataset.remarkId || 0);
    const scope = cell.dataset.scope || "General";
    const eventDate = cell.dataset.eventDate || todayIst;
    const rowVersion = cell.dataset.rowVersion || "";

    const isUpdate = remarkId > 0;
    const endpoint = isUpdate
      ? `/api/projects/${projectId}/remarks/${remarkId}`
      : `/api/projects/${projectId}/remarks`;

    const payload = isUpdate
      ? {
        body,
        scope,
        eventDate,
        rowVersion,
        actorRole: "HoD",
        meta: "Inline edit from Ongoing Projects table"
      }
      : {
        type: "External",
        scope: "General",
        eventDate: todayIst || eventDate,
        body,
        actorRole: "HoD",
        meta: "Inline create from Ongoing Projects table"
      };

    const headers = {
      "Content-Type": "application/json"
    };

    const token = getToken();
    if (token) {
      headers.RequestVerificationToken = token;
    }

    const response = await fetch(endpoint, {
      method: isUpdate ? "PUT" : "POST",
      headers,
      body: JSON.stringify(payload)
    });

    let responseBody = null;
    try {
      responseBody = await response.json();
    } catch {
      responseBody = null;
    }

    if (response.ok) {
      return { ok: true, data: responseBody, body };
    }

    return { ok: false, status: response.status, data: responseBody, body };
  };

  // SECTION: Event handlers
  const handleEditClick = (event) => {
    const cell = getCell(event);
    if (!cell) {
      return;
    }

    event.preventDefault();
    beginEdit(cell);
  };

  const handleCancelClick = (event) => {
    const cell = getCell(event);
    if (!cell) {
      return;
    }

    event.preventDefault();
    cancelEdit(cell);
  };

  const handleSaveClick = async (event) => {
    const cell = getCell(event);
    if (!cell) {
      return;
    }

    event.preventDefault();
    setBusy(cell, true);
    setError(cell, "");

    try {
      const result = await saveRemark(cell);

      if (result.ok) {
        setRemarkData(cell, result.data, result.body);
        setEditorState(cell, false);
        activeCell = null;
        return;
      }

      if (result.status === 403) {
        setError(cell, "Not authorised.");
      } else if (result.status === 409) {
        setError(cell, "This remark was changed by someone else. Reload and try again.");
      } else if (result.status === 400) {
        const message = result.data?.detail || result.data?.title || "Invalid remark data.";
        setError(cell, message);
      } else {
        setError(cell, "Unable to save remark right now.");
      }
    } catch {
      setError(cell, "Unable to save remark right now.");
    } finally {
      setBusy(cell, false);
    }
  };

  // SECTION: Event wiring
  document.addEventListener("click", (event) => {
    const target = event.target;
    if (!(target instanceof Element)) {
      return;
    }

    if (target.closest(selectors.editButton)) {
      handleEditClick(event);
    } else if (target.closest(selectors.cancelButton)) {
      handleCancelClick(event);
    } else if (target.closest(selectors.saveButton)) {
      void handleSaveClick(event);
    }
  });
})();
