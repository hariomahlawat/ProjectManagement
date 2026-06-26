const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, 'stages.js');
const source = fs.readFileSync(scriptPath, 'utf8');

test('project officer stage update uses approval-oriented wording', () => {
  assert.match(source, /Stage update submitted\. It is now visible and awaiting HoD approval\./);
  assert.match(source, /Update .* stage/);
  assert.doesNotMatch(source, /showToast\('Request submitted\.'/);
});

test('project officer update reloads server-rendered pending state', () => {
  assert.match(source, /queueStageRefresh\('Stage update submitted/);
  assert.match(source, /window\.location\.reload\(\)/);
});

test('project officer status choices are contextual and exclude no-op status', () => {
  assert.match(source, /function transitionOptions\(currentStatus\)/);
  assert.match(source, /case 'InProgress':[\s\S]*Complete stage[\s\S]*Mark blocked[\s\S]*Skip stage/);
  assert.match(source, /case 'Completed':[\s\S]*Reopen stage/);
});
