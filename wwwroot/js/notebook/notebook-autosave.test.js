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
test('notebook autosave flush waits for an active save and ignores stale saved callbacks', async () => {
  const { createAutosave } = await loadModule('notebook-autosave.js');
  const saved = [];
  let releaseFirst;
  const first = new Promise((resolve) => { releaseFirst = resolve; });
  const autosave = createAutosave({
    delay: 1,
    save: (payload, revision) => revision === 1 ? first.then(() => payload) : Promise.resolve(payload),
    onSaved: (result) => saved.push(result.value)
  });

  autosave.schedule(() => ({ value: 'first' }));
  await new Promise((resolve) => setTimeout(resolve, 5));
  autosave.schedule(() => ({ value: 'second' }));
  await autosave.flush();
  releaseFirst();
  await new Promise((resolve) => setTimeout(resolve, 0));

  assert.deepEqual(saved, ['second']);
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
