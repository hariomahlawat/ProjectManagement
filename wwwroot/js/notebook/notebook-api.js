// SECTION: Notebook API error type and fetch wrapper
export class NotebookApiError extends Error {
  constructor(message, { status = 0, code = null, errors = null } = {}) {
    super(message); this.name = 'NotebookApiError'; this.status = status; this.code = code; this.errors = errors;
  }
}
const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
async function request(url, options = {}) {
  const headers = { Accept: 'application/json', ...(options.headers || {}) };
  if (!(options.body instanceof FormData) && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
  if (token()) headers.RequestVerificationToken = token();
  let response;
  try { response = await fetch(url, { credentials: 'same-origin', ...options, headers }); }
  catch (error) { throw new NotebookApiError('Unable to reach the notebook service.', { status: 0, errors: error }); }
  if (response.status === 204) return null;
  const contentType = response.headers.get('content-type') || '';
  const payload = contentType.includes('application/json') ? await response.json() : await response.text();
  if (!response.ok) throw new NotebookApiError(payload?.message || payload?.error || 'The notebook operation failed.', { status: response.status, code: payload?.code, errors: payload?.errors });
  return payload;
}
export const NotebookApi = {
  createItem: (payload) => request('/api/notebook/items', { method: 'POST', body: JSON.stringify(payload) }),
  getItem: (id) => request(`/api/notebook/items/${id}`),
  updateItem: (id, payload) => request(`/api/notebook/items/${id}`, { method: 'PATCH', body: JSON.stringify(payload) }),
  setPinned: (id, isPinned) => request(`/api/notebook/items/${id}/pin`, { method: 'POST', body: JSON.stringify({ isPinned }) }),
  archiveItem: (id) => request(`/api/notebook/items/${id}/archive`, { method: 'POST', body: '{}' }),
  completeItem: (id) => request(`/api/notebook/items/${id}/complete`, { method: 'POST', body: '{}' }),
  reopenItem: (id) => request(`/api/notebook/items/${id}/reopen`, { method: 'POST', body: '{}' }),
  duplicateItem: (id) => request(`/api/notebook/items/${id}/duplicate`, { method: 'POST', body: '{}' }),
  deleteItem: (id) => request(`/api/notebook/items/${id}`, { method: 'DELETE' }),
  restoreItem: (id) => request(`/api/notebook/items/${id}/restore`, { method: 'POST', body: '{}' }),
  showCheckboxes: (id) => request(`/api/notebook/items/${id}/show-checkboxes`, { method: 'POST', body: '{}' }),
  hideCheckboxes: (id) => request(`/api/notebook/items/${id}/hide-checkboxes`, { method: 'POST', body: '{}' }),
  toggleChecklistItem: (itemId, rowId, isDone, version) => request(`/api/notebook/items/${itemId}/checklist-items/${rowId}`, { method: 'PATCH', body: JSON.stringify({ isDone, version }) }),
  getCardHtml: (id, view = 'home') => request(`/api/notebook/items/${id}/card?view=${encodeURIComponent(view)}`, { headers: { Accept: 'text/html' } })
};
