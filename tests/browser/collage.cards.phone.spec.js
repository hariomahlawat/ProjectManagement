'use strict';

// Invitation cards on a phone (cold review, 19 Nov): typing in Card details makes the sheet
// shorter, so the card above stays big enough to read; quick picks are big enough to touch.

const { test, expect } = require('./test');
const { openFresh } = require('./helpers');

test('on a phone, typing in Card details leaves more of the card showing, and quick picks are 36 px tall', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await expect(page.locator('[data-card-field="venue"]')).toBeAttached();
  const sheetHeight = () => page.locator('[data-drawer]').evaluate((element) => element.getBoundingClientRect().height);
  const before = await sheetHeight();
  await page.locator('[data-card-field="venue"]').focus();
  await expect(page.locator('body')).toHaveClass(/is-card-typing/);
  await expect.poll(sheetHeight).toBeLessThan(before - 40);
  const pick = await page.locator('[data-card-pick]').first().evaluate((element) => element.getBoundingClientRect().height);
  expect(pick).toBeGreaterThanOrEqual(36);
  await page.locator('[data-card-field="venue"]').blur();
  await expect(page.locator('body')).not.toHaveClass(/is-card-typing/);
  expect(errors).toEqual([]);
});
