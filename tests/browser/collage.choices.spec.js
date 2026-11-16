'use strict';

// Nothing the user did is undone without asking (docs/ux/collage-review-2-fixes.md, PR 6):
// deleting a photo from a layout leaves its frame, deletes offer Undo on the screen, photos past
// the layout's frames wait in the tray, and new words do not land on earlier ones.

const { test, expect } = require('@playwright/test');
const { openFresh, photoFile, newestDraft, showLayers, showSettings } = require('./helpers');

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

  await showLayers(page);
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

test('any photo can have a caption, shown on a strip under it and kept in the draft', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 1));
  await showLayers(page);
  await page.locator('[data-layer-select]').first().click();
  await showSettings(page);
  const caption = page.locator('.clg-props [data-caption]');
  await expect(caption).toBeVisible();
  await expect(page.locator('#clgCaptionHint')).toContainText('white strip');
  await caption.fill('Annual day 2026');
  await expect.poll(async () => (await newestDraft(page))?.model.items[0]?.caption).toBe('Annual day 2026');
  expect(errors).toEqual([]);
});

test('Save starts on WhatsApp, shows the purpose chosen, and remembers it next time', async ({ page }) => {
  const errors = await openFresh(page);
  await page.evaluate(() => { try { localStorage.removeItem('prism-collage-save-purpose'); } catch { /* none */ } });
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 1));
  await page.getByRole('button', { name: 'Save as…' }).click();
  await expect(page.getByRole('button', { name: 'WhatsApp or email' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('input[name="format"][value="jpeg"]')).toBeChecked();
  await page.getByRole('button', { name: 'Printing' }).click();
  await expect(page.getByRole('button', { name: 'Printing' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('button', { name: 'WhatsApp or email' })).toHaveAttribute('aria-pressed', 'false');
  // Changing the type by hand: no purpose matches it any more.
  await page.locator('input[name="format"][value="png"]').check();
  await expect(page.locator('[data-save-purpose][aria-pressed="true"]')).toHaveCount(0);
  await page.locator('[data-action="save-close"]').first().click();
  await page.getByRole('button', { name: 'Save as…' }).click();
  await expect(page.getByRole('button', { name: 'Printing' })).toHaveAttribute('aria-pressed', 'true');
  expect(errors).toEqual([]);
});

test('Preview leaves out the empty frames, as Save does; a white frame gets a shadow', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^1 big, 2 small/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 1));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'slot').length).toBe(2);
  const visibleSlots = () => page.evaluate(() => window.Konva.stages[0].find('.item').filter((n) => n.getAttr('kind') === 'slot' && n.visible()).length);
  expect(await visibleSlots()).toBe(2);
  await page.getByRole('button', { name: /^Preview/ }).click();
  expect(await visibleSlots()).toBe(0);
  await page.keyboard.press('Escape');
  expect(await visibleSlots()).toBe(2);

  await showLayers(page);
  const photoRow = page.locator('[data-layer-select]').filter({ hasText: 'p0.jpg' });
  await photoRow.click();
  await showSettings(page);
  await page.locator('.clg-props [data-finish="frame"][data-value="border"]').click();
  await expect.poll(async () => (await newestDraft(page))?.model.items.find((i) => i.kind === 'photo')).toMatchObject({ frame: 'border', shadow: true });
  expect(errors).toEqual([]);
});

test('settings: rarely used groups are folded and stay open once opened; Layers is a tab', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 1));
  const order = page.locator('.clg-props [data-fold="order"]');
  await expect(order).not.toHaveAttribute('open', '');
  await expect(page.locator('.clg-props [data-action="forward"]')).toBeHidden();
  await order.locator('summary').click();
  await expect(page.locator('.clg-props [data-action="forward"]')).toBeVisible();
  // Drawn again after a change, it is still open.
  await page.locator('.clg-props [data-finish="shape"][data-value="oval"]').click();
  await expect(page.locator('.clg-props [data-fold="order"]')).toHaveAttribute('open', '');

  // Layers: a tab, with the arrow keys moving between the two tabs.
  await page.locator('.clg-props [data-props-tab="settings"]').focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.locator('.clg-props [data-props-tab="layers"]')).toHaveAttribute('aria-selected', 'true');
  await expect(page.locator('.clg-props [data-layer-select]')).toHaveCount(1);
  expect(errors).toEqual([]);
});

test('arrow keys on the tabs do not move the selected item; choosing it on the page shows Settings', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 1));
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');
  const before = (await newestDraft(page)).model.items[0];
  await page.locator('.clg-props [data-props-tab="settings"]').focus();
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowLeft');
  await page.waitForTimeout(700);
  const after = (await newestDraft(page)).model.items[0];
  expect([after.x, after.y]).toEqual([before.x, before.y]);

  // Picked in Layers, then chosen again on the page: its settings come back.
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  await page.locator('[data-layer-select]').first().click();
  const box = await page.locator('[data-konva] canvas').last().boundingBox();
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
  await expect(page.locator('.clg-props [data-props-tab="settings"]')).toHaveAttribute('aria-selected', 'true');
  expect(errors).toEqual([]);
});
