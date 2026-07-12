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
  assert.match(source, /case 'NotStarted':[\s\S]*Start stage[\s\S]*Complete stage directly[\s\S]*Mark blocked[\s\S]*Skip stage/);
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

test('projected lifecycle shows existing pending updates and current revisions', () => {
  assert.match(source, /Projected lifecycle|proposalDescription/);
  assert.match(source, /Existing pending/);
  assert.match(source, /Current revision/);
  assert.match(source, /Pending update/);
});

test('completion revisions retain and display an earlier proposed start', () => {
  assert.match(source, /pendingStartDate/);
  assert.match(source, /retainedStartDate/);
  assert.match(source, /field remains editable|remains editable until submission/);
});

test('direct completion submits an editable requested start date', () => {
  assert.match(source, /requestedStartDate/);
  assert.match(source, /data-stage-request-start-date/);
  assert.match(source, /Complete stage directly/);
});

test('start and direct completion use the workflow-derived editable defaults', () => {
  assert.match(source, /stage\?\.suggestedStart \|\| todayIso\(\)/);
  assert.match(source, /retainedStartDate\(stage\)/);
  assert.match(source, /No usable predecessor completion date is recorded\. Enter a start date or leave it blank\./);
});

test('editing a pending update uses save wording', () => {
  assert.match(source, /isEditingPending \? 'Save update' : 'Submit update'/);
});
