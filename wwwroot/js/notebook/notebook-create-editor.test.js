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
    .concat('\nmodule.exports = { toIstIso, parseLabels, buildCreatePayload };');
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

test('parseLabels trims, removes blanks and deduplicates', () => {
  const { parseLabels } = loadHelpers();
  assert.deepEqual(Array.from(parseLabels(' docs, procurement, docs, ')), ['docs', 'procurement']);
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
