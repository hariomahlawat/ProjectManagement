'use strict';

// Phones (docs/ux/collage-usability-plan.md, PR 2): the three choices sit in one row on the
// first screen, and the chosen section starts on it too.

const { test, expect } = require('./test');
const { openFresh } = require('./helpers');

test('on a phone the three choices fit one row on the first screen', async ({ page }) => {
  const errors = await openFresh(page);

  const boxes = await page.locator('[data-start-choice]').evaluateAll((buttons) => buttons.map((b) => b.getBoundingClientRect().toJSON()));
  expect(boxes).toHaveLength(3);
  expect(new Set(boxes.map((box) => Math.round(box.top))).size).toBe(1);
  const height = page.viewportSize().height;
  expect(boxes[0].bottom).toBeLessThan(height);
  for (const box of boxes) expect(box.height).toBeGreaterThanOrEqual(44);
  // Nothing goes wider than the screen.
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  await page.locator('[data-start-choice="collage"]').tap();
  const layoutsTop = await page.locator('[data-start-layouts]').evaluate((el) => el.getBoundingClientRect().top);
  expect(layoutsTop).toBeLessThan(height);

  expect(errors).toEqual([]);
});
