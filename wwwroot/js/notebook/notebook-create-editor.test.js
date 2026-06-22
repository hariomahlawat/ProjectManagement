const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadHelpers() {
  const file = path.join(__dirname, 'notebook-create-editor.js');
  let source = fs.readFileSync(file, 'utf8');
  source = source
    .replace(/^import .*;\r?\n/gm, '')
    .replace(/export function /g, 'function ')
    .concat('\nmodule.exports = { toIstIso, parseLabels, getCreateTypeUi, buildCreatePayload };');
  const context = { module: { exports: {} }, exports: {}, Set, String, Boolean };
  vm.createContext(context);
  vm.runInContext(source, context);
  return context.module.exports;
}

test('toIstIso creates an explicit IST offset', () => {
  const { toIstIso } = loadHelpers();
  assert.equal(toIstIso('2026-06-30T09:15'), '2026-06-30T09:15:00+05:30');
  assert.equal(toIstIso(''), null);
});

test('parseLabels trims, removes blanks and deduplicates case-insensitively', () => {
  const { parseLabels } = loadHelpers();
  assert.deepEqual(Array.from(parseLabels(' Docs, procurement, docs, ')), ['Docs', 'procurement']);
});

test('getCreateTypeUi uses human-readable sticky-note wording and adaptive layout', () => {
  const { getCreateTypeUi } = loadHelpers();
  const sticky = getCreateTypeUi('Sticky');
  assert.equal(sticky.actionLabel, 'Create sticky note');
  assert.equal(sticky.showBody, true);
  assert.equal(sticky.openDetails, false);

  const reminder = getCreateTypeUi('Reminder');
  assert.equal(reminder.titlePlaceholder, 'Reminder title');
  assert.equal(reminder.openDetails, true);

  const checklist = getCreateTypeUi('Checklist');
  assert.equal(checklist.showChecklist, true);
  assert.equal(checklist.showBody, false);
});

test('buildCreatePayload includes reminder and checklist data only for relevant types', () => {
  const { buildCreatePayload } = loadHelpers();
  const reminder = buildCreatePayload({ type: 'Reminder', title: 'Brief', body: '', reminderLocal: '2026-07-01T10:00', priority: 'High', labels: 'ops', checklistRows: [], clientRequestId: 'id' });
  assert.equal(reminder.reminderAtUtc, '2026-07-01T10:00:00+05:30');
  assert.equal(reminder.type, 'Reminder');
  assert.deepEqual(Array.from(reminder.labels), ['ops']);

  const checklist = buildCreatePayload({ type: 'Checklist', title: '', body: '', checklistRows: [{ text: ' First ' }, { text: '' }], clientRequestId: 'id' });
  assert.equal(checklist.checklistRows.length, 1);
  assert.equal(checklist.checklistRows[0].text, 'First');
  assert.equal(checklist.reminderAtUtc, null);
});
