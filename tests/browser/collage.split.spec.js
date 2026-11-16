'use strict';

// The editor split, part 2 (docs/ux/collage-review-3-fixes.md, PR 11): the settings tabs name their
// panel, and the busy line is announced.

const { test, expect } = require('./test');
const { openFresh, photoFile, showLayers, showSettings } = require('./helpers');

test('the settings tabs control one tab panel, named by the tab shown', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'one.jpg' }));
  const panel = page.locator('.clg-props [role="tabpanel"]');
  await expect(panel).toHaveCount(1);
  await expect(page.getByRole('tab', { name: 'Settings' })).toHaveAttribute('aria-controls', 'clgPropsPanel');
  await expect(panel).toHaveAttribute('aria-labelledby', 'clgTab-settings');
  await showLayers(page);
  await expect(page.locator('.clg-props [role="tabpanel"]')).toHaveAttribute('aria-labelledby', 'clgTab-layers');
  await expect(page.locator('.clg-props [role="tabpanel"] [data-layer-select]')).toHaveCount(1);
  await showSettings(page);
  await expect(page.locator('.clg-props [role="tabpanel"] [data-caption]')).toBeVisible();
  expect(errors).toEqual([]);
});

test('the busy line is a status a screen reader announces', async ({ page }) => {
  await openFresh(page);
  await expect(page.locator('[data-busy]')).toHaveAttribute('role', 'status');
});
