'use strict';

// Drafts never lose photos when they are deleted while open (docs/ux/collage-review-2-fixes.md,
// PR 2): from the start screen while the draft opens, or from another tab.

const { test, expect } = require('./test');
const { openFresh, startOn, photoFile, showLayers } = require('./helpers');

/** Draft records and photo counts in the drafts database, by draft id. */
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
        resolve(drafts.result.map((d) => ({
          id: d.id, deletedAt: d.deletedAt, photos: photos.result.filter((p) => p.draft === d.id).length
        })));
      };
    };
  }));
}

async function draftWithPhotos(page, count) {
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  const files = [];
  for (let i = 0; i < count; i += 1) files.push(await photoFile(page, { name: `p${i}.jpg`, colour: `hsl(${i * 40}, 60%, 40%)` }));
  await page.locator('[data-file-input]').setInputFiles(files);
  await expect(page.locator('[data-photo-count]')).toHaveText(`${count} of 60 photos`);
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');
}

test('a draft cannot be deleted while it is opening', async ({ page }) => {
  const errors = await openFresh(page);
  await draftWithPhotos(page, 3);
  await page.getByRole('button', { name: 'Back to the start' }).click();
  await expect(page.locator('[data-open-draft]')).toHaveCount(1);

  // Open, and at once try to delete it, as a quick double move would.
  await page.evaluate(() => {
    document.querySelector('[data-open-draft]').click();
    document.querySelector('[data-delete-draft]').click();
  });
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await expect(page.locator('[data-photo-count]')).toHaveText('3 of 60 photos');
  const [draft] = await stored(page);
  expect(draft.deletedAt).toBeNull();
  expect(draft.photos).toBe(3);
  expect(errors).toEqual([]);
});

test('a draft deleted in another tab while open here keeps its photos', async ({ page, context }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  await draftWithPhotos(page, 2);

  // The same drafts in a second tab, where the open draft is deleted and its Undo runs out.
  const other = await context.newPage();
  await other.goto('/Tools/Collage');
  await expect(other.locator('[data-open-draft]')).toHaveCount(1);
  await other.locator('[data-delete-draft]').first().click();
  await expect(other.locator('[data-draft-undo]')).toBeVisible();
  await expect(other.locator('[data-draft-undo]')).toBeHidden({ timeout: 20000 });

  // The tab with the draft open wrote it back, photos and all.
  await expect.poll(async () => (await stored(page)).map((d) => [d.deletedAt, d.photos]), { timeout: 10000 })
    .toEqual([[null, 2]]);
  await other.reload();
  await expect(other.locator('[data-open-draft]')).toHaveCount(1);
  await other.close();
  expect(errors).toEqual([]);
});

test('a photo the page does not show can be removed; Undo of its frame brings it back', async ({ page }) => {
  const errors = await openFresh(page);
  await draftWithPhotos(page, 2);
  await expect(page.locator('[data-remove-photo]')).toHaveCount(0);

  // Take the first photo off the page: it stays in the tray, now removable.
  await showLayers(page);
  await page.locator('[data-layer-select]').last().click();
  await page.locator('[data-action="delete"][data-for="item"]').click();
  await expect(page.locator('[data-remove-photo]')).toHaveCount(1);
  await page.locator('[data-remove-photo]').click();
  await expect(page.locator('[data-photo-count]')).toHaveText('1 of 60 photos');
  await expect(page.locator('[data-tray-photo]')).toHaveCount(1);
  await expect.poll(async () => (await stored(page)).map((d) => d.photos)).toEqual([1]);

  // Undo brings back the frame that showed it, and with it the photo.
  await page.locator('body').click({ position: { x: 5, y: 5 } }).catch(() => {});
  await page.keyboard.press('Control+z');
  await expect(page.locator('[data-photo-count]')).toHaveText('2 of 60 photos');
  await expect.poll(async () => (await stored(page)).map((d) => d.photos)).toEqual([2]);
  expect(errors).toEqual([]);
});
