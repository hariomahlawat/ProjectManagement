'use strict';

// The editor's test gaps (docs/ux/collage-review-4-fixes.md, PR 12): moving a photo in its frame
// with a drag, leaving the editor and closing the page, and a draft deleted in another tab while
// it is open here. (Two-finger zoom is in collage.r4tests.phone.spec.js; the frozen collage and
// the change log are in wwwroot/js/collage/collage-every-change.test.js.)

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft } = require('./helpers');

/**
 * Holds back the draft's own half-second autosave (collage-draft-session.js, SAVE_DELAY_MS) while
 * window.__noAutosave is set, so a test sees only the writes that leaving or hiding the page
 * makes. Nothing else in the editor waits exactly 500 ms.
 */
async function holdAutosave(page) {
  await page.addInitScript(() => {
    const real = window.setTimeout;
    window.setTimeout = function (callback, delay, ...rest) {
      if (window.__noAutosave && delay === 500) return real(() => {}, 0);
      return real.call(window, callback, delay, ...rest);
    };
  });
}

async function startWithPhoto(page, file = {}) {
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'one.jpg', ...file }));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(1);
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');
}

/** The middle of the first photo on the page, in window pixels. */
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

/** The words in a stored draft. */
const wordsIn = (draft) => draft?.model.items.filter((i) => i.kind === 'text').map((i) => i.text) || [];

test('moving a photo in its frame with a drag changes the part shown; Undo puts it back in one step; Done ends it', async ({ page }) => {
  const errors = await openFresh(page);
  await startWithPhoto(page, { width: 1600, height: 900 });
  await page.locator('[data-toolbar] [data-action="adjust"]').click();
  await expect(page.locator('[data-toolbar] [data-action="adjust-done"]')).toBeVisible();
  await expect(page.locator('[data-adjust-hint]')).toBeVisible();
  // Moving begins zoomed in a little, so there is something to move; that is its own step.
  await expect.poll(async () => (await firstPhoto(page)).crop.zoom).toBeGreaterThan(1);
  const before = await firstPhoto(page);

  const from = await photoCentre(page);
  await page.mouse.move(from.x, from.y);
  await page.mouse.down();
  await page.mouse.move(from.x + 70, from.y + 30, { steps: 10 });
  await page.mouse.up();
  await expect.poll(async () => (await firstPhoto(page)).crop.x).not.toBeCloseTo(before.crop.x, 3);
  const moved = await firstPhoto(page);
  // The frame itself stayed where it was: only the part of the photo shown moved.
  expect([moved.x, moved.y, moved.width]).toEqual([before.x, before.y, before.width]);
  expect(moved.crop.zoom).toBeCloseTo(before.crop.zoom, 5);

  // Done ends it: the hint and the Done button go, and the part shown stays.
  await page.locator('[data-toolbar] [data-action="adjust-done"]').click();
  await expect(page.locator('[data-adjust-hint]')).toBeHidden();
  await expect(page.locator('[data-toolbar] [data-action="adjust-done"]')).toBeHidden();
  expect((await firstPhoto(page)).crop).toEqual(moved.crop);

  // One Undo takes back the whole drag (not each step of it), and only the drag.
  await page.locator('[data-action="undo"]').click();
  await expect.poll(async () => {
    const { crop } = await firstPhoto(page);
    return [crop.x, crop.y, crop.zoom].map((v) => Math.round(v * 1e6));
  }).toEqual([before.crop.x, before.crop.y, before.crop.zoom].map((v) => Math.round(v * 1e6)));

  // With moving ended, a drag moves the photo on the page, not the part shown.
  const again = await photoCentre(page);
  await page.keyboard.down('Alt');
  await page.mouse.move(again.x, again.y);
  await page.mouse.down();
  await page.mouse.move(again.x + 60, again.y + 20, { steps: 8 });
  await page.mouse.up();
  await page.keyboard.up('Alt');
  await expect.poll(async () => (await firstPhoto(page)).x).toBeGreaterThan(before.x + 40);
  const placed = await firstPhoto(page);
  expect(placed.crop.x).toBeCloseTo(before.crop.x, 5);
  expect(placed.crop.y).toBeCloseTo(before.crop.y, 5);
  expect(errors).toEqual([]);
});

test.describe('leaving the editor and closing the page', () => {
  test.beforeEach(async ({ page }) => { await holdAutosave(page); });

  const addHeadingNow = (page) => page.evaluate(() => {
    document.querySelector('.clg-rail [data-panel="text"]').click();
    document.querySelector('[data-add-text="heading"]').click();
  });

  test('Back to the start writes the change made just before', async ({ page }) => {
    const errors = await openFresh(page);
    await startWithPhoto(page);
    await page.evaluate(() => { window.__noAutosave = true; });
    await addHeadingNow(page);
    await page.waitForTimeout(1200);
    expect(wordsIn(await newestDraft(page)), 'the autosave is held back').toEqual([]);

    await page.getByRole('button', { name: 'Back to the start' }).click();
    await expect(page.locator('[data-view="start"]')).toBeVisible();
    // No question: the draft was written on the way out.
    await expect(page.locator('[data-prism-confirm-dialog]')).toBeHidden();
    expect(wordsIn(await newestDraft(page))).toHaveLength(1);
    expect(errors).toEqual([]);
  });

  test('the page being hidden writes the draft', async ({ page }) => {
    const errors = await openFresh(page);
    await startWithPhoto(page);
    await page.evaluate(() => { window.__noAutosave = true; });
    await addHeadingNow(page);
    await page.waitForTimeout(1200);
    expect(wordsIn(await newestDraft(page)), 'the autosave is held back').toEqual([]);

    // Another app or tab, or the screen locked: the page is hidden.
    await page.evaluate(() => {
      Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'hidden' });
      document.dispatchEvent(new Event('visibilitychange'));
    });
    await expect.poll(async () => wordsIn(await newestDraft(page))).toHaveLength(1);
    await expect(page.locator('[data-view="editor"]')).toBeVisible();
    expect(errors).toEqual([]);
  });

  test('the page being put away (pagehide) writes the draft', async ({ page }) => {
    const errors = await openFresh(page);
    await startWithPhoto(page);
    await page.evaluate(() => { window.__noAutosave = true; });
    await addHeadingNow(page);
    await page.waitForTimeout(1200);
    expect(wordsIn(await newestDraft(page)), 'the autosave is held back').toEqual([]);

    await page.evaluate(() => window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false })));
    await expect.poll(async () => wordsIn(await newestDraft(page))).toHaveLength(1);
    expect(errors).toEqual([]);
  });

  test('closing asks only while a change is not yet written, and writes it', async ({ page }) => {
    const errors = await openFresh(page);
    await startWithPhoto(page);
    const asks = () => page.evaluate(() => {
      const event = new Event('beforeunload', { cancelable: true });
      window.dispatchEvent(event);
      return event.defaultPrevented;
    });
    // Written: closing asks nothing.
    expect(await asks()).toBe(false);

    await page.evaluate(() => { window.__noAutosave = true; });
    await addHeadingNow(page);
    // A change not yet written: the browser asks, and the draft is written meanwhile.
    expect(await asks()).toBe(true);
    await expect.poll(async () => wordsIn(await newestDraft(page))).toHaveLength(1);
    await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');
    // Written again: nothing to ask.
    expect(await asks()).toBe(false);
    expect(errors).toEqual([]);
  });
});

/** Draft ids, whether deleted, and how many photos each has in the drafts database. */
async function stored(page) {
  return page.evaluate(() => new Promise((resolve, reject) => {
    const open = indexedDB.open('prism-collage-studio');
    open.onerror = () => reject(open.error);
    open.onsuccess = () => {
      const db = open.result;
      const tx = db.transaction(['drafts', 'photos']);
      const drafts = tx.objectStore('drafts').getAll();
      const photos = tx.objectStore('photos').getAll();
      tx.oncomplete = () => {
        db.close();
        const out = {};
        for (const d of drafts.result) {
          out[d.id] = { deleted: Boolean(d.deletedAt), photos: photos.result.filter((p) => p.draft === d.id).length, words: d.model.items.filter((i) => i.kind === 'text').length };
        }
        resolve(out);
      };
    };
  }));
}

test('a draft deleted in another tab is written back at once, whole, only when it is the one open here', async ({ page, context }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  const twoPhotos = async () => [
    await photoFile(page, { name: 'a.jpg', colour: '#7b2d26' }),
    await photoFile(page, { name: 'b.jpg', colour: '#264653' })
  ];

  // A first draft, left; then a second, open in this tab.
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await twoPhotos());
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');
  const leftId = (await newestDraft(page)).id;
  await page.getByRole('button', { name: 'Back to the start' }).click();
  await expect(page.locator('[data-open-draft]')).toHaveCount(1);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await twoPhotos());
  await expect.poll(async () => Object.keys(await stored(page)).length).toBe(2);
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');
  const openId = (await newestDraft(page)).id;
  expect(openId).not.toBe(leftId);

  // The other tab deletes the draft that is not open here: it stays deleted, and is purged.
  const other = await context.newPage();
  await other.goto('/Tools/Collage');
  await expect(other.locator('[data-open-draft]')).toHaveCount(2);
  await other.locator(`[data-delete-draft="${leftId}"]`).click();
  await expect(other.locator('[data-draft-undo]')).toBeVisible();
  await page.waitForTimeout(1500);
  expect((await stored(page))[leftId]).toMatchObject({ deleted: true });

  // Then the one open here, with a change still on its way: this tab writes it back at once,
  // well before the other tab's Undo runs out, with both photos and the change.
  await page.evaluate(() => {
    document.querySelector('.clg-rail [data-panel="text"]').click();
    document.querySelector('[data-add-text="heading"]').click();
  });
  await other.locator(`[data-delete-draft="${openId}"]`).click();
  await expect.poll(async () => (await stored(page))[openId], { timeout: 5000 })
    .toEqual({ deleted: false, photos: 2, words: 1 });

  // Once the other tab's Undo has gone, the draft not open here is purged; the open one is
  // still whole (purged there, and written back here again).
  await expect(other.locator('[data-draft-undo]:visible')).toHaveCount(0, { timeout: 20000 });
  await expect.poll(async () => await stored(page), { timeout: 10000 })
    .toEqual({ [openId]: { deleted: false, photos: 2, words: 1 } });
  await other.reload();
  await expect(other.locator('[data-open-draft]')).toHaveCount(1);
  await other.close();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  expect(errors).toEqual([]);
});
