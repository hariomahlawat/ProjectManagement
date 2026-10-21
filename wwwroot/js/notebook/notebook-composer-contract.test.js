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
  // The bell opens the same quick-add box with its reminder panel, not a separate dialog.
  assert.match(markup, /class="notebook-composer__reminder-trigger"[^>]*data-composer-open-reminder/);
  assert.doesNotMatch(markup, /notebook-composer__reminder-trigger"[^>]*data-notebook-create-type/);
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

test('quick composer can set a reminder and saves when clicking outside', () => {
  const composer = fs.readFileSync(path.resolve(__dirname, 'notebook-composer.js'), 'utf8');
  assert.match(markup, /data-composer-reminder-toggle/);
  assert.match(markup, /data-composer-reminder-panel[\s\S]*data-reminder-preset="tomorrow-morning"/);
  assert.match(composer, /reminderAtUtc: reminder\?\.iso \?\? null/);
  assert.match(composer, /document\.addEventListener\('pointerdown'[\s\S]*?closeComposer\(\);/);
});
