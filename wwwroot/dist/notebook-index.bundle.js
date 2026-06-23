var __getOwnPropNames = Object.getOwnPropertyNames;
var __esm = (fn, res) => function __init() {
  return fn && (res = (0, fn[__getOwnPropNames(fn)[0]])(fn = 0)), res;
};
var __commonJS = (cb, mod) => function __require() {
  return mod || (0, cb[__getOwnPropNames(cb)[0]])((mod = { exports: {} }).exports, mod), mod.exports;
};

// wwwroot/js/notebook/notebook-utils.js
var closestAction;
var init_notebook_utils = __esm({
  "wwwroot/js/notebook/notebook-utils.js"() {
    closestAction = (event) => event.target.closest("[data-action]");
  }
});

// wwwroot/js/core/session-auth.js
function notifySessionExpired() {
  if (sessionExpiredShown) return;
  sessionExpiredShown = true;
  document.dispatchEvent(new CustomEvent("app:session-expired"));
}
var sessionExpiredShown;
var init_session_auth = __esm({
  "wwwroot/js/core/session-auth.js"() {
    sessionExpiredShown = false;
  }
});

// wwwroot/js/notebook/notebook-api.js
function isDevelopment() {
  return document.documentElement.dataset.environment === "Development" || location.hostname === "localhost";
}
function logNotebookRequest(url, method, headers, body) {
  if (!isDevelopment() || !isUnsafeMethod(method)) return;
  console.debug("Notebook API request", {
    url,
    method,
    contentType: headers.get("Content-Type"),
    hasAntiForgeryToken: headers.has("RequestVerificationToken"),
    hasBody: body !== void 0 && body !== null
  });
}
function logNotebookFailure(error) {
  if (!isDevelopment()) return;
  console.error("Notebook API request failed", {
    url: error.url,
    method: error.method,
    status: error.status,
    code: error.code,
    errors: error.errors,
    responseText: error.responseText
  });
}
function getAntiForgeryToken() {
  const tokenInput = document.querySelector('#notebook-antiforgery-token input[name="__RequestVerificationToken"]');
  const value = tokenInput?.value?.trim();
  if (!value) {
    throw new NotebookApiError("Notebook security token is unavailable. Refresh the page and try again.", {
      status: 0,
      code: "notebook_antiforgery_missing"
    });
  }
  return value;
}
function isUnsafeMethod(method) {
  const normalised = (method || "GET").toUpperCase();
  return !["GET", "HEAD", "OPTIONS", "TRACE"].includes(normalised);
}
function getDefaultNotebookErrorMessage(status) {
  switch (status) {
    case 400:
      return "The notebook request was invalid.";
    case 401:
      return "Your session has expired. Sign in again.";
    case 403:
      return "You are not authorised to perform this action.";
    case 404:
      return "The note could not be found.";
    case 409:
      return "The note was changed elsewhere.";
    case 415:
      return "The request format is not supported.";
    default:
      return "The notebook operation failed.";
  }
}
function jsonRequestOptions(method, payload, options = {}) {
  if (payload === void 0 || typeof payload === "function" || typeof payload === "symbol") {
    throw new NotebookApiError("Notebook request payload is invalid.", {
      status: 0,
      code: "notebook_invalid_client_payload"
    });
  }
  let body;
  try {
    body = JSON.stringify(payload);
  } catch (error) {
    throw new NotebookApiError("Notebook request payload could not be serialised.", {
      status: 0,
      code: "notebook_payload_serialisation_failed",
      cause: error
    });
  }
  if (typeof body !== "string" || body.length === 0) {
    throw new NotebookApiError("Notebook request payload is empty.", {
      status: 0,
      code: "notebook_empty_client_payload"
    });
  }
  const headers = new Headers(options.headers || {});
  headers.set("Content-Type", "application/json; charset=utf-8");
  return {
    ...options,
    method: String(method).toUpperCase(),
    headers,
    body
  };
}
function isLoginResponse(response) {
  if (!response) return false;
  const responseUrl = response.url || "";
  return Boolean(response.redirected && responseUrl.includes("/Identity/Account/Login"));
}
function createSessionExpiredError(context) {
  notifySessionExpired();
  return new NotebookApiError("Your session has expired. Sign in again.", {
    status: 401,
    code: "notebook_session_expired",
    url: context.url,
    method: context.method
  });
}
async function parseNotebookResponse(response, context) {
  if (isLoginResponse(response)) throw createSessionExpiredError(context);
  if (response.status === 204) return null;
  const contentType = response.headers.get("content-type") || "";
  if (contentType.includes("text/html") && (response.url || "").includes("/Identity/Account/Login")) {
    throw createSessionExpiredError(context);
  }
  let payload = null;
  let rawText = null;
  if (contentType.includes("application/json") || contentType.includes("application/problem+json")) {
    try {
      payload = await response.json();
    } catch {
      payload = null;
    }
  } else {
    rawText = await response.text();
  }
  if (!response.ok) {
    if (response.status === 401) notifySessionExpired();
    throw new NotebookApiError(
      payload?.message || payload?.detail || payload?.title || payload?.error || rawText || getDefaultNotebookErrorMessage(response.status),
      {
        status: response.status,
        code: payload?.code,
        errors: payload?.errors,
        responseText: rawText,
        url: context.url,
        method: context.method,
        currentVersion: payload?.currentVersion ?? null,
        currentItem: payload?.currentItem ?? null
      }
    );
  }
  return payload ?? rawText;
}
async function request(url, options = {}) {
  const method = String(options.method || "GET").toUpperCase();
  const headers = new Headers(options.headers || {});
  if (!headers.has("Accept")) headers.set("Accept", "application/json");
  const hasBody = options.body !== void 0 && options.body !== null;
  const isFormData = typeof FormData !== "undefined" && options.body instanceof FormData;
  if (hasBody && !isFormData && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json; charset=utf-8");
  }
  if (isUnsafeMethod(method)) headers.set("RequestVerificationToken", getAntiForgeryToken());
  logNotebookRequest(url, method, headers, options.body);
  let response;
  try {
    response = await fetch(url, { ...options, method, headers, credentials: "same-origin" });
  } catch (error) {
    if (error?.name === "AbortError") {
      throw new NotebookApiError("The notebook request was cancelled.", {
        status: 0,
        code: "notebook_request_aborted",
        url,
        method,
        cause: error
      });
    }
    const apiError = new NotebookApiError("The notebook service could not be reached.", {
      status: 0,
      code: "notebook_network_error",
      url,
      method,
      cause: error
    });
    logNotebookFailure(apiError);
    throw apiError;
  }
  try {
    return await parseNotebookResponse(response, { url, method });
  } catch (error) {
    if (error instanceof NotebookApiError) logNotebookFailure(error);
    throw error;
  }
}
var NotebookApiError, NotebookApi;
var init_notebook_api = __esm({
  "wwwroot/js/notebook/notebook-api.js"() {
    init_session_auth();
    NotebookApiError = class extends Error {
      constructor(message, { status = 0, code = null, errors = null, responseText = null, url = null, method = null, cause = null, currentVersion = null, currentItem = null } = {}) {
        super(message);
        this.name = "NotebookApiError";
        this.status = status;
        this.code = code;
        this.errors = errors;
        this.responseText = responseText;
        this.url = url;
        this.method = method;
        this.cause = cause;
        this.currentVersion = currentVersion;
        this.currentItem = currentItem;
      }
    };
    NotebookApi = {
      createItem: (payload) => request("/api/notebook/items", jsonRequestOptions("POST", payload)),
      getItem: (id, options = {}) => request(`/api/notebook/items/${encodeURIComponent(id)}`, options),
      updateItem: (id, payload) => request(`/api/notebook/items/${encodeURIComponent(id)}`, jsonRequestOptions("PATCH", payload)),
      updateContent: (id, payload, options = {}) => request(`/api/notebook/items/${encodeURIComponent(id)}/content`, jsonRequestOptions("PATCH", payload, options)),
      updateChecklist: (id, payload, options = {}) => request(`/api/notebook/items/${encodeURIComponent(id)}/checklist`, jsonRequestOptions("PUT", payload, options)),
      setPinned: (id, isPinned, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/pin`, jsonRequestOptions("POST", { isPinned, version })),
      setColour: (id, colorKey, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/colour`, jsonRequestOptions("POST", { colorKey: colorKey || null, version })),
      getLabels: () => request("/api/notebook/labels"),
      createLabel: (name) => request("/api/notebook/labels", jsonRequestOptions("POST", { name })),
      setLabels: (id, labels, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/labels`, jsonRequestOptions("POST", { labels, version })),
      renameLabel: (id, name) => request(`/api/notebook/labels/${encodeURIComponent(id)}`, jsonRequestOptions("PATCH", { name })),
      deleteLabel: (id) => request(`/api/notebook/labels/${encodeURIComponent(id)}`, { method: "DELETE" }),
      archiveItem: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/archive`, jsonRequestOptions("POST", { version })),
      completeItem: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/complete`, jsonRequestOptions("POST", { version })),
      reopenItem: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/reopen`, jsonRequestOptions("POST", { version })),
      duplicateItem: (id) => request(`/api/notebook/items/${encodeURIComponent(id)}/duplicate`, jsonRequestOptions("POST", {})),
      deleteItem: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}`, jsonRequestOptions("DELETE", { version })),
      restoreItem: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/restore`, jsonRequestOptions("POST", { version })),
      showCheckboxes: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/show-checkboxes`, jsonRequestOptions("POST", { version })),
      hideCheckboxes: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/hide-checkboxes`, jsonRequestOptions("POST", { version })),
      toggleChecklistItem: (itemId, rowId, isDone, version) => request(`/api/notebook/items/${encodeURIComponent(itemId)}/checklist-items/${encodeURIComponent(rowId)}`, jsonRequestOptions("PATCH", { isDone, version })),
      getCounts: () => request("/api/notebook/counts"),
      getCardHtml: (id, view = "home") => request(`/api/notebook/items/${encodeURIComponent(id)}/card?view=${encodeURIComponent(view)}`, { headers: { Accept: "text/html" } })
    };
  }
});

// wwwroot/js/notebook/notebook-errors.js
function getValidationMessages(error) {
  const errors = error?.errors;
  if (!errors || typeof errors !== "object") return [];
  return Object.entries(errors).flatMap(([field, value]) => {
    const messages = Array.isArray(value) ? value : [value];
    return messages.filter((message) => typeof message === "string" && message.trim().length > 0).map((message) => ({ field, message: message.trim() }));
  });
}
function getFirstValidationMessage(error) {
  const messages = getValidationMessages(error);
  if (messages.length > 0) return messages[0].message;
  return error?.message || "The note contains invalid information.";
}
var NotebookCardHtmlError, NotebookBoardTargetError;
var init_notebook_errors = __esm({
  "wwwroot/js/notebook/notebook-errors.js"() {
    NotebookCardHtmlError = class extends Error {
      constructor(message) {
        super(message);
        this.name = "NotebookCardHtmlError";
        this.code = "notebook_invalid_card_html";
      }
    };
    NotebookBoardTargetError = class extends Error {
      constructor(message) {
        super(message);
        this.name = "NotebookBoardTargetError";
        this.code = "notebook_target_board_missing";
      }
    };
  }
});

// wwwroot/js/notebook/notebook-board.js
function createNotebookBoard(root = document) {
  const findCard = (id) => root.querySelector(`[data-note-id="${CSS.escape(id)}"]`);
  const getSection = (isPinned) => root.querySelector(`[data-notebook-section="${isPinned ? "pinned" : "others"}"]`);
  const getBoard = (isPinned) => {
    const namedBoard = root.querySelector(`[data-notebook-board="${isPinned ? "pinned" : "others"}"]`);
    return namedBoard || root.querySelector('[data-notebook-board]:not([data-notebook-board="pinned"]):not([data-notebook-board="others"])');
  };
  function htmlToCardElement(html, expectedId) {
    if (typeof html !== "string" || !html.trim()) {
      throw new NotebookCardHtmlError("Notebook card HTML was empty.");
    }
    const template = document.createElement("template");
    template.innerHTML = html.trim();
    const elements = template.content.children;
    if (elements.length !== 1) {
      throw new NotebookCardHtmlError("Notebook card response must contain exactly one root element.");
    }
    const card = elements[0];
    if (!card.matches("[data-note-id]")) {
      throw new NotebookCardHtmlError("Notebook card response did not contain a note card.");
    }
    if (expectedId !== void 0 && expectedId !== null && card.dataset.noteId !== String(expectedId)) {
      throw new NotebookCardHtmlError("Notebook card response did not match the requested note.");
    }
    return card;
  }
  function refreshBoardLayout(board) {
    if (!board) return;
    const count = board.querySelectorAll(":scope > [data-note-id]").length;
    board.dataset.itemCount = String(count);
    const policy = board.dataset.layoutPolicy || "fixed-grid";
    board.dataset.layout = policy === "masonry-threshold" && count > 4 ? "masonry" : "grid";
  }
  const refreshSectionVisibility = () => {
    root.querySelectorAll("[data-notebook-board]").forEach(refreshBoardLayout);
    ["pinned", "others"].forEach((name) => {
      const section = root.querySelector(`[data-notebook-section="${name}"]`);
      const board = root.querySelector(`[data-notebook-board="${name}"]`);
      if (!section || !board) return;
      const count = Number(board.dataset.itemCount || 0);
      if (name === "pinned") section.hidden = count === 0;
      const countEl = root.querySelector(`[data-notebook-count="${name}"]`);
      if (countEl) countEl.textContent = String(count);
    });
  };
  const refreshEmptyState = () => {
    const empty = root.querySelector('[data-notebook-empty-state="current"]') || root.querySelector("[data-notebook-empty-state]") || root.querySelector("[data-notebook-empty]");
    if (!empty) return;
    const count = [...root.querySelectorAll("[data-notebook-board]")].reduce((total, board) => total + board.querySelectorAll(":scope > [data-note-id]").length, 0);
    empty.hidden = count > 0;
  };
  const upsertCard = (id, html, isPinned, options = {}) => {
    const current = findCard(id);
    const targetBoard = getBoard(isPinned);
    if (!targetBoard) throw new NotebookBoardTargetError(`Notebook board "${isPinned ? "pinned" : "others"}" was not found.`);
    const fragment = htmlToCardElement(html, id);
    const sameBoard = current && current.parentElement === targetBoard;
    const preservePosition = options.preservePosition !== false;
    if (sameBoard && preservePosition) {
      current.replaceWith(fragment);
    } else {
      current?.remove();
      options.prepend === false ? targetBoard.append(fragment) : targetBoard.prepend(fragment);
    }
    refreshSectionVisibility();
    refreshEmptyState();
    return fragment;
  };
  const replaceCard = (id, html) => {
    const current = findCard(id);
    if (!current) return null;
    const fragment = htmlToCardElement(html, id);
    current.replaceWith(fragment);
    refreshSectionVisibility();
    refreshEmptyState();
    return fragment;
  };
  const insertCard = (html, pinned = false) => {
    const fragment = htmlToCardElement(html);
    const board = getBoard(pinned);
    if (!board) throw new NotebookBoardTargetError(`Notebook board "${pinned ? "pinned" : "others"}" was not found.`);
    board.prepend(fragment);
    refreshSectionVisibility();
    refreshEmptyState();
    return fragment;
  };
  const removeCard = (id) => {
    findCard(id)?.remove();
    refreshSectionVisibility();
    refreshEmptyState();
  };
  return { findCard, getSection, getBoard, replaceCard, insertCard, upsertCard, removeCard, refreshSectionVisibility, refreshBoardLayout, refreshEmptyState, htmlToCardElement };
}
var init_notebook_board = __esm({
  "wwwroot/js/notebook/notebook-board.js"() {
    init_notebook_errors();
  }
});

// wwwroot/js/notebook/notebook-checklist-editor.js
function createClientKey() {
  if (globalThis.crypto && typeof globalThis.crypto.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return [Date.now().toString(36), Math.random().toString(36).slice(2), Math.random().toString(36).slice(2)].join("-");
}
function createChecklistEditor(root, options = {}) {
  const maxLength = options.maxLength || 500;
  let rows = [];
  let isReconciling = false;
  const notify = () => {
    if (!isReconciling) options.onChange?.();
  };
  function normalizeRow(row = {}, index = 0) {
    return {
      id: row.id ?? null,
      clientKey: normaliseClientKey(row),
      text: row.text || "",
      isDone: Boolean(row.isDone),
      sortOrder: Number.isFinite(row.sortOrder) ? row.sortOrder : (index + 1) * 1e3,
      element: row.element || null
    };
  }
  function normaliseClientKey(row = {}) {
    if (row.clientKey) return row.clientKey;
    return row.id === null || row.id === void 0 ? createClientKey() : null;
  }
  function rowTemplate(row) {
    const wrapper = document.createElement("div");
    wrapper.className = "notebook-checklist-row";
    wrapper.dataset.checklistRow = "";
    wrapper.innerHTML = `<input type="checkbox" data-checklist-done><input type="text" data-checklist-text maxlength="${maxLength}" placeholder="List item"><button type="button" data-checklist-remove aria-label="Remove checklist item">×</button>`;
    row.element = wrapper;
    updateRowElement(row, { forceContent: true });
    return wrapper;
  }
  function updateRowElement(row, { forceContent = false } = {}) {
    if (!row.element) return;
    row.element.dataset.rowId = row.id ?? "";
    row.element.dataset.clientKey = row.clientKey || "";
    const done = row.element.querySelector("[data-checklist-done]");
    const text = row.element.querySelector("[data-checklist-text]");
    if (done && (forceContent || done.checked !== Boolean(row.isDone))) done.checked = Boolean(row.isDone);
    if (text && (forceContent || text.value !== (row.text || ""))) text.value = row.text || "";
  }
  function readRowElement(row, index) {
    if (!row.element) return row;
    row.id = parseNullableInt(row.element.dataset.rowId);
    row.clientKey = row.element.dataset.clientKey || row.clientKey || normaliseClientKey(row);
    row.text = row.element.querySelector("[data-checklist-text]")?.value || "";
    row.isDone = Boolean(row.element.querySelector("[data-checklist-done]")?.checked);
    row.sortOrder = (index + 1) * 1e3;
    return row;
  }
  function findRowByElement(element) {
    return rows.find((row) => row.element === element) || null;
  }
  function captureFocusState() {
    const active = document.activeElement;
    const row = active?.closest?.("[data-checklist-row]");
    if (!row || !root.contains(row)) return null;
    return {
      rowId: row.dataset.rowId || null,
      clientKey: row.dataset.clientKey || null,
      selectionStart: typeof active.selectionStart === "number" ? active.selectionStart : null,
      selectionEnd: typeof active.selectionEnd === "number" ? active.selectionEnd : null
    };
  }
  function restoreFocusState(state2) {
    if (!state2) return;
    const row = rows.find((candidate) => state2.rowId && String(candidate.id) === state2.rowId || state2.clientKey && candidate.clientKey === state2.clientKey);
    const input = row?.element?.querySelector("[data-checklist-text]");
    if (!input) return;
    input.focus();
    if (state2.selectionStart !== null && typeof input.setSelectionRange === "function") {
      const end = state2.selectionEnd ?? state2.selectionStart;
      input.setSelectionRange(Math.min(state2.selectionStart, input.value.length), Math.min(end, input.value.length));
    }
  }
  function findMatchingRow(target, byId, byClientKey) {
    if (target?.id !== null && target?.id !== void 0) {
      const byPermanentId = byId.get(String(target.id));
      if (byPermanentId) return byPermanentId;
    }
    if (target?.clientKey) return byClientKey.get(target.clientKey) ?? null;
    return null;
  }
  function appendReconciledRow(reconciled, row, seenRows, seenIdentities) {
    const identity = row.id !== null && row.id !== void 0 ? `id:${row.id}` : row.clientKey ? `client:${row.clientKey}` : null;
    if (seenRows.has(row) || identity && seenIdentities.has(identity)) return;
    seenRows.add(row);
    if (identity) seenIdentities.add(identity);
    reconciled.push(row);
  }
  function wasAddedAfterDispatch(localRow, submittedById, submittedByClientKey) {
    return !findMatchingRow(localRow, submittedById, submittedByClientKey);
  }
  function removeStaleRowElements(reconciledRows) {
    const retainedElements = new Set(reconciledRows.map((row) => row.element).filter(Boolean));
    root.querySelectorAll("[data-checklist-row]").forEach((element) => {
      if (!retainedElements.has(element)) element.remove();
    });
  }
  function ensureAddItemControl() {
    let button = root.querySelector("[data-checklist-add]");
    if (!button) {
      button = document.createElement("button");
      button.type = "button";
      button.className = "notebook-checklist-add";
      button.dataset.checklistAdd = "";
      button.textContent = "+ List item";
      root.append(button);
    }
    return button;
  }
  function setRows(nextRows) {
    root.replaceChildren();
    rows = (nextRows || []).map(normalizeRow);
    rows.forEach((row) => root.append(rowTemplate(row)));
    ensureAddItemControl();
  }
  function addRow(afterElement = null, row = {}) {
    const insertAt = afterElement ? rows.findIndex((candidate) => candidate.element === afterElement) + 1 : rows.length;
    const model = normalizeRow(row, insertAt);
    const el = rowTemplate(model);
    if (afterElement) afterElement.after(el);
    else root.insertBefore(el, ensureAddItemControl());
    rows.splice(insertAt < 0 ? rows.length : insertAt, 0, model);
    return el;
  }
  function removeRow(element) {
    const row = findRowByElement(element);
    const prev = element.previousElementSibling;
    rows = rows.filter((candidate) => candidate !== row);
    element.remove();
    (prev?.querySelector("[data-checklist-text]") || root.querySelector("[data-checklist-text]"))?.focus();
    notify();
  }
  function getRows() {
    rows.forEach(readRowElement);
    return rows.map((row, index) => ({ id: row.id, clientKey: row.clientKey, text: row.text.trim(), isDone: row.isDone, sortOrder: index })).filter((row) => row.text.length > 0);
  }
  function reconcileRows(serverRows, submittedRows = null) {
    isReconciling = true;
    const focusState = captureFocusState();
    const scrollTop = root.scrollTop;
    try {
      rows.forEach(readRowElement);
      const originalLocalRows = [...rows];
      const hasSubmittedSnapshot = Array.isArray(submittedRows);
      const baseRows = hasSubmittedSnapshot ? submittedRows : [];
      const submittedById = new Map(baseRows.filter((row) => row.id !== null && row.id !== void 0).map((row) => [String(row.id), row]));
      const submittedByClientKey = new Map(baseRows.filter((row) => row.clientKey).map((row) => [row.clientKey, row]));
      const localById = new Map(originalLocalRows.filter((row) => row.id !== null && row.id !== void 0).map((row) => [String(row.id), row]));
      const localByClientKey = new Map(originalLocalRows.filter((row) => row.clientKey).map((row) => [row.clientKey, row]));
      const reconciled = [];
      const seenRows = /* @__PURE__ */ new Set();
      const seenIdentities = /* @__PURE__ */ new Set();
      (serverRows || []).forEach((serverRow, index) => {
        const submittedRow = findMatchingRow(serverRow, submittedById, submittedByClientKey);
        let localRow = findMatchingRow(serverRow, localById, localByClientKey);
        if (hasSubmittedSnapshot && submittedRow && !localRow) return;
        if (!localRow) localRow = normalizeRow(serverRow, index);
        localRow.id = serverRow.id ?? localRow.id;
        localRow.clientKey = serverRow.clientKey ?? localRow.clientKey ?? normaliseClientKey(localRow);
        if (!submittedRow || localRow.text === (submittedRow.text ?? "")) localRow.text = serverRow.text || "";
        if (!submittedRow || localRow.isDone === Boolean(submittedRow.isDone)) localRow.isDone = Boolean(serverRow.isDone);
        localRow.sortOrder = serverRow.sortOrder ?? (index + 1) * 1e3;
        if (!localRow.element) rowTemplate(localRow);
        updateRowElement(localRow);
        appendReconciledRow(reconciled, localRow, seenRows, seenIdentities);
      });
      if (hasSubmittedSnapshot) originalLocalRows.forEach((localRow) => {
        if (wasAddedAfterDispatch(localRow, submittedById, submittedByClientKey)) {
          appendReconciledRow(reconciled, localRow, seenRows, seenIdentities);
        }
      });
      removeStaleRowElements(reconciled);
      rows = reconciled;
      rows.forEach((row) => root.insertBefore(row.element, ensureAddItemControl()));
      ensureAddItemControl();
      root.scrollTop = scrollTop;
      restoreFocusState(focusState);
    } finally {
      isReconciling = false;
    }
  }
  function handleInput(event) {
    if (isReconciling) return;
    if (event.target.matches("[data-checklist-text]")) notify();
  }
  function handleChange(event) {
    if (isReconciling) return;
    if (event.target.matches("[data-checklist-done]")) notify();
  }
  function handleClick(event) {
    if (isReconciling) return;
    if (event.target.closest("[data-checklist-add]")) {
      addRow().querySelector("[data-checklist-text]")?.focus();
      return;
    }
    const button = event.target.closest("[data-checklist-remove]");
    if (button) removeRow(button.closest("[data-checklist-row]"));
  }
  function handleKeydown(event) {
    if (isReconciling) return;
    const input = event.target.closest("[data-checklist-text]");
    if (!input) return;
    const row = input.closest("[data-checklist-row]");
    if (event.key === "Enter") {
      event.preventDefault();
      addRow(row).querySelector("[data-checklist-text]").focus();
      notify();
    }
    if (event.key === "Backspace" && input.value.length === 0 && root.querySelectorAll("[data-checklist-row]").length > 1) {
      event.preventDefault();
      removeRow(row);
    }
  }
  function destroy() {
    root.removeEventListener("input", handleInput);
    root.removeEventListener("change", handleChange);
    root.removeEventListener("click", handleClick);
    root.removeEventListener("keydown", handleKeydown);
    root.replaceChildren();
    rows = [];
  }
  root.addEventListener("input", handleInput);
  root.addEventListener("change", handleChange);
  root.addEventListener("click", handleClick);
  root.addEventListener("keydown", handleKeydown);
  return { setRows, getRows, addRow, removeRow, reconcileRows, replaceRows: setRows, renderRows: setRows, getFocusedRowState: captureFocusState, restoreFocusedRowState: restoreFocusState, focusFirst: () => (root.querySelector("[data-checklist-text]") || ensureAddItemControl())?.focus(), clear: () => setRows([]), destroy };
}
var parseNullableInt;
var init_notebook_checklist_editor = __esm({
  "wwwroot/js/notebook/notebook-checklist-editor.js"() {
    parseNullableInt = (value) => value ? Number.parseInt(value, 10) : null;
  }
});

// wwwroot/js/notebook/notebook-reconcile.js
function requireMutationItem(response, message = "The notebook response did not contain an updated item.") {
  if (!response?.item) {
    throw new NotebookApiError(message, { code: "notebook_invalid_mutation_response" });
  }
  return response.item;
}
function updateCardConcurrencyState(card, item) {
  if (!card || !item) return;
  card.dataset.version = item.version;
  card.dataset.isPinned = String(item.isPinned).toLowerCase();
  card.dataset.status = item.status;
}
function logReconciliationFailure(item, stage, error) {
  console.error("Notebook card reconciliation failed", { itemId: item?.id, stage, error });
}
async function reconcileMutation({
  response,
  board,
  view = "home",
  getCardHtml,
  applyCounts,
  preservePosition = true,
  prepend = false,
  showGlobalError,
  existingCard = null,
  command = "unknown",
  renderFailureMessage = "The note was updated, but its card could not be rendered. Reload the page.",
  reconcileFailureMessage = "The note was updated, but the board could not refresh. Reload the page."
}) {
  const item = requireMutationItem(response);
  applyCounts?.(response.counts);
  updateCardConcurrencyState(existingCard || board?.findCard?.(item.id), item);
  let html = response.cardHtml;
  if (!html) {
    console.warn("Notebook mutation response did not contain card HTML.", { itemId: item.id, command });
    try {
      html = await getCardHtml(item.id, view);
    } catch (error) {
      logReconciliationFailure(item, "server-card-rendering", error);
      showGlobalError?.(renderFailureMessage);
      return { item, reconciled: false, code: "notebook_card_render_failed" };
    }
  }
  if (typeof html !== "string" || !html.trim()) {
    const error = new NotebookCardHtmlError("Notebook card response was empty.");
    logReconciliationFailure(item, "empty-card-response", error);
    showGlobalError?.(renderFailureMessage);
    return { item, reconciled: false, code: "notebook_empty_card_response" };
  }
  try {
    board.upsertCard(item.id, html, item.isPinned, { preservePosition, prepend });
    return { item, reconciled: true };
  } catch (error) {
    const classification = classifyReconciliationError(error);
    logReconciliationFailure(item, classification.stage, error);
    showGlobalError?.(classification.isRenderFailure ? renderFailureMessage : reconcileFailureMessage);
    updateCardConcurrencyState(existingCard || board?.findCard?.(item.id), item);
    return { item, reconciled: false, code: classification.code };
  }
}
function classifyReconciliationError(error) {
  switch (error?.code) {
    case "notebook_invalid_card_html":
      return { stage: "invalid-card-html", code: "notebook_invalid_card_html", isRenderFailure: true };
    case "notebook_target_board_missing":
      return { stage: "target-board", code: "notebook_target_board_missing", isRenderFailure: false };
    case "notebook_board_update_failed":
      return { stage: "card-replacement", code: "notebook_board_update_failed", isRenderFailure: false };
    default:
      return { stage: "card-replacement", code: "notebook_board_reconcile_failed", isRenderFailure: false };
  }
}
var init_notebook_reconcile = __esm({
  "wwwroot/js/notebook/notebook-reconcile.js"() {
    init_notebook_api();
    init_notebook_errors();
  }
});

// wwwroot/js/notebook/notebook-composer.js
function initNotebookComposer(root, board, view, options = {}) {
  if (!root) return null;
  const collapsed = root.querySelector("[data-composer-collapsed]");
  const expanded = root.querySelector("[data-composer-expanded]");
  const title = root.querySelector("[data-composer-title]");
  const body = root.querySelector("[data-composer-body]");
  const checklistRoot = root.querySelector("[data-composer-checklist]");
  const status = root.querySelector("[data-composer-status]");
  const pin = root.querySelector("[data-composer-pin]");
  const closeButton = root.querySelector("[data-composer-close]");
  const checklistButton = root.querySelector("[data-composer-open-checklist]");
  const checklist = createChecklistEditor(checklistRoot);
  const showGlobalError = options.showGlobalError || (() => {
  });
  const applyCounts = options.applyCounts || (() => {
  });
  let mode = "collapsed";
  let isPinned = false;
  let created = null;
  let isSaving = false;
  let clientRequestId = crypto.randomUUID();
  const setStatus = (text) => {
    if (status) status.textContent = text || "";
  };
  const setDisabled = (disabled) => {
    if (closeButton) closeButton.disabled = disabled;
    if (checklistButton) checklistButton.disabled = disabled;
    if (pin) pin.disabled = disabled;
  };
  const setMode = (next) => {
    mode = next;
    root.dataset.state = next;
    collapsed.hidden = next !== "collapsed";
    expanded.hidden = next === "collapsed";
    body.hidden = next === "checklist";
    checklistRoot.hidden = next !== "checklist";
  };
  const reset = () => {
    title.value = "";
    body.value = "";
    checklist.clear();
    isPinned = false;
    created = null;
    clientRequestId = crypto.randomUUID();
    pin.classList.remove("is-active");
    setStatus("");
  };
  const payload = () => ({
    title: title.value.trim(),
    body: body.value.trim(),
    type: mode === "checklist" ? "Checklist" : "Note",
    priority: "Normal",
    reminderAtUtc: null,
    colorKey: null,
    isPinned,
    labels: [],
    clientRequestId,
    checklistRows: mode === "checklist" ? checklist.getRows().map((row, index) => ({ id: row.id, text: row.text.trim(), isDone: row.isDone, sortOrder: (index + 1) * 1e3 })).filter((row) => row.text.length > 0) : []
  });
  const meaningful = (p) => Boolean(p.title || p.body || p.checklistRows.length);
  async function closeComposer() {
    const data = payload();
    if (!meaningful(data)) {
      reset();
      setMode("collapsed");
      return true;
    }
    if (isSaving) return false;
    isSaving = true;
    setDisabled(true);
    setStatus("Saving…");
    try {
      if (!created) created = await NotebookApi.createItem(data);
      if (!created?.item) {
        throw new NotebookApiError("The create response did not contain the new note.", { code: "notebook_invalid_mutation_response" });
      }
      await reconcileMutation({
        response: created,
        board,
        view: view || "home",
        getCardHtml: NotebookApi.getCardHtml,
        applyCounts,
        preservePosition: false,
        prepend: true,
        showGlobalError,
        renderFailureMessage: "The note was saved, but its card could not be rendered. Reload the page.",
        reconcileFailureMessage: "The note was saved, but the board could not refresh. Reload the page."
      });
      reset();
      setMode("collapsed");
      return true;
    } catch (error) {
      setStatus(error.message || "Unable to save the note.");
      return false;
    } finally {
      isSaving = false;
      setDisabled(false);
    }
  }
  root.querySelector("[data-composer-open-note]")?.addEventListener("click", () => {
    if (isSaving) return;
    setMode("note");
    body.focus();
  });
  checklistButton?.addEventListener("click", () => {
    if (isSaving) return;
    setMode("checklist");
    checklist.setRows([{ text: "" }]);
    checklist.focusFirst();
  });
  closeButton?.addEventListener("click", closeComposer);
  pin?.addEventListener("click", () => {
    if (isSaving) return;
    isPinned = !isPinned;
    pin.classList.toggle("is-active", isPinned);
  });
  return { close: closeComposer, isOpen: () => mode !== "collapsed" };
}
var init_notebook_composer = __esm({
  "wwwroot/js/notebook/notebook-composer.js"() {
    init_notebook_api();
    init_notebook_checklist_editor();
    init_notebook_reconcile();
  }
});

// wwwroot/js/notebook/notebook-autosave.js
function isPlainObject(value) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) return false;
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}
function clonePayload(payload) {
  if (typeof structuredClone === "function") return structuredClone(payload);
  return JSON.parse(JSON.stringify(payload));
}
function assertPayloadObject(payload) {
  if (!isPlainObject(payload)) throw new TypeError("Autosave payload must be a plain object.");
}
function createAutosave({ save, delay = 800, onSaving, onPersisted, onSaveError, onReconcileError, onSaved, onError }) {
  let timer = null;
  let activePromise = null;
  let latestPayload = null;
  let dirty = false;
  let stopped = false;
  let activeController = null;
  let operationSequence = 0;
  async function runLoop() {
    if (activePromise) return activePromise;
    activePromise = (async () => {
      while (!stopped && dirty && latestPayload) {
        const payload = latestPayload;
        dirty = false;
        await onSaving?.();
        let result;
        try {
          activeController = typeof AbortController !== "undefined" ? new AbortController() : null;
          const operation = {
            sequence: ++operationSequence,
            signal: activeController?.signal ?? null
          };
          result = await save(payload, operation);
        } catch (error) {
          const disposition = await (onSaveError || onError)?.(error);
          dirty = disposition?.retryable === true;
          if (!dirty) latestPayload = null;
          throw error;
        } finally {
          activeController = null;
        }
        try {
          await (onPersisted || onSaved)?.(result);
        } catch (error) {
          await onReconcileError?.(error, result);
        }
      }
    })();
    try {
      return await activePromise;
    } finally {
      activePromise = null;
    }
  }
  function schedule(payload) {
    if (stopped) return;
    assertPayloadObject(payload);
    latestPayload = clonePayload(payload);
    dirty = true;
    if (timer) window.clearTimeout(timer);
    timer = window.setTimeout(() => {
      timer = null;
      runLoop().catch(() => {
      });
    }, delay);
  }
  async function flush() {
    if (timer) {
      window.clearTimeout(timer);
      timer = null;
    }
    if (activePromise) await activePromise;
    if (dirty) await runLoop();
  }
  function cancel({ abortActive = true } = {}) {
    if (timer) {
      window.clearTimeout(timer);
      timer = null;
    }
    dirty = false;
    latestPayload = null;
    if (abortActive) activeController?.abort();
  }
  function stop() {
    stopped = true;
    cancel();
  }
  return {
    schedule,
    flush,
    cancel,
    stop,
    hasPending: () => Boolean(timer || activePromise || dirty),
    hasActiveRequest: () => Boolean(activeController)
  };
}
var init_notebook_autosave = __esm({
  "wwwroot/js/notebook/notebook-autosave.js"() {
  }
});

// wwwroot/js/notebook/notebook-colour-picker.js
function normaliseNotebookColour(value) {
  const key = String(value || "").trim().toLowerCase();
  return ALLOWED_COLOURS.includes(key) ? key : "";
}
function applyNotebookSurfaceColour(element, value) {
  if (!element) return;
  const key = normaliseNotebookColour(value);
  element.classList.remove(...COLOUR_CLASSES);
  if (key) element.classList.add(`notebook-surface-colour-${key}`);
  element.dataset.colourValue = key;
}
function setNotebookColourSelection(root, value) {
  if (!root) return;
  const key = normaliseNotebookColour(value);
  root.dataset.colourValue = key;
  root.querySelectorAll("[data-colour-choice]").forEach((choice) => {
    const selected = normaliseNotebookColour(choice.dataset.colourChoice) === key;
    choice.classList.toggle("is-selected", selected);
    choice.setAttribute("aria-checked", String(selected));
  });
}
function closeNotebookColourPickers(scope = document, except = null) {
  scope.querySelectorAll("[data-notebook-colour-picker]").forEach((picker) => {
    if (picker === except) return;
    const popover = picker.querySelector("[data-colour-picker-popover]");
    const toggle = picker.querySelector("[data-colour-picker-toggle]");
    if (popover) popover.hidden = true;
    if (toggle) toggle.setAttribute("aria-expanded", "false");
  });
}
function initNotebookColourPicker(root, options = {}) {
  if (!root) throw new Error("Notebook colour picker root is required.");
  const toggle = root.querySelector("[data-colour-picker-toggle]");
  const popover = root.querySelector("[data-colour-picker-popover]");
  if (!toggle || !popover) throw new Error("Notebook colour picker markup is incomplete.");
  let value = normaliseNotebookColour(options.value ?? root.dataset.colourValue);
  let busy = false;
  setNotebookColourSelection(root, value);
  const close = () => {
    popover.hidden = true;
    toggle.setAttribute("aria-expanded", "false");
  };
  const open = () => {
    closeNotebookColourPickers(document, root);
    popover.hidden = false;
    toggle.setAttribute("aria-expanded", "true");
    popover.querySelector(".is-selected,[data-colour-choice]")?.focus?.();
  };
  const setValue = (next, { notify = false } = {}) => {
    const normalised = normaliseNotebookColour(next);
    const previous = value;
    value = normalised;
    setNotebookColourSelection(root, value);
    if (notify && previous !== value) options.onSelect?.(value, previous);
  };
  toggle.addEventListener("click", (event) => {
    event.preventDefault();
    event.stopPropagation();
    if (busy) return;
    popover.hidden ? open() : close();
  });
  popover.addEventListener("click", async (event) => {
    const choice = event.target.closest("[data-colour-choice]");
    if (!choice || busy) return;
    event.preventDefault();
    event.stopPropagation();
    const next = normaliseNotebookColour(choice.dataset.colourChoice);
    const previous = value;
    setValue(next);
    close();
    if (previous === next) return;
    busy = true;
    root.classList.add("is-busy");
    try {
      await options.onSelect?.(next, previous);
    } catch (error) {
      setValue(previous);
      throw error;
    } finally {
      busy = false;
      root.classList.remove("is-busy");
    }
  });
  root.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !popover.hidden) {
      event.preventDefault();
      close();
      toggle.focus();
    }
  });
  return {
    open,
    close,
    getValue: () => value,
    setValue,
    setBusy(next) {
      busy = Boolean(next);
      root.classList.toggle("is-busy", busy);
      toggle.disabled = busy;
    }
  };
}
var ALLOWED_COLOURS, COLOUR_CLASSES;
var init_notebook_colour_picker = __esm({
  "wwwroot/js/notebook/notebook-colour-picker.js"() {
    ALLOWED_COLOURS = Object.freeze(["", "white", "blue", "amber", "green", "rose", "slate"]);
    COLOUR_CLASSES = ALLOWED_COLOURS.filter(Boolean).map((key) => `notebook-surface-colour-${key}`);
  }
});

// wwwroot/js/notebook/notebook-label-picker.js
function normaliseLabelName(value) {
  return String(value || "").trim().replace(/^#+/, "").trim();
}
function normaliseLabels(values) {
  const seen = /* @__PURE__ */ new Set();
  return (Array.isArray(values) ? values : []).map(normaliseLabelName).filter((name) => {
    if (!name) return false;
    const key = name.toLocaleLowerCase();
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}
function setNotebookLabelCatalog(labels = [], documentRef = document) {
  state.labels = (Array.isArray(labels) ? labels : []).map((label) => ({
    id: Number(label?.id || 0),
    name: normaliseLabelName(label?.name),
    count: Number(label?.count || 0)
  })).filter((label) => label.id > 0 && label.name).sort((a, b) => a.name.localeCompare(b.name));
  documentRef.dispatchEvent(new CustomEvent("notebook:labels-changed", {
    detail: { labels: state.labels.map((label) => ({ ...label })) }
  }));
}
function getNotebookLabelCatalog(documentRef = document) {
  const script = documentRef.querySelector("#notebook-label-catalog");
  if (script && state.labels.length === 0) {
    try {
      setNotebookLabelCatalog(JSON.parse(script.textContent || "[]"), documentRef);
    } catch {
      state.labels = [];
    }
  }
  return state.labels.map((label) => ({ ...label }));
}
async function refreshNotebookLabelCatalog(documentRef = document) {
  const labels = await NotebookApi.getLabels();
  setNotebookLabelCatalog(labels || [], documentRef);
  return getNotebookLabelCatalog(documentRef);
}
function initNotebookLabelPicker(root, options = {}) {
  if (!root) return null;
  const toggle = root.querySelector("[data-label-picker-toggle]");
  const popover = root.querySelector("[data-label-picker-popover]");
  const close = root.querySelector("[data-label-picker-close]");
  const selectedRoot = root.querySelector("[data-label-picker-selected]");
  const input = root.querySelector("[data-label-picker-input]");
  const suggestions = root.querySelector("[data-label-picker-suggestions]");
  const create = root.querySelector("[data-label-picker-create]");
  const floatingHost = root.closest("[data-notebook-card-label-host]");
  let selected = normaliseLabels(options.value || []);
  let busy = false;
  let onChange = options.onChange;
  function setBusy(value) {
    busy = Boolean(value);
    if (toggle) toggle.disabled = busy;
    input.disabled = busy;
    suggestions.querySelectorAll("button").forEach((button) => {
      button.disabled = busy;
    });
    create.disabled = busy;
    root.classList.toggle("is-busy", busy);
  }
  function renderSelected() {
    selectedRoot.innerHTML = "";
    selected.forEach((name) => {
      const chip = document.createElement("button");
      chip.type = "button";
      chip.className = "notebook-label-chip";
      chip.dataset.removeLabel = name;
      chip.setAttribute("aria-label", `Remove label ${name}`);
      chip.innerHTML = `<span>${escapeHtml(name)}</span><i class="bi bi-x"></i>`;
      selectedRoot.appendChild(chip);
    });
    selectedRoot.hidden = selected.length === 0;
  }
  function renderSuggestions() {
    const queryText = normaliseLabelName(input.value);
    const query = queryText.toLocaleLowerCase();
    const selectedKeys = new Set(selected.map((x) => x.toLocaleLowerCase()));
    const matches = getNotebookLabelCatalog().filter((label) => !query || label.name.toLocaleLowerCase().includes(query));
    suggestions.innerHTML = "";
    matches.slice(0, 50).forEach((label) => {
      const checked = selectedKeys.has(label.name.toLocaleLowerCase());
      const button = document.createElement("button");
      button.type = "button";
      button.className = "notebook-label-picker__suggestion";
      button.dataset.toggleLabel = label.name;
      button.setAttribute("role", "option");
      button.setAttribute("aria-selected", String(checked));
      button.innerHTML = `<i class="bi ${checked ? "bi-check-square" : "bi-square"}" aria-hidden="true"></i><span>${escapeHtml(label.name)}</span><small>${label.count}</small>`;
      suggestions.appendChild(button);
    });
    const exact = getNotebookLabelCatalog().some((label) => label.name.toLocaleLowerCase() === query);
    create.hidden = !queryText || exact;
    create.textContent = queryText ? `Create “${queryText}”` : "";
  }
  async function commit(next) {
    if (busy) return;
    const previous = selected;
    selected = normaliseLabels(next);
    renderSelected();
    renderSuggestions();
    try {
      setBusy(true);
      await onChange?.([...selected], [...previous]);
    } catch (error) {
      selected = previous;
      renderSelected();
      renderSuggestions();
      throw error;
    } finally {
      setBusy(false);
    }
  }
  function positionFloating(anchor) {
    if (!floatingHost || !anchor) return;
    const rect = anchor.getBoundingClientRect();
    const width = Math.min(320, window.innerWidth - 24);
    const left = Math.min(Math.max(12, rect.left), window.innerWidth - width - 12);
    const top = Math.min(rect.bottom + 8, window.innerHeight - 360);
    floatingHost.style.left = `${left}px`;
    floatingHost.style.top = `${Math.max(12, top)}px`;
    floatingHost.style.width = `${width}px`;
  }
  function open(anchor = null) {
    if (busy) return;
    if (floatingHost) {
      floatingHost.hidden = false;
      positionFloating(anchor);
    }
    popover.hidden = false;
    toggle?.setAttribute("aria-expanded", "true");
    renderSelected();
    renderSuggestions();
    queueMicrotask(() => input.focus());
  }
  function closePicker() {
    popover.hidden = true;
    toggle?.setAttribute("aria-expanded", "false");
    input.value = "";
    renderSuggestions();
    if (floatingHost) floatingHost.hidden = true;
  }
  toggle?.addEventListener("click", () => popover.hidden ? open(toggle) : closePicker());
  close.addEventListener("click", closePicker);
  input.addEventListener("input", renderSuggestions);
  input.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      event.preventDefault();
      closePicker();
    }
    if (event.key === "Enter") {
      event.preventDefault();
      const first = suggestions.querySelector("[data-toggle-label]");
      if (first) first.click();
      else if (!create.hidden) create.click();
    }
  });
  suggestions.addEventListener("click", (event) => {
    const button = event.target.closest("[data-toggle-label]");
    if (!button) return;
    const name = button.dataset.toggleLabel;
    const exists = selected.some((value) => value.toLocaleLowerCase() === name.toLocaleLowerCase());
    const next = exists ? selected.filter((value) => value.toLocaleLowerCase() !== name.toLocaleLowerCase()) : [...selected, name];
    commit(next).catch(() => {
    });
  });
  selectedRoot.addEventListener("click", (event) => {
    const button = event.target.closest("[data-remove-label]");
    if (!button) return;
    const key = button.dataset.removeLabel.toLocaleLowerCase();
    commit(selected.filter((value) => value.toLocaleLowerCase() !== key)).catch(() => {
    });
  });
  create.addEventListener("click", async () => {
    const name = normaliseLabelName(input.value);
    if (!name || busy) return;
    try {
      setBusy(true);
      const result = await NotebookApi.createLabel(name);
      setNotebookLabelCatalog(result?.labels || []);
      const canonical = result?.label?.name || name;
      input.value = "";
      setBusy(false);
      await commit([...selected, canonical]);
    } catch (error) {
      setBusy(false);
      options.onError?.(error);
    }
  });
  document.addEventListener("click", (event) => {
    if (popover.hidden) return;
    if (root.contains(event.target) || floatingHost?.contains(event.target)) return;
    closePicker();
  });
  document.addEventListener("notebook:labels-changed", renderSuggestions);
  renderSelected();
  renderSuggestions();
  return {
    getValue: () => [...selected],
    setValue: (value) => {
      selected = normaliseLabels(value);
      renderSelected();
      renderSuggestions();
    },
    setBusy,
    setOnChange: (handler) => {
      onChange = handler;
    },
    configure: ({ value, onChange: nextOnChange } = {}) => {
      if (value !== void 0) selected = normaliseLabels(value);
      if (nextOnChange !== void 0) onChange = nextOnChange;
      renderSelected();
      renderSuggestions();
    },
    open,
    close: closePicker,
    refresh: () => refreshNotebookLabelCatalog()
  };
}
function escapeHtml(value) {
  return String(value).replace(/[&<>'"]/g, (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" })[character]);
}
var state;
var init_notebook_label_picker = __esm({
  "wwwroot/js/notebook/notebook-label-picker.js"() {
    init_notebook_api();
    state = { labels: [] };
  }
});

// wwwroot/js/notebook/notebook-editor.js
function requireEditorElement(root, selector) {
  const element = root?.querySelector?.(selector);
  if (!element) {
    const error = new Error(`Notebook editor template is missing required element: ${selector}`);
    error.code = "notebook_editor_template_invalid";
    throw error;
  }
  return element;
}
function cloneNotebookEditorTemplate(documentRef = document) {
  const template = documentRef?.querySelector?.(EditorSelectors.template);
  if (!template || template.tagName !== "TEMPLATE") {
    const error = new Error(`Notebook editor template was not found: ${EditorSelectors.template}`);
    error.code = "notebook_editor_template_missing";
    throw error;
  }
  const editor = template.content?.firstElementChild?.cloneNode(true);
  if (!editor?.matches?.(EditorSelectors.editor)) {
    const error = new Error(`Notebook editor template must contain a single ${EditorSelectors.editor} root element.`);
    error.code = "notebook_editor_template_invalid";
    throw error;
  }
  [
    EditorSelectors.title,
    EditorSelectors.body,
    EditorSelectors.checklist,
    EditorSelectors.pin,
    EditorSelectors.saveState,
    EditorSelectors.conflict,
    EditorSelectors.conflictMessage,
    EditorSelectors.useLocal,
    EditorSelectors.reloadLatest,
    EditorSelectors.copyLocal
  ].forEach((selector) => requireEditorElement(editor, selector));
  return editor;
}
function shouldTreatDraftAsConflict(draft, currentItem) {
  return Boolean(draft?.sourceVersion && currentItem?.version && draft.sourceVersion !== currentItem.version);
}
function shouldIgnoreSaveResult(saveResult, currentConflictGeneration) {
  return Number.isInteger(saveResult?.conflictGenerationAtDispatch) && saveResult.conflictGenerationAtDispatch !== currentConflictGeneration;
}
function serialiseNotebookContent({ title = "", body = "", type = "Note", checklistRows = [] } = {}) {
  const sections = [String(title).trim(), String(body).trim()].filter(Boolean);
  if (type === "Checklist") {
    const rows = (Array.isArray(checklistRows) ? checklistRows : []).filter((row) => String(row?.text ?? "").trim().length > 0).map((row) => `${row?.isDone ? "☑" : "☐"} ${String(row.text).trim()}`);
    if (rows.length) sections.push(rows.join("\n"));
  }
  return sections.join("\n\n");
}
function initNotebookEditor(board, view, options = {}) {
  let modal;
  let item;
  let autosave;
  let checklist;
  let colourPicker;
  let labelPicker;
  let trigger;
  let openedByPushState = false;
  let currentSaveError = null;
  let draftSourceVersion = null;
  let blockedByValidation = false;
  let lastValidationFingerprint = null;
  let conflictGeneration = 0;
  let directSaveSequence = 0;
  const conflictState = {
    active: false,
    type: null,
    pendingServerItem: null,
    localDraft: null,
    message: null,
    resolving: false,
    error: null,
    pendingColour: null,
    pendingLabels: null
  };
  const shell = options.shell || document.querySelector(".notebook-shell");
  const dirtyState = { title: false, body: false, checklist: false };
  const editRevision = { title: 0, body: 0, checklist: 0 };
  const buildNoteUrl = (id) => {
    const url = new URL(location.href);
    id ? url.searchParams.set("note", id) : url.searchParams.delete("note");
    return url;
  };
  const focusableSelector = 'a[href],button:not([disabled]),textarea:not([disabled]),input:not([disabled]),select:not([disabled]),[tabindex]:not([tabindex="-1"])';
  function setSaveStatus(text, state2 = SaveState.Idle) {
    const el = modal?.querySelector("[data-notebook-save-state]");
    if (el) {
      el.textContent = text || "";
      el.dataset.state = state2;
    }
    const retry = modal?.querySelector("[data-notebook-retry]");
    const reloadApplication = modal?.querySelector("[data-notebook-reload-application]");
    const discard = modal?.querySelector("[data-modal-discard]");
    const signIn = modal?.querySelector("[data-notebook-sign-in]");
    const copy = modal?.querySelector("[data-notebook-copy-unsaved]");
    if (retry) retry.hidden = !["network", "server", "error"].includes(state2);
    if (reloadApplication) reloadApplication.hidden = state2 !== "client-version";
    if (discard) discard.hidden = !["network", "server", "error", "client-version", "session-expired", "forbidden"].includes(state2);
    if (signIn) signIn.hidden = state2 !== "session-expired";
    if (copy) copy.hidden = !["session-expired", "forbidden", "network", "server", "error"].includes(state2);
  }
  function renderConflictState() {
    const panel = modal?.querySelector("[data-notebook-conflict]");
    const message = modal?.querySelector("[data-notebook-conflict-message]");
    const pin = modal?.querySelector("[data-modal-pin]");
    const useLocal = modal?.querySelector("[data-notebook-use-local]");
    const reloadLatestButton = modal?.querySelector("[data-notebook-reload-latest]");
    const copyLocal = modal?.querySelector("[data-notebook-copy-local]");
    if (!panel) return;
    panel.hidden = !conflictState.active;
    if (message) {
      message.textContent = conflictState.resolving ? "Saving your changes…" : conflictState.error || conflictState.message || "This note changed elsewhere.";
    }
    if (pin) pin.disabled = conflictState.active;
    if (useLocal) useLocal.disabled = conflictState.resolving;
    if (reloadLatestButton) reloadLatestButton.disabled = conflictState.resolving;
    if (copyLocal) copyLocal.disabled = conflictState.resolving;
    if (conflictState.active) setSaveStatus("", SaveState.Idle);
  }
  function clearConflictState() {
    conflictState.active = false;
    conflictState.type = null;
    conflictState.pendingServerItem = null;
    conflictState.localDraft = null;
    conflictState.message = null;
    conflictState.resolving = false;
    conflictState.error = null;
    conflictState.pendingColour = null;
    conflictState.pendingLabels = null;
    renderConflictState();
  }
  function isConflictBlocked() {
    return conflictState.active;
  }
  function activateConflict({ type, pendingServerItem = null, localDraft = null, pendingColour = void 0, pendingLabels = void 0, message }) {
    conflictGeneration += 1;
    conflictState.active = true;
    conflictState.type = type;
    if (pendingServerItem) conflictState.pendingServerItem = pendingServerItem;
    conflictState.localDraft = localDraft ?? conflictState.localDraft;
    if (pendingColour !== void 0) conflictState.pendingColour = pendingColour;
    if (pendingLabels !== void 0) conflictState.pendingLabels = pendingLabels;
    conflictState.message = message || "This note changed elsewhere.";
    conflictState.resolving = false;
    conflictState.error = null;
    autosave?.cancel?.({ abortActive: true });
    preserveUnsavedDraft();
    renderConflictState();
  }
  function buildCurrentPayload() {
    return buildUpdatePayload({
      title: modal.querySelector("[data-modal-title]").value,
      body: modal.querySelector("[data-modal-body]").value,
      type: item.type,
      checklistRows: item.type === "Checklist" ? checklist.getRows() : []
    });
  }
  function markChanged(field) {
    editRevision[field] += 1;
    dirtyState[field] = true;
    if (!draftSourceVersion) draftSourceVersion = item?.version ?? null;
  }
  function resetDirtyState() {
    dirtyState.title = false;
    dirtyState.body = false;
    dirtyState.checklist = false;
  }
  function resetEditRevision() {
    editRevision.title = 0;
    editRevision.body = 0;
    editRevision.checklist = 0;
  }
  function applySubmittedRevision(submittedRevision = {}) {
    dirtyState.title = editRevision.title !== submittedRevision.title;
    dirtyState.body = editRevision.body !== submittedRevision.body;
    dirtyState.checklist = editRevision.checklist !== submittedRevision.checklist;
  }
  function hasDirtyChanges() {
    return dirtyState.title || dirtyState.body || dirtyState.checklist;
  }
  function scheduleAutosave() {
    if (isConflictBlocked()) {
      preserveUnsavedDraft();
      return;
    }
    const nextPayload = buildCurrentPayload();
    const nextFingerprint = validationFingerprint(nextPayload);
    if (blockedByValidation && nextFingerprint === lastValidationFingerprint) return;
    if (blockedByValidation) clearValidationBlock();
    autosave?.schedule(nextPayload);
  }
  function clearValidationBlock() {
    blockedByValidation = false;
    lastValidationFingerprint = null;
    renderValidationErrors([]);
  }
  function configureAutosave() {
    autosave?.stop();
    autosave = createAutosave({
      save: saveEditorPayload,
      onSaving: () => {
        if (!isConflictBlocked()) setSaveStatus("Saving…", SaveState.Saving);
      },
      onPersisted: applyPersistedResponse,
      onSaveError: handleEditorError,
      onReconcileError: handleReconcileError
    });
  }
  function renderPin() {
    const pin = modal.querySelector("[data-modal-pin]");
    pin?.classList.toggle("is-active", Boolean(item?.isPinned));
    if (pin) {
      pin.setAttribute("aria-label", item?.isPinned ? "Unpin note" : "Pin note");
      pin.disabled = conflictState.active;
    }
  }
  function applyAuthoritativeItem(updated) {
    item = updated;
    colourPicker?.setValue(updated.colorKey || "");
    labelPicker?.setValue((updated.labels || []).map((label) => label?.name ?? label));
    applyNotebookSurfaceColour(modal.querySelector(".notebook-modal__dialog"), updated.colorKey || "");
    modal.querySelector("[data-modal-title]").value = updated.title || "";
    modal.querySelector("[data-modal-body]").value = updated.body || "";
    modal.querySelector("[data-modal-checklist]").hidden = updated.type !== "Checklist";
    if (updated.type === "Checklist") checklist.reconcileRows(updated.checklistRows || []);
    else checklist.setRows([]);
    resetDirtyState();
    resetEditRevision();
    draftSourceVersion = updated.version ?? null;
    clearValidationBlock();
    renderPin();
  }
  function renderMode() {
    applyAuthoritativeItem(item);
    clearConflictState();
    currentSaveError = null;
    setSaveStatus("", SaveState.Idle);
  }
  function build() {
    modal = cloneNotebookEditorTemplate(document);
    document.body.appendChild(modal);
    const titleInput = requireEditorElement(modal, EditorSelectors.title);
    const bodyInput = requireEditorElement(modal, EditorSelectors.body);
    const checklistRoot = requireEditorElement(modal, EditorSelectors.checklist);
    const pinButton = requireEditorElement(modal, EditorSelectors.pin);
    checklist = createChecklistEditor(checklistRoot, {
      onChange: () => {
        markChanged("checklist");
        scheduleAutosave();
      }
    });
    colourPicker = initNotebookColourPicker(
      requireEditorElement(modal, "[data-notebook-colour-picker]"),
      {
        value: "",
        onSelect: changeColour
      }
    );
    labelPicker = initNotebookLabelPicker(
      requireEditorElement(modal, "[data-notebook-label-picker]"),
      {
        value: [],
        onChange: changeLabels
      }
    );
    modal.addEventListener("click", (event) => {
      if (event.target.closest("[data-close]")) requestClose();
    });
    modal.addEventListener("keydown", trapFocus);
    titleInput.addEventListener("input", () => {
      markChanged("title");
      scheduleAutosave();
    });
    bodyInput.addEventListener("input", () => {
      markChanged("body");
      scheduleAutosave();
    });
    pinButton.addEventListener("click", pinItem);
    modal.querySelector("[data-notebook-retry]")?.addEventListener("click", retrySave);
    modal.querySelector("[data-notebook-reload-application]")?.addEventListener("click", () => window.location.reload());
    modal.querySelector("[data-notebook-sign-in]")?.addEventListener("click", signInAgain);
    modal.querySelector("[data-notebook-copy-unsaved]")?.addEventListener("click", copyUnsavedContent);
    modal.querySelector("[data-modal-discard]")?.addEventListener("click", discardChangesAndClose);
    requireEditorElement(modal, EditorSelectors.useLocal).addEventListener("click", useMyChanges);
    requireEditorElement(modal, EditorSelectors.reloadLatest).addEventListener("click", reloadLatest);
    requireEditorElement(modal, EditorSelectors.copyLocal).addEventListener("click", copyLocalChanges);
  }
  async function saveEditorPayload(data, operation = {}) {
    const submittedRows = item.type === "Checklist" ? structuredCloneSafe(data.checklistRows || []) : [];
    const submittedRevision = { ...editRevision };
    const requestPayload = { ...data, version: item.version };
    const conflictGenerationAtDispatch = Number.isInteger(operation.conflictGenerationAtDispatch) ? operation.conflictGenerationAtDispatch : conflictGeneration;
    assertValidVersion(requestPayload.version);
    const requestOptions = operation.signal ? { signal: operation.signal } : {};
    const response = item.type === "Checklist" ? await NotebookApi.updateChecklist(item.id, requestPayload, requestOptions) : await NotebookApi.updateContent(item.id, requestPayload, requestOptions);
    return {
      response,
      submittedRows,
      submittedRevision,
      operationSequence: operation.sequence ?? ++directSaveSequence,
      conflictGenerationAtDispatch,
      deliberateConflictResolution: operation.deliberateConflictResolution === true
    };
  }
  async function applyPersistedResponse(saveResult) {
    if (shouldIgnoreSaveResult(saveResult, conflictGeneration)) {
      preserveUnsavedDraft();
      return;
    }
    const response = saveResult?.response ?? saveResult;
    const submittedRows = Array.isArray(saveResult?.submittedRows) ? saveResult.submittedRows : [];
    const submittedRevision = saveResult?.submittedRevision ?? { ...editRevision };
    item = requireMutationItem(response);
    if (item.type === "Checklist" && Array.isArray(item.checklistRows)) {
      checklist.reconcileRows(item.checklistRows, submittedRows);
    }
    applySubmittedRevision(submittedRevision);
    clearValidationBlock();
    draftSourceVersion = item.version ?? draftSourceVersion;
    const resolvedConflict = conflictState.active && saveResult?.deliberateConflictResolution === true;
    if (resolvedConflict) clearConflictState();
    if (hasDirtyChanges() || conflictState.active) preserveUnsavedDraft();
    else clearStoredDraft(item?.id);
    if (conflictState.active) renderConflictState();
    else setSaveStatus("Saved", SaveState.Saved);
    await reconcileMutation({
      response,
      board,
      view,
      getCardHtml: NotebookApi.getCardHtml,
      applyCounts: options.applyCounts,
      preservePosition: true,
      showGlobalError: options.showGlobalError,
      renderFailureMessage: "The note was saved, but its card could not refresh. Reload the page.",
      reconcileFailureMessage: "The note was saved, but the board could not refresh. Reload the page."
    });
    if (resolvedConflict && hasDirtyChanges()) scheduleAutosave();
  }
  function handleReconcileError() {
    options.showGlobalError?.("The note was saved, but the board could not refresh. Reload the page.");
    setSaveStatus("Saved", SaveState.Saved);
  }
  function handleEditorError(error) {
    currentSaveError = classifyNotebookSaveError(error);
    if (error?.code === "notebook_request_aborted" && conflictState.active) {
      return { retryable: false };
    }
    if (currentSaveError.kind === "conflict") {
      const pendingServerItem = error?.currentItem ?? (error?.currentVersion ? { ...conflictState.pendingServerItem || item, version: error.currentVersion } : null);
      activateConflict({
        type: ConflictType.VersionConflict,
        pendingServerItem,
        message: conflictState.resolving ? "This note changed again before your changes could be saved." : "This note changed elsewhere."
      });
    } else if (conflictState.active) {
      conflictState.resolving = false;
      conflictState.error = currentSaveError.message;
      preserveUnsavedDraft();
      renderConflictState();
    } else {
      setSaveStatus(currentSaveError.message, currentSaveError.kind);
    }
    if (currentSaveError.kind === "validation") {
      renderValidationErrors(currentSaveError.validationErrors);
      const submittedPayload = buildCurrentPayload();
      blockedByValidation = true;
      lastValidationFingerprint = validationFingerprint(submittedPayload);
      autosave?.cancel?.();
    }
    if (isDevelopment2()) {
      const submittedPayload = buildCurrentPayload();
      console.error("Notebook update failed", {
        noteId: item?.id,
        status: error?.status,
        code: error?.code,
        errors: error?.errors,
        responseText: error?.responseText,
        payload: describeUpdatePayload(submittedPayload)
      });
    }
    return { retryable: isRetryableSaveError(error) && !isConflictBlocked() };
  }
  function isDevelopment2() {
    return document.documentElement.dataset.environment === "Development" || location.hostname === "localhost";
  }
  async function disposeCurrentItem() {
    if (!item || !autosave) return;
    if (isConflictBlocked()) {
      preserveUnsavedDraft();
      autosave.cancel();
    } else {
      await autosave.flush();
    }
    autosave.stop();
    autosave = null;
  }
  function setBackgroundInert(inert) {
    if (shell) shell.inert = inert;
    if (modal) modal.inert = false;
    document.body.classList.toggle("notebook-modal-open", inert);
  }
  function trapFocus(event) {
    if (event.key !== "Tab" || modal.hidden) return;
    const focusable = [...modal.querySelectorAll(focusableSelector)].filter((element) => element.offsetParent !== null);
    if (!focusable.length) {
      event.preventDefault();
      return;
    }
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }
  function preserveUnsavedDraft() {
    if (!item?.id || !modal) return;
    sessionStorage.setItem(`notebook-draft:${item.id}`, JSON.stringify({
      itemId: item.id,
      type: item.type,
      title: modal.querySelector("[data-modal-title]").value,
      body: modal.querySelector("[data-modal-body]").value,
      checklistRows: item.type === "Checklist" ? checklist.getRows() : [],
      sourceVersion: draftSourceVersion || item.version,
      savedAtUtc: (/* @__PURE__ */ new Date()).toISOString()
    }));
  }
  function clearStoredDraft(itemId) {
    if (itemId) sessionStorage.removeItem(`notebook-draft:${itemId}`);
  }
  function readStoredDraft(itemId) {
    const key = `notebook-draft:${itemId}`;
    const raw = sessionStorage.getItem(key);
    if (!raw) return null;
    try {
      return JSON.parse(raw);
    } catch {
      sessionStorage.removeItem(key);
      return null;
    }
  }
  function restoreStoredDraftIfNeeded() {
    const storedDraft = readStoredDraft(item.id);
    if (!storedDraft) return;
    const differs = storedDraft.title !== item.title || storedDraft.body !== item.body || JSON.stringify(storedDraft.checklistRows || []) !== JSON.stringify(item.checklistRows || []);
    if (!differs) {
      clearStoredDraft(item.id);
      return;
    }
    const stale = shouldTreatDraftAsConflict(storedDraft, item);
    const prompt = stale ? "A newer saved version exists. Restore your local changes for review?" : "Restore your unsaved local draft for this note?";
    if (!window.confirm(prompt)) return;
    modal.querySelector("[data-modal-title]").value = storedDraft.title || "";
    modal.querySelector("[data-modal-body]").value = storedDraft.body || "";
    if (item.type === "Checklist") checklist.setRows(storedDraft.checklistRows || []);
    draftSourceVersion = storedDraft.sourceVersion || item.version;
    markChanged("title");
    markChanged("body");
    if (item.type === "Checklist") markChanged("checklist");
    if (stale) {
      activateConflict({
        type: ConflictType.StaleDraft,
        pendingServerItem: item,
        localDraft: storedDraft,
        message: "A newer saved version exists."
      });
    } else {
      scheduleAutosave();
    }
  }
  function signInAgain() {
    preserveUnsavedDraft();
    const returnUrl = window.location.pathname + window.location.search + window.location.hash;
    window.location.assign("/Identity/Account/Login?ReturnUrl=" + encodeURIComponent(returnUrl));
  }
  async function copyUnsavedContent() {
    const copied = await copyLocalChanges();
    if (copied) {
      setSaveStatus("Unsaved note text copied. Sign in again before saving.", currentSaveError?.kind || "session-expired");
    }
  }
  async function writeTextToClipboard(text) {
    if (navigator.clipboard?.writeText) {
      try {
        await navigator.clipboard.writeText(text);
        return true;
      } catch {
      }
    }
    const textarea = document.createElement("textarea");
    textarea.value = text;
    textarea.setAttribute("readonly", "");
    textarea.style.position = "fixed";
    textarea.style.opacity = "0";
    document.body.appendChild(textarea);
    textarea.select();
    let copied = false;
    try {
      copied = document.execCommand?.("copy") === true;
    } finally {
      textarea.remove();
    }
    return copied;
  }
  async function copyLocalChanges() {
    const text = serialiseNotebookContent({
      ...buildCurrentPayload(),
      type: item?.type
    });
    const copied = await writeTextToClipboard(text);
    const copyButton = modal?.querySelector("[data-notebook-copy-local]");
    if (copyButton) {
      const original = copyButton.textContent;
      copyButton.textContent = copied ? "Copied" : "Copy failed";
      window.setTimeout(() => {
        copyButton.textContent = original;
      }, 1500);
    }
    if (!copied && conflictState.active) {
      conflictState.error = "The note could not be copied automatically. Select and copy the text manually.";
      renderConflictState();
    }
    return copied;
  }
  async function retrySave() {
    if (isConflictBlocked()) return;
    const button = modal.querySelector("[data-notebook-retry]");
    button.disabled = true;
    try {
      clearValidationBlock();
      autosave.schedule(buildCurrentPayload());
      await autosave.flush();
    } finally {
      button.disabled = false;
    }
  }
  function closeEditor({ fromHistory = false } = {}) {
    const closedId = item?.id;
    autosave?.stop();
    autosave = null;
    modal.hidden = true;
    setBackgroundInert(false);
    item = null;
    currentSaveError = null;
    clearConflictState();
    clearValidationBlock();
    if (!fromHistory) {
      if (openedByPushState) history.back();
      else history.replaceState(history.state, "", buildNoteUrl(null));
    }
    (board.findCard(closedId) || trigger)?.focus?.();
  }
  async function discardChangesAndClose() {
    if (!window.confirm("Discard unsaved changes and close this note?")) return;
    const itemId = item?.id;
    autosave?.cancel?.();
    clearStoredDraft(itemId);
    resetDirtyState();
    closeEditor();
  }
  async function useMyChanges() {
    if (!conflictState.active || conflictState.resolving || !item) return;
    if (!window.confirm("Save your current changes over the newer saved version?")) return;
    const resolutionGeneration = conflictGeneration;
    conflictState.resolving = true;
    conflictState.error = null;
    renderConflictState();
    preserveUnsavedDraft();
    try {
      const knownLatest = conflictState.pendingServerItem;
      const latest = knownLatest?.version ? knownLatest : await NotebookApi.getItem(item.id);
      if (resolutionGeneration !== conflictGeneration) return;
      item.version = latest.version;
      draftSourceVersion = latest.version;
      currentSaveError = null;
      clearValidationBlock();
      const pendingColour = conflictState.pendingColour;
      const pendingLabels = conflictState.pendingLabels;
      const result = await saveEditorPayload(buildCurrentPayload(), {
        sequence: ++directSaveSequence,
        conflictGenerationAtDispatch: resolutionGeneration,
        deliberateConflictResolution: (pendingColour === null || pendingColour === void 0) && (pendingLabels === null || pendingLabels === void 0)
      });
      await applyPersistedResponse(result);
      if (pendingColour !== null && pendingColour !== void 0) {
        const colourResponse = await NotebookApi.setColour(item.id, pendingColour, item.version);
        item = requireMutationItem(colourResponse, "The colour response did not contain the updated note.");
        draftSourceVersion = item.version;
        colourPicker?.setValue(item.colorKey || "");
        applyNotebookSurfaceColour(modal.querySelector(".notebook-modal__dialog"), item.colorKey || "");
        await reconcileMutation({
          response: colourResponse,
          board,
          view,
          getCardHtml: NotebookApi.getCardHtml,
          applyCounts: options.applyCounts,
          preservePosition: true,
          showGlobalError: options.showGlobalError
        });
        setSaveStatus("Saved", SaveState.Saved);
      }
      if (pendingLabels !== null && pendingLabels !== void 0) {
        const labelsResponse = await NotebookApi.setLabels(item.id, pendingLabels, item.version);
        item = requireMutationItem(labelsResponse, "The labels response did not contain the updated note.");
        draftSourceVersion = item.version;
        labelPicker?.setValue((item.labels || []).map((label) => label?.name ?? label));
        await reconcileMutation({ response: labelsResponse, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts: options.applyCounts, preservePosition: true, showGlobalError: options.showGlobalError });
        setSaveStatus("Saved", SaveState.Saved);
      }
      if (pendingColour !== null && pendingColour !== void 0 || pendingLabels !== null && pendingLabels !== void 0) clearConflictState();
    } catch (error) {
      handleEditorError(error);
    } finally {
      if (conflictState.active) {
        conflictState.resolving = false;
        renderConflictState();
      }
    }
  }
  async function reloadLatest() {
    if (!item || conflictState.resolving) return;
    if (hasDirtyChanges() && !window.confirm("Discard your unsaved changes and load the latest saved version?")) return;
    const button = modal.querySelector("[data-notebook-reload-latest]");
    button.disabled = true;
    try {
      const latest = await NotebookApi.getItem(item.id);
      applyAuthoritativeItem(latest);
      clearStoredDraft(item.id);
      clearConflictState();
      currentSaveError = null;
      setSaveStatus("Saved", SaveState.Saved);
    } catch (error) {
      if (conflictState.active) {
        conflictState.message = error?.message || "Unable to load the latest saved version.";
        renderConflictState();
      } else {
        setSaveStatus(error?.message || "Unable to reload the note.", SaveState.Error);
      }
    } finally {
      button.disabled = false;
    }
  }
  async function changeColour(colorKey, previousColorKey) {
    if (!item || isConflictBlocked()) {
      colourPicker?.setValue(previousColorKey || "");
      return;
    }
    colourPicker?.setBusy(true);
    applyNotebookSurfaceColour(modal.querySelector(".notebook-modal__dialog"), colorKey);
    try {
      await autosave?.flush();
      const response = await NotebookApi.setColour(item.id, colorKey, item.version);
      item = requireMutationItem(response, "The colour response did not contain the updated note.");
      draftSourceVersion = item.version;
      colourPicker?.setValue(item.colorKey || "");
      applyNotebookSurfaceColour(modal.querySelector(".notebook-modal__dialog"), item.colorKey || "");
      await reconcileMutation({
        response,
        board,
        view,
        getCardHtml: NotebookApi.getCardHtml,
        applyCounts: options.applyCounts,
        preservePosition: true,
        showGlobalError: options.showGlobalError,
        reconcileFailureMessage: "The note colour was changed, but the board could not refresh. Reload the page."
      });
      setSaveStatus("Saved", SaveState.Saved);
    } catch (error) {
      if (error?.status === 409) {
        const pendingServerItem = error?.currentItem ?? (error?.currentVersion ? { ...item || {}, version: error.currentVersion } : null);
        activateConflict({
          type: ConflictType.VersionConflict,
          pendingServerItem,
          pendingColour: colorKey,
          message: "This note changed elsewhere. Resolve the conflict before applying the colour."
        });
      } else {
        colourPicker?.setValue(previousColorKey || "");
        applyNotebookSurfaceColour(modal.querySelector(".notebook-modal__dialog"), previousColorKey || "");
        handleEditorError(error);
      }
    } finally {
      colourPicker?.setBusy(false);
    }
  }
  async function changeLabels(labels, previousLabels) {
    if (!item || isConflictBlocked()) {
      labelPicker?.setValue(previousLabels || []);
      return;
    }
    const nextLabels = normaliseLabels(labels);
    labelPicker?.setBusy(true);
    try {
      await autosave?.flush();
      const response = await NotebookApi.setLabels(item.id, nextLabels, item.version);
      item = requireMutationItem(response, "The labels response did not contain the updated note.");
      draftSourceVersion = item.version;
      labelPicker?.setValue((item.labels || []).map((label) => label?.name ?? label));
      await reconcileMutation({
        response,
        board,
        view,
        getCardHtml: NotebookApi.getCardHtml,
        applyCounts: options.applyCounts,
        preservePosition: true,
        showGlobalError: options.showGlobalError,
        reconcileFailureMessage: "The note labels were changed, but the board could not refresh. Reload the page."
      });
      setSaveStatus("Saved", SaveState.Saved);
    } catch (error) {
      if (error?.status === 409) {
        activateConflict({
          type: ConflictType.VersionConflict,
          pendingServerItem: error?.currentItem ?? null,
          pendingLabels: nextLabels,
          message: "This note changed elsewhere. Resolve the conflict before applying labels."
        });
      } else {
        labelPicker?.setValue(previousLabels || []);
        handleEditorError(error);
      }
    } finally {
      labelPicker?.setBusy(false);
    }
  }
  async function pinItem() {
    if (!item || isConflictBlocked()) return;
    const button = modal.querySelector("[data-modal-pin]");
    button.disabled = true;
    try {
      await autosave?.flush();
      const response = await NotebookApi.setPinned(item.id, !item.isPinned, item.version);
      item = requireMutationItem(response, "The pin response did not contain the updated note.");
      draftSourceVersion = item.version;
      renderPin();
      await reconcileMutation({
        response,
        board,
        view,
        getCardHtml: NotebookApi.getCardHtml,
        applyCounts: options.applyCounts,
        preservePosition: false,
        prepend: true,
        showGlobalError: options.showGlobalError,
        reconcileFailureMessage: `The note was ${item.isPinned ? "pinned" : "unpinned"}, but the board could not refresh. Reload the page.`
      });
      setSaveStatus("Saved", SaveState.Saved);
    } catch (error) {
      handleEditorError(error);
    } finally {
      button.disabled = conflictState.active;
    }
  }
  async function open(id, openOptions = {}) {
    if (!modal) build();
    if (item && item.id !== id) await disposeCurrentItem();
    trigger = document.activeElement;
    item = await NotebookApi.getItem(id);
    configureAutosave();
    renderMode();
    restoreStoredDraftIfNeeded();
    modal.hidden = false;
    setBackgroundInert(true);
    modal.querySelector("[data-modal-title]").focus();
    if (openOptions.pushHistory !== false) {
      openedByPushState = true;
      history.pushState({ ...history.state || {}, notebookModal: true, notebookNoteId: id }, "", buildNoteUrl(id));
    } else {
      openedByPushState = false;
    }
  }
  async function requestClose({ fromHistory = false } = {}) {
    if (!item || !modal || modal.hidden) return;
    const closeButton = modal.querySelector("[data-close]:not(.notebook-modal__backdrop)");
    closeButton.disabled = true;
    try {
      if (isConflictBlocked()) {
        preserveUnsavedDraft();
        autosave?.cancel?.();
        closeEditor({ fromHistory });
        return;
      }
      await autosave?.flush();
      closeEditor({ fromHistory });
    } catch (error) {
      handleEditorError(error);
    } finally {
      closeButton.disabled = false;
    }
  }
  function syncExternalUpdate(updated) {
    if (!item || item.id !== updated.id) return;
    if (hasDirtyChanges() || autosave?.hasPending?.()) {
      activateConflict({
        type: ConflictType.ExternalUpdate,
        pendingServerItem: updated,
        message: "This note changed elsewhere."
      });
      return;
    }
    applyAuthoritativeItem(updated);
    clearStoredDraft(updated.id);
    setSaveStatus("", SaveState.Idle);
  }
  return {
    open,
    requestClose,
    isOpen: () => Boolean(item && modal && !modal.hidden),
    syncExternalUpdate
  };
}
function assertValidVersion(version) {
  if (typeof version !== "string" || !guidPattern.test(version)) {
    throw new NotebookApiError("The note version is invalid. Reload the note and try again.", {
      status: 0,
      code: "notebook_invalid_local_version"
    });
  }
}
function buildUpdatePayload({ title, body, type = "Note", checklistRows = [] }) {
  const payload = {
    title: String(title ?? "").trim(),
    body: String(body ?? "").trim()
  };
  if (type === "Checklist") payload.checklistRows = Array.isArray(checklistRows) ? checklistRows : [];
  return payload;
}
function structuredCloneSafe(value) {
  if (typeof structuredClone === "function") return structuredClone(value);
  return JSON.parse(JSON.stringify(value));
}
function validationFingerprint(payload) {
  return JSON.stringify({
    titleLength: typeof payload?.title === "string" ? payload.title.length : null,
    bodyLength: typeof payload?.body === "string" ? payload.body.length : null,
    type: payload?.type,
    priority: payload?.priority,
    reminderAtUtc: payload?.reminderAtUtc,
    labelsIsArray: Array.isArray(payload?.labels),
    checklistRowsIsArray: Array.isArray(payload?.checklistRows)
  });
}
function describeUpdatePayload(payload) {
  return {
    titleType: typeof payload?.title,
    titleLength: typeof payload?.title === "string" ? payload.title.length : null,
    bodyType: typeof payload?.body,
    bodyLength: typeof payload?.body === "string" ? payload.body.length : null,
    typeValue: payload?.type,
    typeValueType: typeof payload?.type,
    priorityValue: payload?.priority,
    priorityValueType: typeof payload?.priority,
    reminderAtUtc: payload?.reminderAtUtc,
    labelsIsArray: Array.isArray(payload?.labels),
    labelsCount: Array.isArray(payload?.labels) ? payload.labels.length : null,
    checklistRowsIsArray: Array.isArray(payload?.checklistRows),
    checklistRowCount: Array.isArray(payload?.checklistRows) ? payload.checklistRows.length : null
  };
}
function renderValidationErrors(hostOrErrors, maybeErrors) {
  const host = Array.isArray(hostOrErrors) ? document.querySelector("[data-notebook-validation-summary]") : hostOrErrors;
  const validationErrors = Array.isArray(hostOrErrors) ? hostOrErrors : maybeErrors;
  if (!host || !Array.isArray(validationErrors) || validationErrors.length === 0) {
    if (host) {
      host.hidden = true;
      host.replaceChildren();
    }
    return;
  }
  const list = document.createElement("ul");
  validationErrors.forEach((error) => {
    const errorItem = document.createElement("li");
    errorItem.textContent = error.message;
    list.appendChild(errorItem);
  });
  host.replaceChildren(list);
  host.hidden = false;
}
function classifyNotebookSaveError(error) {
  if (error instanceof NotebookApiError) {
    if (error.status === 401) return { kind: "session-expired", message: "Your session has expired. Sign in again to save this note.", actions: ["sign-in", "copy", "discard"] };
    if (error.status === 403) return { kind: "forbidden", message: "You are not authorised to edit this note.", actions: ["copy", "discard"] };
    if (error.status === 415) return { kind: "client-version", message: "The editor is using an outdated application file. Reload the page and try again.", actions: ["reload", "discard"] };
    if (error.status === 409) return { kind: "conflict", message: "This note was changed elsewhere." };
    if (error.status === 400) return { kind: "validation", message: getFirstValidationMessage(error), validationErrors: getValidationMessages(error), retryable: false };
    if (error.status >= 500) return { kind: "server", message: error.message || "The note could not be saved because of a server error." };
  }
  return { kind: "network", message: error?.message || "The notebook service could not be reached." };
}
function isRetryableSaveError(error) {
  if (error?.code === "notebook_network_error") return true;
  return [500, 502, 503, 504].includes(error?.status);
}
var ConflictType, SaveState, EditorSelectors, guidPattern;
var init_notebook_editor = __esm({
  "wwwroot/js/notebook/notebook-editor.js"() {
    init_notebook_api();
    init_notebook_autosave();
    init_notebook_checklist_editor();
    init_notebook_reconcile();
    init_notebook_errors();
    init_notebook_colour_picker();
    init_notebook_label_picker();
    ConflictType = Object.freeze({
      StaleDraft: "stale-draft",
      ExternalUpdate: "external-update",
      VersionConflict: "version-conflict"
    });
    SaveState = Object.freeze({
      Idle: "idle",
      Saving: "saving",
      Saved: "saved",
      Error: "error"
    });
    EditorSelectors = Object.freeze({
      template: "#notebook-editor-template",
      editor: "[data-notebook-editor]",
      title: "[data-modal-title]",
      body: "[data-modal-body]",
      checklist: "[data-modal-checklist]",
      pin: "[data-modal-pin]",
      saveState: "[data-notebook-save-state]",
      conflict: "[data-notebook-conflict]",
      conflictMessage: "[data-notebook-conflict-message]",
      useLocal: "[data-notebook-use-local]",
      reloadLatest: "[data-notebook-reload-latest]",
      copyLocal: "[data-notebook-copy-local]"
    });
    guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
  }
});

// wwwroot/js/notebook/notebook-create-editor.js
function toIstIso(localValue) {
  const value = String(localValue || "").trim();
  if (!value) return null;
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value);
  if (!match) return null;
  return `${match[1]}-${match[2]}-${match[3]}T${match[4]}:${match[5]}:00+05:30`;
}
function parseLabels(value) {
  const seen = /* @__PURE__ */ new Set();
  const source = Array.isArray(value) ? value : String(value || "").split(",");
  return source.map((label) => String(label || "").trim().replace(/^#+/, "").trim()).filter((label) => {
    if (!label) return false;
    const key = label.toLocaleLowerCase();
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}
function getCreateTypeUi(type) {
  const safeType = ALLOWED_TYPES.has(type) ? type : "Note";
  const names = {
    Note: "note",
    Checklist: "checklist",
    Reminder: "reminder",
    Idea: "idea",
    Draft: "draft",
    Sticky: "sticky note"
  };
  return {
    type: safeType,
    actionLabel: `Create ${names[safeType]}`,
    titlePlaceholder: safeType === "Reminder" ? "Reminder title" : "Title",
    bodyPlaceholder: safeType === "Reminder" ? "Add notes…" : "Take a note…",
    showChecklist: safeType === "Checklist",
    showBody: safeType !== "Checklist",
    openDetails: safeType === "Reminder"
  };
}
function buildCreatePayload({ type, title, body, reminderLocal, priority, colorKey, labels, isPinned, checklistRows, clientRequestId }) {
  const safeType = ALLOWED_TYPES.has(type) ? type : "Note";
  return {
    title: String(title || "").trim(),
    body: String(body || "").trim() || null,
    type: safeType,
    priority: priority || "Normal",
    reminderAtUtc: safeType === "Reminder" || reminderLocal ? toIstIso(reminderLocal) : null,
    colorKey: colorKey || null,
    isPinned: Boolean(isPinned),
    labels: parseLabels(labels),
    clientRequestId,
    checklistRows: safeType === "Checklist" ? (checklistRows || []).map((row, index) => ({
      id: row.id ?? null,
      clientKey: row.clientKey || null,
      text: String(row.text || "").trim(),
      isDone: Boolean(row.isDone),
      sortOrder: index
    })).filter((row) => row.text.length > 0) : []
  };
}
function initNotebookCreateEditor(board, view, options = {}) {
  const showGlobalError = options.showGlobalError || (() => {
  });
  const applyCounts = options.applyCounts || (() => {
  });
  let modal = null;
  let checklist = null;
  let isOpen = false;
  let isSubmitting = false;
  let isPinned = false;
  let clientRequestId = crypto.randomUUID();
  let colourPicker = null;
  let labelPicker = null;
  function clearCreateQuery() {
    const url = new URL(location.href);
    url.searchParams.delete("mode");
    url.searchParams.delete("type");
    history.replaceState(history.state, "", url);
  }
  function getElements() {
    return {
      title: requireEditorElement(modal, EditorSelectors.title),
      body: requireEditorElement(modal, EditorSelectors.body),
      checklistRoot: requireEditorElement(modal, EditorSelectors.checklist),
      pin: requireEditorElement(modal, EditorSelectors.pin),
      detailsToggle: requireEditorElement(modal, "[data-notebook-create-details-toggle]"),
      details: requireEditorElement(modal, "[data-notebook-create-details]"),
      type: requireEditorElement(modal, "[data-create-type]"),
      reminderField: requireEditorElement(modal, "[data-create-reminder-field]"),
      reminder: requireEditorElement(modal, "[data-create-reminder]"),
      priority: requireEditorElement(modal, "[data-create-priority]"),
      colourPickerRoot: requireEditorElement(modal, "[data-notebook-colour-picker]"),
      labelPickerRoot: requireEditorElement(modal, "[data-notebook-label-picker]"),
      feedback: requireEditorElement(modal, "[data-notebook-create-feedback]"),
      submit: requireEditorElement(modal, "[data-notebook-create-submit]")
    };
  }
  function setFeedback(message = "", isError = false) {
    const { feedback } = getElements();
    feedback.textContent = message;
    feedback.hidden = !message;
    feedback.classList.toggle("is-error", isError);
  }
  function setDetailsExpanded(expanded) {
    const elements = getElements();
    const open2 = Boolean(expanded);
    elements.details.hidden = !open2;
    elements.detailsToggle.setAttribute("aria-expanded", String(open2));
    elements.detailsToggle.classList.toggle("is-expanded", open2);
  }
  function applyType(type, { preserveDetails = false } = {}) {
    const elements = getElements();
    const ui = getCreateTypeUi(type);
    elements.type.value = ui.type;
    elements.checklistRoot.hidden = !ui.showChecklist;
    elements.body.hidden = !ui.showBody;
    elements.reminderField.hidden = ui.type !== "Reminder";
    elements.title.placeholder = ui.titlePlaceholder;
    elements.body.placeholder = ui.bodyPlaceholder;
    elements.submit.textContent = ui.actionLabel;
    modal.dataset.createType = ui.type.toLowerCase();
    if (ui.showChecklist && checklist.getRows().length === 0) checklist.setRows([{ text: "" }]);
    if (!preserveDetails || ui.openDetails) setDetailsExpanded(ui.openDetails);
  }
  function reset(type = "Note") {
    const elements = getElements();
    elements.title.value = "";
    elements.body.value = "";
    elements.reminder.value = "";
    elements.priority.value = "Normal";
    labelPicker?.setValue([]);
    checklist.clear();
    isPinned = false;
    clientRequestId = crypto.randomUUID();
    elements.pin.classList.remove("is-active");
    elements.pin.setAttribute("aria-label", "Pin item");
    colourPicker?.setValue("");
    applyNotebookSurfaceColour(modal.querySelector(".notebook-modal__dialog"), "");
    setFeedback("");
    applyType(type);
  }
  function close() {
    if (!modal) return;
    modal.hidden = true;
    modal.classList.remove("is-create-mode");
    document.body.classList.remove("notebook-modal-open");
    isOpen = false;
    clearCreateQuery();
  }
  async function submit() {
    if (isSubmitting) return;
    const elements = getElements();
    const payload = buildCreatePayload({
      type: elements.type.value,
      title: elements.title.value,
      body: elements.body.value,
      reminderLocal: elements.reminder.value,
      priority: elements.priority.value,
      colorKey: colourPicker?.getValue() || null,
      labels: labelPicker?.getValue() || [],
      isPinned,
      checklistRows: checklist.getRows(),
      clientRequestId
    });
    if (!payload.title && !payload.body && payload.checklistRows.length === 0) {
      setFeedback("Add a title, note or checklist item before creating.", true);
      elements.title.focus();
      return;
    }
    if (payload.type === "Reminder" && !payload.reminderAtUtc) {
      setFeedback("Choose a reminder date and time.", true);
      setDetailsExpanded(true);
      elements.reminder.focus();
      return;
    }
    isSubmitting = true;
    elements.submit.disabled = true;
    setFeedback("Creating…");
    try {
      const response = await NotebookApi.createItem(payload);
      if (!response?.item) {
        throw new NotebookApiError("The create response did not contain the new notebook item.", { code: "notebook_invalid_mutation_response" });
      }
      await reconcileMutation({
        response,
        board,
        view: view || "home",
        getCardHtml: NotebookApi.getCardHtml,
        applyCounts,
        preservePosition: false,
        prepend: true,
        showGlobalError,
        renderFailureMessage: "The item was created, but its card could not be rendered. Reload the page.",
        reconcileFailureMessage: "The item was created, but the board could not refresh. Reload the page."
      });
      close();
      reset("Note");
    } catch (error) {
      setFeedback(error.message || "Unable to create the notebook item.", true);
    } finally {
      isSubmitting = false;
      elements.submit.disabled = false;
    }
  }
  function build() {
    modal = cloneNotebookEditorTemplate(document);
    modal.classList.add("is-create-mode");
    document.body.appendChild(modal);
    const elements = getElements();
    checklist = createChecklistEditor(elements.checklistRoot);
    colourPicker = initNotebookColourPicker(elements.colourPickerRoot, {
      value: "",
      onSelect: (value) => {
        applyNotebookSurfaceColour(modal.querySelector(".notebook-modal__dialog"), value);
      }
    });
    labelPicker = initNotebookLabelPicker(elements.labelPickerRoot, {
      value: [],
      onChange: () => {
      }
    });
    elements.detailsToggle.hidden = false;
    elements.submit.hidden = false;
    modal.querySelector("[data-notebook-save-state]")?.closest(".notebook-save-feedback")?.setAttribute("hidden", "");
    modal.querySelector("[data-notebook-conflict]")?.setAttribute("hidden", "");
    elements.type.addEventListener("change", () => applyType(elements.type.value));
    elements.detailsToggle.addEventListener("click", () => setDetailsExpanded(elements.detailsToggle.getAttribute("aria-expanded") !== "true"));
    elements.pin.addEventListener("click", () => {
      if (isSubmitting) return;
      isPinned = !isPinned;
      elements.pin.classList.toggle("is-active", isPinned);
      elements.pin.setAttribute("aria-label", isPinned ? "Unpin item" : "Pin item");
    });
    elements.submit.addEventListener("click", submit);
    modal.addEventListener("click", (event) => {
      if (event.target.closest("[data-close]")) close();
    });
  }
  function open(type = "Note") {
    if (!modal) build();
    reset(type);
    modal.hidden = false;
    modal.classList.add("is-create-mode");
    document.body.classList.add("notebook-modal-open");
    isOpen = true;
    const elements = getElements();
    queueMicrotask(() => elements.title.focus());
  }
  return { open, close, isOpen: () => isOpen };
}
var ALLOWED_TYPES;
var init_notebook_create_editor = __esm({
  "wwwroot/js/notebook/notebook-create-editor.js"() {
    init_notebook_api();
    init_notebook_checklist_editor();
    init_notebook_reconcile();
    init_notebook_editor();
    init_notebook_colour_picker();
    init_notebook_label_picker();
    ALLOWED_TYPES = /* @__PURE__ */ new Set(["Note", "Checklist", "Reminder", "Idea", "Draft", "Sticky"]);
  }
});

// wwwroot/js/notebook/notebook-label-manager.js
function initNotebookLabelManager(root, options = {}) {
  if (!root) return null;
  const feedback = root.querySelector("[data-label-manager-feedback]");
  const list = root.querySelector("[data-label-manager-list]");
  const empty = root.querySelector("[data-label-manager-empty]");
  const loading = root.querySelector("[data-label-manager-loading]");
  const createInput = root.querySelector("[data-label-manager-create-input]");
  const createButton = root.querySelector("[data-label-manager-create]");
  let busy = false;
  const setFeedback = (text = "", error = false) => {
    feedback.textContent = text;
    feedback.hidden = !text;
    feedback.classList.toggle("is-error", error);
  };
  function render(labels = getNotebookLabelCatalog()) {
    list.innerHTML = "";
    empty.hidden = labels.length !== 0;
    labels.forEach((label) => {
      const row = document.createElement("div");
      row.className = "notebook-label-manager__row";
      row.dataset.labelId = String(label.id);
      row.dataset.originalName = label.name;
      row.innerHTML = `
        <i class="bi bi-tag" aria-hidden="true"></i>
        <input value="${escapeHtml2(label.name)}" maxlength="60" aria-label="Label name ${escapeHtml2(label.name)}" />
        <span title="${label.count} labelled items">${label.count}</span>
        <button type="button" data-label-rename aria-label="Save ${escapeHtml2(label.name)}"><i class="bi bi-check-lg"></i></button>
        <button type="button" data-label-delete aria-label="Delete ${escapeHtml2(label.name)}"><i class="bi bi-trash"></i></button>`;
      list.appendChild(row);
    });
  }
  async function load() {
    loading.hidden = false;
    empty.hidden = true;
    try {
      const labels = await refreshNotebookLabelCatalog();
      render(labels);
    } catch (error) {
      setFeedback(error.message || "Unable to load labels.", true);
    } finally {
      loading.hidden = true;
    }
  }
  async function createLabel() {
    const name = normaliseLabelName(createInput.value);
    if (!name || busy) {
      if (!name) {
        setFeedback("Enter a label name.", true);
        createInput.focus();
      }
      return;
    }
    try {
      busy = true;
      createButton.disabled = true;
      setFeedback("Creating…");
      const result = await NotebookApi.createLabel(name);
      setNotebookLabelCatalog(result.labels || []);
      render(result.labels || []);
      createInput.value = "";
      setFeedback("Label created.");
      options.onCatalogChange?.(result.labels || []);
      createInput.focus();
    } catch (error) {
      setFeedback(error.message || "Unable to create the label.", true);
    } finally {
      busy = false;
      createButton.disabled = false;
    }
  }
  const open = async () => {
    root.hidden = false;
    document.body.classList.add("notebook-modal-open");
    setFeedback("");
    await load();
    queueMicrotask(() => createInput.focus());
  };
  const close = () => {
    root.hidden = true;
    document.body.classList.remove("notebook-modal-open");
    setFeedback("");
  };
  createButton.addEventListener("click", createLabel);
  createInput.addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      event.preventDefault();
      createLabel();
    }
    if (event.key === "Escape" && createInput.value) {
      createInput.value = "";
    }
  });
  root.addEventListener("click", async (event) => {
    if (event.target.closest("[data-label-manager-close]")) {
      close();
      return;
    }
    const row = event.target.closest("[data-label-id]");
    if (!row || busy) return;
    const id = Number(row.dataset.labelId);
    const input = row.querySelector("input");
    if (event.target.closest("[data-label-rename]")) {
      const name = normaliseLabelName(input.value);
      if (!name) {
        setFeedback("Enter a label name.", true);
        input.focus();
        return;
      }
      try {
        busy = true;
        setFeedback("Saving…");
        const result = await NotebookApi.renameLabel(id, name);
        setNotebookLabelCatalog(result.labels || []);
        render(result.labels || []);
        setFeedback("Label updated.");
        options.onCatalogChange?.(result.labels || []);
      } catch (error) {
        input.value = row.dataset.originalName;
        setFeedback(error.message || "Unable to rename the label.", true);
      } finally {
        busy = false;
      }
    }
    if (event.target.closest("[data-label-delete]")) {
      if (!confirm(`Delete label “${input.value}” from all notes? Notes will not be deleted.`)) return;
      try {
        busy = true;
        setFeedback("Deleting…");
        const result = await NotebookApi.deleteLabel(id);
        setNotebookLabelCatalog(result.labels || []);
        render(result.labels || []);
        setFeedback("Label deleted.");
        options.onCatalogChange?.(result.labels || []);
        const currentTag = new URL(location.href).searchParams.get("tag");
        if (currentTag && currentTag.toLocaleLowerCase() === row.dataset.originalName.toLocaleLowerCase()) {
          history.replaceState({}, "", "/Notebook?view=labels");
        }
      } catch (error) {
        setFeedback(error.message || "Unable to delete the label.", true);
      } finally {
        busy = false;
      }
    }
  });
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !root.hidden) close();
  });
  render(getNotebookLabelCatalog());
  return { open, close, reload: load, render };
}
function escapeHtml2(value) {
  return String(value).replace(/[&<>'"]/g, (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" })[character]);
}
var init_notebook_label_manager = __esm({
  "wwwroot/js/notebook/notebook-label-manager.js"() {
    init_notebook_api();
    init_notebook_label_picker();
  }
});

// wwwroot/js/notebook/notebook-app.js
function renderNotebookLabelNavigation(shell, labels = []) {
  if (!shell) return;
  const safeLabels = Array.isArray(labels) ? labels : [];
  const rail = shell.querySelector("[data-notebook-label-rail]");
  if (rail) {
    rail.innerHTML = "";
    safeLabels.slice(0, 20).forEach((label) => {
      const link = document.createElement("a");
      link.href = `/Notebook?view=labels&tag=${encodeURIComponent(label.name)}`;
      const currentTag = new URL(location.href).searchParams.get("tag");
      if (currentTag && currentTag.toLocaleLowerCase() === String(label.name).toLocaleLowerCase()) link.classList.add("is-active");
      link.innerHTML = `<i class="bi bi-tag"></i><span>${escapeLabelHtml(label.name)}</span><b>${Number(label.count || 0)}</b>`;
      rail.appendChild(link);
    });
  }
  const directory = shell.querySelector("[data-notebook-label-directory-list]");
  if (directory) {
    directory.innerHTML = "";
    safeLabels.forEach((label) => {
      const link = document.createElement("a");
      link.href = `/Notebook?view=labels&tag=${encodeURIComponent(label.name)}`;
      link.innerHTML = `<i class="bi bi-tag"></i>${escapeLabelHtml(label.name)} <span>${Number(label.count || 0)}</span>`;
      directory.appendChild(link);
    });
  }
  const empty = shell.querySelector("[data-notebook-label-directory-empty]");
  if (empty) empty.hidden = safeLabels.length !== 0;
  shell.querySelectorAll('[data-notebook-count="labels"]').forEach((element) => {
    element.textContent = String(safeLabels.length);
  });
}
function parseCardLabels(card) {
  try {
    return JSON.parse(card?.dataset?.labels || "[]");
  } catch {
    return [];
  }
}
function escapeLabelHtml(value) {
  return String(value || "").replace(/[&<>'"]/g, (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" })[character]);
}
function initNotebookApp() {
  const shell = document.querySelector(".notebook-shell");
  if (!shell) return;
  const view = new URL(location.href).searchParams.get("view") || "home";
  const board = createNotebookBoard(shell);
  let composer;
  const globalError = document.querySelector("[data-notebook-global-error]");
  const globalErrorText = document.querySelector("[data-notebook-global-error-text]");
  const showGlobalError = (message) => {
    if (!globalError || !globalErrorText) {
      shell.dataset.error = message || "Notebook action failed.";
      return;
    }
    globalErrorText.textContent = message || "Notebook action failed.";
    globalError.hidden = false;
  };
  const applyCounts = (counts) => {
    if (!counts) return;
    Object.entries(counts).forEach(([key, value]) => shell.querySelectorAll(`[data-notebook-count="${key}"]`).forEach((el) => {
      el.textContent = String(value);
    }));
  };
  const refreshCounts = async () => applyCounts(await NotebookApi.getCounts());
  const editor = initNotebookEditor(board, view, { shell, showGlobalError, applyCounts });
  getNotebookLabelCatalog(document);
  const createEditor = initNotebookCreateEditor(board, view, { shell, showGlobalError, applyCounts });
  const labelManager = initNotebookLabelManager(document.querySelector("[data-notebook-label-manager]"), {
    showGlobalError,
    onCatalogChange: (labels) => renderNotebookLabelNavigation(shell, labels)
  });
  document.querySelectorAll("[data-open-label-manager]").forEach((button) => button.addEventListener("click", () => labelManager?.open()));
  let activeLabelCard = null;
  const cardLabelPicker = initNotebookLabelPicker(
    document.querySelector("[data-notebook-card-label-host] [data-notebook-label-picker]"),
    {
      value: [],
      onError: (error) => showGlobalError(error?.message || "Unable to create the label."),
      onChange: async (labels) => {
        const card = activeLabelCard;
        if (!card?.dataset?.noteId) return;
        const apply = async (version) => {
          const response = await NotebookApi.setLabels(card.dataset.noteId, labels, version);
          const updated = requireMutationItem(response);
          updateCardConcurrencyState(card, updated);
          await reconcileMutation({
            response,
            board,
            view,
            getCardHtml: NotebookApi.getCardHtml,
            applyCounts,
            preservePosition: true,
            showGlobalError,
            existingCard: card
          });
          editor.syncExternalUpdate?.(updated);
          activeLabelCard = shell.querySelector(`[data-note-id="${updated.id}"]`) ?? card;
          const catalogue = await refreshNotebookLabelCatalog();
          renderNotebookLabelNavigation(shell, catalogue);
        };
        try {
          await apply(card.dataset.version);
        } catch (error) {
          if (error?.status === 409 && window.confirm("This note changed elsewhere. Apply these labels to the latest saved version?")) {
            const latest = error.currentItem ?? await NotebookApi.getItem(card.dataset.noteId);
            await apply(latest.version);
            return;
          }
          throw error;
        }
      }
    }
  );
  document.addEventListener("notebook:labels-changed", (event) => {
    renderNotebookLabelNavigation(shell, event.detail?.labels || getNotebookLabelCatalog());
  });
  renderNotebookLabelNavigation(shell, getNotebookLabelCatalog());
  composer = initNotebookComposer(shell.querySelector("[data-notebook-composer]"), board, view, { showGlobalError, applyCounts });
  document.querySelector("[data-notebook-global-error-close]")?.addEventListener("click", () => {
    globalError.hidden = true;
    globalErrorText.textContent = "";
  });
  const storageKey = "notebook.boardView";
  const viewButtons = [...shell.querySelectorAll("[data-notebook-view]")];
  function applyBoardView(next) {
    const selected = next === "list" ? "list" : "grid";
    shell.dataset.boardView = selected;
    localStorage.setItem(storageKey, selected);
    viewButtons.forEach((button) => {
      const active = button.dataset.notebookView === selected;
      button.classList.toggle("is-active", active);
      button.setAttribute("aria-pressed", String(active));
    });
  }
  viewButtons.forEach((button) => button.addEventListener("click", () => applyBoardView(button.dataset.notebookView)));
  applyBoardView(localStorage.getItem(storageKey) || shell.dataset.boardView || "grid");
  document.addEventListener("click", async (event) => {
    const cardColourToggle = event.target.closest(".notebook-card [data-colour-picker-toggle]");
    if (cardColourToggle) {
      event.preventDefault();
      event.stopPropagation();
      const picker = cardColourToggle.closest("[data-notebook-colour-picker]");
      const popover = picker?.querySelector("[data-colour-picker-popover]");
      if (!picker || !popover) return;
      const shouldOpen = popover.hidden;
      closeNotebookColourPickers(document, shouldOpen ? picker : null);
      popover.hidden = !shouldOpen;
      cardColourToggle.setAttribute("aria-expanded", String(shouldOpen));
      if (shouldOpen) popover.querySelector(".is-selected,[data-colour-choice]")?.focus?.();
      return;
    }
    const cardColourChoice = event.target.closest(".notebook-card [data-colour-choice]");
    if (cardColourChoice) {
      event.preventDefault();
      event.stopPropagation();
      const card2 = cardColourChoice.closest("[data-note-id]");
      if (!card2) return;
      const picker = cardColourChoice.closest("[data-notebook-colour-picker]");
      const colorKey = normaliseNotebookColour(cardColourChoice.dataset.colourChoice);
      cardColourChoice.disabled = true;
      try {
        const response = await NotebookApi.setColour(card2.dataset.noteId, colorKey, card2.dataset.version);
        const updated = requireMutationItem(response);
        updateCardConcurrencyState(card2, updated);
        await reconcileMutation({
          response,
          board,
          view,
          getCardHtml: NotebookApi.getCardHtml,
          applyCounts,
          preservePosition: true,
          showGlobalError,
          existingCard: card2,
          reconcileFailureMessage: "The note colour was changed, but the board could not refresh. Reload the page."
        });
        editor.syncExternalUpdate?.(updated);
      } catch (error) {
        if (error?.status === 409 && window.confirm("This note changed elsewhere. Apply the selected colour to the latest saved version?")) {
          try {
            const latest = error.currentItem ?? await NotebookApi.getItem(card2.dataset.noteId);
            const retryResponse = await NotebookApi.setColour(card2.dataset.noteId, colorKey, latest.version);
            const updated = requireMutationItem(retryResponse);
            await reconcileMutation({
              response: retryResponse,
              board,
              view,
              getCardHtml: NotebookApi.getCardHtml,
              applyCounts,
              preservePosition: true,
              showGlobalError,
              existingCard: card2
            });
            editor.syncExternalUpdate?.(updated);
          } catch (retryError) {
            showGlobalError(retryError.message || "Unable to change the note colour.");
          }
        } else {
          showGlobalError(error.message || "Unable to change the note colour.");
        }
      } finally {
        cardColourChoice.disabled = false;
        closeNotebookColourPickers(document);
      }
      return;
    }
    if (!event.target.closest("[data-notebook-colour-picker]")) closeNotebookColourPickers(document);
    const createTrigger = event.target.closest("[data-notebook-create-type]");
    if (createTrigger) {
      event.preventDefault();
      createEditor.open(createTrigger.dataset.notebookCreateType || "Note");
      return;
    }
    const action = closestAction(event);
    if (!action) return;
    const card = action.closest("[data-note-id]");
    const id = card?.dataset.noteId;
    if (action.dataset.action === "label-note" && card) {
      event.preventDefault();
      action.closest("details")?.removeAttribute("open");
      activeLabelCard = card;
      cardLabelPicker?.configure({ value: parseCardLabels(card) });
      cardLabelPicker?.open(action);
      return;
    }
    if (action.dataset.action === "open-note" && id) {
      event.preventDefault();
      try {
        await editor.open(id);
      } catch (error) {
        showGlobalError(error.message || "Unable to open the note.");
      }
    }
    if (action.dataset.action === "toggle-checklist" && card) {
      event.preventDefault();
      action.disabled = true;
      try {
        const response = await NotebookApi.toggleChecklistItem(card.dataset.noteId, action.dataset.rowId, action.dataset.isDone !== "true", card.dataset.version);
        const updated = requireMutationItem(response);
        updateCardConcurrencyState(card, updated);
        await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: true, showGlobalError, existingCard: card });
        editor.syncExternalUpdate?.(updated);
      } catch (error) {
        showGlobalError(error.message || "Checklist update failed.");
      } finally {
        action.disabled = false;
      }
    }
    if (["pin-note", "archive-note", "complete-note", "reopen-note", "restore-note", "duplicate-note", "delete-note", "convert-note"].includes(action.dataset.action) && id) {
      event.preventDefault();
      action.disabled = true;
      try {
        if (action.dataset.action === "pin-note") {
          const response = await NotebookApi.setPinned(id, card.dataset.isPinned !== "true", card.dataset.version);
          const updated = requireMutationItem(response);
          updateCardConcurrencyState(card, updated);
          await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: false, prepend: true, showGlobalError, existingCard: card, reconcileFailureMessage: `The note was ${updated.isPinned ? "pinned" : "unpinned"}, but the board could not refresh. Reload the page.` });
        }
        if (action.dataset.action === "archive-note") {
          const response = await NotebookApi.archiveItem(id, card.dataset.version);
          board.removeCard(id);
          applyCounts(response?.counts);
        }
        if (action.dataset.action === "complete-note") {
          const response = await NotebookApi.completeItem(id, card.dataset.version);
          board.removeCard(id);
          applyCounts(response?.counts);
        }
        if (action.dataset.action === "reopen-note") {
          const response = await NotebookApi.reopenItem(id, card.dataset.version);
          board.removeCard(id);
          applyCounts(response?.counts);
        }
        if (action.dataset.action === "restore-note") {
          const response = await NotebookApi.restoreItem(id, card.dataset.version);
          const updated = requireMutationItem(response);
          updateCardConcurrencyState(card, updated);
          if (view === "archive" || view === "archived") {
            board.removeCard(id);
            applyCounts(response?.counts);
          } else {
            await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: false, prepend: true, showGlobalError, existingCard: card });
          }
        }
        if (action.dataset.action === "duplicate-note") {
          const response = await NotebookApi.duplicateItem(id);
          await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: false, prepend: true, showGlobalError });
        }
        if (action.dataset.action === "delete-note") {
          const response = await NotebookApi.deleteItem(id, card.dataset.version);
          board.removeCard(response?.removedItemId || id);
          applyCounts(response?.counts);
        }
        if (action.dataset.action === "convert-note") {
          const response = action.dataset.convertTo === "Checklist" ? await NotebookApi.showCheckboxes(id, card.dataset.version) : await NotebookApi.hideCheckboxes(id, card.dataset.version);
          const converted = requireMutationItem(response);
          updateCardConcurrencyState(card, converted);
          await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts, preservePosition: true, showGlobalError, existingCard: card });
        }
      } catch (error) {
        showGlobalError(error.message || "Notebook action failed.");
      } finally {
        action.disabled = false;
      }
    }
  });
  document.addEventListener("keydown", async (event) => {
    if (event.key !== "Escape") return;
    if (createEditor.isOpen()) {
      event.preventDefault();
      createEditor.close();
      return;
    }
    if (editor.isOpen()) {
      event.preventDefault();
      await editor.requestClose();
      return;
    }
    if (composer?.isOpen()) {
      event.preventDefault();
      await composer.close();
    }
  });
  window.addEventListener("popstate", async () => {
    try {
      const id = new URL(location.href).searchParams.get("note");
      id ? await editor.open(id, { pushHistory: false }) : await editor.requestClose({ fromHistory: true });
    } catch (error) {
      showGlobalError(error.message || "Unable to open the note.");
    }
  });
  const initialUrl = new URL(location.href);
  if (initialUrl.searchParams.get("mode") === "new") {
    createEditor.open(initialUrl.searchParams.get("type") || "Note");
  }
  const directId = initialUrl.searchParams.get("note");
  if (directId) editor.open(directId, { pushHistory: false }).catch((error) => {
    showGlobalError(error.message || "Unable to open the note.");
    const url = new URL(location.href);
    url.searchParams.delete("note");
    history.replaceState(history.state, "", url);
  });
}
var init_notebook_app = __esm({
  "wwwroot/js/notebook/notebook-app.js"() {
    init_notebook_utils();
    init_notebook_api();
    init_notebook_board();
    init_notebook_composer();
    init_notebook_editor();
    init_notebook_create_editor();
    init_notebook_reconcile();
    init_notebook_colour_picker();
    init_notebook_label_manager();
    init_notebook_label_picker();
  }
});

// wwwroot/js/pages/notebook-index.js
var require_notebook_index = __commonJS({
  "wwwroot/js/pages/notebook-index.js"() {
    init_notebook_app();
    function initLegacyNotebookEnhancements() {
      document.querySelectorAll("[data-autoresize]").forEach((textarea) => {
        const resize = () => {
          textarea.style.height = "auto";
          textarea.style.height = `${textarea.scrollHeight}px`;
        };
        textarea.addEventListener("input", resize);
        resize();
      });
      const typeSelect = document.querySelector("[data-notebook-type-select]");
      const fieldGroups = Array.from(document.querySelectorAll("[data-notebook-type-fields]"));
      const normalize = (value) => (value || "").toString().trim().toLowerCase();
      const selectedTypeName = () => normalize(typeSelect?.options[typeSelect.selectedIndex]?.text || typeSelect?.value);
      const setGroupEnabled = (group, isEnabled) => {
        group.hidden = !isEnabled;
        group.querySelectorAll("input, select, textarea, button").forEach((control) => {
          control.disabled = !isEnabled;
        });
      };
      const updateFields = () => {
        const selected = selectedTypeName();
        fieldGroups.forEach((group) => {
          const allowedTypes = (group.dataset.notebookTypeFields || "").split(",").map(normalize);
          setGroupEnabled(group, allowedTypes.includes(selected));
        });
      };
      if (typeSelect && fieldGroups.length) {
        typeSelect.addEventListener("change", updateFields);
        updateFields();
      }
      document.querySelectorAll("[data-submit-on-change]").forEach((input) => input.addEventListener("change", () => input.form?.submit()));
      const root = document.querySelector(".notebook-shell");
      const saved = localStorage.getItem("notebook-board-view") || "grid";
      root?.setAttribute("data-board-view", saved);
      document.querySelectorAll("[data-notebook-view]").forEach((button) => button.addEventListener("click", () => {
        localStorage.setItem("notebook-board-view", button.dataset.notebookView);
        root?.setAttribute("data-board-view", button.dataset.notebookView);
      }));
    }
    document.addEventListener("DOMContentLoaded", () => {
      initLegacyNotebookEnhancements();
      initNotebookApp();
    });
  }
});
export default require_notebook_index();
//# sourceMappingURL=notebook-index.bundle.js.map
