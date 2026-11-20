'use strict';

// Fewer choices for new users (docs/ux/collage-usability-plan.md, round 3, PR 4, the owner's
// decision of 20 Nov): the common card sizes first with "More sizes…", and A4 sheets and own
// pages first in Save, with A3 and edge to edge under "More sheets".

const { test, expect } = require('./test');
const { openFresh, startOn } = require('./helpers');

const shownSizes = (page) => page.locator('[data-card-size] option').evaluateAll((options) => options.filter((o) => !o.hidden).map((o) => o.value));

test('the card size menu shows the common sizes, then every size after More sizes…', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'card');
  expect(await shownSizes(page)).toEqual(['a5p', 'a5l', 'a6p', 'a6l', 'a7p', 'fold6', 'more', 'custom']);
  await page.locator('[data-card-size]').selectOption('more');
  // The size stays as it was; every size is listed now, and More sizes… is gone.
  await expect(page.locator('[data-card-size]')).toHaveValue('a5p');
  const all = await shownSizes(page);
  expect(all).toContain('dlp');
  expect(all).toContain('passl');
  expect(all).not.toContain('more');
  expect(all.length).toBeGreaterThan(20);
  expect(errors).toEqual([]);
});

test('Save shows A4 sheets and own pages; A3 and edge to edge wait under More sheets', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'card');
  await page.locator('[data-card-size]').selectOption('a6p');
  await page.locator('[data-start-design="classic"]').click();
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'Printing' }).click();
  await expect(page.locator('[data-save-sheet] input[value="a4"]')).toBeVisible();
  await expect(page.locator('[data-save-sheet] input[value="card"]')).toBeVisible();
  const more = page.locator('[data-more-sheets]');
  await expect(more).not.toHaveAttribute('open', '');
  await expect(page.locator('[data-save-sheet] input[value="a3"]')).toBeHidden();
  await more.locator('summary').click();
  await expect(page.locator('[data-save-sheet] input[value="a3"]')).toBeVisible();
  expect(errors).toEqual([]);
});
