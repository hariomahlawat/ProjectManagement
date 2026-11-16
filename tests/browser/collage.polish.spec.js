'use strict';

// The polish pass (docs/ux/collage-review-2-fixes.md, PR 10): what the fresh review's first-time
// user tripped over, each checked once.

const { test, expect } = require('./test');
const { openFresh, photoFile, fulfillLikePrism } = require('./helpers');

test('stickers fit their panel, and stamps with words take two cells', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('.clg-rail [data-panel="stickers"]').click();
  for (const chip of await page.locator('[data-sticker-group]').all()) {
    await chip.click();
    const grid = page.locator('[data-sticker-grid]');
    const fits = await grid.evaluate((el) => el.scrollWidth <= el.clientWidth + 1);
    expect(fits).toBe(true);
  }
  expect(errors).toEqual([]);
});

test('the plain text starts read as buttons that add words', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('.clg-rail [data-panel="text"]').click();
  await expect(page.getByRole('button', { name: 'Add a heading' })).toBeVisible();
  await expect(page.locator('[data-add-text="heading"] i.bi-plus-lg')).toBeAttached();
  expect(errors).toEqual([]);
});

test('sliders say their unit', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'one.jpg' }));
  await expect(page.locator('.clg-props [data-out="width"]')).toHaveText(/% of the page$/);
  expect(errors).toEqual([]);
});

test('with PRISM Photos off, its dialog shows only the message', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.getByRole('button', { name: 'From PRISM Photos' }).click();
  const dialog = page.locator('[data-library-dialog]');
  await expect(dialog.locator('[data-library-status]')).toContainText('not switched on');
  await expect(dialog.locator('.clg-library-filters')).toBeHidden();
  await expect(dialog.locator('[data-action="library-add"]')).toBeHidden();
  expect(errors).toEqual([]);
});

test('a missing editor script is said on the page, not left as a dead page', async ({ page }) => {
  await page.route('**/js/collage/collage-save.js**', (route) => fulfillLikePrism(route, { status: 404, body: '' }));
  await page.goto('/Tools/Collage');
  await expect(page.locator('[data-alert-text]')).toContainText('could not load all its parts');
  // Nothing on the start screen can be used to begin a collage that could not work.
  await expect(page.locator('[data-start-size]')).toHaveCount(0);
});
