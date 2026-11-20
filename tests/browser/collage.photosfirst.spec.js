'use strict';

// Photos first (docs/ux/collage-usability-plan.md, PR 3): photos chosen or dropped on the start
// screen open in a layout made for them; the Layouts panel marks the layouts that fit; and a
// "did not fit" message offers the layout that fits, in one click.

const { test, expect } = require('./test');
const { openFresh, startOn, photoFile, newestDraft } = require('./helpers');

async function photos(page, count) {
  const files = [];
  for (let i = 0; i < count; i += 1) files.push(await photoFile(page, { name: `p${i}.jpg`, colour: `hsl(${i * 50}, 55%, 40%)` }));
  return files;
}

const placed = async (page) => (await newestDraft(page))?.model.items.filter((item) => item.kind === 'photo').length;

test('six photos chosen on the start screen open in the 6-photo layout, none left out', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await expect(page.getByRole('heading', { name: 'Start with your photos' })).toBeVisible();

  const chooser = page.waitForEvent('filechooser');
  await page.locator('[data-start-drop]').getByRole('button', { name: 'Add photos' }).click();
  await (await chooser).setFiles(await photos(page, 6));

  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await expect(page.locator('[data-photo-count]')).toHaveText('6 of 60 photos');
  await expect.poll(() => placed(page)).toBe(6);
  const draft = await newestDraft(page);
  expect(draft.model.layout.key).toBe('t6');
  expect(draft.model.items.filter((item) => item.kind === 'slot')).toHaveLength(0);
  await expect(page.locator('[data-alert-text]')).toHaveText('');

  expect(errors).toEqual([]);
});

test('photos dropped on the start screen open in a layout made for them', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  const files = await photos(page, 3);
  const transfer = await page.evaluateHandle((list) => {
    const data = new DataTransfer();
    for (const { name, mimeType, base64 } of list) {
      const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
      data.items.add(new File([bytes], name, { type: mimeType }));
    }
    return data;
  }, files.map((file) => ({ name: file.name, mimeType: file.mimeType, base64: file.buffer.toString('base64') })));
  const area = page.locator('[data-start-drop]');
  await area.dispatchEvent('dragover', { dataTransfer: transfer });
  await expect(area).toHaveClass(/is-over/);
  await area.dispatchEvent('drop', { dataTransfer: transfer });

  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await expect.poll(() => placed(page)).toBe(3);
  expect((await newestDraft(page)).model.layout.key).toBe('t3');

  expect(errors).toEqual([]);
});

test('one photo chosen on the start screen fills the page in one frame', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  const chooser = page.waitForEvent('filechooser');
  await page.locator('[data-start-drop]').getByRole('button', { name: 'Add photos' }).click();
  await (await chooser).setFiles(await photoFile(page, { name: 'one.jpg', width: 1600, height: 1000 }));

  await expect.poll(() => placed(page)).toBe(1);
  const [photo] = (await newestDraft(page)).model.items;
  // The page is 1,000 units wide; the frame keeps the layouts' usual margin of 12 on each side.
  expect(photo.width).toBeCloseTo(976, 0);
  expect(photo.x).toBeCloseTo(500, 0);

  expect(errors).toEqual([]);
});

test('Layouts marks the layouts with a frame for each photo', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^2 side by side/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 4));
  await expect(page.locator('[data-photo-count]')).toHaveText('4 of 60 photos');

  await page.locator('.clg-rail [data-panel="layouts"]').click();
  const marked = page.locator('[data-layout-grid] [data-layout-fit]:not([hidden])');
  await expect(marked).toHaveCount(2);
  await expect(marked.first()).toHaveText('Fits all 4');
  await expect(page.locator('[data-layout-grid] [data-layout="t4"]')).toHaveAccessibleName('4 grid, 4 photos; fits your 4 photos');
  await expect(page.locator('[data-layout-grid] [data-layout="t4s"] [data-layout-fit]')).toBeVisible();
  await expect(page.locator('[data-layout-grid] [data-layout="t6"] [data-layout-fit]')).toHaveCount(0);

  expect(errors).toEqual([]);
});

test('photos that did not fit: the message offers the layout that fits, and one click uses it', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^4 grid/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 6));
  await expect(page.locator('[data-alert-text]')).toContainText("2 photos did not fit the layout's frames and are in Photos: use a 6-photo layout");
  await expect.poll(() => placed(page)).toBe(4);

  await page.getByRole('button', { name: 'Use a 6-photo layout' }).click();
  await expect.poll(() => placed(page)).toBe(6);
  expect((await newestDraft(page)).model.layout.key).toBe('t6');
  await expect(page.locator('[data-layout-grid] [data-layout="t6"]')).toHaveAttribute('aria-pressed', 'true');

  // One Undo goes back to the 4 grid with two photos waiting.
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await newestDraft(page))?.model.layout.key).toBe('t4');
  await expect.poll(() => placed(page)).toBe(4);

  expect(errors).toEqual([]);
});

test('a smaller layout chosen by hand offers the one that fits all the photos', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^6 grid/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photos(page, 6));
  await expect.poll(() => placed(page)).toBe(6);

  await page.locator('.clg-rail [data-panel="layouts"]').click();
  await page.locator('[data-layout-grid] [data-layout="t2"]').click();
  await expect(page.locator('[data-alert-text]')).toContainText('4 photos did not fit this layout');
  await page.getByRole('button', { name: 'Use a 6-photo layout' }).click();
  await expect.poll(() => placed(page)).toBe(6);
  expect((await newestDraft(page)).model.layout.key).toBe('t6');

  expect(errors).toEqual([]);
});

// docs/ux/collage-usability-plan.md, round 3, PR 2: wide photos for WhatsApp go into rows.
test('six wide photos for WhatsApp open in "6 in rows", in the order picked, as one Undo step', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.locator('[data-start-for="share"]').click();
  const files = [];
  for (let i = 0; i < 6; i += 1) files.push(await photoFile(page, { name: `w${i + 1}.jpg`, width: 1600, height: 900, colour: `hsl(${i * 50}, 55%, 40%)` }));
  const chooser = page.waitForEvent('filechooser');
  await page.locator('[data-start-drop]').getByRole('button', { name: 'Add photos' }).click();
  await (await chooser).setFiles(files);
  await expect.poll(() => placed(page)).toBe(6);
  const draft = await newestDraft(page);
  expect(draft.model.size).toBe('square');
  expect(draft.model.layout.key).toBe('t6r');
  // Wide frames: wider than tall.
  for (const item of draft.model.items.filter((i) => i.kind === 'photo')) expect(item.aspect).toBeGreaterThan(1);
  // Layouts shows both 6-photo layouts as fitting, the chosen one pressed.
  await page.locator('.clg-rail [data-panel="layouts"]').click();
  await expect(page.locator('[data-layout-grid] [data-layout="t6r"]')).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('[data-layout-grid] [data-layout="t6"] [data-layout-fit]')).toBeVisible();
  // One Undo takes the photos and the layout away together, back to the empty start.
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.filter((item) => item.kind === 'photo').length ?? 0).toBe(0);
  expect(errors).toEqual([]);
});
