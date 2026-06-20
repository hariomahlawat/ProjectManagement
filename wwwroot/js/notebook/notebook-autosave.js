// SECTION: Notebook serial autosave utility
function isPlainObject(value) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

function clonePayload(payload) {
  if (typeof structuredClone === 'function') return structuredClone(payload);
  return JSON.parse(JSON.stringify(payload));
}

export function assertPayloadObject(payload) {
  if (!isPlainObject(payload)) throw new TypeError('Autosave payload must be a plain object.');
}

export function createAutosave({ save, delay = 800, onSaving, onPersisted, onSaveError, onReconcileError, onSaved, onError }) {
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
    try { return await activePromise; }
    finally { activePromise = null; }
  }

  function schedule(payload) {
    if (stopped) return;
    assertPayloadObject(payload);
    latestPayload = clonePayload(payload);
    dirty = true;
    if (timer) window.clearTimeout(timer);
    timer = window.setTimeout(() => { timer = null; runLoop().catch(() => {}); }, delay);
  }

  async function flush() {
    if (timer) { window.clearTimeout(timer); timer = null; }
    if (activePromise) await activePromise;
    if (dirty) await runLoop();
  }

  function cancel() {
    if (timer) { window.clearTimeout(timer); timer = null; }
    dirty = false;
    latestPayload = null;
  }

  function stop() { stopped = true; cancel(); }

  return { schedule, flush, cancel, stop, hasPending: () => Boolean(timer || activePromise || dirty) };
}
