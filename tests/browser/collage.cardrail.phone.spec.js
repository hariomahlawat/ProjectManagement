'use strict';

// On a phone, a card's bottom rail fits the screen (docs/ux/collage-usability-plan.md, round 2,
// PR 1): with Layouts gone, every button, Layers too, is whole and can be tapped.

const { test, expect } = require('./test');
const { openFresh, startOn } = require('./helpers');

test('on a phone a card\'s rail buttons all fit the screen', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'card');
  await page.locator('[data-start-design="classic"]').click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  const width = page.viewportSize().width;
  const boxes = await page.locator('.clg-rail > button').evaluateAll((buttons) => buttons
    .filter((button) => !button.hidden && getComputedStyle(button).display !== 'none')
    .map((button) => ({ name: button.textContent.trim(), left: button.getBoundingClientRect().left, right: button.getBoundingClientRect().right })));
  expect(boxes.map((box) => box.name)).toContain('Layers');
  for (const box of boxes) {
    expect(box.left, box.name).toBeGreaterThanOrEqual(0);
    expect(box.right, box.name).toBeLessThanOrEqual(width);
  }
  expect(errors).toEqual([]);
});
