'use strict';

// A lock is the same everywhere, and a change of layout asks about locked photos
// (docs/ux/collage-review-3-fixes.md, PR 2).

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft, showLayers, showSettings } = require('./helpers');

async function photos(page, count) {
  const files = [];
  for (let i = 0; i < count; i += 1) files.push(await photoFile(page, { name: `p${i}.jpg`, colour: `hsl(${i * 70}, 55%, 40%)` }));
  return files;
}

/** Starts "1 big, 2 small" with three photos and locks the first one, from the toolbar. */
async function lockedFirst(page) {
  await page.getByRole('button', { name: /^1 big, 2 small/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 3));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(3);
  await showLayers(page);
  await page.locator('[data-layer-select]').last().click();
  const toolbar = page.locator('[data-toolbar]');
  await toolbar.locator('[data-action="lock"]').click();
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.locked).length).toBe(1);
  return (await newestDraft(page)).model.items.find((i) => i.locked);
}

test('a locked photo cannot be deleted, moved in its frame, replaced or given other corners', async ({ page }) => {
  const errors = await openFresh(page);
  const locked = await lockedFirst(page);
  const toolbar = page.locator('[data-toolbar]');
  for (const action of ['delete', 'adjust', 'replace']) await expect(toolbar.locator(`[data-action="${action}"]`)).toBeDisabled();
  await expect(toolbar.locator('[data-action="lock"]')).toBeEnabled();
  await expect(toolbar.locator('[data-action="duplicate"]')).toBeEnabled();

  // The keys say so and change nothing (pressed on the page: on the Lock button it is the button's).
  await page.evaluate(() => document.activeElement.blur());
  await page.keyboard.press('Delete');
  await expect(page.locator('[data-announce]')).toHaveText('It is locked. Unlock it first.');
  // Said on the screen too, not only to a screen reader.
  await expect(page.locator('[data-prism-toast]').filter({ hasText: 'It is locked' })).toBeVisible();

  // Its own corners are locked in its settings.
  await showSettings(page);
  await page.locator('.clg-props summary', { hasText: 'Corners and see-through' }).click();
  await expect(page.locator('.clg-props [data-prop="radius"]')).toBeDisabled();
  await expect(page.locator('.clg-props [data-action="replace"]')).toBeDisabled();

  // The layout's corners change every frame but the locked one.
  await page.locator('.clg-rail [data-panel="layouts"]').click();
  await page.locator('[data-layout-input="radius"]').fill('20');
  await expect.poll(async () => (await newestDraft(page))?.model.items.map((i) => [i.id === locked.id, i.radius]).sort())
    .toEqual([[false, 20], [false, 20], [true, 0]]);
  const after = (await newestDraft(page)).model.items.find((i) => i.id === locked.id);
  expect(after).toEqual(locked);
  expect(errors).toEqual([]);
});

test('a new layout asks about a locked photo: kept where it is, only the frames it mostly covers are left out', async ({ page }) => {
  const errors = await openFresh(page);
  const locked = await lockedFirst(page);
  await page.locator('.clg-rail [data-panel="layouts"]').click();
  await page.getByRole('button', { name: /^4 grid/ }).click();
  const dialog = page.locator('[data-prism-confirm-dialog]');
  await expect(dialog).toContainText('1 photo is locked');
  await expect(dialog).toContainText("2 of the new layout's 4 frames are left out");
  await dialog.getByRole('button', { name: 'Keep it where it is' }).click();
  await expect.poll(async () => (await newestDraft(page))?.model.layout?.key).toBe('t4');
  const model = (await newestDraft(page)).model;
  const kept = model.items.find((i) => i.id === locked.id);
  expect({ x: kept.x, y: kept.y, width: kept.width }).toEqual({ x: locked.x, y: locked.y, width: locked.width });
  // The two right-hand frames stay, with the other photos in them; none is mostly under the locked photo.
  expect(model.items.filter((i) => i.id !== kept.id && i.kind === 'photo').length).toBe(2);
  const box = (i) => ({ l: i.x - i.width / 2, r: i.x + i.width / 2, t: i.y - i.width / i.aspect / 2, b: i.y + i.width / i.aspect / 2 });
  const k = box(kept);
  for (const other of model.items.filter((i) => i.id !== kept.id && (i.kind === 'photo' || i.kind === 'slot'))) {
    const o = box(other);
    const w = Math.min(k.r, o.r) - Math.max(k.l, o.l);
    const h = Math.min(k.b, o.b) - Math.max(k.t, o.t);
    expect(w > 0 && h > 0 ? (w * h) / ((o.r - o.l) * (o.b - o.t)) : 0).toBeLessThanOrEqual(0.5);
  }
  expect(errors).toEqual([]);
});

test('a new layout asks about a locked photo: put in the layout, it takes a frame and stays locked', async ({ page }) => {
  const errors = await openFresh(page);
  const locked = await lockedFirst(page);
  await page.locator('.clg-rail [data-panel="layouts"]').click();
  await page.getByRole('button', { name: /^4 grid/ }).click();
  await page.locator('[data-prism-confirm-dialog]').getByRole('button', { name: 'Put it in the layout' }).click();
  await expect.poll(async () => (await newestDraft(page))?.model.layout?.key).toBe('t4');
  const model = (await newestDraft(page)).model;
  const placed = model.items.find((i) => i.id === locked.id);
  expect(placed.locked).toBe(true);
  expect(Number.isInteger(placed.cell)).toBe(true);
  expect(model.items.filter((i) => Number.isInteger(i.cell)).length).toBe(4);
  expect(errors).toEqual([]);
});
