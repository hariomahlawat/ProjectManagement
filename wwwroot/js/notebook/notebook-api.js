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
