'use strict';

// Casual prints stay on the page, a batch fills frames by shape, and words over photos can be read
// (docs/ux/collage-review-4-fixes.md, PR 4).

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft, startOn } = require('./helpers');

const turnedBox = (i) => {
  const r = (i.angle || 0) * Math.PI / 180;
  const h = i.width / i.aspect;
  const hw = (i.width * Math.abs(Math.cos(r)) + h * Math.abs(Math.sin(r))) / 2;
  const hh = (i.width * Math.abs(Math.sin(r)) + h * Math.abs(Math.cos(r))) / 2;
  return { l: i.x - hw, r: i.x + hw, t: i.y - hh, b: i.y + hh };
};

test('"Make it casual" keeps every print on the page', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^4 grid/ }).click();
  const files = [];
  for (let i = 0; i < 4; i += 1) files.push(await photoFile(page, { name: `p${i}.jpg`, colour: `hsl(${i * 70}, 55%, 40%)` }));
  await page.locator('[data-file-input]').setInputFiles(files);
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(4);
  await page.locator('.clg-rail [data-panel="layouts"]').click();
  await page.locator('[data-action="casual"]').click();
  await expect.poll(async () => (await newestDraft(page))?.model.items.every((i) => i.angle)).toBe(true);
  const model = (await newestDraft(page)).model;
  const height = 1000 * 2480 / 3508;
  for (const print of model.items) {
    const b = turnedBox(print);
    expect(b.l).toBeGreaterThanOrEqual(-30);
    expect(b.t).toBeGreaterThanOrEqual(-30);
    expect(b.r).toBeLessThanOrEqual(1030);
    expect(b.b).toBeLessThanOrEqual(height + 30);
  }
  expect(errors).toEqual([]);
});

test('a tall photo added with wide ones goes into the tall frame', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^1 big, 2 small/ }).click();
  await page.locator('[data-file-input]').setInputFiles([
    await photoFile(page, { name: 'wide-1.jpg', width: 1200, height: 800 }),
    await photoFile(page, { name: 'wide-2.jpg', width: 1200, height: 800, colour: '#7a3b2e' }),
    await photoFile(page, { name: 'tall.jpg', width: 800, height: 1200, colour: '#2e4a7a' })
  ]);
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(3);
  const draft = await newestDraft(page);
  const names = Object.fromEntries(await page.evaluate(() => [...document.querySelectorAll('[data-tray-photo]')].map((b) => [b.dataset.trayPhoto, b.getAttribute('aria-label') || b.title || ''])));
  const big = draft.model.items.find((i) => i.cell === 0);
  expect(names[big.photo] || '').toContain('tall');
  expect(errors).toEqual([]);
});

test('a heading added over photos gets a white outline so it can be read', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^4 grid/ }).click();
  const files = [];
  for (let i = 0; i < 4; i += 1) files.push(await photoFile(page, { name: `p${i}.jpg` }));
  await page.locator('[data-file-input]').setInputFiles(files);
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(4);
  await page.locator('.clg-rail [data-panel="text"]').click();
  await page.locator('[data-add-text="heading"]').click();
  await expect.poll(async () => (await newestDraft(page))?.model.items.find((i) => i.kind === 'text')?.outline).toBe(3);
  expect(errors).toEqual([]);
});

test('with more photos than empty frames, the one matched to no frame waits in the tray, with the message', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^2 side by side/ }).click();
  // One photo fills a frame first, so one empty frame is left.
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'first.jpg' }));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(1);
  // A tall photo, then a photo shaped like the empty frame.
  const slot = (await newestDraft(page)).model.items.find((i) => i.kind === 'slot');
  const w = 1200;
  await page.locator('[data-file-input]').setInputFiles([
    await photoFile(page, { name: 'tall.jpg', width: 700, height: 1400, colour: '#2e4a7a' }),
    await photoFile(page, { name: 'matching.jpg', width: w, height: Math.round(w / slot.aspect), colour: '#7a3b2e' })
  ]);
  await expect(page.locator('[data-alert-text]')).toContainText('did not fit');
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(2);
  const model = (await newestDraft(page)).model;
  const names = Object.fromEntries(await page.evaluate(() => [...document.querySelectorAll('[data-tray-photo]')].map((b) => [b.dataset.trayPhoto, b.getAttribute('aria-label') || b.title || ''])));
  const filled = model.items.find((i) => i.id === slot.id);
  expect(names[filled.photo] || '').toContain('matching');
  expect(errors).toEqual([]);
});
