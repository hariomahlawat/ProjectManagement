const fs = require('fs');
const path = require('path');
const assert = require('assert');

const root = path.resolve(__dirname, '../../..');
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8');

function test(name, fn) {
  try {
    fn();
    console.log(`PASS ${name}`);
  } catch (error) {
    console.error(`FAIL ${name}`);
    console.error(error.message);
    process.exitCode = 1;
  }
}

test('activities index uses the actual ActivityMediaPreview contract type', () => {
  const source = read('Pages/Activities/Index.cshtml.cs');
  assert.doesNotMatch(source, /ActivityMediaPreviewDto/);
  assert.match(source, /BuildPhotoThumbnailUrl\(\s*ActivityMediaPreview\s+media,/);
});

test('activity details avoids inline Razor else transition that breaks parsing', () => {
  const view = read('Pages/Activities/Details.cshtml');
  assert.doesNotMatch(view, /else\s*\{\s*@:/);
  assert.match(view, /if \(activity\.LastModifiedAtUtc is \{ \} updated\)[\s\S]*?else[\s\S]*?<span>—<\/span>/);
});
