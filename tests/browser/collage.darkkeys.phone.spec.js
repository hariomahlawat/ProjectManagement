'use strict';

// The phone start screen (PR 8 of docs/ux/collage-review-3-fixes.md): cards come first in a row
// that scrolls sideways; the Collage section's layouts use the full width, then the blank sizes.

const { test, expect } = require('./test');
const { openFresh, startOn } = require('./helpers');

test('on a phone the start screen uses the full width and shows page sizes sooner', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const errors = await openFresh(page);
  // Cards come first (docs/ux/collage-usability-plan.md, PR 2): on one row that scrolls sideways,
  // near the top (cold review, 19 Nov: on a phone they were three screens down).
  const cardsTop = await page.locator('[data-start-designs]').evaluate((element) => element.getBoundingClientRect().top);
  expect(cardsTop).toBeLessThan(450);
  const across = await page.locator('[data-start-designs] > *').evaluateAll((cards) => new Set(cards.map((card) => Math.round(card.getBoundingClientRect().top))).size);
  expect(across).toBe(1);
  // In the Collage section, layouts use the full width, three across, and the blank page sizes
  // follow on the second screen.
  await startOn(page, 'collage');
  const row = await page.evaluate(() => {
    const grid = document.querySelector('[data-start-layouts]').getBoundingClientRect();
    const cards = [...document.querySelectorAll('[data-start-layouts] > *')].map((card) => card.getBoundingClientRect());
    const firstRow = cards.filter((card) => Math.abs(card.top - cards[0].top) < 2);
    return { across: firstRow.length, used: Math.max(...firstRow.map((card) => card.right)) - grid.left, width: grid.width };
  });
  expect(row.across).toBe(3);
  expect(row.used).toBeGreaterThan(row.width - 2);
  const sizesTop = await page.locator('[data-sizes]').evaluate((element) => element.getBoundingClientRect().top);
  expect(sizesTop).toBeLessThan(1688);
  // Nothing on the start screen runs past the screen's edge.
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  expect(errors).toEqual([]);
});
