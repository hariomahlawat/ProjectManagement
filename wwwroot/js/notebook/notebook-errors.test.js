const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

// SECTION: ESM loader helper for Notebook validation helpers.
async function loadModule(fileName) {
  const filePath = path.resolve(__dirname, fileName);
  const source = fs.readFileSync(filePath, 'utf8');
  return import(`data:text/javascript;charset=utf-8,${encodeURIComponent(source)}`);
}

test('getValidationMessages flattens server ModelState errors', async () => {
  const { getValidationMessages, getFirstValidationMessage } = await loadModule('notebook-errors.js');
  const error = { message: 'One or more validation errors occurred.', errors: { '$.version': ['The JSON value could not be converted to System.Guid.'], priority: 'Invalid priority.' } };

  assert.deepEqual(getValidationMessages(error), [
    { field: '$.version', message: 'The JSON value could not be converted to System.Guid.' },
    { field: 'priority', message: 'Invalid priority.' }
  ]);
  assert.equal(getFirstValidationMessage(error), 'The JSON value could not be converted to System.Guid.');
});
