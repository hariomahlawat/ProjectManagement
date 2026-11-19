'use strict';

// Collage Studio on a phone-sized screen (docs/ux/collage-review-fixes.md, PR 11). Runs only in
// the "phone" project of playwright.config.js.

const { test, expect } = require('./test');
const { openFresh, newestDraft, startOn } = require('./helpers');

test('on a phone, panels are sheets, new words open their settings, and Layers is always reachable', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();

  // No sideways scrolling on a phone.
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  await page.locator('.clg-rail [data-panel="text"]').click();
  await page.locator('[data-add-text="heading"]').click();
  // The Words box is in the settings sheet, which opens with it ready to type in.
  await expect(page.locator('.clg-props [data-text]')).toBeVisible();
  await page.locator('.clg-props [data-text]').fill('Picnic 2026');
  await expect.poll(async () => (await newestDraft(page))?.model.items[0]?.text).toBe('Picnic 2026');

  await page.locator('[data-action="close-props"]').click();
  await expect(page.locator('.clg-props')).toBeHidden();
  await page.getByRole('button', { name: 'Layers' }).click();
  await expect(page.locator('.clg-props .clg-layer__name')).toHaveText(['Picnic 2026']);

  // Leaving the page (switching app) writes the draft at once.
  await page.locator('[data-action="close-props"]').click();
  await page.locator('.clg-rail [data-panel="text"]').click();
  await page.locator('[data-add-text="heading"]').click();
  await page.locator('.clg-props [data-text]').fill('Before leaving');
  await page.evaluate(() => window.dispatchEvent(new Event('pagehide')));
  await expect.poll(async () => (await newestDraft(page))?.model.items.map((item) => item.text)).toContain('Before leaving');
  expect(errors).toEqual([]);
});

test('zoomed all the way in on a phone, every page canvas stays under the browser limit', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 portrait/ }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  // The zoom buttons are hidden on phones; the same zoom a pinch gives, as far as it goes.
  await page.evaluate(() => { for (let i = 0; i < 12; i += 1) document.querySelector('[data-action="zoom-in"]').click(); });
  const areas = await page.evaluate(() => [...document.querySelectorAll('[data-konva] canvas')].map((c) => c.width * c.height));
  expect(areas.length).toBeGreaterThan(0);
  for (const area of areas) expect(area).toBeLessThanOrEqual(16000000);
  expect(errors).toEqual([]);
});

test('on a phone the header is one line, every button has a name, and hints speak of tapping', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  // The icon-only buttons are named for screen readers.
  await expect(page.getByRole('button', { name: 'Save as…' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Preview' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'PRISM' })).toBeAttached();
  // The brand fits on one line, and the name box has room.
  const brand = await page.locator('.pdft-brand__text').boundingBox();
  expect(brand.height).toBeLessThan(30);
  const name = await page.locator('[data-name]').boundingBox();
  expect(name.width).toBeGreaterThan(110);
  // Touch words, not mouse words.
  await expect(page.locator('[data-empty] .clg-for-touch')).toBeVisible();
  await expect(page.locator('[data-empty] .clg-for-mouse')).toBeHidden();
  expect(errors).toEqual([]);
});
