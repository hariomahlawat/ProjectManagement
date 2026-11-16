'use strict';

// Dark theme, keyboard and panels (PR 8 of docs/ux/collage-review-3-fixes.md): the third
// review's Low findings, each checked once.

const { test, expect } = require('./test');
const { openFresh, photoFile } = require('./helpers');

/** Contrast of an element's text against the first solid background behind it, with opacity. */
function contrastOf(element) {
  const parse = (value) => {
    const [r, g, b, a = 1] = value.match(/[\d.]+/g).map(Number);
    return { r, g, b, a };
  };
  const over = (top, bottom) => ({
    r: top.r * top.a + bottom.r * (1 - top.a),
    g: top.g * top.a + bottom.g * (1 - top.a),
    b: top.b * top.a + bottom.b * (1 - top.a),
    a: 1
  });
  let background = null;
  for (let node = element; node && node.nodeType === 1; node = node.parentElement) {
    const colour = parse(getComputedStyle(node).backgroundColor);
    if (colour.a > 0) background = background ? over(background, colour) : colour;
    if (background && background.a >= 1) break;
  }
  background = background || { r: 255, g: 255, b: 255, a: 1 };
  let opacity = 1;
  for (let node = element; node && node.nodeType === 1; node = node.parentElement) opacity *= Number(getComputedStyle(node).opacity);
  const text = parse(getComputedStyle(element).color);
  const shown = over({ ...text, a: text.a * opacity }, background);
  const luminance = ({ r, g, b }) => {
    const channel = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; };
    return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
  };
  const [light, dark] = [luminance(shown), luminance(background)].sort((a, b) => b - a);
  return (light + 0.05) / (dark + 0.05);
}

async function startA4(page) {
  await page.getByRole('button', { name: /^A4 landscape/ }).first().click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
}

test('in the Dark theme, buttons that are not chosen are readable', async ({ page }) => {
  const errors = await openFresh(page);
  await page.evaluate(() => document.documentElement.setAttribute('data-bs-theme', 'dark'));
  await startA4(page);
  // Before anything is added Undo and Redo cannot be used yet; they must still be readable.
  const names = ['[data-action="undo"]', '[data-action="redo"]', '[data-action="zoom-out"]', '[data-action="zoom-fit"]', '[data-action="zoom-in"]', '[data-action="preview"]'];
  for (const selector of names) {
    const ratio = await page.locator(`.clg-bar ${selector}`).evaluate(contrastOf);
    expect(ratio, selector).toBeGreaterThanOrEqual(4.5);
  }
  await page.locator('.clg-rail [data-panel="text"]').click();
  await page.getByRole('button', { name: 'Add a heading' }).click();
  const choices = page.locator('.clg-props [data-text-toggle="italic"], .clg-props [data-text-set="align"][aria-pressed="false"]');
  await expect(choices.first()).toBeVisible();
  for (const choice of await choices.all()) {
    const ratio = await choice.evaluate(contrastOf);
    expect(ratio, await choice.textContent()).toBeGreaterThanOrEqual(4.5);
  }
  expect(errors).toEqual([]);
});

test('the collage name field shows a focus ring', async ({ page }) => {
  const errors = await openFresh(page);
  await startA4(page);
  await page.locator('#clgName').focus();
  const ring = await page.locator('#clgName').evaluate((element) => {
    const style = getComputedStyle(element);
    return { style: style.outlineStyle, width: parseFloat(style.outlineWidth) };
  });
  expect(ring.style).not.toBe('none');
  expect(ring.width).toBeGreaterThanOrEqual(2);
  expect(errors).toEqual([]);
});

test('"Skip to settings" comes early and takes the keyboard to the settings', async ({ page }) => {
  const errors = await openFresh(page);
  await startA4(page);
  await page.locator('#clgEditorTitle').focus();
  await page.keyboard.press('Tab');
  const skip = page.getByRole('link', { name: 'Skip to settings' });
  await expect(skip).toBeFocused();
  // It shows while it has the keyboard.
  const box = await skip.boundingBox();
  expect(box.y).toBeGreaterThanOrEqual(0);
  await page.keyboard.press('Enter');
  await expect(page.locator('.clg-props')).toBeFocused();
  // The next Tab is inside the settings.
  await page.keyboard.press('Tab');
  expect(await page.evaluate(() => Boolean(document.activeElement.closest('.clg-props')))).toBe(true);
  expect(errors).toEqual([]);
});

test('a panel keeps its title in view while it scrolls, and "Make it casual" can be reached', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  const errors = await openFresh(page);
  await startA4(page);
  await page.locator('.clg-rail [data-panel="layouts"]').click();
  const drawer = page.locator('[data-drawer]');
  // Scrolled to its end, as a user looking for "Mix it up" would.
  await drawer.evaluate((element) => { element.scrollTop = element.scrollHeight; });
  expect(await drawer.evaluate((element) => element.scrollTop)).toBeGreaterThan(60);
  await expect(page.locator('[data-action="casual"]')).toBeInViewport();
  const [title, panel] = await Promise.all([
    page.locator('#clgLayoutsTitle').boundingBox(),
    drawer.boundingBox()
  ]);
  expect(title.y).toBeGreaterThanOrEqual(panel.y - 1);
  expect(title.y).toBeLessThan(panel.y + 20);
  expect(errors).toEqual([]);
});

test('a deleted draft offers Undo in the glass toast, and Undo brings it back', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).last().click();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'keep.jpg' }));
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');
  await page.getByRole('button', { name: 'Back to the start' }).click();
  await expect(page.locator('[data-open-draft]')).toHaveCount(1);
  // The card's details keep "A4 landscape" whole.
  await expect(page.locator('.clg-draft__meta').filter({ hasText: 'A4 landscape' })).toHaveCSS('white-space', 'nowrap');

  await page.locator('[data-delete-draft]').first().click();
  const confirm = page.locator('.prism-confirm-modal.show [data-confirm-accept], .prism-confirm-modal.show .btn-danger, .prism-confirm-modal.show .btn-primary');
  if (await confirm.count()) await confirm.first().click();
  await expect(page.locator('[data-open-draft]')).toHaveCount(0);
  const toast = page.locator('.prism-toast[data-draft-undo]');
  await expect(toast).toBeVisible();
  await expect(toast).toContainText('deleted');
  await expect(page.locator('.clg-undo-bar')).toHaveCount(0);

  await toast.getByRole('button', { name: 'Undo' }).click();
  await expect(page.locator('[data-open-draft]')).toHaveCount(1);
  await expect(toast).toHaveCount(0);
  expect(errors).toEqual([]);
});

test('a draft card shows its delete button on hover or focus', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).last().click();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'one.jpg' }));
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');
  await page.getByRole('button', { name: 'Back to the start' }).click();
  const remove = page.locator('[data-delete-draft]').first();
  await page.mouse.move(0, 0);
  await expect(remove).toHaveCSS('opacity', '0');
  await page.locator('[data-open-draft]').first().hover();
  await expect(remove).toHaveCSS('opacity', '1');
  await page.mouse.move(0, 0);
  await remove.focus();
  await expect(remove).toHaveCSS('opacity', '1');
  expect(errors).toEqual([]);
});

test('at 1366 wide the page is shown larger by default, and zoom still steps', async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  const errors = await openFresh(page);
  await startA4(page);
  const pageWidth = () => page.evaluate(() => {
    const world = window.Konva.stages[0].find('Group').find((group) => Math.abs(group.scaleX() - 1) > 0.001);
    return world.scaleX() * 1000;
  });
  // Before this change 100% showed the page at about 45% of the window's width (612 px).
  await expect.poll(pageWidth).toBeGreaterThan(1366 * 0.5);
  const fitted = await pageWidth();
  await page.locator('[data-action="zoom-in"]').click();
  await expect(page.locator('[data-zoom-value]')).toHaveText('125%');
  expect((await pageWidth()) / fitted).toBeCloseTo(1.25, 2);
  await page.locator('[data-action="zoom-fit"]').click();
  expect(await pageWidth()).toBeCloseTo(fitted, 1);
  expect(errors).toEqual([]);
});
