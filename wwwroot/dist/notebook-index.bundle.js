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
        method: context.method
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
      constructor(message, { status = 0, code = null, errors = null, responseText = null, url = null, method = null, cause = null } = {}) {
        super(message);
        this.name = "NotebookApiError";
        this.status = status;
        this.code = code;
        this.errors = errors;
        this.responseText = responseText;
        this.url = url;
        this.method = method;
        this.cause = cause;
      }
    };
    NotebookApi = {
      createItem: (payload) => request("/api/notebook/items", jsonRequestOptions("POST", payload)),
      getItem: (id) => request(`/api/notebook/items/${encodeURIComponent(id)}`),
      updateItem: (id, payload) => request(`/api/notebook/items/${encodeURIComponent(id)}`, jsonRequestOptions("PATCH", payload)),
      updateContent: (id, payload) => request(`/api/notebook/items/${encodeURIComponent(id)}/content`, jsonRequestOptions("PATCH", payload)),
      updateChecklist: (id, payload) => request(`/api/notebook/items/${encodeURIComponent(id)}/checklist`, jsonRequestOptions("PUT", payload)),
      setPinned: (id, isPinned, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/pin`, jsonRequestOptions("POST", { isPinned, version })),
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
  const refreshSectionVisibility = () => {
    ["pinned", "others"].forEach((name) => {
      const section = root.querySelector(`[data-notebook-section="${name}"]`);
      const board = root.querySelector(`[data-notebook-board="${name}"]`);
      if (!section || !board) return;
      const count = board.querySelectorAll("[data-note-id]").length;
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
  return { findCard, getSection, getBoard, replaceCard, insertCard, upsertCard, removeCard, refreshSectionVisibility, refreshEmptyState, htmlToCardElement };
}
var init_notebook_board = __esm({
  "wwwroot/js/notebook/notebook-board.js"() {
    init_notebook_errors();
  }
});

// wwwroot/js/notebook/notebook-checklist-editor.js
function createChecklistEditor(root, options = {}) {
  const maxLength = options.maxLength || 500;
  const notify = () => options.onChange?.();
  function rowTemplate(row = {}) {
    const wrapper = document.createElement("div");
    wrapper.className = "notebook-checklist-row";
    wrapper.dataset.checklistRow = "";
    wrapper.dataset.rowId = row.id ?? "";
    wrapper.innerHTML = `<input type="checkbox" data-checklist-done><input type="text" data-checklist-text maxlength="${maxLength}" placeholder="List item"><button type="button" data-checklist-remove aria-label="Remove checklist item">\xD7</button>`;
    wrapper.querySelector("[data-checklist-done]").checked = Boolean(row.isDone);
    wrapper.querySelector("[data-checklist-text]").value = row.text || "";
    return wrapper;
  }
  function addRow(afterElement = null, row = {}) {
    const el = rowTemplate(row);
    afterElement ? afterElement.after(el) : root.append(el);
    return el;
  }
  function removeRow(element) {
    const prev = element.previousElementSibling;
    element.remove();
    (prev?.querySelector("[data-checklist-text]") || root.querySelector("[data-checklist-text]"))?.focus();
    notify();
  }
  function setRows(rows) {
    root.replaceChildren();
    (rows?.length ? rows : [{ text: "" }]).forEach((row) => addRow(null, row));
  }
  function getRows() {
    return [...root.querySelectorAll("[data-checklist-row]")].map((row, index) => ({ id: parseNullableInt(row.dataset.rowId), text: row.querySelector("[data-checklist-text]").value.trim(), isDone: row.querySelector("[data-checklist-done]").checked, sortOrder: (index + 1) * 1e3 })).filter((row) => row.text.length > 0);
  }
  root.addEventListener("input", (event) => {
    if (event.target.matches("[data-checklist-text]")) notify();
  });
  root.addEventListener("change", (event) => {
    if (event.target.matches("[data-checklist-done]")) notify();
  });
  root.addEventListener("click", (event) => {
    const button = event.target.closest("[data-checklist-remove]");
    if (button) removeRow(button.closest("[data-checklist-row]"));
  });
  root.addEventListener("keydown", (event) => {
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
  });
  return { setRows, getRows, addRow, removeRow, focusFirst: () => root.querySelector("[data-checklist-text]")?.focus(), clear: () => setRows([]), destroy: () => root.replaceChildren() };
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
    setStatus("Saving\u2026");
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
  async function runLoop() {
    if (activePromise) return activePromise;
    activePromise = (async () => {
      while (!stopped && dirty && latestPayload) {
        const payload = latestPayload;
        dirty = false;
        await onSaving?.();
        let result;
        try {
          result = await save(payload);
        } catch (error) {
          const disposition = await (onSaveError || onError)?.(error);
          dirty = disposition?.retryable === true;
          if (!dirty) latestPayload = null;
          throw error;
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
  function cancel() {
    if (timer) {
      window.clearTimeout(timer);
      timer = null;
    }
    dirty = false;
    latestPayload = null;
  }
  function stop() {
    stopped = true;
    cancel();
  }
  return { schedule, flush, cancel, stop, hasPending: () => Boolean(timer || activePromise || dirty) };
}
var init_notebook_autosave = __esm({
  "wwwroot/js/notebook/notebook-autosave.js"() {
  }
});

// wwwroot/js/notebook/notebook-editor.js
function initNotebookEditor(board, view, options = {}) {
  let modal, item, autosave, checklist, trigger, openedByPushState = false, currentSaveError = null, pendingExternalUpdate = null;
  let blockedByValidation = false;
  let lastValidationFingerprint = null;
  const shell = options.shell || document.querySelector(".notebook-shell");
  const dirtyState = { title: false, body: false, checklist: false };
  const buildNoteUrl = (id) => {
    const url = new URL(location.href);
    id ? url.searchParams.set("note", id) : url.searchParams.delete("note");
    return url;
  };
  const focusableSelector = 'a[href],button:not([disabled]),textarea:not([disabled]),input:not([disabled]),select:not([disabled]),[tabindex]:not([tabindex="-1"])';
  function setStatus(text, state = "idle") {
    const el = modal?.querySelector("[data-notebook-save-state]");
    if (el) {
      el.textContent = text || "";
      el.dataset.state = state;
    }
    const retry = modal?.querySelector("[data-notebook-retry]");
    const reload = modal?.querySelector("[data-notebook-reload-latest]");
    const discard = modal?.querySelector("[data-modal-discard]");
    const signIn = modal?.querySelector("[data-notebook-sign-in]");
    const copy = modal?.querySelector("[data-notebook-copy-unsaved]");
    if (retry) retry.hidden = !["network", "server", "error"].includes(state);
    if (reload) {
      reload.hidden = !["conflict", "client-version"].includes(state);
      reload.textContent = state === "client-version" ? "Reload application" : "Reload latest";
    }
    if (discard) discard.hidden = !["network", "server", "error", "conflict", "client-version", "session-expired", "forbidden"].includes(state);
    if (signIn) signIn.hidden = state !== "session-expired";
    if (copy) copy.hidden = !["session-expired", "forbidden", "network", "server", "error"].includes(state);
  }
  function buildCurrentPayload() {
    return buildUpdatePayload({
      title: modal.querySelector("[data-modal-title]").value,
      body: modal.querySelector("[data-modal-body]").value,
      type: item.type,
      checklistRows: item.type === "Checklist" ? checklist.getRows() : []
    });
  }
  function scheduleAutosave() {
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
    autosave = createAutosave({ save: saveEditorPayload, onSaving: () => setStatus("Saving\u2026", "saving"), onPersisted: applyPersistedResponse, onSaveError: handleEditorError, onReconcileError: handleReconcileError });
  }
  function renderPin() {
    const pin = modal.querySelector("[data-modal-pin]");
    pin?.classList.toggle("is-active", !!item?.isPinned);
    if (pin) pin.setAttribute("aria-label", item?.isPinned ? "Unpin note" : "Pin note");
  }
  function renderMode() {
    modal.querySelector("[data-modal-title]").value = item.title || "";
    modal.querySelector("[data-modal-body]").value = item.body || "";
    checklist.setRows(item.checklistRows || []);
    modal.querySelector("[data-modal-checklist]").hidden = item.type !== "Checklist";
    dirtyState.title = dirtyState.body = dirtyState.checklist = false;
    pendingExternalUpdate = null;
    clearValidationBlock();
    renderPin();
  }
  function build() {
    modal = document.createElement("div");
    modal.className = "notebook-modal";
    modal.hidden = true;
    modal.setAttribute("role", "dialog");
    modal.setAttribute("aria-modal", "true");
    modal.setAttribute("aria-labelledby", "notebook-modal-title");
    modal.innerHTML = '<div class="notebook-modal__backdrop" data-close></div><section class="notebook-modal__dialog"><header><input id="notebook-modal-title" data-modal-title class="notebook-modal__title" maxlength="220"><button type="button" class="notebook-action-icon" data-modal-pin aria-label="Pin note"><i class="bi bi-pin-angle"></i></button></header><textarea data-modal-body class="notebook-modal__body" maxlength="20000" placeholder="Take a note\u2026"></textarea><div class="notebook-editor__validation" data-notebook-validation-summary role="alert" hidden></div><div data-modal-checklist class="notebook-checklist-editor" hidden></div><div class="notebook-save-feedback"><span class="notebook-modal__save-state" data-notebook-save-state aria-live="polite"></span><button type="button" data-notebook-retry hidden>Retry</button><button type="button" data-notebook-reload-latest hidden>Reload latest</button><button type="button" data-notebook-sign-in hidden>Sign in again</button><button type="button" data-notebook-copy-unsaved hidden>Copy note text</button><button type="button" data-modal-discard hidden>Discard changes</button></div><footer><button type="button" class="btn btn-sm btn-link" data-close aria-label="Close note editor">Close</button></footer></section>';
    document.body.appendChild(modal);
    checklist = createChecklistEditor(modal.querySelector("[data-modal-checklist]"), { onChange: () => {
      dirtyState.checklist = true;
      scheduleAutosave();
    } });
    modal.addEventListener("click", (e) => {
      if (e.target.matches("[data-close]")) requestClose();
    });
    modal.addEventListener("keydown", trapFocus);
    modal.querySelector("[data-modal-title]").addEventListener("input", () => {
      dirtyState.title = true;
      scheduleAutosave();
    });
    modal.querySelector("[data-modal-body]").addEventListener("input", () => {
      dirtyState.body = true;
      scheduleAutosave();
    });
    modal.querySelector("[data-modal-pin]").addEventListener("click", pinItem);
    modal.querySelector("[data-notebook-retry]")?.addEventListener("click", retrySave);
    modal.querySelector("[data-notebook-reload-latest]")?.addEventListener("click", reloadLatest);
    modal.querySelector("[data-notebook-sign-in]")?.addEventListener("click", signInAgain);
    modal.querySelector("[data-notebook-copy-unsaved]")?.addEventListener("click", copyUnsavedContent);
    modal.querySelector("[data-modal-discard]")?.addEventListener("click", discardChangesAndClose);
  }
  async function saveEditorPayload(data) {
    const requestPayload = { ...data, version: item.version };
    assertValidVersion(requestPayload.version);
    return item.type === "Checklist" ? NotebookApi.updateChecklist(item.id, requestPayload) : NotebookApi.updateContent(item.id, requestPayload);
  }
  async function applyPersistedResponse(response) {
    item = requireMutationItem(response);
    dirtyState.title = dirtyState.body = dirtyState.checklist = false;
    clearValidationBlock();
    clearStoredDraft(item?.id);
    setStatus("Saved", "saved");
    await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts: options.applyCounts, preservePosition: true, showGlobalError: options.showGlobalError, renderFailureMessage: "The note was saved, but its card could not refresh. Reload the page.", reconcileFailureMessage: "The note was saved, but the board could not refresh. Reload the page." });
  }
  function handleReconcileError() {
    options.showGlobalError?.("The note was saved, but the board could not refresh. Reload the page.");
    setStatus("Saved", "saved");
  }
  function classifySaveError(error) {
    return classifyNotebookSaveError(error);
  }
  function handleEditorError(error) {
    currentSaveError = classifySaveError(error);
    setStatus(currentSaveError.message, currentSaveError.kind);
    if (currentSaveError.kind === "validation") {
      setStatus(currentSaveError.message, "validation");
      renderValidationErrors(currentSaveError.validationErrors);
      const submittedPayload = buildCurrentPayload();
      blockedByValidation = true;
      lastValidationFingerprint = validationFingerprint(submittedPayload);
      autosave?.cancel?.();
    }
    if (isDevelopment2()) {
      const submittedPayload = buildCurrentPayload();
      console.error("Notebook update failed", { noteId: item?.id, status: error?.status, code: error?.code, errors: error?.errors, responseText: error?.responseText, payload: describeUpdatePayload(submittedPayload) });
    }
    return { retryable: isRetryableSaveError(error) };
  }
  function isDevelopment2() {
    return document.documentElement.dataset.environment === "Development" || location.hostname === "localhost";
  }
  async function disposeCurrentItem() {
    if (!item || !autosave) return;
    await autosave.flush();
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
    const focusable = [...modal.querySelectorAll(focusableSelector)].filter((el) => el.offsetParent !== null);
    if (!focusable.length) {
      event.preventDefault();
      return;
    }
    const first = focusable[0], last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }
  function preserveUnsavedDraft() {
    if (!item?.id) return;
    sessionStorage.setItem(`notebook-draft:${item.id}`, JSON.stringify({ title: modal.querySelector("[data-modal-title]").value, body: modal.querySelector("[data-modal-body]").value, savedAtUtc: (/* @__PURE__ */ new Date()).toISOString() }));
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
    if (storedDraft && (storedDraft.title !== item.title || storedDraft.body !== item.body) && window.confirm("Restore your unsaved local draft for this note?")) {
      modal.querySelector("[data-modal-title]").value = storedDraft.title || "";
      modal.querySelector("[data-modal-body]").value = storedDraft.body || "";
      scheduleAutosave();
    }
  }
  function signInAgain() {
    preserveUnsavedDraft();
    const returnUrl = window.location.pathname + window.location.search + window.location.hash;
    window.location.assign("/Identity/Account/Login?ReturnUrl=" + encodeURIComponent(returnUrl));
  }
  async function copyUnsavedContent() {
    const title = modal.querySelector("[data-modal-title]").value.trim();
    const body = modal.querySelector("[data-modal-body]").value.trim();
    const text = [title, body].filter(Boolean).join("\n\n");
    await navigator.clipboard.writeText(text);
    setStatus("Unsaved note text copied. Sign in again before saving.", currentSaveError?.kind || "session-expired");
  }
  async function retrySave() {
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
  async function discardChangesAndClose() {
    if (!window.confirm("Discard unsaved changes and close this note?")) return;
    const closedId = item?.id;
    autosave?.cancel?.();
    autosave?.stop();
    autosave = null;
    currentSaveError = null;
    clearValidationBlock();
    item = null;
    modal.hidden = true;
    setBackgroundInert(false);
    history.replaceState(history.state, "", buildNoteUrl(null));
    (board.findCard(closedId) || trigger)?.focus?.();
  }
  async function reloadLatest() {
    if (currentSaveError?.kind === "client-version") {
      window.location.reload();
      return;
    }
    if (!window.confirm("Reload the latest saved version? Unsaved changes in this editor will be discarded.")) return;
    const button = modal.querySelector("[data-notebook-reload-latest]");
    button.disabled = true;
    try {
      item = await NotebookApi.getItem(item.id);
      renderMode();
      clearValidationBlock();
      setStatus("", "idle");
    } catch (error) {
      setStatus(error.message || "Unable to reload the note.", "error");
    } finally {
      button.disabled = false;
    }
  }
  async function pinItem() {
    if (!item) return;
    const button = modal.querySelector("[data-modal-pin]");
    button.disabled = true;
    try {
      await autosave?.flush();
      const response = await NotebookApi.setPinned(item.id, !item.isPinned, item.version);
      item = requireMutationItem(response, "The pin response did not contain the updated note.");
      renderPin();
      await reconcileMutation({ response, board, view, getCardHtml: NotebookApi.getCardHtml, applyCounts: options.applyCounts, preservePosition: false, prepend: true, showGlobalError: options.showGlobalError, reconcileFailureMessage: `The note was ${item.isPinned ? "pinned" : "unpinned"}, but the board could not refresh. Reload the page.` });
      setStatus("Saved", "saved");
    } catch (error) {
      handleEditorError(error);
    } finally {
      button.disabled = false;
    }
  }
  async function open(id, options2 = {}) {
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
    if (options2.pushHistory !== false) {
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
      await autosave?.flush();
      autosave?.stop();
      autosave = null;
      modal.hidden = true;
      setBackgroundInert(false);
      const closedId = item.id;
      item = null;
      if (!fromHistory) {
        if (openedByPushState) history.back();
        else history.replaceState(history.state, "", buildNoteUrl(null));
      }
      (board.findCard(closedId) || trigger)?.focus?.();
    } catch (error) {
      handleEditorError(error);
    } finally {
      closeButton.disabled = false;
    }
  }
  function syncExternalUpdate(updated) {
    if (!item || item.id !== updated.id) return;
    if (dirtyState.title || dirtyState.body || dirtyState.checklist) {
      pendingExternalUpdate = updated;
      setStatus("This checklist was changed elsewhere. Save or reload before continuing.", "conflict");
      return;
    }
    item = updated;
    checklist.setRows(updated.checklistRows || []);
    modal.querySelector("[data-modal-title]").value = updated.title || "";
    modal.querySelector("[data-modal-body]").value = updated.body || "";
    renderPin();
  }
  return { open, requestClose, isOpen: () => !!item && !!modal && !modal.hidden, syncExternalUpdate };
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
    const item = document.createElement("li");
    item.textContent = error.message;
    list.appendChild(item);
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
var guidPattern;
var init_notebook_editor = __esm({
  "wwwroot/js/notebook/notebook-editor.js"() {
    init_notebook_api();
    init_notebook_autosave();
    init_notebook_checklist_editor();
    init_notebook_reconcile();
    init_notebook_errors();
    guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
  }
});

// wwwroot/js/notebook/notebook-app.js
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
    const action = closestAction(event);
    if (!action) return;
    const card = action.closest("[data-note-id]");
    const id = card?.dataset.noteId;
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
  const directId = new URL(location.href).searchParams.get("note");
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
    init_notebook_reconcile();
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
