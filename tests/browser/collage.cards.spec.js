'use strict';

// Card sizes and the SDD logo (docs/ux/invitations-posters.md, PR 4): the size menu has cards and
// pocket cards in their own groups, and PRISM's own SDD logo is first in Crests and logos and
// goes into the saved picture.

const { test, expect } = require('./test');
const { openFresh } = require('./helpers');

test('a car pass size and the SDD logo: the menu groups sizes, and Save draws the logo at the card shape', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();

  const size = page.locator('[data-size]');
  expect(await size.locator('optgroup').evaluateAll((groups) => groups.map((g) => g.label))).toEqual(['Pages and screens', 'Cards', 'Pocket cards']);
  await size.selectOption('passl');
  await expect(size).toHaveValue('passl');

  await page.locator('.clg-rail [data-panel="stickers"]').click();
  await page.locator('[data-sticker-group="crests"]').click();
  const sdd = page.locator('[data-add-sticker="logo:sdd"]');
  await expect(sdd).toHaveAttribute('aria-label', 'Add SDD');
  // The picker shows the logo itself, from the picture shipped with PRISM.
  await expect.poll(() => sdd.locator('img').evaluate((img) => img.complete && img.naturalWidth)).toBe(1732);
  await sdd.click();
  await expect(page.locator('[data-props] .clg-panel-title')).toContainText('SDD');

  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'A slide or a screen' }).click();
  const download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const file = await download;
  await expect(page.locator('[data-save-result]')).toContainText('Saved');
  await expect(page.locator('[data-save-result]')).not.toContainText('could not be loaded');

  const picture = await page.evaluate(async (base64) => {
    const text = atob(base64);
    const bytes = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i += 1) bytes[i] = text.charCodeAt(i);
    const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
    const canvas = document.createElement('canvas');
    canvas.width = bitmap.width;
    canvas.height = bitmap.height;
    const context = canvas.getContext('2d');
    context.drawImage(bitmap, 0, 0);
    const data = context.getImageData(0, 0, bitmap.width, bitmap.height).data;
    // The archer's gold: red well above blue.
    let gold = 0;
    for (let i = 0; i < data.length; i += 4) if (data[i] > 150 && data[i] - data[i + 2] > 70) gold += 1;
    return { ratio: bitmap.width / bitmap.height, gold };
  }, (await streamBytes(file)).toString('base64'));
  // A visiting card, 89 × 51 mm.
  expect(Math.abs(picture.ratio - 88.9 / 50.8)).toBeLessThan(0.01);
  expect(picture.gold).toBeGreaterThan(500);
  expect(errors).toEqual([]);
});

async function streamBytes(download) {
  const chunks = [];
  for await (const chunk of await download.createReadStream()) chunks.push(chunk);
  return Buffer.concat(chunks);
}
