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
  const context = { module: { exports: {} }, exports: {}, Set, String, Boolean, toIstIsoFromParts: (date, time) => date && time ? `${date}T${time}:00+05:30` : null };
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

test('getCreateTypeUi exposes only structural creation types', () => {
  const { getCreateTypeUi } = loadHelpers();

  const reminder = getCreateTypeUi('Reminder');
  assert.equal(reminder.titlePlaceholder, 'Reminder title');
  assert.equal(reminder.openDetails, true);
  assert.equal(reminder.showReminderScheduler, true);

  const checklist = getCreateTypeUi('Checklist');
  assert.equal(checklist.showChecklist, true);
  assert.equal(checklist.showBody, false);

  for (const legacyType of ['Idea', 'Draft', 'Sticky']) {
    const normalized = getCreateTypeUi(legacyType);
    assert.equal(normalized.type, 'Note');
    assert.equal(normalized.actionLabel, 'Create note');
  }
});

test('create editor template contains only Note, Checklist and Reminder options', () => {
  const template = fs.readFileSync(path.join(__dirname, '../../../Pages/Notebook/_NotebookEditorTemplate.cshtml'), 'utf8');
  const optionValues = [...template.matchAll(/<option value="([^"]+)"/g)].map((match) => match[1]);
  assert.deepEqual(optionValues.filter((value) => ['Note', 'Checklist', 'Reminder', 'Idea', 'Draft', 'Sticky'].includes(value)), ['Note', 'Checklist', 'Reminder']);
});

test('buildCreatePayload includes reminder and checklist data only for relevant types', () => {
  const { buildCreatePayload } = loadHelpers();
  const reminder = buildCreatePayload({ type: 'Reminder', title: 'Brief', body: '', reminderDate: '2026-07-01', reminderTime: '10:00', priority: 'High', labels: 'ops', checklistRows: [], clientRequestId: 'id' });
  assert.equal(reminder.reminderAtUtc, '2026-07-01T10:00:00+05:30');
  assert.equal(reminder.type, 'Reminder');
  assert.deepEqual(Array.from(reminder.labels), ['ops']);

  const checklist = buildCreatePayload({ type: 'Checklist', title: '', body: '', checklistRows: [{ text: ' First ' }, { text: '' }], clientRequestId: 'id' });
  assert.equal(checklist.checklistRows.length, 1);
  assert.equal(checklist.checklistRows[0].text, 'First');
  assert.equal(checklist.reminderAtUtc, null);
});


test('reminder template uses resilient date and time controls instead of datetime-local', () => {
  const template = fs.readFileSync(path.join(__dirname, '../../../Pages/Notebook/_NotebookEditorTemplate.cshtml'), 'utf8');
  assert.match(template, /data-reminder-date/);
  assert.match(template, /data-reminder-time/);
  assert.match(template, /data-reminder-preset="tomorrow-morning"/);
  assert.doesNotMatch(template, /type="datetime-local"/);
  assert.match(template, /data-notebook-create-discard/);
});

test('create editor protects backdrop clicks and exposes protected close', () => {
  const source = fs.readFileSync(path.join(__dirname, 'notebook-create-editor.js'), 'utf8');
  assert.match(source, /notebook-modal__backdrop/);
  assert.match(source, /async function requestClose/);
  assert.match(source, /persistDraftNow\(\)/);
});


test('direct reminder creation treats scheduling as primary and hides the redundant type selector', () => {
  const source = fs.readFileSync(path.join(__dirname, 'notebook-create-editor.js'), 'utf8');
  assert.match(source, /directReminderMode = safeType === 'Reminder'/);
  assert.match(source, /elements\.typeField\.hidden = directReminderMode/);
  assert.match(source, /elements\.detailsToggle\.hidden = ui\.showReminderScheduler/);
});
