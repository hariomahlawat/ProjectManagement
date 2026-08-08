const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

async function loadModule() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'notebook-textarea-autosize-test-'));
  fs.writeFileSync(path.join(tempDir, 'package.json'), '{"type":"module"}');
  fs.copyFileSync(
    path.resolve(__dirname, 'notebook-textarea-autosize.js'),
    path.join(tempDir, 'notebook-textarea-autosize.js')
  );
  return import(`file://${path.join(tempDir, 'notebook-textarea-autosize.js')}`);
}

test('resizeTextareaToContent uses the configured minimum for short content', async () => {
  const { resizeTextareaToContent } = await loadModule();
  const textarea = { scrollHeight: 18, style: {} };

  const result = resizeTextareaToContent(textarea, { minimumHeight: 38, maximumHeight: 150 });

  assert.deepEqual(result, { height: 38, overflowing: false });
  assert.equal(textarea.style.height, '38px');
  assert.equal(textarea.style.overflowY, 'hidden');
});

test('resizeTextareaToContent grows to measured content height', async () => {
  const { resizeTextareaToContent } = await loadModule();
  const textarea = { scrollHeight: 96, style: {} };

  const result = resizeTextareaToContent(textarea, { minimumHeight: 38, maximumHeight: 150 });

  assert.deepEqual(result, { height: 96, overflowing: false });
  assert.equal(textarea.style.height, '96px');
});

test('resizeTextareaToContent caps excessive content and enables scrolling', async () => {
  const { resizeTextareaToContent } = await loadModule();
  const textarea = { scrollHeight: 280, style: {} };

  const result = resizeTextareaToContent(textarea, { minimumHeight: 38, maximumHeight: 150 });

  assert.deepEqual(result, { height: 150, overflowing: true });
  assert.equal(textarea.style.height, '150px');
  assert.equal(textarea.style.overflowY, 'auto');
});
