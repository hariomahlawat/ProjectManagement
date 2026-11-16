'use strict';

// Words are measured for drawing without changing the collage; a font that arrives later gives
// the words their size as a recorded change, kept in the draft (docs/ux/collage-review-4-fixes.md, PR 10).

const { test, expect } = require('./test');
const { openFresh, newestDraft } = require('./helpers');

/** A heading whose stored size is from before its font arrived: opened, put right, then Undone. Returns its real width. */
async function staleHeading(page) {
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('.clg-rail [data-panel="text"]').click();
  await page.locator('[data-add-text="heading"]').click();
  await page.locator('.clg-props [data-text]').fill('Farewell');
  await expect.poll(async () => (await newestDraft(page))?.model.items.find((i) => i.kind === 'text')?.text).toBe('Farewell');
  const right = (await newestDraft(page)).model.items.find((i) => i.kind === 'text').width;
  await page.getByRole('button', { name: 'Back to the start' }).click();

  // The stored draft says the words are much narrower than they are, as a draft saved before its
  // font arrived would.
  await page.evaluate(() => new Promise((resolve, reject) => {
    const open = indexedDB.open('prism-collage-studio');
    open.onerror = () => reject(open.error);
    open.onsuccess = () => {
      const db = open.result;
      const store = db.transaction('drafts', 'readwrite').objectStore('drafts');
      const all = store.getAll();
      all.onsuccess = () => {
        const record = all.result[0];
        const text = record.model.items.find((i) => i.kind === 'text');
        text.width = 20;
        store.put(record);
        store.transaction.oncomplete = () => { db.close(); resolve(); };
      };
    };
  }));
  await page.reload();
  await page.locator('[data-open-draft]').first().click();
  // When the fonts arrive the size is put right, and the draft is written with it.
  await expect.poll(async () => (await newestDraft(page))?.model.items.find((i) => i.kind === 'text')?.width, { timeout: 10000 }).toBeGreaterThan(right * 0.9);

  // Undo brings back the size from before the font arrived; the words are still drawn and
  // selected at the size they really take (as Save draws them), not the old one.
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await newestDraft(page))?.model.items.find((i) => i.kind === 'text')?.width).toBeLessThan(right * 0.5);
  return right;
}

test('a heading whose stored size is out of date is given its size as a change the draft keeps', async ({ page }) => {
  const errors = await openFresh(page);
  const right = await staleHeading(page);
  const drawn = await page.evaluate(() => window.Konva.stages[0].find((n) => n.getAttr('kind') === 'text')[0].width());
  expect(drawn).toBeGreaterThan(right * 0.9);

  // Lined up on the left of the page, the words land by their real width, not the old one.
  const textId = (await newestDraft(page)).model.items.find((i) => i.kind === 'text').id;
  await page.locator('[data-props-tab="layers"]').click();
  await page.locator(`[data-layer-select="${textId}"]`).click();
  await page.locator('[data-props-tab="settings"]').click();
  await page.locator('.clg-props summary', { hasText: 'Line up' }).click();
  await page.locator('.clg-props [data-align="left"]').click();
  await expect.poll(async () => {
    const text = (await newestDraft(page))?.model.items.find((i) => i.kind === 'text');
    return text && Math.round(text.x - text.width / 2);
  }).toBe(0);
  const lined = (await newestDraft(page)).model.items.find((i) => i.kind === 'text');
  expect(lined.width).toBeGreaterThan(right * 0.9);
  expect(errors).toEqual([]);
});

test('a heading with an out-of-date stored size snaps to the page edge by the width it is drawn at', async ({ page }) => {
  const errors = await openFresh(page);
  const right = await staleHeading(page);
  const box = await page.evaluate(() => {
    const stage = window.Konva.stages[0];
    const host = stage.container().getBoundingClientRect();
    const node = stage.find((n) => n.getAttr('kind') === 'text')[0];
    const page = stage.findOne('.page').getClientRect({ skipShadow: true, skipStroke: true });
    const r = node.getClientRect({ skipShadow: true, skipStroke: true });
    return { cx: r.x + host.x + r.width / 2, cy: r.y + host.y + r.height / 2, left: r.x + host.x, pageLeft: page.x + host.x };
  });
  // Dragged so its drawn left edge is just inside the page's left edge: it snaps onto the edge.
  const dx = box.pageLeft + 3 - box.left;
  await page.mouse.move(box.cx, box.cy);
  await page.mouse.down();
  for (let i = 1; i <= 10; i += 1) await page.mouse.move(box.cx + (dx * i) / 10, box.cy);
  await page.mouse.up();
  await expect.poll(async () => {
    const text = (await newestDraft(page))?.model.items.find((i) => i.kind === 'text');
    return text && Math.round(text.x - right / 2);
  }).toBe(0);
  expect(errors).toEqual([]);
});
