const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');
const app = read('wwwroot', 'js', 'notebook', 'notebook-app.js');
const css = read('wwwroot', 'css', 'notebook.css');
const partial = read('Pages', 'Notebook', '_NotebookConferenceDigest.cshtml');

test('system colour palette is collision-positioned within the Notebook content viewport', () => {
  assert.match(app, /positionSystemColourPopover/);
  assert.match(app, /shell\.querySelector\('\.notebook-main'\)/);
  assert.match(app, /getBoundingClientRect\(\)/);
  assert.match(app, /const preferredLeft = pickerRect\.right - popoverRect\.width/);
  assert.match(app, /data\.floatingPlacement|dataset\.floatingPlacement/);
  assert.match(app, /window\.addEventListener\('resize', scheduleOpenSystemColourReposition/);
});

test('system card releases overflow only while a floating surface is open', () => {
  assert.match(css, /\.notebook-conference-shared-card\.has-open-popover[\s\S]*?overflow:\s*visible/);
  assert.match(css, /\.notebook-conference-shared-card:has\(\.notebook-colour-picker__popover:not\(\[hidden\]\)\)/);
});

test('system more menu has enough width for the remove command', () => {
  assert.match(css, /\.notebook-conference-shared-card \.notebook-card-more__menu[\s\S]*?min-width:\s*224px/);
  assert.match(css, /\.notebook-conference-shared-card \.notebook-card-more__menu button[\s\S]*?white-space:\s*nowrap/);
});

test('Shared surface exposes and updates My Notebook placement state', () => {
  assert.match(partial, /data-system-home-state/);
  assert.match(partial, /data-system-home-control/);
  assert.match(app, /renderSystemHomeControl/);
  assert.match(app, /homeState\.hidden = !showInHome/);
});
