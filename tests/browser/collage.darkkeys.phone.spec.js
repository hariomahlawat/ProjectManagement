'use strict';

// The phone start screen (PR 8 of docs/ux/collage-review-3-fixes.md): layouts use the full
// width, and the blank page sizes come higher up.

const { test, expect } = require('./test');
const { openFresh } = require('./helpers');

test('on a phone the start screen uses the full width and shows page sizes sooner', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const errors = await openFresh(page);
  const row = await page.evaluate(() => {
    const grid = document.querySelector('[data-start-layouts]').getBoundingClientRect();
    const cards = [...document.querySelectorAll('[data-start-layouts] > *')].map((card) => card.getBoundingClientRect());
    const firstRow = cards.filter((card) => Math.abs(card.top - cards[0].top) < 2);
    return { across: firstRow.length, used: Math.max(...firstRow.map((card) => card.right)) - grid.left, width: grid.width };
  });
  expect(row.across).toBe(3);
  expect(row.used).toBeGreaterThan(row.width - 2);
  // Before, the page sizes started about 1,020 pixels down.
  const sizesTop = await page.locator('[data-sizes]').evaluate((element) => element.getBoundingClientRect().top);
  expect(sizesTop).toBeLessThan(760);
  // Nothing on the start screen runs past the screen's edge.
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  expect(errors).toEqual([]);
});
