import { notifySessionExpired } from '../core/session-auth.js';

// SECTION: Notebook API error type and fetch wrapper
export class NotebookApiError extends Error {
  constructor(message, { status = 0, code = null, errors = null, responseText = null, url = null, method = null, cause = null, currentVersion = null, currentItem = null } = {}) {
    super(message);
    this.name = 'NotebookApiError';
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
}

// SECTION: Environment and diagnostics helpers
function isDevelopment() {
  return document.documentElement.dataset.environment === 'Development' || location.hostname === 'localhost';
}

function logNotebookRequest(url, method, headers, body) {
  if (!isDevelopment() || !isUnsafeMethod(method)) return;
  console.debug('Notebook API request', {
    url,
    method,
    contentType: headers.get('Content-Type'),
    hasAntiForgeryToken: headers.has('RequestVerificationToken'),
    hasBody: body !== undefined && body !== null
  });
}

function logNotebookFailure(error) {
  if (!isDevelopment()) return;
  console.error('Notebook API request failed', {
    url: error.url,
    method: error.method,
    status: error.status,
    code: error.code,
    errors: error.errors,
    responseText: error.responseText
  });
}

// SECTION: Anti-forgery token handling
function getAntiForgeryToken() {
  const tokenInput = document.querySelector('#notebook-antiforgery-token input[name="__RequestVerificationToken"]');
  const value = tokenInput?.value?.trim();
  if (!value) {
    throw new NotebookApiError('Notebook security token is unavailable. Refresh the page and try again.', {
      status: 0,
      code: 'notebook_antiforgery_missing'
    });
  }
  return value;
}

function isUnsafeMethod(method) {
  const normalised = (method || 'GET').toUpperCase();
  return !['GET', 'HEAD', 'OPTIONS', 'TRACE'].includes(normalised);
}

export function getDefaultNotebookErrorMessage(status) {
  switch (status) {
    case 400: return 'The notebook request was invalid.';
    case 401: return 'Your session has expired. Sign in again.';
    case 403: return 'You are not authorised to perform this action.';
    case 404: return 'The note could not be found.';
    case 409: return 'The note was changed elsewhere.';
    case 415: return 'The request format is not supported.';
    default: return 'The notebook operation failed.';
  }
}

// SECTION: Shared JSON request options helper
export function jsonRequestOptions(method, payload, options = {}) {
  if (payload === undefined || typeof payload === 'function' || typeof payload === 'symbol') {
    throw new NotebookApiError('Notebook request payload is invalid.', {
      status: 0,
      code: 'notebook_invalid_client_payload'
    });
  }

  let body;
  try {
    body = JSON.stringify(payload);
  } catch (error) {
    throw new NotebookApiError('Notebook request payload could not be serialised.', {
      status: 0,
      code: 'notebook_payload_serialisation_failed',
      cause: error
    });
  }

  if (typeof body !== 'string' || body.length === 0) {
    throw new NotebookApiError('Notebook request payload is empty.', {
      status: 0,
      code: 'notebook_empty_client_payload'
    });
  }

  const headers = new Headers(options.headers || {});
  headers.set('Content-Type', 'application/json; charset=utf-8');
  return {
    ...options,
    method: String(method).toUpperCase(),
    headers,
    body
  };
}

// SECTION: Response authentication helpers
export function isLoginResponse(response) {
  if (!response) return false;
  const responseUrl = response.url || '';
  return Boolean(response.redirected && responseUrl.includes('/Identity/Account/Login'));
}

function createSessionExpiredError(context) {
  notifySessionExpired();
  return new NotebookApiError('Your session has expired. Sign in again.', {
    status: 401,
    code: 'notebook_session_expired',
    url: context.url,
    method: context.method
  });
}

// SECTION: Response parsing helpers
async function parseNotebookResponse(response, context) {
  if (isLoginResponse(response)) throw createSessionExpiredError(context);
  if (response.status === 204) return null;

  const contentType = response.headers.get('content-type') || '';
  if (contentType.includes('text/html') && (response.url || '').includes('/Identity/Account/Login')) {
    throw createSessionExpiredError(context);
  }

  let payload = null;
  let rawText = null;

  if (contentType.includes('application/json') || contentType.includes('application/problem+json')) {
    try { payload = await response.json(); }
    catch { payload = null; }
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

// SECTION: Fetch wrapper and API surface
export async function request(url, options = {}) {
  const method = String(options.method || 'GET').toUpperCase();
  const headers = new Headers(options.headers || {});

  if (!headers.has('Accept')) headers.set('Accept', 'application/json');

  const hasBody = options.body !== undefined && options.body !== null;
  const isFormData = typeof FormData !== 'undefined' && options.body instanceof FormData;
  if (hasBody && !isFormData && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json; charset=utf-8');
  }

  if (isUnsafeMethod(method)) headers.set('RequestVerificationToken', getAntiForgeryToken());
  logNotebookRequest(url, method, headers, options.body);

  let response;
  try {
    response = await fetch(url, { ...options, method, headers, credentials: 'same-origin' });
  } catch (error) {
    if (error?.name === 'AbortError') {
      throw new NotebookApiError('The notebook request was cancelled.', {
        status: 0,
        code: 'notebook_request_aborted',
        url,
        method,
        cause: error
      });
    }
    const apiError = new NotebookApiError('The notebook service could not be reached.', {
      status: 0,
      code: 'notebook_network_error',
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

// SECTION: Notebook API commands
export const NotebookApi = {
  createItem: (payload) => request('/api/notebook/items', jsonRequestOptions('POST', payload)),
  getItem: (id, options = {}) => request(`/api/notebook/items/${encodeURIComponent(id)}`, options),
  updateItem: (id, payload) => request(`/api/notebook/items/${encodeURIComponent(id)}`, jsonRequestOptions('PATCH', payload)),
  updateContent: (id, payload, options = {}) => request(`/api/notebook/items/${encodeURIComponent(id)}/content`, jsonRequestOptions('PATCH', payload, options)),
  updateChecklist: (id, payload, options = {}) => request(`/api/notebook/items/${encodeURIComponent(id)}/checklist`, jsonRequestOptions('PUT', payload, options)),
  setPinned: (id, isPinned, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/pin`, jsonRequestOptions('POST', { isPinned, version })),
  setColour: (id, colorKey, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/colour`, jsonRequestOptions('POST', { colorKey: colorKey || null, version })),
  getLabels: () => request('/api/notebook/labels'),
  setLabels: (id, labels, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/labels`, jsonRequestOptions('POST', { labels, version })),
  renameLabel: (id, name) => request(`/api/notebook/labels/${encodeURIComponent(id)}`, jsonRequestOptions('PATCH', { name })),
  deleteLabel: (id) => request(`/api/notebook/labels/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  archiveItem: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/archive`, jsonRequestOptions('POST', { version })),
  completeItem: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/complete`, jsonRequestOptions('POST', { version })),
  reopenItem: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/reopen`, jsonRequestOptions('POST', { version })),
  duplicateItem: (id) => request(`/api/notebook/items/${encodeURIComponent(id)}/duplicate`, jsonRequestOptions('POST', {})),
  deleteItem: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}`, jsonRequestOptions('DELETE', { version })),
  restoreItem: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/restore`, jsonRequestOptions('POST', { version })),
  showCheckboxes: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/show-checkboxes`, jsonRequestOptions('POST', { version })),
  hideCheckboxes: (id, version) => request(`/api/notebook/items/${encodeURIComponent(id)}/hide-checkboxes`, jsonRequestOptions('POST', { version })),
  toggleChecklistItem: (itemId, rowId, isDone, version) => request(`/api/notebook/items/${encodeURIComponent(itemId)}/checklist-items/${encodeURIComponent(rowId)}`, jsonRequestOptions('PATCH', { isDone, version })),
  getCounts: () => request('/api/notebook/counts'),
  getCardHtml: (id, view = 'home') => request(`/api/notebook/items/${encodeURIComponent(id)}/card?view=${encodeURIComponent(view)}`, { headers: { Accept: 'text/html' } })
};
