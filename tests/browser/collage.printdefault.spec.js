'use strict';

// Printing a card starts on A4 sheets with cut marks (docs/ux/collage-usability-plan.md, round 2,
// PR 4, the owner's decision of 19 Nov): ready for an office printer. Each card on its own page
// stays one click away, and a PDF's summary speaks of paper, not pixels.

const { test, expect } = require('./test');
const { openFresh, startOn } = require('./helpers');

const sheet = (page, value) => page.locator(`[data-save-sheet] input[value="${value}"]`);

test('Printing a card picks A4 sheets with cut marks; own pages stay one click away', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'card');
  await page.locator('[data-card-size]').selectOption('a6p');
  await page.locator('[data-start-design="classic"]').click();
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'Printing' }).click();
  await expect(sheet(page, 'a4')).toBeChecked();
  await expect(page.locator('[data-save-summary]')).toHaveText('A PDF at print quality: 2 × A6 card to each A4 sheet, at true size.');

  // The user's own pick stays while the dialog is open.
  await sheet(page, 'card').check();
  await expect(sheet(page, 'card')).toBeChecked();
  await expect(page.locator('[data-save-summary]')).toHaveText('A PDF at print quality: A6 card at its true size.');
  await expect(page.locator('[data-save-summary]')).not.toContainText('pixels');

  // Opened again, Save starts on A4 sheets again.
  await page.locator('[data-action="save-close"]').first().click();
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'Printing' }).click();
  await expect(sheet(page, 'a4')).toBeChecked();
  expect(errors).toEqual([]);
});

test('a folded card, saved one to a page, has no sheet choice to make', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'card');
  await page.locator('[data-card-size]').selectOption('fold6');
  await page.locator('[data-start-design="classic"]').click();
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'Printing' }).click();
  await expect(page.locator('[data-save-sheet]')).toBeHidden();
  await expect(page.locator('[data-save-summary]')).not.toContainText('A4 sheet');
  expect(errors).toEqual([]);
});

// Codex review on #3478: the flip hint follows the sheet A4 starts on, not the page before it.
test('a landscape card with a back says to flip on the long edge on A4 sheets, the short edge on its own pages', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'card');
  await page.locator('[data-card-size]').selectOption('a6l');
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('[data-card-side="add"]').click();
  await expect.poll(async () => page.locator('[data-card-side]').count()).toBeGreaterThan(1);
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'Printing' }).click();
  await expect(sheet(page, 'a4')).toBeChecked();
  await expect(page.locator('[data-sides-flip]')).toHaveText('To print both on one card, flip on the long edge.');
  await sheet(page, 'card').check();
  await expect(page.locator('[data-sides-flip]')).toHaveText('To print both on one card, flip on the short edge.');
  expect(errors).toEqual([]);
});
