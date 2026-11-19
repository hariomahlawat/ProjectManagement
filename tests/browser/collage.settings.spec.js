'use strict';

// Settings that say what they do (docs/ux/collage-review-3-fixes.md, PR 5).

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft, startOn } = require('./helpers');

async function onePhoto(page) {
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'farewell-group.jpg' }));
  await expect.poll(async () => (await newestDraft(page))?.model.items.length).toBe(1);
  await expect(page.locator('.clg-props [data-caption]')).toBeVisible();
}

const frameButton = (page, value) => page.locator(`.clg-props [data-finish="frame"][data-value="${value}"]`);

test('with a shape other than Square the frame shows None, and the chosen frame comes back with Square', async ({ page }) => {
  const errors = await openFresh(page);
  await onePhoto(page);
  await frameButton(page, 'polaroid').click();
  await expect(frameButton(page, 'polaroid')).toHaveAttribute('aria-pressed', 'true');
  await page.locator('.clg-props [data-finish="shape"][data-value="heart"]').click();
  await expect(frameButton(page, 'none')).toHaveAttribute('aria-pressed', 'true');
  await expect(frameButton(page, 'polaroid')).toHaveAttribute('aria-pressed', 'false');
  await expect(page.locator('.clg-props [data-frame-note]')).toContainText('Polaroid frame comes back if you choose Square');
  await page.locator('.clg-props [data-finish="shape"][data-value="rect"]').click();
  await expect(frameButton(page, 'polaroid')).toHaveAttribute('aria-pressed', 'true');
  expect(errors).toEqual([]);
});

test('the caption example cannot be taken for a caption', async ({ page }) => {
  const errors = await openFresh(page);
  await onePhoto(page);
  const field = page.locator('.clg-props [data-caption]');
  await expect(field).toHaveAttribute('placeholder', /^e\.g\. /);
  const style = await field.evaluate((el) => {
    const p = getComputedStyle(el, '::placeholder');
    return { opacity: Number(p.opacity), italic: p.fontStyle };
  });
  expect(style.opacity).toBeLessThan(1);
  expect(style.italic).toBe('italic');
  expect(errors).toEqual([]);
});

test('a photo background is named by its file and shown chosen', async ({ page }) => {
  const errors = await openFresh(page);
  await onePhoto(page);
  await page.locator('.clg-rail [data-panel="background"]').click();
  const tile = page.locator('[data-bg-photos] [data-background]').first();
  await expect(tile).toHaveAttribute('aria-label', 'farewell-group.jpg, blurred');
  await tile.click();
  await expect(page.locator('[data-bg-photos] [data-background]').first()).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('[data-announce]')).toContainText('Background: farewell-group.jpg, blurred.');
  expect(errors).toEqual([]);
});

test('the collage says once that it is named after its first words', async ({ page }) => {
  const errors = await openFresh(page);
  await onePhoto(page);
  await page.locator('.clg-rail [data-panel="text"]').click();
  await page.locator('[data-add-text="heading"]').click();
  await page.locator('.clg-props [data-text]').fill('Farewell Col Rao');
  const note = page.locator('[data-prism-toast]').filter({ hasText: 'named after its first words' });
  await expect(note).toHaveCount(1);
  await expect(page.locator('[data-name]')).toHaveAttribute('placeholder', 'Farewell Col Rao');
  await expect(page.locator('[data-name]')).toHaveAttribute('title', /Named after your first words/);
  await page.locator('[data-add-text="heading"]').click();
  await page.locator('.clg-props [data-text]').fill('Thank you');
  await expect(note).toHaveCount(1);
  expect(errors).toEqual([]);
});

test('the naming note comes for each collage, also for a saved draft opened later', async ({ page }) => {
  const errors = await openFresh(page);
  const note = page.locator('[data-prism-toast]').filter({ hasText: 'named after its first words' });
  const addWords = async (text) => {
    await page.locator('.clg-rail [data-panel="text"]').click();
    await page.locator('[data-add-text="heading"]').click();
    await page.locator('.clg-props [data-text]').fill(text);
  };
  // A draft with a photo and no words yet.
  await onePhoto(page);
  await page.getByRole('button', { name: 'Back to the start' }).click();
  // Another collage, named by its words: the note.
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 portrait/ }).click();
  await addWords('Second collage');
  await expect(note).toHaveCount(1);
  await note.locator('[data-prism-toast-close]').click();
  await expect(note).toHaveCount(0);
  await page.getByRole('button', { name: 'Back to the start' }).click();

  // The first draft opened again: its first words bring the note again.
  await page.locator('[data-open-draft]').filter({ hasNotText: 'Second collage' }).first().click();
  await expect(page.locator('[data-photo-count]')).toHaveText('1 of 60 photos');
  await addWords('First collage');
  await expect(note).toHaveCount(1);
  expect(errors).toEqual([]);
});
