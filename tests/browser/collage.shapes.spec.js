'use strict';

// Lines and borders (docs/ux/invitations-posters.md, PR 3): ready-made shapes from the Stickers
// panel go on the page, stretch freely, change colour and thickness, and Save draws them.

const { test, expect } = require('./test');
const { openFresh } = require('./helpers');

const kinds = (page) => page.evaluate(() => window.Konva.stages[0].find('.item').map((n) => `${n.getAttr('kind')}`));

test('a page border and a band go on the page, stretch freely, and Save draws them', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 portrait/ }).click();
  await page.locator('.clg-rail [data-panel="stickers"]').click();
  await page.locator('[data-sticker-group="lines"]').click();
  await expect(page.locator('[data-add-shape]')).toHaveCount(8);

  // A page border goes round the page, a little way in.
  await page.locator('[data-add-shape="border"]').click();
  expect(await kinds(page)).toEqual(['shape']);
  await expect(page.locator('[data-props] .clg-panel-title')).toHaveText('Rectangle');
  const border = await page.evaluate(() => {
    const node = window.Konva.stages[0].findOne('.item');
    return { box: node.getClientRect({ relativeTo: node.getLayer() }), page: window.Konva.stages[0].findOne('.page')?.getClientRect({ relativeTo: node.getLayer() }) };
  });
  expect(border.box.width).toBeGreaterThan(0);
  // A shape stretches freely from its corners too; a sticker keeps its shape.
  expect(await page.evaluate(() => window.Konva.stages[0].findOne('Transformer').keepRatio())).toBe(false);

  // Thicker, with a fill: the settings change the selected shape.
  await page.locator('[data-prop="strokeWidth"]').fill('12');
  await page.locator('[data-text-set="fill"][data-value="#fbf7ee"]').click();

  // A navy band, placed where it covers nothing (the border takes no room). The Stickers panel
  // is still open on Lines and borders.
  await page.locator('[data-add-shape="band"]').click();
  expect(await kinds(page)).toEqual(['shape', 'shape']);
  await expect(page.locator('[data-props] .clg-panel-title')).toHaveText('Rectangle');

  // A sticker still keeps its shape when it is selected.
  await page.locator('[data-sticker-group="shapes"]').click();
  await page.locator('[data-add-sticker^="draw:"]').first().click();
  expect(await page.evaluate(() => window.Konva.stages[0].findOne('Transformer').keepRatio())).toBe(true);

  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'A slide or a screen' }).click();
  const download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const file = await download;
  expect(file.suggestedFilename()).toMatch(/\.png$/);
  await expect(page.locator('[data-save-result]')).toContainText('Saved');

  const colours = await page.evaluate(async (base64) => {
    const text = atob(base64);
    const bytes = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i += 1) bytes[i] = text.charCodeAt(i);
    const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
    const canvas = document.createElement('canvas');
    canvas.width = bitmap.width;
    canvas.height = bitmap.height;
    const context = canvas.getContext('2d');
    context.drawImage(bitmap, 0, 0);
    const at = (fx, fy) => [...context.getImageData(Math.round(fx * bitmap.width), Math.round(fy * bitmap.height), 1, 1).data].slice(0, 3);
    const data = context.getImageData(0, 0, bitmap.width, bitmap.height).data;
    const near = (r, g, b) => {
      for (let i = 0; i < data.length; i += 4) {
        if (Math.abs(data[i] - r) < 10 && Math.abs(data[i + 1] - g) < 10 && Math.abs(data[i + 2] - b) < 10) return true;
      }
      return false;
    };
    // Outside the border the page is white; just inside it is the ivory fill.
    return { corner: at(0.005, 0.004), inside: at(0.2, 0.06), gold: near(0xa8, 0x84, 0x2f), navy: near(0x1d, 0x2c, 0x55) };
  }, (await streamBytes(file)).toString('base64'));
  expect(colours.corner).toEqual([255, 255, 255]);
  expect(colours.inside).toEqual([0xfb, 0xf7, 0xee]);
  expect(colours.gold).toBe(true);
  expect(colours.navy).toBe(true);
  expect(errors).toEqual([]);
});

test('a line stretches along its length only from its side handle, and its thickness stays', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('.clg-rail [data-panel="stickers"]').click();
  await page.locator('[data-sticker-group="lines"]').click();
  await page.locator('[data-add-shape="line"]').click();
  await expect(page.locator('[data-props] .clg-panel-title')).toHaveText('Line');

  const before = await page.evaluate(() => {
    const node = window.Konva.stages[0].findOne('.item');
    return { width: node.width() * node.scaleX(), height: node.height() * node.scaleY() };
  });
  const anchor = await page.evaluate(() => {
    const t = window.Konva.stages[0].findOne('Transformer');
    const r = t.findOne('.middle-right').getClientRect();
    const box = window.Konva.stages[0].container().getBoundingClientRect();
    return { x: box.left + r.x + r.width / 2, y: box.top + r.y + r.height / 2 };
  });
  await page.mouse.move(anchor.x, anchor.y);
  await page.mouse.down();
  await page.mouse.move(anchor.x + 120, anchor.y, { steps: 6 });
  await page.mouse.up();
  const after = await page.evaluate(() => {
    const node = window.Konva.stages[0].findOne('.item');
    return { width: node.width() * node.scaleX(), height: node.height() * node.scaleY() };
  });
  expect(after.width).toBeGreaterThan(before.width + 50);
  expect(Math.abs(after.height - before.height)).toBeLessThan(1);
  await expect(page.locator('[data-prop="strokeWidth"]')).toHaveValue('6');
  expect(errors).toEqual([]);
});

async function streamBytes(download) {
  const chunks = [];
  for await (const chunk of await download.createReadStream()) chunks.push(chunk);
  return Buffer.concat(chunks);
}
