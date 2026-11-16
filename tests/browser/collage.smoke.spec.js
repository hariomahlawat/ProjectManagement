'use strict';

// Smoke test: the page loads without errors, a collage starts, a photo goes in, and Save makes a
// picture. The fuller flows are in collage.flows.spec.js.

const { test, expect } = require('./test');
const { photoFile, openFresh } = require('./helpers');

test('a collage starts, takes a photo and saves a PNG', async ({ page }) => {
  const errors = await openFresh(page);

  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();

  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { width: 1600, height: 1200, name: 'picnic.jpg' }));
  await expect(page.locator('[data-photo-count]')).toHaveText('1 of 60 photos');
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');

  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'A slide or a screen' }).click();
  const download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const file = await download;
  expect(file.suggestedFilename()).toMatch(/\.png$/);
  await expect(page.locator('[data-save-result]')).toContainText('Saved');

  expect(errors).toEqual([]);
});
