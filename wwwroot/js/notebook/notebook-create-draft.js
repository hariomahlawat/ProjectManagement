export const NOTEBOOK_CREATE_DRAFT_VERSION = 1;

function normaliseType(type) {
  return ['Note', 'Checklist', 'Reminder'].includes(type) ? type : 'Note';
}

export function hasMeaningfulCreateDraft(draft = {}) {
  const checklistHasText = Array.isArray(draft.checklistRows)
    && draft.checklistRows.some((row) => String(row?.text || '').trim().length > 0);
  return Boolean(
    String(draft.title || '').trim()
    || String(draft.body || '').trim()
    || checklistHasText
    || (Array.isArray(draft.labels) && draft.labels.length > 0)
    || draft.isPinned
    || (draft.colorKey && draft.colorKey !== 'white')
    || (draft.priority && draft.priority !== 'Normal')
    || (draft.type === 'Reminder' && draft.scheduleTouched && draft.reminderDate && draft.reminderTime)
  );
}

export function createNotebookCreateDraftStore({ storage, userId, nowProvider = () => new Date() } = {}) {
  const safeStorage = storage || globalThis.sessionStorage;
  const safeUserId = String(userId || '').trim();
  const enabled = Boolean(safeStorage && safeUserId);
  const keyFor = (type) => `notebook:create-draft:v${NOTEBOOK_CREATE_DRAFT_VERSION}:${safeUserId}:${normaliseType(type).toLowerCase()}`;

  function save(type, draft) {
    if (!enabled) return false;
    const safeType = normaliseType(type);
    const payload = {
      ...draft,
      version: NOTEBOOK_CREATE_DRAFT_VERSION,
      userId: safeUserId,
      type: safeType,
      savedAtUtc: nowProvider().toISOString()
    };
    try {
      safeStorage.setItem(keyFor(safeType), JSON.stringify(payload));
      return true;
    } catch {
      return false;
    }
  }

  function load(type) {
    if (!enabled) return null;
    const safeType = normaliseType(type);
    let raw;
    try { raw = safeStorage.getItem(keyFor(safeType)); }
    catch { return null; }
    if (!raw) return null;
    try {
      const parsed = JSON.parse(raw);
      if (parsed?.version !== NOTEBOOK_CREATE_DRAFT_VERSION || parsed?.userId !== safeUserId || parsed?.type !== safeType) {
        try { safeStorage.removeItem(keyFor(safeType)); } catch {}
        return null;
      }
      return parsed;
    } catch {
      try { safeStorage.removeItem(keyFor(safeType)); } catch {}
      return null;
    }
  }

  function remove(type) {
    if (enabled) { try { safeStorage.removeItem(keyFor(type)); } catch {} }
  }

  function removeAll() {
    ['Note', 'Checklist', 'Reminder'].forEach(remove);
  }

  return { enabled, keyFor, load, remove, removeAll, save };
}
