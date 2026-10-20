const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const source = fs.readFileSync(path.resolve(__dirname, 'actuals-edit.js'), 'utf8');

test('actuals editor derives chronology from the current projected predecessor values', () => {
  assert.match(source, /const resolveChronologyBoundary/);
  assert.match(source, /facts\.status === 'skipped'/);
  assert.match(source, /facts\.status === 'completed'/);
  assert.match(source, /inputOf\(row, field\)\?\.value/);
  assert.match(source, /refreshChronologyBoundaries/);
});

test('actuals editor permits equality and rejects dates before the projected boundary', () => {
  assert.match(source, /chronologyDate = startDate \|\| completedDate/);
  assert.match(source, /chronologyDate < earliestStartDate/);
  assert.match(source, /Same-day commencement is permitted/);
});
