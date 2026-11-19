'use strict';

// Help that fits the page (docs/ux/collage-usability-plan.md, PR 6): with nothing selected, the
// settings panel shows one line of help for what is open, and Tips opens every tip.

const { test, expect } = require('./test');
const { openFresh, startOn } = require('./helpers');

test('a collage shows one line of help, and Tips opens every tip and closes again', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 landscape/ }).click();

  const props = page.locator('.clg-props');
  await expect(props.locator('[data-help-line]')).toHaveText('Click a photo to change it; drag it to move it.', { useInnerText: true });
  // The long list is no longer in the panel.
  await expect(props.locator('.clg-tips')).toHaveCount(0);
  await expect(props).not.toContainText('Shift gives 15° steps');

  await props.getByRole('button', { name: 'Tips' }).click();
  const dialog = page.locator('[data-help-dialog]');
  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('heading', { name: 'Tips' })).toBeVisible();
  await expect(dialog).toContainText('Shift gives 15° steps');
  await expect(dialog).toContainText('Ctrl+Z undoes');
  await dialog.getByRole('button', { name: 'Close' }).last().click();
  await expect(dialog).toBeHidden();

  // Escape closes it too.
  await props.getByRole('button', { name: 'Tips' }).click();
  await expect(dialog).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  expect(errors).toEqual([]);
});

test('a card\'s line of help speaks of Card details, not of photo handles', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'card');
  await page.locator('[data-start-design="classic"]').click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();

  const line = page.locator('.clg-props [data-help-line]');
  await expect(line).toHaveText('Type the words in Card details, or click anything on the card to move or restyle it.', { useInnerText: true });
  await expect(page.locator('.clg-props')).not.toContainText('Drag a corner to resize');
  expect(errors).toEqual([]);
});
