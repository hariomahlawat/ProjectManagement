'use strict';

// Editor bugs from the fourth review (docs/ux/collage-review-4-fixes.md, PR 8): one frame's
// corners stay its own (E3); the space, corners and colour pickers hold their edit (E4); leaving
// frees every photo, stale imports too (E6); Delete on a button does nothing; leaving keeps a
// change still pending.

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft, showLayers, showSettings } = require('./helpers');

async function photos(page, count, from = 0) {
  const files = [];
  for (let i = from; i < from + count; i += 1) files.push(await photoFile(page, { name: `p${i}.jpg`, colour: `hsl(${i * 70}, 55%, 40%)` }));
  return files;
}

/** "1 big, 2 small" with three photos, written to its draft. */
async function threeInLayout(page) {
  await page.getByRole('button', { name: /^1 big, 2 small/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 3));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(3);
}

/** Watches every working copy: a canvas a photo is decoded onto, kept in window.__working. */
function watchWorkingCopies(page) {
  return page.addInitScript(() => {
    window.__working = [];
    const draw = CanvasRenderingContext2D.prototype.drawImage;
    CanvasRenderingContext2D.prototype.drawImage = function (source, ...rest) {
      if (source instanceof ImageBitmap && !this.canvas.isConnected) window.__working.push(this.canvas);
      return draw.call(this, source, ...rest);
    };
  });
}

test('one frame\'s corners stay its own: the layout\'s corners and a change of space leave them', async ({ page }) => {
  const errors = await openFresh(page);
  await threeInLayout(page);
  await showLayers(page);
  await page.locator('[data-layer-select]').first().click();
  await showSettings(page);
  await page.locator('.clg-props summary', { hasText: 'Corners and see-through' }).click();
  await page.locator('.clg-props [data-prop="radius"]').fill('30');
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.radius === 30).length).toBe(1);
  let model = (await newestDraft(page)).model;
  expect(model.layout.radius).toBe(0);
  const own = model.items.find((i) => i.radius === 30).id;

  // The layout's corners slider still shows the layout's.
  await page.locator('.clg-rail [data-panel="layouts"]').click();
  await expect(page.locator('[data-layout-input="radius"]')).toHaveValue('0');

  // A change of space lays the frames out again; the frame keeps its own corners.
  await page.locator('[data-layout-input="gap"]').fill('30');
  await expect.poll(async () => (await newestDraft(page))?.model.layout.gap).toBe(30);
  model = (await newestDraft(page)).model;
  expect(model.items.find((i) => i.id === own).radius).toBe(30);
  expect(model.items.filter((i) => i.id !== own && i.kind === 'photo').map((i) => i.radius)).toEqual([0, 0]);
  expect(errors).toEqual([]);
});

test('the space and corners sliders and the colour picker are kept in the draft while they move', async ({ page }) => {
  const errors = await openFresh(page);
  await threeInLayout(page);
  await page.locator('.clg-rail [data-panel="layouts"]').click();

  // Moved, not let go, and the page hidden (another app on a phone): the draft has the new value.
  const moveOnly = (selector, value) => page.evaluate(({ selector, value }) => {
    const input = document.querySelector(selector);
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    window.dispatchEvent(new Event('pagehide'));
  }, { selector, value });

  await moveOnly('[data-layout-input="gap"]', '40');
  await expect.poll(async () => (await newestDraft(page))?.model.layout.gap, { timeout: 3000 }).toBe(40);

  await moveOnly('[data-layout-input="radius"]', '25');
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').map((i) => i.radius), { timeout: 3000 })
    .toEqual([25, 25, 25]);

  await moveOnly('[data-bg-pick]', '#ff0000');
  await expect.poll(async () => (await newestDraft(page))?.model.background, { timeout: 3000 })
    .toEqual({ type: 'color', value: '#ff0000' });
  expect(errors).toEqual([]);
});

test('leaving the collage lets go of every photo\'s working copy', async ({ page }) => {
  await watchWorkingCopies(page);
  const errors = await openFresh(page);
  await threeInLayout(page);
  expect(await page.evaluate(() => window.__working.length)).toBe(3);
  await page.getByRole('button', { name: 'Back to the start' }).click();
  await expect(page.locator('[data-view="start"]')).toBeVisible();
  expect(await page.evaluate(() => window.__working.map((c) => c.width))).toEqual([0, 0, 0]);
  expect(errors).toEqual([]);
});

test('a photo read for a collage that was left meanwhile is let go', async ({ page }) => {
  await watchWorkingCopies(page);
  await page.addInitScript(() => {
    // While held, decoding waits until the test lets it go on.
    const decode = window.createImageBitmap.bind(window);
    window.__hold = false;
    window.createImageBitmap = async (...args) => {
      if (window.__hold) {
        window.__waiting = true;
        await new Promise((resolve) => { window.__release = resolve; });
      }
      return decode(...args);
    };
  });
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 1));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(1);

  await page.evaluate(() => { window.__hold = true; });
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 1, 1));
  await expect.poll(() => page.evaluate(() => window.__waiting === true)).toBe(true);
  await page.getByRole('button', { name: 'Back to the start' }).click();
  await expect(page.locator('[data-view="start"]')).toBeVisible();

  await page.evaluate(() => { window.__hold = false; window.__release(); });
  await expect.poll(() => page.evaluate(() => window.__working.length)).toBe(2);
  await expect.poll(() => page.evaluate(() => window.__working.map((c) => c.width))).toEqual([0, 0]);
  expect(errors).toEqual([]);
});

test('Delete on a button does nothing; on the page or the item\'s Layers row it deletes', async ({ page }) => {
  const errors = await openFresh(page);
  await threeInLayout(page);
  await showLayers(page);
  await page.locator('[data-layer-select]').first().click();
  const photoCount = async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length;

  for (const button of ['[data-toolbar] [data-action="duplicate"]', '.clg-props [data-props-tab="settings"]']) {
    await page.locator(button).focus();
    await page.keyboard.press('Delete');
    await page.keyboard.press('Backspace');
  }
  // Nothing was asked to be removed: the draft keeps all three, and nothing waits to be written.
  await page.waitForTimeout(800);
  expect(await photoCount()).toBe(3);
  await expect(page.locator('[data-toolbar]')).toBeVisible();

  // On the selected item's own row in Layers, the key deletes it.
  await showLayers(page);
  await page.locator('[data-layer-select][aria-current="true"]').click();
  await page.keyboard.press('Delete');
  await expect.poll(photoCount).toBe(2);
  expect(errors).toEqual([]);
});

test('leaving while a change is still being written asks, rather than dropping it', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 1));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(1);
  const id = (await newestDraft(page)).id;

  // Another tab keeps saying this draft was deleted, each time it is written, so this tab keeps
  // writing it back: there is always a change waiting.
  await page.evaluate((draftId) => {
    const channel = new BroadcastChannel('prism-collage-drafts');
    window.__nagging = true;
    const put = IDBObjectStore.prototype.put;
    IDBObjectStore.prototype.put = function (...args) {
      if (window.__nagging && this.name === 'drafts') channel.postMessage({ type: 'gone', id: draftId });
      return put.apply(this, args);
    };
    // A write is told it is done a little late, so the message has always arrived by then.
    const transaction = IDBDatabase.prototype.transaction;
    IDBDatabase.prototype.transaction = function (...args) {
      const tx = transaction.apply(this, args);
      if (!window.__nagging || args[1] !== 'readwrite') return tx;
      let handler = null;
      Object.defineProperty(tx, 'oncomplete', { get: () => handler, set: (fn) => { handler = fn; } });
      tx.addEventListener('complete', (event) => setTimeout(() => handler?.(event), 50));
      return tx;
    };
    channel.postMessage({ type: 'gone', id: draftId });
  }, id);
  await page.getByRole('button', { name: 'Back to the start' }).click();
  const dialog = page.locator('[data-prism-confirm-dialog]');
  await expect(dialog).toContainText('Leave this collage?');
  await expect(dialog).toContainText('still being saved');
  await page.evaluate(() => { window.__nagging = false; });
  await dialog.getByRole('button', { name: 'Keep working' }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  expect(errors).toEqual([]);
});
