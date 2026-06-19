// SECTION: Notebook serial autosave utility
export function createAutosave({ save, delay = 800, onSaving, onSaved, onError }) {
  let timer = null;
  let activePromise = null;
  let latestPayloadFactory = null;
  let dirty = false;
  let stopped = false;

  async function runLoop() {
    if (activePromise) return activePromise;
    activePromise = (async () => {
      while (!stopped && dirty && latestPayloadFactory) {
        dirty = false;
        const payload = latestPayloadFactory();
        onSaving?.();
        try {
          const result = await save(payload);
          onSaved?.(result);
        } catch (error) {
          dirty = true;
          onError?.(error);
          throw error;
        }
      }
    })();
    try { return await activePromise; }
    finally { activePromise = null; }
  }

  function schedule(payloadFactory) {
    if (stopped) return;
    latestPayloadFactory = payloadFactory;
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
    latestPayloadFactory = null;
  }

  function stop() { stopped = true; cancel(); }

  return { schedule, flush, cancel, stop, hasPending: () => Boolean(timer || activePromise || dirty) };
}
