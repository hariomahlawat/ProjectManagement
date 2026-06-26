const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, 'stages.js');
const source = fs.readFileSync(scriptPath, 'utf8');

test('project officer stage update uses approval-oriented wording', () => {
  assert.match(source, /stage update\$\{stagesPayload\.length === 1 \? '' : 's'\} submitted/);
  assert.match(source, /Update .* stage/);
  assert.doesNotMatch(source, /showToast\('Request submitted\.'/);
});

test('project officer update reloads server-rendered pending state', () => {
  assert.match(source, /queueStageRefresh\(`/);
  assert.match(source, /window\.location\.reload\(\)/);
});

test('project officer status choices are contextual and support historical completion', () => {
  assert.match(source, /function transitionOptions\(currentStatus\)/);
  assert.match(source, /case 'NotStarted':[\s\S]*Start stage[\s\S]*Record completion[\s\S]*Mark blocked[\s\S]*Skip stage/);
  assert.match(source, /case 'InProgress':[\s\S]*Complete stage[\s\S]*Mark blocked[\s\S]*Skip stage/);
  assert.match(source, /case 'Completed':[\s\S]*Reopen stage/);
});

test('multi-stage updates are supported without forcing notes for routine rows', () => {
  assert.match(source, /Add every stage that needs an update|data-stage-request-add/);
  assert.doesNotMatch(source, /rowCount > 1/);
  assert.match(source, /Include each stage once in this submission/);
});

test('validation errors are deduplicated and server messages are preserved', () => {
  assert.match(source, /Array\.from\(new Set\(/);
  assert.doesNotMatch(source, /Complete required predecessor stages first \(\$\{missing\.join/);
});
