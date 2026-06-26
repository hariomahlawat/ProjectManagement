const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, 'stages.js');
const source = fs.readFileSync(scriptPath, 'utf8');

test('direct stage application reloads the server-rendered portfolio after success', () => {
  assert.match(source, /function queueStageRefresh\(/);
  assert.match(source, /window\.location\.reload\(\)/);
  assert.match(source, /queueStageRefresh\(message, variant\)/);
});

test('authorised completion override uses one consolidated refresh message', () => {
  assert.match(
    source,
    /Stage completed through an authorised override\. Mandatory completion-date backfill has been created\./
  );
  assert.doesNotMatch(source, /showToast\('Stage updated\.'/);
});
