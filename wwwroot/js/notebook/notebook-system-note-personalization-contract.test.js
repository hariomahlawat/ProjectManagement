const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');

const model = read('Models', 'NotebookSystemItemPreference.cs');
const service = read('Services', 'Notebook', 'NotebookSystemItemPreferenceService.cs');
const controller = read('Controllers', 'Api', 'NotebookSystemItemsController.cs');
const migration = read('Migrations', '20261207200000_AddNotebookSystemItemPreferences.cs');
const snapshot = read('Migrations', 'ApplicationDbContextModelSnapshot.cs');
const app = read('wwwroot', 'js', 'notebook', 'notebook-app.js');
const drag = read('wwwroot', 'js', 'notebook', 'notebook-drag-order.js');
const api = read('wwwroot', 'js', 'notebook', 'notebook-api.js');
const partial = read('Pages', 'Notebook', '_NotebookConferenceDigest.cshtml');

test('system note persistence stores presentation metadata only', () => {
  assert.match(model, /ShowInHome/);
  assert.match(model, /IsPinned/);
  assert.match(model, /HomePosition/);
  assert.match(model, /ColorKey/);
  assert.match(model, /ICollection<NotebookSystemItemTag> Tags/);
  assert.doesNotMatch(model, /DirectionText|BodyMarkdown|ConferenceDirection/);
  assert.match(migration, /CreateTable\(\s*name: "NotebookSystemItemPreferences"/);
  assert.match(migration, /CreateTable\(\s*name: "NotebookSystemItemTags"/);
  assert.match(snapshot, /ProjectManagement\.Models\.NotebookSystemItemPreference/);
  assert.match(snapshot, /WithMany\("SystemItems"\)/);
});

test('preference service is command-scoped and reuses the users own Notebook labels', () => {
  assert.match(service, /RoleNames\.Comdt/);
  assert.match(service, /RoleNames\.HoD/);
  assert.match(service, /SyncLabelsAsync/);
  assert.match(service, /_db\.NotebookTags/);
  assert.match(service, /NotebookSystemItemKeys\.ConferenceDirections/);
});

test('system-note API exposes only personal presentation mutations', () => {
  assert.match(controller, /\[Authorize\(Roles = RoleNames\.Comdt \+ "," \+ RoleNames\.HoD\)\]/);
  assert.match(controller, /\[HttpPatch\("\{key\}"\)\]/);
  assert.match(controller, /\[HttpPut\("\{key\}\/placement"\)\]/);
  assert.match(controller, /ShowInHome/);
  assert.match(controller, /IsPinned/);
  assert.match(controller, /ColorKey/);
  assert.match(controller, /Labels/);
  assert.doesNotMatch(controller, /DirectionText|ReminderAtUtc|Archive|Complete/);
});

test('client supports colour labels pin add remove and dedicated placement persistence', () => {
  assert.match(partial, /data-action="system-pin-note"/);
  assert.match(partial, /_NotebookColourPicker/);
  assert.match(partial, /data-action="label-note"/);
  assert.match(partial, /data-action="system-add-home"/);
  assert.match(partial, /data-action="system-remove-home"/);
  assert.match(app, /updateSystemPreferenceWithSingleRetry\(key, \{ colorKey \}\)/);
  assert.match(app, /updateSystemPreferenceWithSingleRetry\(key, \{ labels \}\)/);
  assert.match(api, /setSystemItemPlacement/);
  assert.match(drag, /api\.setSystemItemPlacement/);
  assert.match(drag, /serialiseBoard\(board\)/);
  assert.match(drag, /return ownedCards\(board\)/);
});
