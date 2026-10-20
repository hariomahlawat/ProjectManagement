const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');
const source = fs.readFileSync(path.join(__dirname, 'actuals-edit.js'), 'utf8');

// Cross-stage chronology belongs to the server's shared Actual validator (C7.2). The
// editor must never block a post on chronology, natively (`min`) or inline: the server
// accepts or names the affected stage, and a refused post reopens with the entries.
// Markup follows _EditActualsForm.cshtml.
function row({ code, status, start = '', completed = '', officialStart = start }) {
  const editable = status === 'Completed' || status === 'InProgress';
  return `<section class="actuals-editor__stage" data-actuals-row data-stage-code="${code}" data-stage-name="${code}"
      data-stage-status="${status}" data-earliest-start="" data-official-start="${officialStart}" data-start-source="">
    <input type="date" class="form-control" name="Input.Rows[0].ActualStart" value="${start}" max="2026-03-01" data-field="start"${editable ? '' : ' disabled'} />
    <input type="date" class="form-control" name="Input.Rows[0].CompletedOn" value="${completed}" max="2026-03-01" data-field="completed"${status === 'Completed' ? '' : ' disabled'} />
    <div class="text-danger small d-none" data-row-error role="alert"></div>
    <input type="hidden" name="Input.Rows[0].ClearActualStart" value="false" data-clear-start />
  </section>`;
}

function editor(stages, { restored = false } = {}) {
  const dom = new JSDOM(`<div id="offcanvasActualDates"><form method="post" data-actuals-form${restored ? ' data-actuals-restored="true"' : ''} data-today="2026-03-01">
    <fieldset data-actuals-list><div class="d-grid gap-3">${stages.map(row).join('')}</div></fieldset>
    <button class="btn btn-primary" type="submit" data-actuals-save disabled>Save changes</button>
  </form></div>`, { runScripts: 'outside-only' });
  dom.window.HTMLElement.prototype.scrollIntoView = () => {};
  dom.window.eval(source);
  const doc = dom.window.document;
  const rowOf = (code) => doc.querySelector(`[data-stage-code="${code}"]`);
  return {
    dom,
    errors: () => Array.from(doc.querySelectorAll('[data-row-error]:not(.d-none)')).map((el) => el.textContent),
    error: (code) => {
      const el = rowOf(code).querySelector('[data-row-error]');
      return el.classList.contains('d-none') ? '' : el.textContent;
    },
    submitDisabled: () => doc.querySelector('[data-actuals-save]').disabled,
    // Native constraint validation, as the browser applies it before a submit event fires.
    nativelyValid: () => doc.querySelector('form').checkValidity(),
    mins: () => Array.from(doc.querySelectorAll('input[type="date"]')).map((input) => input.min).filter(Boolean),
    input(code, field, value) {
      const element = rowOf(code).querySelector(`[data-field="${field}"]`);
      element.value = value;
      element.dispatchEvent(new dom.window.Event('input', { bubbles: true }));
    }
  };
}

const expectPostable = (ui) => {
  assert.deepEqual(ui.errors(), []);
  assert.deepEqual(ui.mins(), [], 'no native min can block the post');
  assert.equal(ui.nativelyValid(), true);
  assert.equal(ui.submitDisabled(), false);
};

test('C7.2: moving SOW completion past IPA start leaves the post to the server', () => {
  const ui = editor([
    { code: 'SOW', status: 'Completed', start: '2026-02-01', completed: '2026-02-10' },
    { code: 'IPA', status: 'Completed', start: '2026-02-11', completed: '2026-02-20' }
  ]);
  ui.input('SOW', 'completed', '2026-02-12');

  // Previously IPA's start got min=2026-02-12: the form failed native validation and only
  // the browser tooltip explained it. The server now names IPA in the reopened editor.
  expectPostable(ui);
  ui.dom.window.close();
});

// Each case is one where display-order client chronology disagreed with the server.
const serverDecidedCases = [
  {
    name: 'parallel sibling (TEC and BM both follow BID)',
    stages: [
      { code: 'BID', status: 'Completed', start: '2026-01-01', completed: '2026-01-10' },
      { code: 'TEC', status: 'Completed', start: '2026-01-11', completed: '2026-01-20' },
      { code: 'BM', status: 'Completed', start: '2026-01-15', completed: '2026-01-25' }
    ],
    edit: ['TEC', 'completed', '2026-01-24']
  },
  {
    name: 'converging join with a historical violation on another branch',
    stages: [
      { code: 'TEC', status: 'Completed', start: '2026-01-11', completed: '2026-01-20' },
      { code: 'BM', status: 'Completed', start: '2026-01-11', completed: '2026-01-25' },
      { code: 'COB', status: 'Completed', start: '2026-01-22', completed: '2026-02-05' }
    ],
    edit: ['TEC', 'completed', '2026-01-21']
  },
  {
    name: 'historical start violation with only the completion changed (P2)',
    stages: [
      { code: 'FS', status: 'Completed', start: '2026-01-01', completed: '2026-01-10' },
      { code: 'IPA', status: 'Completed', start: '2026-01-05', completed: '2026-01-15' }
    ],
    edit: ['IPA', 'completed', '2026-01-16']
  },
  {
    name: 'legacy skipped stage retaining dates',
    stages: [
      { code: 'SOW', status: 'Completed', start: '2026-02-01', completed: '2026-02-10' },
      { code: 'IPA', status: 'Skipped', start: '2026-02-11', completed: '2026-02-11' },
      { code: 'AON', status: 'Completed', start: '2026-02-15', completed: '2026-02-20' }
    ],
    edit: ['SOW', 'completed', '2026-02-12']
  },
  {
    name: 'retained PNC while PNC is not applicable',
    stages: [
      { code: 'COB', status: 'Completed', start: '2026-01-26', completed: '2026-02-05' },
      { code: 'PNC', status: 'Completed', start: '2026-02-03', completed: '2026-02-04' }
    ],
    edit: ['COB', 'completed', '2026-02-08']
  }
];

for (const { name, stages, edit } of serverDecidedCases) {
  test(`the editor does not decide chronology: ${name}`, () => {
    const ui = editor(stages);
    ui.input(...edit);
    expectPostable(ui);
    ui.dom.window.close();
  });
}

test('field-level checks on an edited row still block locally', () => {
  const ui = editor([
    { code: 'SOW', status: 'Completed', start: '2026-02-01', completed: '2026-02-10' },
    { code: 'IPA', status: 'InProgress', start: '2026-02-11' }
  ]);

  ui.input('SOW', 'start', '2026-02-15');
  assert.equal(ui.error('SOW'), 'Completion must be on or after the start date.');
  assert.equal(ui.submitDisabled(), true);

  ui.input('SOW', 'start', '2026-02-01');
  ui.input('SOW', 'completed', '2026-03-05');
  assert.equal(ui.error('SOW'), 'Completion date cannot be in the future.');

  ui.input('SOW', 'completed', '2026-02-10');
  ui.input('IPA', 'start', '');
  assert.match(ui.error('IPA'), /^A stage in progress needs its start date/);
  assert.equal(ui.error('SOW'), '', 'every row is re-validated, so a fixed row clears');
  assert.equal(ui.submitDisabled(), true);
  ui.dom.window.close();
});

test('a reopened refused post can be submitted again, corrected or as entered', () => {
  // Server-rendered with the user's entries (SOW 12 Feb) after the server named IPA.
  const ui = editor([
    { code: 'SOW', status: 'Completed', start: '2026-02-01', completed: '2026-02-12' },
    { code: 'IPA', status: 'Completed', start: '2026-02-11', completed: '2026-02-20' }
  ], { restored: true });
  expectPostable(ui);

  ui.input('IPA', 'start', '2026-02-12');
  expectPostable(ui);

  ui.input('IPA', 'start', '2026-02-25');
  assert.equal(ui.error('IPA'), 'Completion must be on or after the start date.');
  assert.equal(ui.submitDisabled(), true);
  ui.dom.window.close();
});

test('an untouched editor without a refused post stays disabled', () => {
  const ui = editor([{ code: 'SOW', status: 'Completed', start: '2026-02-01', completed: '2026-02-10' }]);
  assert.equal(ui.submitDisabled(), true);
  ui.dom.window.close();
});
