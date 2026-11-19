'use strict';

// The collage changes only through edit() (docs/ux/collage-review-4-fixes.md, PR 9): it is frozen
// in between, so any part that changed it directly would throw. These paths change it in less
// usual ways and must keep working.

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft, startOn } = require('./helpers');

test('the wheel zooms a photo in its frame while it is moved, and the change is kept', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'one.jpg' }));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(1);
  await page.locator('[data-toolbar] [data-action="adjust"]').click();
  await expect(page.locator('[data-toolbar] [data-action="adjust-done"]')).toBeVisible();
  const before = (await newestDraft(page)).model.items[0].crop.zoom;
  const box = await page.locator('[data-stage]').boundingBox();
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  for (let i = 0; i < 4; i += 1) await page.mouse.wheel(0, -100);
  await expect.poll(async () => (await newestDraft(page))?.model.items[0].crop.zoom).toBeGreaterThan(before);
  await page.locator('[data-toolbar] [data-action="adjust-done"]').click();
  expect(errors).toEqual([]);
});
