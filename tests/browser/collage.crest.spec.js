'use strict';

// A crest from Administration › Crests and logos on a collage, under PRISM's production headers
// (docs/ux/collage-review-3-fixes.md, PR 14). The list and the picture are PRISM's own: the C#
// test CollageBrowserFixtureTests uploads a crest through the real service and writes the list,
// the picture's headers and the stored PNG to tests/browser/fixtures.

const { test, expect } = require('./test');
const { openFresh, startOn } = require('./helpers');
const samples = require('./fixtures/collage-api-samples.json');

const crest = samples.stickers.stickers[0];

test('a crest goes on the collage, is drawn, and Save makes a PNG with it', async ({ page }) => {
  const errors = await openFresh(page);
  const pictureAnswers = [];
  page.on('response', (response) => {
    if (new URL(response.url()).pathname === `/api/collage/stickers/${crest.id}/image`) pictureAnswers.push(response);
  });

  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('.clg-rail [data-panel="stickers"]').click();
  await page.locator('[data-sticker-group="crests"]').click();
  const button = page.locator(`[data-add-sticker="unit:${crest.id}"]`);
  await expect(button).toHaveAttribute('aria-label', `Add ${crest.name}`);
  // The picker shows the crest's picture, loaded under the production headers.
  await expect.poll(() => button.locator('img').evaluate((img) => img.complete && img.naturalWidth)).toBe(crest.width);
  await button.click();

  const answer = pictureAnswers[0];
  expect(answer, 'the crest picture was requested').toBeTruthy();
  expect(answer.status()).toBe(200);
  expect(answer.headers()['content-type']).toBe('image/png');
  expect(answer.headers()['content-security-policy']).toContain("img-src 'self' data: blob:");
  expect(answer.headers()['cross-origin-resource-policy']).toBe('same-origin');

  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'A slide or a screen' }).click();
  const download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const file = await download;
  expect(file.suggestedFilename()).toMatch(/\.png$/);
  await expect(page.locator('[data-save-result]')).toContainText('Saved');
  // Nothing was left out: the crest loaded and was drawn into the saved picture.
  await expect(page.locator('[data-save-result]')).not.toContainText('could not be loaded');

  // The saved picture has the crest's navy and gold in it.
  const colours = await page.evaluate(async (base64) => {
    const text = atob(base64);
    const bytes = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i += 1) bytes[i] = text.charCodeAt(i);
    const blob = new Blob([bytes], { type: 'image/png' });
    const bitmap = await createImageBitmap(blob);
    const canvas = document.createElement('canvas');
    canvas.width = bitmap.width;
    canvas.height = bitmap.height;
    const context = canvas.getContext('2d');
    context.drawImage(bitmap, 0, 0);
    const data = context.getImageData(0, 0, bitmap.width, bitmap.height).data;
    const near = (r, g, b) => {
      for (let i = 0; i < data.length; i += 4) {
        if (Math.abs(data[i] - r) < 12 && Math.abs(data[i + 1] - g) < 12 && Math.abs(data[i + 2] - b) < 12) return true;
      }
      return false;
    };
    return { navy: near(0x1f, 0x3a, 0x68), gold: near(0xd4, 0xa0, 0x17) };
  }, (await streamBytes(file)).toString('base64'));
  expect(colours).toEqual({ navy: true, gold: true });

  expect(errors).toEqual([]);
});

async function streamBytes(download) {
  const chunks = [];
  for await (const chunk of await download.createReadStream()) chunks.push(chunk);
  return Buffer.concat(chunks);
}
