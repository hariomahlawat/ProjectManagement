// SECTION: Notebook API error type and fetch wrapper
export class NotebookApiError extends Error {
  constructor(message, { status = 0, code = null, errors = null, responseText = null } = {}) {
    super(message);
    this.name = 'NotebookApiError';
    this.status = status;
    this.code = code;
    this.errors = errors;
    this.responseText = responseText;
  }
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

function getDefaultNotebookErrorMessage(status) {
  switch (status) {
    case 400: return 'The notebook request was invalid.';
    case 401: return 'Your session has expired. Sign in again.';
    case 403: return 'You do not have permission to perform this action.';
    case 404: return 'The note could not be found.';
    case 409: return 'This note was changed elsewhere. Reload the latest version.';
    case 500: return 'The notebook operation could not be completed.';
    default: return `Notebook request failed with HTTP ${status}.`;
  }
}

// SECTION: Fetch wrapper and API surface
async function request(url, options = {}) {
  const method = (options.method || 'GET').toUpperCase();
  const headers = { Accept: 'application/json', ...(options.headers || {}) };
  if (!(options.body instanceof FormData) && options.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
  if (isUnsafeMethod(method)) headers.RequestVerificationToken = getAntiForgeryToken();

  let response;
  try { response = await fetch(url, { credentials: 'same-origin', ...options, method, headers }); }
  catch (error) { throw new NotebookApiError('The notebook service could not be reached.', { status: 0, code: 'notebook_network_error', errors: error }); }
  if (response.status === 204) return null;

  const contentType = response.headers.get('content-type') || '';
  let payload = null; let rawText = null;
  if (contentType.includes('application/json')) payload = await response.json();
  else rawText = await response.text();

  if (!response.ok) {
    throw new NotebookApiError(payload?.message || payload?.title || payload?.error || getDefaultNotebookErrorMessage(response.status), {
      status: response.status,
      code: payload?.code,
      errors: payload?.errors,
      responseText: rawText
    });
  }
  return payload ?? rawText;
}

export const NotebookApi = {
  createItem: (payload) => request('/api/notebook/items', { method: 'POST', body: JSON.stringify(payload) }),
  getItem: (id) => request(`/api/notebook/items/${id}`),
  updateItem: (id, payload) => request(`/api/notebook/items/${id}`, { method: 'PATCH', body: JSON.stringify(payload) }),
  setPinned: (id, isPinned, version) => request(`/api/notebook/items/${id}/pin`, { method: 'POST', body: JSON.stringify({ isPinned, version }) }),
  archiveItem: (id, version) => request(`/api/notebook/items/${id}/archive`, { method: 'POST', body: JSON.stringify({ version }) }),
  completeItem: (id, version) => request(`/api/notebook/items/${id}/complete`, { method: 'POST', body: JSON.stringify({ version }) }),
  reopenItem: (id, version) => request(`/api/notebook/items/${id}/reopen`, { method: 'POST', body: JSON.stringify({ version }) }),
  duplicateItem: (id) => request(`/api/notebook/items/${id}/duplicate`, { method: 'POST', body: '{}' }),
  deleteItem: (id, version) => request(`/api/notebook/items/${id}`, { method: 'DELETE', body: JSON.stringify({ version }) }),
  restoreItem: (id, version) => request(`/api/notebook/items/${id}/restore`, { method: 'POST', body: JSON.stringify({ version }) }),
  showCheckboxes: (id, version) => request(`/api/notebook/items/${id}/show-checkboxes`, { method: 'POST', body: JSON.stringify({ version }) }),
  hideCheckboxes: (id, version) => request(`/api/notebook/items/${id}/hide-checkboxes`, { method: 'POST', body: JSON.stringify({ version }) }),
  toggleChecklistItem: (itemId, rowId, isDone, version) => request(`/api/notebook/items/${itemId}/checklist-items/${rowId}`, { method: 'PATCH', body: JSON.stringify({ isDone, version }) }),
  getCounts: () => request('/api/notebook/counts'),
  getCardHtml: (id, view = 'home') => request(`/api/notebook/items/${id}/card?view=${encodeURIComponent(view)}`, { headers: { Accept: 'text/html' } })
};
