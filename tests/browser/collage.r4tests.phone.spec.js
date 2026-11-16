'use strict';

// Two fingers on a phone zoom the page, and one finger still moves an item
// (docs/ux/collage-review-4-fixes.md, PR 12). Real touches are sent through the browser's own
// input (CDP), as a finger would make them.

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft } = require('./helpers');

/** The page's zoom as shown, in per cent, and the photo's width on the screen. */
async function zoomNow(page) {
  return page.evaluate(() => {
    const stage = window.Konva.stages[0];
    const node = stage.find((n) => n.getAttr('kind') === 'photo')[0];
    return {
      shown: parseInt(document.querySelector('[data-zoom-value]').textContent, 10),
      photoWidth: node.getClientRect({ skipShadow: true, skipStroke: true }).width
    };
  });
}

async function photoCentre(page) {
  return page.evaluate(() => {
    const stage = window.Konva.stages[0];
    const node = stage.find((n) => n.getAttr('kind') === 'photo')[0];
    const r = node.getClientRect({ skipShadow: true, skipStroke: true });
    const host = stage.container().getBoundingClientRect();
    return { x: r.x + host.x + r.width / 2, y: r.y + host.y + r.height / 2 };
  });
}

const firstPhoto = async (page) => (await newestDraft(page)).model.items.find((i) => i.kind === 'photo');

test('two fingers moving apart zoom the page in; one finger still moves a photo', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'one.jpg', width: 1600, height: 900 }));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(1);
  await page.locator('.clg-drawer [data-action="close-drawer"]').click().catch(() => {});
  const cdp = await page.context().newCDPSession(page);
  const touch = (type, points) => cdp.send('Input.dispatchTouchEvent', { type, touchPoints: points.map(([x, y], id) => ({ x, y, id })) });

  // Two fingers on the photo, moving apart: the page zooms in, and the photo is not moved.
  const before = await zoomNow(page);
  const placed = await firstPhoto(page);
  const c = await photoCentre(page);
  await touch('touchStart', [[c.x - 20, c.y]]);
  await touch('touchStart', [[c.x - 20, c.y], [c.x + 20, c.y]]);
  for (let step = 1; step <= 8; step += 1) {
    await touch('touchMove', [[c.x - 20 - step * 8, c.y], [c.x + 20 + step * 8, c.y]]);
  }
  await touch('touchEnd', []);
  await expect.poll(async () => (await zoomNow(page)).shown).toBeGreaterThan(before.shown);
  const after = await zoomNow(page);
  expect(after.photoWidth).toBeGreaterThan(before.photoWidth * 1.5);
  await page.waitForTimeout(700);
  const unmoved = await firstPhoto(page);
  expect([unmoved.x, unmoved.y, unmoved.width]).toEqual([placed.x, placed.y, placed.width]);

  // One finger, once every finger has lifted, drags the photo on the page.
  const from = await photoCentre(page);
  await touch('touchStart', [[from.x, from.y]]);
  for (let step = 1; step <= 8; step += 1) await touch('touchMove', [[from.x + step * 6, from.y + step * 3]]);
  await touch('touchEnd', []);
  await expect.poll(async () => (await firstPhoto(page)).x).toBeGreaterThan(placed.x + 5);
  expect((await zoomNow(page)).shown).toBe(after.shown);
  expect(errors).toEqual([]);
});
