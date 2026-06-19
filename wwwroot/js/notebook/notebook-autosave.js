// SECTION: Notebook debounced autosave utility
export function createAutosave({ save, delay = 800, onSaving, onSaved, onError }) {
  let timer = null;
  let activePromise = null;
  let scheduledRevision = 0;
  let appliedRevision = 0;
  let latestPayloadFactory = null;

  async function execute(revision, payloadFactory) {
    if (!payloadFactory) return null;
    onSaving?.();
    const promise = Promise.resolve().then(() => save(payloadFactory(), revision));
    activePromise = promise;
    try {
      const result = await promise;
      if (revision < scheduledRevision) return { ignored: true, result };
      appliedRevision = revision;
      onSaved?.(result);
      return { ignored: false, result };
    } catch (error) {
      if (revision >= scheduledRevision) onError?.(error);
      throw error;
    } finally {
      if (activePromise === promise) activePromise = null;
    }
  }

  function schedule(payloadFactory) {
    latestPayloadFactory = payloadFactory;
    scheduledRevision += 1;
    const revision = scheduledRevision;
    if (timer) clearTimeout(timer);
    timer = window.setTimeout(() => { timer = null; execute(revision, payloadFactory).catch(() => {}); }, delay);
  }

  async function flush() {
    if (timer) {
      clearTimeout(timer); timer = null;
      return execute(scheduledRevision, latestPayloadFactory);
    }
    if (activePromise) return activePromise;
    return null;
  }

  return { schedule, flush, cancel: () => { if (timer) clearTimeout(timer); timer = null; latestPayloadFactory = null; }, hasPending: () => Boolean(timer || activePromise), latestRevision: () => scheduledRevision, appliedRevision: () => appliedRevision };
}
