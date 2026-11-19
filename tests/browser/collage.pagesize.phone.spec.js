'use strict';

// On a phone the page size is in the top bar (docs/ux/collage-usability-plan.md, round 2, PR 2):
// before, it was hidden there and reached only through Layers, Settings and a list of 30 sizes.

const { test, expect } = require('./test');
const { openFresh, startOn } = require('./helpers');

test('on a phone the size menu is in the one-line top bar and changes the page', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  const size = page.locator('.clg-bar [data-size]');
  await expect(size).toBeVisible();
  await expect(size).toBeInViewport({ ratio: 1 });
  // The bar is still one line, and nothing runs past the screen.
  const tops = await page.locator('.clg-bar > :not([hidden])').evaluateAll((items) => [...new Set(items
    .filter((item) => getComputedStyle(item).display !== 'none' && item.getBoundingClientRect().width > 0 && !item.classList.contains('visually-hidden'))
    .map((item) => Math.round(item.getBoundingClientRect().top / 8)))]);
  expect(tops.length).toBe(1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  await size.selectOption('square');
  await expect(size).toHaveValue('square');
  const shape = await page.locator('[data-konva] canvas').first().evaluate((canvas) => canvas.width / canvas.height);
  expect(shape).toBeGreaterThan(0);
  expect(errors).toEqual([]);
});
