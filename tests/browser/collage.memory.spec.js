'use strict';

// Working copies stay within their memory budget (docs/ux/collage-review-3-fixes.md, PR 9): each
// photo is decoded smaller as the collage keeps more photos, and copies made while there were
// fewer are made smaller when more are added. The sizes are read by watching the browser's own
// decoding and drawing; the editor has no test hook for this.

const { test, expect } = require('./test');
const { openFresh, photoFile } = require('./helpers');

// The same numbers as core.PHOTO_LIMITS: 60 million pixels in all, at most 2,000 px a side.
const BUDGET = 60 * 1000 * 1000;

test.setTimeout(120000);

test('many photos are decoded smaller, and earlier copies shrink to fit the budget', async ({ page }) => {
  await page.addInitScript(() => {
    window.__decoded = [];
    window.__shrunk = [];
    window.__order = [];
    const decode = window.createImageBitmap.bind(window);
    window.createImageBitmap = (source, ...rest) => {
      const options = rest[rest.length - 1];
      if (options && options.resizeWidth && options.resizeHeight) {
        window.__decoded.push(options.resizeWidth * options.resizeHeight);
        window.__order.push('decode');
      }
      return decode(source, ...rest);
    };
    const draw = CanvasRenderingContext2D.prototype.drawImage;
    CanvasRenderingContext2D.prototype.drawImage = function (source, ...rest) {
      // A photo-sized working copy drawn again onto a canvas of its own (not the page, not a tray
      // picture, not a small preview).
      if (rest.length === 4 && source instanceof HTMLCanvasElement && !this.canvas.isConnected
        && source.width * source.height >= 1000 * 1000 && rest[2] * rest[3] > 240 * 240) {
        window.__shrunk.push({ from: source.width * source.height, to: rest[2] * rest[3] });
        window.__order.push('shrink');
      }
      return draw.call(this, source, ...rest);
    };
  });
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();

  const files = async (from, count) => {
    const list = [];
    for (let i = from; i < from + count; i += 1) {
      list.push(await photoFile(page, { width: 2400, height: 1800, name: `m${i}.jpg`, colour: `hsl(${i * 13}, 55%, 45%)` }));
    }
    return list;
  };

  // Ten photos: each keeps the full 2,000 px working copy.
  await page.locator('[data-file-input]').setInputFiles(await files(0, 10));
  await expect(page.locator('[data-photo-count]')).toHaveText('10 of 60 photos');
  const first = await page.evaluate(() => window.__decoded.slice());
  expect(first).toHaveLength(10);
  for (const size of first) expect(size).toBe(2000 * 1500);
  expect(await page.evaluate(() => window.__shrunk.length)).toBe(0);

  // Twenty more (30 in all): the new ones are decoded at 2 million pixels each, and the first
  // ten are drawn again at that size.
  await page.locator('[data-file-input]').setInputFiles(await files(10, 20));
  await expect(page.locator('[data-photo-count]')).toHaveText('30 of 60 photos', { timeout: 60000 });
  const second = (await page.evaluate(() => window.__decoded.slice())).slice(10);
  expect(second).toHaveLength(20);
  for (const size of second) expect(size).toBeLessThanOrEqual(BUDGET / 30);
  const shrunk = await page.evaluate(() => window.__shrunk.slice());
  expect(shrunk).toHaveLength(10);
  for (const step of shrunk) {
    expect(step.from).toBe(2000 * 1500);
    expect(step.to).toBeLessThanOrEqual(BUDGET / 30);
  }
  // The first ten are made smaller before the batch is decoded, so the budget holds meanwhile.
  const order = await page.evaluate(() => window.__order.slice(10));
  expect(order).toEqual([...Array(10).fill('shrink'), ...Array(20).fill('decode')]);
  const total = second.reduce((sum, size) => sum + size, 0) + shrunk.reduce((sum, step) => sum + step.to, 0);
  expect(total).toBeLessThanOrEqual(BUDGET);

  expect(errors).toEqual([]);
});
