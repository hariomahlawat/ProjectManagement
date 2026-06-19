// SECTION: Shared notebook checklist row editor
const parseNullableInt = (value) => value ? Number.parseInt(value, 10) : null;
export function createChecklistEditor(root, options = {}) {
  const maxLength = options.maxLength || 500;
  const notify = () => options.onChange?.();
  function rowTemplate(row = {}) {
    const wrapper = document.createElement('div');
    wrapper.className = 'notebook-checklist-row'; wrapper.dataset.checklistRow = ''; wrapper.dataset.rowId = row.id ?? '';
    wrapper.innerHTML = `<input type="checkbox" data-checklist-done><input type="text" data-checklist-text maxlength="${maxLength}" placeholder="List item"><button type="button" data-checklist-remove aria-label="Remove checklist item">×</button>`;
    wrapper.querySelector('[data-checklist-done]').checked = Boolean(row.isDone);
    wrapper.querySelector('[data-checklist-text]').value = row.text || '';
    return wrapper;
  }
  function addRow(afterElement = null, row = {}) { const el = rowTemplate(row); afterElement ? afterElement.after(el) : root.append(el); return el; }
  function removeRow(element) { const prev = element.previousElementSibling; element.remove(); (prev?.querySelector('[data-checklist-text]') || root.querySelector('[data-checklist-text]'))?.focus(); notify(); }
  function setRows(rows) { root.replaceChildren(); (rows?.length ? rows : [{ text: '' }]).forEach((row) => addRow(null, row)); }
  function getRows() { return [...root.querySelectorAll('[data-checklist-row]')].map((row, index) => ({ id: parseNullableInt(row.dataset.rowId), text: row.querySelector('[data-checklist-text]').value.trim(), isDone: row.querySelector('[data-checklist-done]').checked, sortOrder: (index + 1) * 1000 })).filter((row) => row.text.length > 0); }
  root.addEventListener('input', (event) => { if (event.target.matches('[data-checklist-text]')) notify(); });
  root.addEventListener('change', (event) => { if (event.target.matches('[data-checklist-done]')) notify(); });
  root.addEventListener('click', (event) => { const button = event.target.closest('[data-checklist-remove]'); if (button) removeRow(button.closest('[data-checklist-row]')); });
  root.addEventListener('keydown', (event) => { const input = event.target.closest('[data-checklist-text]'); if (!input) return; const row = input.closest('[data-checklist-row]'); if (event.key === 'Enter') { event.preventDefault(); addRow(row).querySelector('[data-checklist-text]').focus(); notify(); } if (event.key === 'Backspace' && input.value.length === 0 && root.querySelectorAll('[data-checklist-row]').length > 1) { event.preventDefault(); removeRow(row); } });
  return { setRows, getRows, addRow, removeRow, focusFirst: () => root.querySelector('[data-checklist-text]')?.focus(), clear: () => setRows([]), destroy: () => root.replaceChildren() };
}
