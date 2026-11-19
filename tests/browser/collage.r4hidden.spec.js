'use strict';

// Hidden items and Save (docs/ux/collage-review-4-fixes.md, PR 6): a hidden item shows as a faint
// dashed outline in the editor only, Save says how many hidden items it leaves out, the purposes
// not chosen read clearly, and a reopened draft says Undo starts again from there.

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft, showLayers, startOn } = require('./helpers');

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

/** The dashed outlines drawn for hidden items, as Konva has them on the page. */
const outlines = (page) => page.evaluate(() => window.Konva.stages[0].find('.hidden-outline')
  .filter((shape) => shape.isVisible()).map((shape) => ({ dash: shape.dash(), stroke: shape.stroke() })));

async function twoPhotos(page) {
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^2 side by side/ }).click();
  await page.locator('[data-file-input]').setInputFiles([
    await photoFile(page, { name: 'a.jpg', colour: '#2d6a4f' }),
    await photoFile(page, { name: 'b.jpg', colour: '#9d4edd' })
  ]);
  await expect(page.locator('[data-photo-count]')).toHaveText('2 of 60 photos');
}

test('a hidden photo is a dashed outline in the editor, gone in Preview, and named by Save', async ({ page }) => {
  const errors = await openFresh(page);
  await twoPhotos(page);
  expect(await outlines(page)).toEqual([]);

  await showLayers(page);
  await page.locator('[data-layer-hide]').first().click();
  const shown = await outlines(page);
  expect(shown).toHaveLength(1);
  expect(shown[0].dash.length).toBeGreaterThan(0);

  // Preview shows what Save makes: no outline.
  await page.locator('[data-action="preview"]').first().click();
  await expect(page.locator('[data-preview-exit]')).toBeVisible();
  expect(await outlines(page)).toEqual([]);
  await page.locator('[data-preview-exit]').click();
  expect(await outlines(page)).toHaveLength(1);

  await page.locator('[data-action="save-open"]').first().click();
  await expect(page.locator('[data-save-empty]')).toBeVisible();
  await expect(page.locator('[data-save-empty]')).toHaveText('1 hidden item left out; show it from Layers to keep it.');
  await page.keyboard.press('Escape');

  // Shown again: no outline, and Save leaves nothing out.
  await page.locator('[data-layer-hide]').first().click();
  expect(await outlines(page)).toEqual([]);
  await page.locator('[data-action="save-open"]').first().click();
  await expect(page.locator('[data-save-empty]')).toBeHidden();
  expect(errors).toEqual([]);
});

for (const theme of ['light', 'dark']) {
  test(`in Save, the purposes not chosen read as clearly as normal words (${theme})`, async ({ page }) => {
    const errors = await openFresh(page);
    await page.evaluate((value) => document.documentElement.setAttribute('data-bs-theme', value), theme);
    await twoPhotos(page);
    await page.locator('[data-action="save-open"]').first().click();
    const others = page.locator('[data-save-purpose][aria-pressed="false"]');
    await expect(others).toHaveCount(2);
    for (const choice of await others.all()) {
      const ratio = await choice.evaluate(contrastOf);
      expect(ratio, `${theme}: ${await choice.textContent()}`).toBeGreaterThanOrEqual(7);
    }
    expect(errors).toEqual([]);
  });
}

test('a reopened draft says that Undo starts again from there', async ({ page }) => {
  const errors = await openFresh(page);
  await twoPhotos(page);
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(2);
  await page.locator('[data-action="home"]').filter({ visible: true }).first().click();
  await expect(page.locator('[data-open-draft]')).toHaveCount(1);
  await page.locator('[data-open-draft]').click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await expect(page.locator('[data-prism-toast]').filter({ hasText: 'Undo starts again from here' })).toHaveCount(1);
  await expect(page.locator('[data-announce]')).toContainText('Undo starts again from here');
  expect(errors).toEqual([]);
});
