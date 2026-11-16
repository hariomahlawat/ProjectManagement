'use strict';

// Nothing the user did is undone without asking (docs/ux/collage-review-2-fixes.md, PR 6):
// deleting a photo from a layout leaves its frame, deletes offer Undo on the screen, photos past
// the layout's frames wait in the tray, and new words do not land on earlier ones.

const { test, expect } = require('@playwright/test');
const { openFresh, photoFile, newestDraft } = require('./helpers');

async function photos(page, count) {
  const files = [];
  for (let i = 0; i < count; i += 1) files.push(await photoFile(page, { name: `p${i}.jpg`, colour: `hsl(${i * 50}, 55%, 40%)` }));
  return files;
}

test('a photo deleted from a layout leaves its frame empty, and the toast brings it back', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^1 big, 2 small/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 3));
  await expect.poll(async () => (await newestDraft(page))?.model.items.map((i) => i.kind)).toEqual(['photo', 'photo', 'photo']);

  await page.locator('[data-layer-select]').first().click();
  await page.locator('[data-action="delete"][data-for="item"]').click();
  await expect.poll(async () => (await newestDraft(page))?.model.items.map((i) => i.kind).sort()).toEqual(['photo', 'photo', 'slot']);
  const toast = page.locator('[data-prism-toast]').filter({ hasText: 'its frame is empty' });
  await expect(toast).toBeVisible();
  await toast.getByRole('button', { name: 'Undo' }).click();
  await expect.poll(async () => (await newestDraft(page))?.model.items.map((i) => i.kind)).toEqual(['photo', 'photo', 'photo']);
  expect(errors).toEqual([]);
});

test('photos past the layout\'s frames wait in the tray, with a message', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^1 big, 2 small/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 5));
  await expect(page.locator('[data-photo-count]')).toHaveText('5 of 60 photos');
  await expect(page.locator('[data-alert-text]')).toContainText("2 photos did not fit the layout's frames");
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(3);
  // The two waiting photos can be taken out of the collage from the tray.
  await expect(page.locator('[data-remove-photo]')).toHaveCount(2);
  expect(errors).toEqual([]);
});

test('a second heading does not land on the first, and placeholder words do not name the collage', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('.clg-rail [data-panel="text"]').click();
  await page.locator('[data-add-text="heading"]').click();
  // The Text panel may still be open; it is opened only if it is not.
  if (!(await page.locator('[data-add-text="hindi"]').isVisible())) await page.locator('.clg-rail [data-panel="text"]').click();
  await page.locator('[data-add-text="hindi"]').click();
  const draft = await expect.poll(async () => (await newestDraft(page))?.model.items.length).toBe(2).then(() => newestDraft(page));
  const [a, b] = draft.model.items;
  const apart = Math.abs(a.y - b.y) >= (a.width / a.aspect + b.width / b.aspect) / 2
    || Math.abs(a.x - b.x) >= (a.width + b.width) / 2;
  expect(apart).toBe(true);
  expect(draft.title).toMatch(/^Collage, /);
  expect(errors).toEqual([]);
});

test('a tray photo clicked when every frame is full says how to make room', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^1 big, 2 small/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 4));
  await expect(page.locator('[data-remove-photo]')).toHaveCount(1);
  await page.locator('[data-action="alert-close"]').first().click().catch(() => {});
  await page.locator('[data-tray-photo]').last().click();
  await expect(page.locator('[data-alert-text]')).toContainText('Every frame of this layout has a photo');
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(3);
  expect(errors).toEqual([]);
});
