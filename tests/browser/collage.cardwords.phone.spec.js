'use strict';

// One place to type a card's words, on a phone (docs/ux/collage-usability-plan.md, PR 5): the
// settings sheet's "Edit in Card details" opens the Card sheet with the cursor in the field.

const { test, expect } = require('./test');
const { openFresh, startOn, newestDraft } = require('./helpers');

test('on a phone, Edit in Card details opens the Card sheet with the cursor in the field, in view', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'card');
  await page.locator('[data-start-design="classic"]').click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  // Close the Card sheet the card opens on, then pick the venue in Layers.
  await page.locator('[data-action="close-drawer"]').click();
  await page.getByRole('button', { name: 'Layers' }).click();
  await page.locator('[data-layer-select]').filter({ hasText: 'Officers’ Mess' }).click();
  await page.locator('.clg-props [data-props-tab="settings"]').click();
  await expect(page.locator('.clg-props [data-text]')).toHaveCount(0);
  await page.getByRole('button', { name: 'Edit in Card details' }).click();

  const venue = page.locator('[data-card-field="venue"]');
  await expect(venue).toBeFocused();
  await expect(venue).toBeInViewport();
  await page.keyboard.type('Sudarshan Hall');
  await venue.blur();
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.field === 'venue')?.text).toBe('Sudarshan Hall');
  expect(errors).toEqual([]);
});
