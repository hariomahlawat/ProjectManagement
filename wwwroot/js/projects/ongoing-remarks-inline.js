/* SECTION: Ongoing projects inline external remark editing */
(() => {
  "use strict";

  // SECTION: Selectors
  const selectors = {
    tableRoot: "#oprTableRoot",
    tokenInput: "#oprInlineRemarkToken input[name=\"__RequestVerificationToken\"]",
    remarkCell: ".js-ongoing-remark-cell",
    display: ".pm-remark-text",
    editor: ".opr-remark-editor",
    textarea: ".opr-remark-editor-input",
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

  const getRemarkField = (remark, key) => {
    if (!remark || typeof remark !== "object") {
      return undefined;
    }

    const pascalKey = key.charAt(0).toUpperCase() + key.slice(1);
    if (key in remark) {
      return remark[key];
    }

    if (pascalKey in remark) {
      return remark[pascalKey];
    }

    return undefined;
  };

  const normalizeRemark = (remark) => {
    if (!remark) {
      return null;
    }

    return {
      id: getRemarkField(remark, "id"),
      scope: getRemarkField(remark, "scope"),
      eventDate: getRemarkField(remark, "eventDate"),
      rowVersion: getRemarkField(remark, "rowVersion"),
      body: getRemarkField(remark, "body")
    };
  };

  const getCellFromEvent = (event) => {
    const target = event.target;
    if (!(target instanceof Element)) {
      return null;
    }

    return target.closest(selectors.remarkCell);
  };

  const ensureEditor = (cell) => {
    let editor = cell.querySelector(selectors.editor);
    if (editor) {
      return editor;
    }

    editor = document.createElement("div");
    editor.className = "opr-remark-editor d-none";

    const textarea = document.createElement("textarea");
    textarea.className = "form-control opr-remark-editor-input";
    textarea.rows = 3;

    const actions = document.createElement("div");
    actions.className = "d-flex gap-2 mt-2";

    const saveButton = document.createElement("button");
    saveButton.type = "button";
    saveButton.className = "btn btn-sm btn-primary opr-remark-save";
    saveButton.textContent = "Save";

    const cancelButton = document.createElement("button");
    cancelButton.type = "button";
    cancelButton.className = "btn btn-sm btn-outline-secondary opr-remark-cancel";
    cancelButton.textContent = "Cancel";

    const error = document.createElement("div");
    error.className = "text-danger opr-remark-error d-none mt-2";

    actions.append(saveButton, cancelButton);
    editor.append(textarea, actions, error);
    cell.append(editor);

    return editor;
  };

  const setEditorState = (cell, isEditing) => {
    const display = cell.querySelector(selectors.display);
    const editor = ensureEditor(cell);

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
    const displayText = cell.querySelector(selectors.display);
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

    const textarea = ensureEditor(cell).querySelector(selectors.textarea);
    if (textarea) {
      const emptyText = cell.dataset.emptyText || "N/A";
      const displayText = cell.querySelector(selectors.display);
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
    const normalized = normalizeRemark(data);
    const body = typeof normalized?.body === "string" ? normalized.body : fallbackBody;
    const rowVersion = typeof normalized?.rowVersion === "string" ? normalized.rowVersion : cell.dataset.rowVersion || "";
    const remarkId = typeof normalized?.id === "number" ? normalized.id : Number(cell.dataset.remarkId || 0);
    const eventDate = typeof normalized?.eventDate === "string" ? normalized.eventDate : cell.dataset.eventDate || todayIst;
    const scope = typeof normalized?.scope === "string" ? normalized.scope : cell.dataset.scope || "General";

    cell.dataset.remarkBody = body || "";
    cell.dataset.currentText = body || "";
    cell.dataset.rowVersion = rowVersion;
    cell.dataset.remarkId = String(remarkId);
    cell.dataset.eventDate = eventDate;
    cell.dataset.scope = scope;

    updateDisplay(cell, body);
  };

  // SECTION: Network helpers
  const fetchLatestExternalRemark = async (projectId) => {
    const response = await fetch(
      `/api/projects/${projectId}/remarks?scope=General&type=External&pageSize=1&page=1`
    );

    if (!response.ok) {
      return { ok: false, status: response.status };
    }

    const data = await response.json().catch(() => null);
    const items = data?.items || data?.Items || [];
    const latest = items.length ? normalizeRemark(items[0]) : null;
    return { ok: true, data: latest };
  };

  const saveRemark = async (cell) => {
    const textarea = cell.querySelector(selectors.textarea);
    const body = textarea ? textarea.value.trim() : "";

    const projectId = Number(cell.dataset.projectId || 0);
    const remarkId = Number(cell.dataset.remarkId || 0);

    let latestRemark = null;
    if (remarkId > 0) {
      const latestResponse = await fetchLatestExternalRemark(projectId);
      if (!latestResponse.ok) {
        return { ok: false, status: latestResponse.status, body };
      }

      latestRemark = latestResponse.data;
    }

    const isUpdate = Boolean(latestRemark?.id);
    const endpoint = isUpdate
      ? `/api/projects/${projectId}/remarks/${latestRemark.id}`
      : `/api/projects/${projectId}/remarks`;

    const payload = isUpdate
      ? {
        body,
        scope: latestRemark?.scope || cell.dataset.scope || "General",
        eventDate: latestRemark?.eventDate || cell.dataset.eventDate || todayIst,
        rowVersion: latestRemark?.rowVersion || cell.dataset.rowVersion || "",
        meta: "Inline edit from Ongoing Projects table"
      }
      : {
        type: "External",
        scope: "General",
        eventDate: todayIst || cell.dataset.eventDate || "",
        body,
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
  const handleCellDblClick = (event) => {
    const cell = getCellFromEvent(event);
    if (!cell || cell.dataset.canEdit !== "1") {
      return;
    }

    const target = event.target;
    if (target instanceof Element && target.closest(selectors.editor)) {
      return;
    }

    event.preventDefault();
    beginEdit(cell);
  };

  const handleCancelClick = (event) => {
    const cell = getCellFromEvent(event);
    if (!cell) {
      return;
    }

    event.preventDefault();
    cancelEdit(cell);
  };

  const handleSaveClick = async (event) => {
    const cell = getCellFromEvent(event);
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
      } else if (result.status === 404) {
        setError(cell, "Latest remark could not be found. Please retry.");
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
  document.addEventListener("dblclick", handleCellDblClick);
  document.addEventListener("click", (event) => {
    const target = event.target;
    if (!(target instanceof Element)) {
      return;
    }

    if (target.closest(selectors.cancelButton)) {
      handleCancelClick(event);
    } else if (target.closest(selectors.saveButton)) {
      void handleSaveClick(event);
    }
  });
})();
