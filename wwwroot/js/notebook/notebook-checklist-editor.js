// SECTION: Shared notebook checklist row editor
const parseNullableInt = (value) => value ? Number.parseInt(value, 10) : null;

// SECTION: Client-side checklist identity helpers
export function createClientKey() {
  if (globalThis.crypto && typeof globalThis.crypto.randomUUID === 'function') {
    return globalThis.crypto.randomUUID();
  }

  return [Date.now().toString(36), Math.random().toString(36).slice(2), Math.random().toString(36).slice(2)].join('-');
}

export function createChecklistEditor(root, options = {}) {
  const maxLength = options.maxLength || 500;
  let rows = [];
  let isReconciling = false;
  const notify = () => { if (!isReconciling) options.onChange?.(); };

  // SECTION: Row state and DOM construction
  function normalizeRow(row = {}, index = 0) {
    return {
      id: row.id ?? null,
      clientKey: normaliseClientKey(row),
      text: row.text || '',
      isDone: Boolean(row.isDone),
      sortOrder: Number.isFinite(row.sortOrder) ? row.sortOrder : (index + 1) * 1000,
      element: row.element || null
    };
  }

  function normaliseClientKey(row = {}) {
    if (row.clientKey) return row.clientKey;
    return row.id === null || row.id === undefined ? createClientKey() : null;
  }

  function rowTemplate(row) {
    const wrapper = document.createElement('div');
    wrapper.className = 'notebook-checklist-row';
    wrapper.dataset.checklistRow = '';
    wrapper.innerHTML = `<label class="notebook-checklist-row__check" aria-label="Checklist item completion"><input type="checkbox" data-checklist-done><span aria-hidden="true"></span></label><input type="text" data-checklist-text maxlength="${maxLength}" placeholder="List item"><button type="button" class="notebook-checklist-remove" data-checklist-remove aria-label="Remove checklist item" title="Remove item"><i class="bi bi-x-lg" aria-hidden="true"></i></button>`;
    row.element = wrapper;
    updateRowElement(row, { forceContent: true });
    return wrapper;
  }

  function updateRowElement(row, { forceContent = false } = {}) {
    if (!row.element) return;
    row.element.dataset.rowId = row.id ?? '';
    row.element.dataset.clientKey = row.clientKey || '';
    const done = row.element.querySelector('[data-checklist-done]');
    const text = row.element.querySelector('[data-checklist-text]');
    if (done && (forceContent || done.checked !== Boolean(row.isDone))) done.checked = Boolean(row.isDone);
    if (text && (forceContent || text.value !== (row.text || ''))) text.value = row.text || '';
  }

  function readRowElement(row, index) {
    if (!row.element) return row;
    row.id = parseNullableInt(row.element.dataset.rowId);
    row.clientKey = row.element.dataset.clientKey || row.clientKey || normaliseClientKey(row);
    row.text = row.element.querySelector('[data-checklist-text]')?.value || '';
    row.isDone = Boolean(row.element.querySelector('[data-checklist-done]')?.checked);
    row.sortOrder = (index + 1) * 1000;
    return row;
  }

  function findRowByElement(element) {
    return rows.find((row) => row.element === element) || null;
  }

  // SECTION: Focus and scroll preservation
  function captureFocusState() {
    const active = document.activeElement;
    const row = active?.closest?.('[data-checklist-row]');
    if (!row || !root.contains(row)) return null;
    return {
      rowId: row.dataset.rowId || null,
      clientKey: row.dataset.clientKey || null,
      selectionStart: typeof active.selectionStart === 'number' ? active.selectionStart : null,
      selectionEnd: typeof active.selectionEnd === 'number' ? active.selectionEnd : null
    };
  }

  function restoreFocusState(state) {
    if (!state) return;
    const row = rows.find((candidate) => (state.rowId && String(candidate.id) === state.rowId) || (state.clientKey && candidate.clientKey === state.clientKey));
    const input = row?.element?.querySelector('[data-checklist-text]');
    if (!input) return;
    input.focus();
    if (state.selectionStart !== null && typeof input.setSelectionRange === 'function') {
      const end = state.selectionEnd ?? state.selectionStart;
      input.setSelectionRange(Math.min(state.selectionStart, input.value.length), Math.min(end, input.value.length));
    }
  }



  function findMatchingRow(target, byId, byClientKey) {
    if (target?.id !== null && target?.id !== undefined) {
      const byPermanentId = byId.get(String(target.id));
      if (byPermanentId) return byPermanentId;
    }

    if (target?.clientKey) return byClientKey.get(target.clientKey) ?? null;

    return null;
  }

  function appendReconciledRow(reconciled, row, seenRows, seenIdentities) {
    const identity = row.id !== null && row.id !== undefined ? `id:${row.id}` : (row.clientKey ? `client:${row.clientKey}` : null);

    if (seenRows.has(row) || (identity && seenIdentities.has(identity))) return;

    seenRows.add(row);
    if (identity) seenIdentities.add(identity);
    reconciled.push(row);
  }

  function wasAddedAfterDispatch(localRow, submittedById, submittedByClientKey) {
    return !findMatchingRow(localRow, submittedById, submittedByClientKey);
  }

  function removeStaleRowElements(reconciledRows) {
    const retainedElements = new Set(reconciledRows.map((row) => row.element).filter(Boolean));
    root.querySelectorAll('[data-checklist-row]').forEach((element) => {
      if (!retainedElements.has(element)) element.remove();
    });
  }

  // SECTION: Add-item control
  function ensureAddItemControl() {
    let button = root.querySelector('[data-checklist-add]');
    if (!button) {
      button = document.createElement('button');
      button.type = 'button';
      button.className = 'notebook-checklist-add';
      button.dataset.checklistAdd = '';
      button.innerHTML = '<i class="bi bi-plus-lg" aria-hidden="true"></i><span>List item</span>';
      root.append(button);
    }
    return button;
  }

  // SECTION: Public row operations
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
    if (afterElement) afterElement.after(el); else root.insertBefore(el, ensureAddItemControl());
    rows.splice(insertAt < 0 ? rows.length : insertAt, 0, model);
    return el;
  }

  function removeRow(element) {
    const row = findRowByElement(element);
    const prev = element.previousElementSibling;
    rows = rows.filter((candidate) => candidate !== row);
    element.remove();
    (prev?.querySelector('[data-checklist-text]') || root.querySelector('[data-checklist-text]'))?.focus();
    notify();
  }

  function getRows() {
    rows.forEach(readRowElement);
    return rows
      .map((row, index) => ({ id: row.id, clientKey: row.clientKey, text: row.text.trim(), isDone: row.isDone, sortOrder: index }))
      .filter((row) => row.text.length > 0);
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
      const submittedById = new Map(baseRows.filter((row) => row.id !== null && row.id !== undefined).map((row) => [String(row.id), row]));
      const submittedByClientKey = new Map(baseRows.filter((row) => row.clientKey).map((row) => [row.clientKey, row]));
      const localById = new Map(originalLocalRows.filter((row) => row.id !== null && row.id !== undefined).map((row) => [String(row.id), row]));
      const localByClientKey = new Map(originalLocalRows.filter((row) => row.clientKey).map((row) => [row.clientKey, row]));
      const reconciled = [];
      const seenRows = new Set();
      const seenIdentities = new Set();

      (serverRows || []).forEach((serverRow, index) => {
        const submittedRow = findMatchingRow(serverRow, submittedById, submittedByClientKey);
        let localRow = findMatchingRow(serverRow, localById, localByClientKey);

        if (hasSubmittedSnapshot && submittedRow && !localRow) return;

        if (!localRow) localRow = normalizeRow(serverRow, index);
        localRow.id = serverRow.id ?? localRow.id;
        localRow.clientKey = serverRow.clientKey ?? localRow.clientKey ?? normaliseClientKey(localRow);
        if (!submittedRow || localRow.text === (submittedRow.text ?? '')) localRow.text = serverRow.text || '';
        if (!submittedRow || localRow.isDone === Boolean(submittedRow.isDone)) localRow.isDone = Boolean(serverRow.isDone);
        localRow.sortOrder = serverRow.sortOrder ?? (index + 1) * 1000;
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

  // SECTION: Checklist event wiring
  function handleInput(event) { if (isReconciling) return; if (event.target.matches('[data-checklist-text]')) notify(); }
  function handleChange(event) { if (isReconciling) return; if (event.target.matches('[data-checklist-done]')) notify(); }
  function handleClick(event) { if (isReconciling) return; if (event.target.closest('[data-checklist-add]')) { addRow().querySelector('[data-checklist-text]')?.focus(); return; } const button = event.target.closest('[data-checklist-remove]'); if (button) removeRow(button.closest('[data-checklist-row]')); }
  function handleKeydown(event) { if (isReconciling) return; const input = event.target.closest('[data-checklist-text]'); if (!input) return; const row = input.closest('[data-checklist-row]'); if (event.key === 'Enter') { event.preventDefault(); addRow(row).querySelector('[data-checklist-text]').focus(); notify(); } if (event.key === 'Backspace' && input.value.length === 0 && root.querySelectorAll('[data-checklist-row]').length > 1) { event.preventDefault(); removeRow(row); } }
  function destroy() { root.removeEventListener('input', handleInput); root.removeEventListener('change', handleChange); root.removeEventListener('click', handleClick); root.removeEventListener('keydown', handleKeydown); root.replaceChildren(); rows = []; }

  root.addEventListener('input', handleInput);
  root.addEventListener('change', handleChange);
  root.addEventListener('click', handleClick);
  root.addEventListener('keydown', handleKeydown);

  return { setRows, getRows, addRow, removeRow, reconcileRows, replaceRows: setRows, renderRows: setRows, getFocusedRowState: captureFocusState, restoreFocusedRowState: restoreFocusState, focusFirst: () => (root.querySelector('[data-checklist-text]') || ensureAddItemControl())?.focus(), clear: () => setRows([]), destroy };
}
