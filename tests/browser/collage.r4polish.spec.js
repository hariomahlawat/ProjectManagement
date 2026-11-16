'use strict';

// The UX Lows (docs/ux/collage-review-4-fixes.md, PR 7).

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft } = require('./helpers');
const samples = require('./fixtures/collage-api-samples.json');

async function onePhoto(page, layout = /^A4 landscape/) {
  await page.getByRole('button', { name: layout }).click();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'one.jpg', width: 1600, height: 900 }));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(1);
}

test('the frame note sits by the shape that took the frame away', async ({ page }) => {
  const errors = await openFresh(page);
  await onePhoto(page);
  await page.locator('.clg-props [data-finish="frame"][data-value="polaroid"]').click();
  await page.locator('.clg-props [data-finish="shape"][data-value="heart"]').click();
  const section = page.locator('.clg-props .clg-section').filter({ has: page.locator('[data-frame-note]') });
  await expect(section.locator('.clg-section__title')).toHaveText('Shape');
  expect(errors).toEqual([]);
});

test('while a photo is moved in its frame, the hidden part of it shows faintly round the frame', async ({ page }) => {
  const errors = await openFresh(page);
  await onePhoto(page, /^2 side by side/);
  const frame = await page.evaluate(() => {
    const stage = window.Konva.stages[0];
    const node = stage.find((n) => n.getAttr('kind') === 'photo')[0];
    const r = node.getClientRect({ skipShadow: true, skipStroke: true });
    const host = stage.container().getBoundingClientRect();
    return { x: r.x + host.x, y: r.y + host.y, w: r.width, h: r.height };
  });
  // A strip well outside the frame (clear of its handles), where a wide photo in a tall frame hides
  // its sides.
  const strip = { x: Math.round(frame.x - 34), y: Math.round(frame.y + frame.h / 2 + 30), width: 6, height: 30 };
  const before = await page.screenshot({ clip: strip });
  await page.locator('[data-toolbar] [data-action="adjust"]').click();
  await expect(page.locator('[data-toolbar] [data-action="adjust-done"]')).toBeVisible();
  const during = await page.screenshot({ clip: strip });
  expect(Buffer.compare(before, during)).not.toBe(0);
  await page.locator('[data-toolbar] [data-action="adjust-done"]').click();
  expect(errors).toEqual([]);
});

test('draft cards have no stray dot, and deleting the last draft says what happened', async ({ page }) => {
  const errors = await openFresh(page);
  await onePhoto(page);
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');
  await page.getByRole('button', { name: 'Back to the start' }).click();
  const meta = page.locator('[data-open-draft] small').first();
  await expect(meta).toBeVisible();
  // The dots come from the parts after the first, so a part that is hidden takes its dot with it.
  expect(await meta.evaluate((el) => [...el.childNodes].some((n) => n.nodeType === 3 && n.textContent.includes('·')))).toBe(false);
  await page.locator('[data-delete-draft]').first().click();
  const confirm = page.locator('.prism-confirm-modal.show [data-confirm-accept], .prism-confirm-modal.show .btn-danger, .prism-confirm-modal.show .btn-primary');
  if (await confirm.count()) await confirm.first().click();
  await expect(page.locator('[data-open-draft]')).toHaveCount(0);
  await expect(page.locator('[data-drafts-empty]')).toBeVisible();
  expect(errors).toEqual([]);
});

test('the name looks editable, a crest carries its name, and the Stickers help opens Credits', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(page.locator('[data-name]')).toHaveCSS('border-bottom-style', 'dashed');
  await page.locator('.clg-rail [data-panel="stickers"]').click();
  await page.locator('[data-sticker-group="crests"]').click();
  const crest = samples.stickers.stickers[0];
  await expect(page.locator(`[data-add-sticker="unit:${crest.id}"] .clg-sticker__name`)).toHaveText(crest.name);
  await page.locator('[data-drawer-panel="stickers"] [data-action="credits"]').click();
  await expect(page.locator('[data-credits]')).toBeVisible();
  expect(errors).toEqual([]);
});
