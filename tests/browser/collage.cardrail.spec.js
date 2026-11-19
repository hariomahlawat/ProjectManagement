'use strict';

// Layouts are for collages only (docs/ux/collage-usability-plan.md, round 2, PR 1): on a card or a
// poster the rail has no Layouts, which would cover the design with empty frames.

const { test, expect } = require('./test');
const { openFresh, startOn } = require('./helpers');

const layouts = (page) => page.locator('.clg-rail [data-panel="layouts"]');

test('a card and a poster have no Layouts; a collage has', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'card');
  await page.locator('[data-start-design="classic"]').click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await expect(layouts(page)).toBeHidden();
  for (const panel of ['card', 'photos', 'text', 'stickers', 'background']) {
    await expect(page.locator(`.clg-rail [data-panel="${panel}"]`)).toBeVisible();
  }

  await page.getByRole('button', { name: 'Back to the start' }).click();
  await startOn(page, 'poster');
  await page.locator('[data-start-poster="notice"]').click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await expect(layouts(page)).toBeHidden();

  await page.getByRole('button', { name: 'Back to the start' }).click();
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(layouts(page)).toBeVisible();
  await expect(page.locator('.clg-rail [data-panel="card"]')).toBeHidden();
  expect(errors).toEqual([]);
});

test('Layouts left open on a collage does not stay open for the next card', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await layouts(page).click();
  await expect(page.locator('[data-drawer-panel="layouts"]')).toBeVisible();
  await page.getByRole('button', { name: 'Back to the start' }).click();
  await startOn(page, 'card');
  await page.locator('[data-start-design="navy"]').click();
  await expect(page.locator('[data-drawer-panel="layouts"]')).toBeHidden();
  await expect(page.locator('[data-drawer-panel="card"]')).toBeVisible();
  expect(errors).toEqual([]);
});
