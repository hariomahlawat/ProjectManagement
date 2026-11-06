const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

// Toasts PR 2 (docs/ux/toasts.md): every script that shows its own toast hands the message to
// the shared glass toast (window.PrismToast) when it is on the page; its old toast code stays
// only as the fallback.
const jsRoot = path.join(__dirname, '..');
const scripts = [
  'pages/project-office-reports/social-media.js',
  'pages/project-office-reports/visits.js',
  'pages/proliferation-data-quality.js',
  'pages/proliferation-manage.js',
  'pages/workspace-briefing-desk.js',
  'process-flow.js',
  'projects/overview-multi-jdp.js',
  'projects/remarks-panel.js',
  'projects/stages.js',
  'docrepo-ui.js',
  'docrepo/upload-queue.js',
  'calendar.js',
  'tasks-page.js',
  'notebook/notebook-toast.js'
];

for (const file of scripts) {
  test(`${file} shows its messages in the shared glass toast`, () => {
    const source = fs.readFileSync(path.join(jsRoot, file), 'utf8');
    assert.match(source, /window\.PrismToast\.show\(/);
  });
}

test('nothing still waits for a global showToast that PRISM never defines', () => {
  for (const file of ['docrepo-ui.js', 'docrepo/upload-queue.js']) {
    const source = fs.readFileSync(path.join(jsRoot, file), 'utf8');
    assert.doesNotMatch(source, /window\.showToast/, file);
  }
});

test('Calendar offers one Undo at a time: a newer change dismisses the older offer', () => {
  const source = fs.readFileSync(path.join(jsRoot, 'calendar.js'), 'utf8');
  assert.match(source, /calendarUndo\?\.dismiss\(\);\s*calendarUndo = window\.PrismToast\.show\(/);
});
