// SECTION: Notebook API fetch wrapper
const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
async function request(url, options = {}) {
  const headers = { 'Accept': 'application/json', ...(options.headers || {}) };
  if (!(options.body instanceof FormData)) headers['Content-Type'] = 'application/json';
  if (token()) headers.RequestVerificationToken = token();
  const response = await fetch(url, { credentials: 'same-origin', ...options, headers });
  if (!response.ok) {
    let details = null;
    try { details = await response.json(); } catch { details = { error: response.statusText }; }
    const error = new Error(details?.error || details?.message || 'Notebook request failed.');
    error.status = response.status; error.details = details; throw error;
  }
  if (response.status === 204) return null;
  const contentType = response.headers.get('content-type') || '';
  return contentType.includes('application/json') ? response.json() : response.text();
}
export const NotebookApi = {
  createItem: (payload) => request('/api/notebook/items', { method: 'POST', body: JSON.stringify(payload) }),
  getItem: (id) => request(`/api/notebook/items/${id}`),
  updateItem: (id, payload) => request(`/api/notebook/items/${id}`, { method: 'PATCH', body: JSON.stringify(payload) }),
  setPinned: (id, isPinned) => request(`/api/notebook/items/${id}/pin`, { method: 'POST', body: JSON.stringify({ isPinned }) }),
  archiveItem: (id) => request(`/api/notebook/items/${id}/archive`, { method: 'POST', body: '{}' }),
  completeItem: (id) => request(`/api/notebook/items/${id}/complete`, { method: 'POST', body: '{}' }),
  reopenItem: (id) => request(`/api/notebook/items/${id}/reopen`, { method: 'POST', body: '{}' }),
  getCardHtml: (id, view = 'home') => request(`/api/notebook/items/${id}/card?view=${encodeURIComponent(view)}`, { headers: { Accept: 'text/html' } })
};
