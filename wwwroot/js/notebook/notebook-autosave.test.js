const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
global.window = { setTimeout, clearTimeout };
const path = require('node:path');

// SECTION: ESM loader helper for browser modules in CommonJS test runner.
async function loadModule(fileName) {
  const filePath = path.resolve(__dirname, fileName);
  const source = fs.readFileSync(filePath, 'utf8');
  return import(`data:text/javascript;charset=utf-8,${encodeURIComponent(source)}`);
}

// SECTION: Autosave sequencing tests.
test('notebook autosave serialises rapid edits and uses latest payload after an active save', async () => {
  const { createAutosave } = await loadModule('notebook-autosave.js');
  const calls = [];
  const saved = [];
  let version = 'v1';
  let releaseFirst;
  const first = new Promise((resolve) => { releaseFirst = resolve; });
  const autosave = createAutosave({
    delay: 1,
    save: async (payload) => {
      calls.push(payload);
      if (calls.length === 1) await first;
      return { version: calls.length === 1 ? 'v2' : 'v3', value: payload.value };
    },
    onSaved: (result) => { version = result.version; saved.push(result.value); }
  });

  autosave.schedule(() => ({ value: 'first', version }));
  await new Promise((resolve) => setTimeout(resolve, 5));
  autosave.schedule(() => ({ value: 'second', version }));
  releaseFirst();
  await autosave.flush();

  assert.deepEqual(calls, [
    { value: 'first', version: 'v1' },
    { value: 'second', version: 'v2' }
  ]);
  assert.deepEqual(saved, ['first', 'second']);
});

test('notebook autosave keeps failed saves dirty for retry', async () => {
  const { createAutosave } = await loadModule('notebook-autosave.js');
  const calls = [];
  const errors = [];
  let fail = true;
  const autosave = createAutosave({
    delay: 1,
    save: async (payload) => { calls.push(payload.value); if (fail) throw new Error('network'); return payload; },
    onError: (error) => errors.push(error.message)
  });

  autosave.schedule(() => ({ value: 'draft' }));
  await assert.rejects(() => autosave.flush(), /network/);
  fail = false;
  await autosave.flush();

  assert.deepEqual(calls, ['draft', 'draft']);
  assert.deepEqual(errors, ['network']);
});

test('notebook autosave routes timer errors to onError without unhandled rejection', async () => {
  const { createAutosave } = await loadModule('notebook-autosave.js');
  const errors = [];
  const autosave = createAutosave({
    delay: 1,
    save: () => Promise.reject(new Error('network')),
    onError: (error) => errors.push(error.message)
  });

  autosave.schedule(() => ({ value: 'draft' }));
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.deepEqual(errors, ['network']);
});

test('notebook autosave awaits async onSaved before starting next dirty iteration', async () => {
  const { createAutosave } = await loadModule('notebook-autosave.js');
  const events = [];
  let version = 'v1';
  let releaseSaved;
  const savedGate = new Promise((resolve) => { releaseSaved = resolve; });
  const autosave = createAutosave({
    delay: 1,
    save: async (payload) => { events.push(`save:${payload.version}`); return { version: payload.version === 'v1' ? 'v2' : 'v3' }; },
    onSaved: async (result) => {
      events.push(`saved-start:${result.version}`);
      if (result.version === 'v2') await savedGate;
      version = result.version;
      events.push(`saved-end:${result.version}`);
    }
  });

  autosave.schedule(() => ({ version }));
  await new Promise((resolve) => setTimeout(resolve, 5));
  autosave.schedule(() => ({ version }));
  await new Promise((resolve) => setTimeout(resolve, 5));

  assert.deepEqual(events, ['save:v1', 'saved-start:v2']);
  releaseSaved();
  await autosave.flush();

  assert.deepEqual(events, ['save:v1', 'saved-start:v2', 'saved-end:v2', 'save:v2', 'saved-start:v3', 'saved-end:v3']);
});
