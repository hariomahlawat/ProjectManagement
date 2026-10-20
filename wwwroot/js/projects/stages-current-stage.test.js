const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// After an inline approval moves the lifecycle on, the next not-started stage becomes
// current: it must regain its full card (no is-future) without a page reload.
const source = fs.readFileSync(path.join(__dirname, 'stages.js'), 'utf8');
const functionSource = source.slice(
  source.indexOf('  function refreshCurrentStage() {'),
  source.indexOf('  function updateStageRow('));

function row(code, status, current, extra = '') {
  return `<div id="timeline-stage-${code}" class="pm-item${current ? '' : status === 'NotStarted' ? ' is-future' : ''}"
    data-timeline-stage data-stage-row="${code}" data-stage-current="${current}" data-stage-status-value="${status}">${extra}</div>`;
}

function run(html) {
  const dom = new JSDOM(`<!DOCTYPE html><body>${html}</body>`);
  vm.runInNewContext(`${functionSource}\nrefreshCurrentStage();`, { document: dom.window.document });
  return dom.window.document;
}

test('the first not-started stage becomes current once the stage before it is approved complete', () => {
  // TEC was current and has just been set to Completed in place; BM is next.
  const doc = run(row('TEC', 'Completed', true) + row('BM', 'NotStarted', false) + row('COB', 'NotStarted', false));
  assert.equal(doc.getElementById('timeline-stage-TEC').dataset.stageCurrent, 'false');
  assert.equal(doc.getElementById('timeline-stage-BM').dataset.stageCurrent, 'true');
  assert.ok(!doc.getElementById('timeline-stage-BM').classList.contains('is-future'));
  assert.ok(doc.getElementById('timeline-stage-COB').classList.contains('is-future'));
});

test('a stage in progress stays current ahead of earlier unresolved stages', () => {
  const doc = run(row('FS', 'NotStarted', false) + row('IPA', 'InProgress', true) + row('AON', 'NotStarted', false));
  assert.equal(doc.getElementById('timeline-stage-IPA').dataset.stageCurrent, 'true');
  assert.ok(doc.getElementById('timeline-stage-FS').classList.contains('is-future'));
});

test('a not-started stage with a pending update keeps its full card', () => {
  const doc = run(row('TEC', 'InProgress', true)
    + row('BM', 'NotStarted', false, '<section class="pm-stage-pending-update"></section>'));
  assert.ok(!doc.getElementById('timeline-stage-BM').classList.contains('is-future'));
});

test('does nothing where the server marked no current stage (a closed lifecycle)', () => {
  const doc = run(row('TEC', 'Completed', false) + row('BM', 'NotStarted', false));
  assert.equal(doc.getElementById('timeline-stage-BM').dataset.stageCurrent, 'false');
});
