'use strict';

// Photos keep their order (docs/ux/collage-usability-plan.md, PR 4): on a grid, where every frame
// has the same shape, photos fill the frames in the order picked, left to right then down; the
// last ones picked are the ones left out. Mixed layouts still match tall photos to tall frames.

const { test, expect } = require('./test');
const { openFresh, startOn, photoFile, newestDraft } = require('./helpers');

/** Wide and tall photos in turn, named p1, p2 and on. */
async function mixedPhotos(page, count) {
  const files = [];
  for (let i = 0; i < count; i += 1) {
    const wide = i % 2 === 0;
    files.push(await photoFile(page, { name: `p${i + 1}.jpg`, width: wide ? 1200 : 800, height: wide ? 800 : 1200, colour: `hsl(${i * 45}, 55%, 40%)` }));
  }
  return files;
}

/** The file name in each frame of the layout, by the frame's place in it. */
async function namesByCell(page) {
  const draft = await newestDraft(page);
  const names = Object.fromEntries(await page.evaluate(() => [...document.querySelectorAll('[data-tray-photo]')]
    .map((button) => [button.dataset.trayPhoto, button.getAttribute('aria-label') || button.title || ''])));
  return draft.model.items
    .filter((item) => item.kind === 'photo' && Number.isInteger(item.cell))
    .sort((a, b) => a.cell - b.cell)
    .map((item) => (names[item.photo] || '').match(/p\d+/)?.[0] || '?');
}

test('six wide and tall photos on "6 grid" read p1 to p6, left to right then down', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^6 grid/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await mixedPhotos(page, 6));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((item) => item.kind === 'photo').length).toBe(6);
  expect(await namesByCell(page)).toEqual(['p1', 'p2', 'p3', 'p4', 'p5', 'p6']);
  expect(errors).toEqual([]);
});

test('photos chosen on the start screen keep their order too', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  const chooser = page.waitForEvent('filechooser');
  await page.locator('[data-start-drop]').getByRole('button', { name: 'Add photos' }).click();
  await (await chooser).setFiles(await mixedPhotos(page, 4));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((item) => item.kind === 'photo').length).toBe(4);
  expect((await newestDraft(page)).model.layout.key).toBe('t4');
  expect(await namesByCell(page)).toEqual(['p1', 'p2', 'p3', 'p4']);
  expect(errors).toEqual([]);
});

test('on a full grid the last photos picked are the ones that wait in Photos', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^4 grid/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await mixedPhotos(page, 6));
  await expect(page.locator('[data-alert-text]')).toContainText('2 photos did not fit');
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((item) => item.kind === 'photo').length).toBe(4);
  expect(await namesByCell(page)).toEqual(['p1', 'p2', 'p3', 'p4']);
  expect(errors).toEqual([]);
});
