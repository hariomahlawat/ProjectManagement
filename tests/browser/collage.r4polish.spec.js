'use strict';

// The UX Lows (docs/ux/collage-review-4-fixes.md, PR 7).

const { test, expect } = require('./test');
const { openFresh, startOn, photoFile, newestDraft } = require('./helpers');
const samples = require('./fixtures/collage-api-samples.json');

async function onePhoto(page, layout = /^A4 landscape/) {
  await startOn(page, 'collage');
  await page.getByRole('button', { name: layout }).click();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'one.jpg', width: 1600, height: 900 }));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(1);
}

test('the frame note sits by the shape that took the frame away', async ({ page }) => {
  const errors = await openFresh(page);
  await onePhoto(page);
  await page.locator('.clg-props [data-finish="frame"][data-value="polaroid"]').click();
  // Shape is folded (docs/ux/collage-usability-plan.md, PR 7).
  await page.locator('.clg-props [data-fold="shape"] > summary').click();
  await page.locator('.clg-props [data-finish="shape"][data-value="heart"]').click();
  // The note is in the Shape group, which stays open while the shape turns the frames off.
  const section = page.locator('.clg-props [data-fold="shape"]');
  await expect(section.locator('[data-frame-note]')).toBeVisible();
  await expect(section).toHaveAttribute('open', '');
  await expect(section.locator('summary')).toHaveText('Shape: Heart');
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
  await startOn(page, 'collage');
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

/** A wide photo, red on its left half and blue on its right, as a file to choose. */
async function twoColourPhoto(page) {
  const base64 = await page.evaluate(async () => {
    const c = document.createElement('canvas');
    c.width = 1600; c.height = 800;
    const x = c.getContext('2d');
    x.fillStyle = '#d00000'; x.fillRect(0, 0, 800, 800);
    x.fillStyle = '#0000d0'; x.fillRect(800, 0, 800, 800);
    const blob = await new Promise((r) => c.toBlob(r, 'image/png'));
    const bytes = new Uint8Array(await blob.arrayBuffer()); let s = ''; for (const b of bytes) s += String.fromCharCode(b); return btoa(s);
  });
  return { name: 'two.png', mimeType: 'image/png', buffer: Buffer.from(base64, 'base64') };
}

/** The colour of the page at a point, read from the page's canvases. */
async function colourAt(page, x, y) {
  const shot = await page.screenshot({ clip: { x, y, width: 1, height: 1 } });
  return page.evaluate(async (b64) => {
    const img = new Image();
    img.src = `data:image/png;base64,${b64}`;
    await img.decode();
    const c = document.createElement('canvas'); c.width = 1; c.height = 1;
    const x = c.getContext('2d'); x.drawImage(img, 0, 0);
    return [...x.getImageData(0, 0, 1, 1).data.slice(0, 3)];
  }, shot.toString('base64'));
}

test('a mirrored photo is shown mirrored round its frame too while it is moved', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^2 side by side/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await twoColourPhoto(page));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(1);
  await page.locator('.clg-props summary', { hasText: 'Brightness' }).click();
  await page.locator('.clg-props [data-toggle="flip"]').check();
  const frame = await page.evaluate(() => {
    const stage = window.Konva.stages[0];
    const node = stage.find((n) => n.getAttr('kind') === 'photo')[0];
    const r = node.getClientRect({ skipShadow: true, skipStroke: true });
    const host = stage.container().getBoundingClientRect();
    return { x: r.x + host.x, y: r.y + host.y, w: r.width, h: r.height };
  });
  await page.locator('[data-toolbar] [data-action="adjust"]').click();
  await expect(page.locator('[data-toolbar] [data-action="adjust-done"]')).toBeVisible();
  // Mirrored, the photo's red half is on the right: the faint part to the right of the frame is red.
  const [r, , b] = await colourAt(page, Math.round(frame.x + frame.w + 40), Math.round(frame.y + frame.h / 2));
  expect(r).toBeGreaterThan(b);
  expect(errors).toEqual([]);
});

test('a draft written while a photo is moved has no faint photo in its picture', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^2 side by side/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await twoColourPhoto(page));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(1);
  await page.locator('[data-toolbar] [data-action="adjust"]').click();
  await expect(page.locator('[data-toolbar] [data-action="adjust-done"]')).toBeVisible();
  // Leaving makes the draft's picture at once, with the photo still being moved.
  await page.getByRole('button', { name: 'Back to the start' }).click();
  await expect(page.locator('[data-open-draft]')).toHaveCount(1);
  // The right half of the page is the empty frame: light grey, not tinted by the faint photo.
  const [r, g, b] = await page.locator('[data-open-draft] img').first().evaluate(async (img) => {
    await img.decode();
    const c = document.createElement('canvas'); c.width = img.naturalWidth; c.height = img.naturalHeight;
    const x = c.getContext('2d'); x.drawImage(img, 0, 0);
    return [...x.getImageData(Math.round(c.width * 0.75), Math.round(c.height / 2), 1, 1).data.slice(0, 3)];
  });
  expect(Math.max(r, g, b) - Math.min(r, g, b)).toBeLessThan(25);
  expect(errors).toEqual([]);
});

test('hiding a photo while it is moved leaves no faint copy of it', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^2 side by side/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await twoColourPhoto(page));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(1);
  await page.locator('[data-toolbar] [data-action="adjust"]').click();
  await expect(page.locator('[data-toolbar] [data-action="adjust-done"]')).toBeVisible();
  const ghostShown = () => page.evaluate(() => window.Konva.stages[0].findOne('.adjust-ghost').visible());
  expect(await ghostShown()).toBe(true);
  await page.locator('[data-props-tab="layers"]').click();
  const photoId = (await newestDraft(page)).model.items.find((i) => i.kind === 'photo').id;
  await page.locator(`[data-layer-hide="${photoId}"]`).click();
  await expect.poll(ghostShown).toBe(false);
  expect(errors).toEqual([]);
});
