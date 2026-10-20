const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');
const source = fs.readFileSync(path.join(__dirname, 'actuals-edit.js'), 'utf8');

// UX discovery C7.2: moving one stage's completion past a successor's recorded start
// must be explained on the successor row, not only by the native `min` tooltip.
// `preds` renders data-predecessors (the workflow graph, as _EditActualsForm.cshtml does);
// rows without it exercise the display-order fallback.
function row({ code, name, status, start = '', completed = '', preds }) {
  const predecessors = preds === undefined ? '' : ` data-predecessors="${preds}"`;
  return `<section class="actuals-editor__stage border rounded p-3" data-actuals-row data-stage-code="${code}" data-stage-name="${name}"
      data-stage-status="${status}" data-earliest-start="" data-official-start="${start}"${predecessors} data-start-source="">
    <input type="date" class="form-control" id="actual-start-${code}" name="Input.Rows[0].ActualStart" value="${start}" max="2026-03-01" data-field="start" />
    <input type="date" class="form-control" id="actual-completed-${code}" name="Input.Rows[0].CompletedOn" value="${completed}" max="2026-03-01" data-field="completed"${status === 'Completed' ? '' : ' disabled'} />
    <div class="text-danger small d-none" data-row-error role="alert"></div>
    <input type="hidden" name="Input.Rows[0].ClearActualStart" value="false" data-clear-start />
  </section>`;
}

function editor(stages) {
  const dom = new JSDOM(`<div id="offcanvasActualDates"><form method="post" data-actuals-form data-today="2026-03-01">
    <fieldset data-actuals-list><div class="d-grid gap-3">${stages.map(row).join('')}</div></fieldset>
    <textarea data-actuals-note></textarea>
    <button class="btn btn-primary" type="submit" data-actuals-save disabled>Submit for approval</button>
  </form></div>`, { runScripts: 'outside-only' });
  dom.window.HTMLElement.prototype.scrollIntoView = () => {};
  dom.window.eval(source);
  const doc = dom.window.document;
  const rowOf = (code) => doc.querySelector(`[data-stage-code="${code}"]`);
  return {
    dom,
    doc,
    error: (code) => {
      const el = rowOf(code).querySelector('[data-row-error]');
      return el.classList.contains('d-none') ? '' : el.textContent;
    },
    clear: (code) => rowOf(code).querySelector('[data-clear-start]').value,
    submitDisabled: () => doc.querySelector('[data-actuals-save]').disabled,
    input(code, field, value) {
      const element = rowOf(code).querySelector(`[data-field="${field}"]`);
      element.value = value;
      element.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    }
  };
}

const coordinatedCase = () => editor([
  { code: 'SOW', name: 'SOW', status: 'Completed', start: '2026-02-01', completed: '2026-02-10' },
  { code: 'IPA', name: 'IPA', status: 'Completed', start: '2026-02-11', completed: '2026-02-20' },
  { code: 'AON', name: 'AON', status: 'InProgress', start: '2026-02-21' },
  { code: 'BM', name: 'BM', status: 'NotStarted' }
]);

test('moving a completion past the successor start explains the conflict on the successor row', () => {
  const ui = coordinatedCase();
  assert.equal(ui.submitDisabled(), true);

  ui.input('SOW', 'completed', '2026-02-12');

  assert.equal(ui.error('SOW'), '');
  assert.match(ui.error('IPA'), /^Actual dates cannot precede SOW completion \(.+\)\. Same-day commencement is permitted\./);
  assert.equal(ui.error('AON'), '');
  assert.equal(ui.error('BM'), '', 'an untouched NotStarted row is not flagged for its status');
  assert.equal(ui.submitDisabled(), true);
  ui.dom.window.close();
});

test('correcting the successor clears its message and enables submission', () => {
  const ui = coordinatedCase();
  ui.input('SOW', 'completed', '2026-02-12');
  ui.input('IPA', 'start', '2026-02-12');

  assert.equal(ui.error('IPA'), '', 'same-day commencement is permitted');
  assert.equal(ui.submitDisabled(), false);
  ui.dom.window.close();
});

test('reverting the predecessor clears the successor message', () => {
  const ui = coordinatedCase();
  ui.input('SOW', 'completed', '2026-02-12');
  ui.input('SOW', 'completed', '2026-02-10');

  assert.equal(ui.error('IPA'), '');
  assert.equal(ui.submitDisabled(), true, 'nothing changed');
  ui.dom.window.close();
});

test('a moved boundary that the successor still satisfies raises no message', () => {
  const ui = coordinatedCase();
  ui.input('SOW', 'completed', '2026-02-11');

  assert.equal(ui.error('IPA'), '');
  assert.equal(ui.submitDisabled(), false);
  ui.dom.window.close();
});

test('every row is re-validated, so a later conflict is shown alongside an earlier one', () => {
  const ui = coordinatedCase();
  ui.input('SOW', 'completed', '2026-02-12');
  ui.input('IPA', 'completed', '2026-02-22');

  assert.match(ui.error('IPA'), /cannot precede SOW completion/);
  assert.match(ui.error('AON'), /cannot precede IPA completion/);
  assert.equal(ui.submitDisabled(), true);
  ui.dom.window.close();
});

test('skipped stages stay transparent when the boundary moves', () => {
  const ui = editor([
    { code: 'SOW', name: 'SOW', status: 'Completed', start: '2026-02-01', completed: '2026-02-10' },
    { code: 'IPA', name: 'IPA', status: 'Skipped' },
    { code: 'AON', name: 'AON', status: 'Completed', start: '2026-02-11', completed: '2026-02-20' }
  ]);
  ui.input('SOW', 'completed', '2026-02-12');

  assert.equal(ui.error('IPA'), '');
  assert.match(ui.error('AON'), /cannot precede SOW completion/);
  ui.dom.window.close();
});

test('an explicit start clear is unchanged and still checked by its completion', () => {
  const ui = coordinatedCase();
  ui.input('IPA', 'start', '');
  assert.equal(ui.clear('IPA'), 'true');
  assert.equal(ui.error('IPA'), '');

  ui.input('SOW', 'completed', '2026-02-12');
  assert.equal(ui.clear('IPA'), 'true');
  assert.equal(ui.error('IPA'), '', 'completion 20 Feb is after the moved boundary');
  assert.equal(ui.submitDisabled(), false);
  ui.dom.window.close();
});

test('retained dates on an untouched Skipped row never block a predecessor correction', () => {
  // Legacy skipped rows may keep dates (LegacySkippedReopenIntegrityTests); the server ignores them.
  const ui = editor([
    { code: 'SOW', name: 'SOW', status: 'Completed', start: '2026-02-01', completed: '2026-02-10' },
    { code: 'IPA', name: 'IPA', status: 'Skipped', start: '2026-02-11', completed: '2026-02-11' },
    { code: 'AON', name: 'AON', status: 'Completed', start: '2026-02-15', completed: '2026-02-20' }
  ]);
  ui.input('SOW', 'completed', '2026-02-12');

  assert.equal(ui.error('IPA'), '');
  assert.equal(ui.error('AON'), '');
  assert.equal(ui.submitDisabled(), false);
  ui.dom.window.close();
});

// Default workflow branch: BID -> TEC and BID -> BM in parallel; COB depends on both.
const branchingCase = () => editor([
  { code: 'BID', name: 'BID', status: 'Completed', start: '2026-01-01', completed: '2026-01-10', preds: 'AON' },
  { code: 'TEC', name: 'TEC', status: 'Completed', start: '2026-01-11', completed: '2026-01-20', preds: 'BID' },
  { code: 'BM', name: 'BM', status: 'Completed', start: '2026-01-15', completed: '2026-01-25', preds: 'BID' },
  { code: 'COB', name: 'COB', status: 'Completed', start: '2026-01-26', completed: '2026-02-05', preds: 'TEC,BM' }
]);

test('a parallel sibling is not bounded by its sibling branch', () => {
  const ui = branchingCase();
  ui.input('TEC', 'completed', '2026-01-24');

  assert.equal(ui.error('BM'), '', 'BM depends on BID, not TEC');
  assert.equal(ui.doc.querySelector('[data-stage-code="BM"] [data-field="start"]').min, '2026-01-10');
  assert.equal(ui.error('COB'), '');
  assert.equal(ui.submitDisabled(), false);
  ui.dom.window.close();
});

test('a join stage is bounded by the latest completion across all its branches', () => {
  const ui = branchingCase();
  ui.input('TEC', 'completed', '2026-01-28');

  assert.match(ui.error('COB'), /^Actual dates cannot precede TEC completion/);
  assert.equal(ui.doc.querySelector('[data-stage-code="COB"] [data-field="start"]').min, '2026-01-28');
  assert.equal(ui.submitDisabled(), true);
  ui.dom.window.close();
});

test('the graph looks through a skipped predecessor to its own predecessors', () => {
  const ui = editor([
    { code: 'BID', name: 'BID', status: 'Completed', start: '2026-01-01', completed: '2026-01-10', preds: '' },
    { code: 'TEC', name: 'TEC', status: 'Skipped', preds: 'BID' },
    { code: 'BM', name: 'BM', status: 'Completed', start: '2026-01-11', completed: '2026-01-12', preds: 'BID' },
    { code: 'COB', name: 'COB', status: 'Completed', start: '2026-01-13', completed: '2026-01-20', preds: 'TEC,BM' }
  ]);
  ui.input('BID', 'completed', '2026-01-14');

  assert.match(ui.error('BM'), /cannot precede BID completion/);
  assert.match(ui.error('COB'), /cannot precede BID completion/, 'reached through skipped TEC');
  ui.dom.window.close();
});
