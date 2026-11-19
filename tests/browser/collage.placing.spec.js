'use strict';

// Placing things (docs/ux/collage-review-3-fixes.md, PR 4): photos added to a blank page form a
// neat grid, new words go to free space, and the space slider offers Undo when it puts photos back.

const { test, expect } = require('./test');
const { openFresh, startOn, photoFile, newestDraft, showLayers } = require('./helpers');

async function photos(page, count) {
  const files = [];
  for (let i = 0; i < count; i += 1) files.push(await photoFile(page, { name: `p${i}.jpg`, colour: `hsl(${i * 60}, 55%, 40%)` }));
  return files;
}

const box = (i) => {
  const h = i.width / i.aspect;
  const c = Math.abs(Math.cos((i.angle || 0) * Math.PI / 180));
  const s = Math.abs(Math.sin((i.angle || 0) * Math.PI / 180));
  const hw = (i.width * c + h * s) / 2;
  const hh = (i.width * s + h * c) / 2;
  return { l: i.x - hw, r: i.x + hw, t: i.y - hh, b: i.y + hh };
};
const overlap = (a, b) => {
  const p = box(a);
  const q = box(b);
  return Math.min(p.r, q.r) - Math.max(p.l, q.l) > 1 && Math.min(p.b, q.b) - Math.max(p.t, q.t) > 1;
};

test('four photos added to a blank page are laid out in a grid, none covering another', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 portrait/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 4));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(4);
  const items = (await newestDraft(page)).model.items;
  for (const a of items) for (const b of items) if (a !== b) expect(overlap(a, b), `${a.id} / ${b.id}`).toBe(false);
  expect(errors).toEqual([]);
});

test('three headings added over a full layout do not cover each other', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^4 grid/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 4));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(4);
  await page.locator('.clg-rail [data-panel="text"]').click();
  for (let i = 0; i < 3; i += 1) {
    await page.locator('[data-add-text="heading"]').click();
    await page.locator('.clg-props [data-text]').fill(`Heading ${i + 1}`);
  }
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'text').length).toBe(3);
  const words = (await newestDraft(page)).model.items.filter((i) => i.kind === 'text');
  for (const a of words) for (const b of words) if (a !== b) expect(overlap(a, b), `${a.text} / ${b.text}`).toBe(false);
  expect(errors).toEqual([]);
});

test('the space slider says when it puts a hand-moved photo back, and Undo brings it back', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^4 grid/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 4));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(4);
  // A photo moved by hand with the arrow keys (the layout stays).
  await showLayers(page);
  await page.locator('[data-layer-select]').first().click();
  const before = (await newestDraft(page)).model.items.map((i) => Math.round(i.x));
  await page.locator('[data-stage]').focus();
  for (let i = 0; i < 5; i += 1) await page.keyboard.press('Shift+ArrowRight');
  await expect.poll(async () => (await newestDraft(page))?.model.items.map((i) => Math.round(i.x))).not.toEqual(before);
  const moved = (await newestDraft(page)).model.items.map((i) => Math.round(i.x));

  await page.locator('.clg-rail [data-panel="layouts"]').click();
  await page.locator('[data-layout-input="gap"]').fill('30');
  const toast = page.locator('[data-prism-toast]').filter({ hasText: 'went back into the layout' });
  await expect(toast).toBeVisible();
  await toast.getByRole('button', { name: 'Undo' }).click();
  await expect.poll(async () => (await newestDraft(page))?.model.items.map((i) => Math.round(i.x))).toEqual(moved);

  expect(errors).toEqual([]);
});
