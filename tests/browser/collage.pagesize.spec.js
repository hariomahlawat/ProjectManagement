'use strict';

// The page size (docs/ux/collage-usability-plan.md, round 2, PR 2): what a collage is for picks its
// page, beside Add photos; one size menu, in the top bar, offers the sizes for what is open.

const { test, expect } = require('./test');
const { openFresh, startOn, photoFile, newestDraft } = require('./helpers');

const visibleGroups = (page) => page.locator('[data-size] optgroup').evaluateAll((groups) => groups.filter((group) => !group.hidden).map((group) => group.label));

test('Printing is chosen at first; WhatsApp opens a square page, and is remembered', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  const share = page.locator('[data-start-for="share"]');
  const print = page.locator('[data-start-for="print"]');
  await expect(print).toHaveAttribute('aria-pressed', 'true');
  await expect(share).toHaveAttribute('aria-pressed', 'false');
  await expect(page.locator('[data-start-layouts-note]')).toHaveText('A4 landscape; the size can be changed later');

  await share.click();
  await expect(share).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('[data-start-layouts-note]')).toHaveText('Square; the size can be changed later');
  const chooser = page.waitForEvent('filechooser');
  await page.locator('[data-start-drop]').getByRole('button', { name: 'Add photos' }).click();
  await (await chooser).setFiles([await photoFile(page, { name: 'a.jpg' }), await photoFile(page, { name: 'b.jpg', colour: '#7a3b2e' })]);
  await expect.poll(async () => (await newestDraft(page))?.model?.size).toBe('square');
  await expect(page.locator('[data-size]')).toHaveValue('square');

  // Remembered: back at the start, and after a reload, WhatsApp is still chosen; a layout opens square too.
  await page.getByRole('button', { name: 'Back to the start' }).click();
  await page.reload();
  await page.locator('[data-view="start"]').waitFor();
  await startOn(page, 'collage');
  await expect(page.locator('[data-start-for="share"]')).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: /^4 grid/ }).click();
  await expect(page.locator('[data-size]')).toHaveValue('square');
  expect(errors).toEqual([]);
});

test('the size menu offers pages for a collage and cards for a card; Page settings keep only Custom size', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  expect(await visibleGroups(page)).toEqual(['Pages and screens', 'Custom']);
  // One size menu: Page settings name the size and keep the custom size.
  await expect(page.locator('.clg-props [data-page-size]')).toHaveCount(0);
  await expect(page.locator('.clg-props')).toContainText('change the size in the menu at the top');
  await expect(page.locator('.clg-props [data-custom-fold]')).toHaveCount(1);
  // An empty collage is not a draft yet, so the page itself shows the change.
  await page.locator('[data-size]').selectOption('slide');
  await expect(page.locator('.clg-props')).toContainText('Slide 16:9: change the size');

  await page.getByRole('button', { name: 'Back to the start' }).click();
  await startOn(page, 'card');
  await page.locator('[data-start-design="classic"]').click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  expect(await visibleGroups(page)).toEqual(['Cards', 'Pocket cards', 'Folded cards', 'Custom']);
  expect(errors).toEqual([]);
});
