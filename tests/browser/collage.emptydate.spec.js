'use strict';

// A new card's date starts empty (docs/ux/collage-usability-plan.md, round 2, PR 3): the example is
// the box's hint, so a card is never printed with a sample date, and Save says when it is empty.

const { test, expect } = require('./test');
const { openFresh, startOn, newestDraft } = require('./helpers');

test('a new card opens with the date empty, the example as its hint, and Save names it until typed', async ({ page }, testInfo) => {
  const errors = await openFresh(page);
  await startOn(page, 'card');
  await page.locator('[data-start-design="classic"]').click();
  const when = page.locator('[data-card-field="when"]');
  await expect(when).toHaveValue('');
  await expect(when).toHaveAttribute('placeholder', 'e.g. Friday, 12 December 2026 at 1930 h');
  await page.locator('.clg-stage').screenshot({ path: testInfo.outputPath('card-without-date.png') });

  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'Printing' }).click();
  await expect(page.locator('[data-save-empty]')).toContainText('The date and time are empty: type them in Card details, or save without them.');
  await page.locator('[data-action="save-close"]').first().click();

  await when.fill('Friday, 9 January 2027 at 1930 h');
  await when.blur();
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.field === 'when')?.text).toBe('Friday, 9 January 2027 at 1930 h');
  await page.getByRole('button', { name: 'Save as…' }).click();
  await expect(page.locator('[data-save-empty]')).toBeHidden();
  expect(errors).toEqual([]);
});

test('a poster\'s date starts empty too, and Save speaks of Poster details', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'poster');
  await page.locator('[data-start-poster="notice"]').click();
  await expect(page.locator('[data-card-field="when"]')).toHaveValue('');
  await page.getByRole('button', { name: 'Save as…' }).click();
  await expect(page.locator('[data-save-empty]')).toContainText('type them in Poster details');
  expect(errors).toEqual([]);
});

test('the start screen\'s card pictures still show an example date', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'card');
  // The pictures are drawn from the design's sample wording, date included.
  const drawn = await page.locator('[data-start-design="classic"] img, [data-start-design="classic"] canvas').count();
  expect(drawn).toBeGreaterThan(0);
  expect(errors).toEqual([]);
});

// docs/ux/collage-usability-plan.md, round 3, PR 3: the Event poster's band waits for the date.
test('the Event poster\'s date band is hidden until a date is typed', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'poster');
  await page.locator('[data-start-poster="event"]').click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  // The shapes drawn on the page: the band is one of them once it shows.
  const shapesShown = () => page.evaluate(() => window.Konva.stages[0].find('.item').filter((n) => n.getAttr('kind') === 'shape' && n.visible()).length);
  // (The logo's backing is hidden too, while a new poster has no logo.)
  await expect.poll(() => page.evaluate(() => window.Konva.stages[0].find('.item').length)).toBeGreaterThan(0);
  const before = await shapesShown();
  const when = page.locator('[data-card-field="when"]');
  await when.fill('Saturday, 10 January 2027 · 0700 h');
  await when.blur();
  await expect.poll(shapesShown).toBe(before + 1);
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.role === 'band')?.hidden ?? false).toBe(false);
  // Save does not count it as a part the user hid.
  await page.getByRole('button', { name: 'Save as…' }).click();
  await expect(page.locator('[data-save-empty]')).not.toContainText('hidden');
  expect(errors).toEqual([]);
});

// Codex on #3483: a band the user shows and hides again stays hidden when a date is typed.
test('the poster band shown and hidden again by the user stays hidden when the date is typed', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'poster');
  await page.locator('[data-start-poster="event"]').click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  // The band is the widest hidden shape (the logo's backing is hidden too).
  const bandId = await page.evaluate(() => window.Konva.stages[0].find('.item')
    .filter((n) => n.getAttr('kind') === 'shape' && !n.visible())
    .sort((a, b) => b.width() - a.width())[0].id());
  const shown = () => page.evaluate((id) => window.Konva.stages[0].findOne(`#${id}`).visible(), bandId);
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  const toggle = page.locator(`[data-layer-hide="${bandId}"]`);
  await toggle.click();
  await expect.poll(shown).toBe(true);
  await toggle.click();
  await expect.poll(shown).toBe(false);
  const when = page.locator('[data-card-field="when"]');
  await when.fill('Saturday, 10 January 2027 · 0700 h');
  await when.blur();
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.field === 'when')?.text).toBe('Saturday, 10 January 2027 · 0700 h');
  expect(await shown()).toBe(false);
  expect(errors).toEqual([]);
});

// Codex on #3483: a band the user hid on a dated poster is named by Save, as any hidden part is.
test('Save names a dated poster\'s band the user hid', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'poster');
  await page.locator('[data-start-poster="event"]').click();
  const when = page.locator('[data-card-field="when"]');
  await when.fill('Saturday, 10 January 2027 · 0700 h');
  await when.blur();
  const bandId = await page.evaluate(() => window.Konva.stages[0].find('.item')
    .filter((n) => n.getAttr('kind') === 'shape' && n.visible())
    .sort((a, b) => b.width() - a.width())[0].id());
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  await page.locator(`[data-layer-hide="${bandId}"]`).click();
  await page.getByRole('button', { name: 'Save as…' }).click();
  await expect(page.locator('[data-save-empty]')).toContainText('hidden');
  expect(errors).toEqual([]);
});

// Codex on #3483: a poster's date deleted on the page takes its band with it.
test('deleting a poster\'s date on the page hides its band, as emptying it in Poster details does', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'poster');
  await page.locator('[data-start-poster="event"]').click();
  const when = page.locator('[data-card-field="when"]');
  await when.fill('Saturday, 10 January 2027 · 0700 h');
  await when.blur();
  const shapesShown = () => page.evaluate(() => window.Konva.stages[0].find('.item').filter((n) => n.getAttr('kind') === 'shape' && n.visible()).length);
  await expect.poll(shapesShown).toBeGreaterThan(0);
  const dated = await shapesShown();
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  await page.locator('[data-layer-select]').filter({ hasText: 'Saturday, 10 January 2027' }).click();
  await page.locator('[data-toolbar] [data-action="delete"]').click();
  await expect.poll(shapesShown).toBe(dated - 1);
  expect(errors).toEqual([]);
});
