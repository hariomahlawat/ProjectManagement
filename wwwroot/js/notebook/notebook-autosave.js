// SECTION: Notebook debounced autosave utility
export function createAutosave(save, delay = 800) {
  let timer = null, pending = false, lastArgs = null, sequence = 0;
  const run = async () => { timer = null; pending = true; const current = ++sequence; try { return await save(...(lastArgs || []), current); } finally { if (current === sequence) pending = false; } };
  return { schedule: (...args) => { lastArgs = args; clearTimeout(timer); timer = setTimeout(run, delay); }, flush: () => timer ? (clearTimeout(timer), run()) : Promise.resolve(), cancel: () => { clearTimeout(timer); timer = null; pending = false; }, isPending: () => pending || !!timer };
}
