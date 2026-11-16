// Save's pure rules (docs/ux/collage-review-3-fixes.md, PR 6): which purpose a type and quality
// match, the line saying what Save will make, and where Tab goes at the ends of the dialog.
const test = require('node:test');
const assert = require('node:assert/strict');
const save = require('./collage-save.js');

test('each purpose sets a type and quality, and a hand-made choice matches no purpose', () => {
  assert.equal(save.purposeFor('jpeg', 'screen'), 'share');
  assert.equal(save.purposeFor('pdf', 'print'), 'print');
  assert.equal(save.purposeFor('png', 'screen'), 'slide');
  assert.equal(save.purposeFor('png', 'print'), null);
  assert.equal(save.purposeFor('jpeg', 'print'), null);
  assert.equal(save.purposeFor(undefined, undefined), null);
  for (const [name, choice] of Object.entries(save.SAVE_PURPOSES)) {
    assert.equal(save.purposeFor(choice.format, choice.quality), name);
  }
});

test('the line under the purposes says what Save will make', () => {
  assert.equal(
    save.saveSummary({ format: 'jpeg', quality: 'screen', pixels: { width: 1920, height: 1358 } }),
    'A JPG picture at screen size: 1,920 × 1,358 pixels.');
  assert.equal(
    save.saveSummary({ format: 'jpeg', quality: 'screen', pixels: { width: 1920, height: 1358 }, target: '500 KB' }),
    'A JPG picture at screen size: 1,920 × 1,358 pixels, kept under 500 KB.');
  assert.equal(
    save.saveSummary({ format: 'pdf', quality: 'print', pixels: { width: 3508, height: 2480 }, paper: 'A4 landscape' }),
    'A PDF at print quality: A4 landscape at its true size, 3,508 × 2,480 pixels.');
  assert.equal(
    save.saveSummary({ format: 'pdf', quality: 'screen', pixels: { width: 1920, height: 1080 } }),
    'A PDF at screen size: 1,920 × 1,080 pixels.');
  // A size to keep under belongs to a JPG only.
  assert.equal(
    save.saveSummary({ format: 'png', quality: 'print', pixels: { width: 3840, height: 2160 }, target: '500 KB' }),
    'A PNG picture at print quality: 3,840 × 2,160 pixels.');
});

test('Tab wraps at the ends of the dialog and is left alone in between', () => {
  const stops = ['close', 'share', 'more', 'save'];
  assert.equal(save.wrapFocus(stops, 'save', false), 'close');
  assert.equal(save.wrapFocus(stops, 'close', true), 'save');
  assert.equal(save.wrapFocus(stops, 'share', false), null);
  assert.equal(save.wrapFocus(stops, 'more', true), null);
  // Focus outside the stops (the dialog itself, or the page behind) comes back in.
  assert.equal(save.wrapFocus(stops, 'body', false), 'close');
  assert.equal(save.wrapFocus(stops, 'body', true), 'save');
  assert.equal(save.wrapFocus([], 'body', false), null);
});
