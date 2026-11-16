'use strict';

// Messages never cover the page (docs/ux/collage-review-3-fixes.md, PR 3): they share PRISM's toast
// stack with the Undo offers, end at the user's next change, and never show in Preview.

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft } = require('./helpers');

async function photos(page, count) {
  const files = [];
  for (let i = 0; i < count; i += 1) files.push(await photoFile(page, { name: `p${i}.jpg`, colour: `hsl(${i * 50}, 55%, 40%)` }));
  return files;
}

const message = (page, text) => page.locator('[data-prism-toast]:not(.is-leaving)').filter({ hasText: text });

test('the "did not fit" message is a toast, hidden in Preview, and gone after the next change', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^1 big, 2 small/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 5));
  await expect(message(page, 'did not fit')).toBeVisible();
  // The page's own alert line stays out of the way when the toasts are there.
  await expect(page.locator('[data-alert]')).toBeHidden();

  await page.locator('[data-action="preview"]').first().click();
  await expect(page.locator('[data-preview-exit]')).toBeVisible();
  await expect(page.locator('.prism-toasts')).toBeHidden();
  await page.locator('[data-preview-exit]').click();

  // The next change ends it.
  await page.locator('.clg-rail [data-panel="text"]').click();
  await page.locator('[data-add-text="heading"]').click();
  await expect(message(page, 'did not fit')).toHaveCount(0);
  await expect(page.locator('[data-alert-text]')).toHaveText('');
  expect(errors).toEqual([]);
});

test('a tray photo dropped beside the page when every frame is full says so, and changes nothing', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^2 side by side/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 3));
  await expect(page.locator('[data-photo-count]')).toHaveText('3 of 60 photos');
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(2);
  const before = (await newestDraft(page)).model;
  await page.locator('.clg-rail [data-panel="photos"]').click();
  const spare = await page.locator('[data-tray-photo]').last().getAttribute('data-tray-photo');

  // Dropped on the grey area beside the page.
  await page.evaluate((id) => {
    const box = document.querySelector('[data-stage]');
    const rect = box.getBoundingClientRect();
    const data = new DataTransfer();
    data.setData('text/x-collage-photo', id);
    box.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer: data, clientX: rect.left + 4, clientY: rect.top + 4 }));
  }, spare);
  await expect(page.locator('[data-alert-text]')).toContainText('Every frame of this layout has a photo');
  await expect(page.locator('[data-announce]')).not.toHaveText('Photo added.');
  expect((await newestDraft(page)).model.items).toEqual(before.items);
  expect(errors).toEqual([]);
});
