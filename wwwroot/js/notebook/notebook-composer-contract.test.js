const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const markup = fs.readFileSync(
  path.resolve(__dirname, '../../../Pages/Notebook/Index.cshtml'),
  'utf8'
);

test('quick composer uses explicit styled controls instead of native browser buttons', () => {
  assert.match(markup, /class="notebook-composer__checklist-trigger"[^>]*data-composer-open-checklist/);
  assert.match(markup, /class="notebook-composer__reminder-trigger"[^>]*data-notebook-create-type="Reminder"/);
  assert.match(markup, /class="notebook-composer__pin"[^>]*data-composer-pin[^>]*aria-pressed="false"/);
  assert.match(markup, /class="notebook-composer__close"[^>]*data-composer-close/);
});


test('quick composer exposes lightweight colour and label controls', () => {
  assert.match(markup, /notebook-composer__tools/);
  assert.match(markup, /<partial name="_NotebookColourPicker"/);
  assert.match(markup, /<partial name="_NotebookLabelPicker"/);
});

test('home boards opt into deterministic masonry in grid view', () => {
  assert.match(markup, /data-notebook-board="pinned"[^>]*data-layout-policy="masonry-always"[^>]*data-layout="masonry"/);
  assert.match(markup, /data-notebook-board="others"[^>]*data-layout-policy="masonry-always"[^>]*data-layout="masonry"/);
});
