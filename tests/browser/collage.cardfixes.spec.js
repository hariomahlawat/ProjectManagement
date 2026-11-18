'use strict';

// Invitation cards and posters, cold review fixes (19 Nov): two logos, the Regimental roundel,
// the Card panel's order, what a poster's panel leaves out, and the custom size box.

const { test, expect } = require('./test');
const { openFresh, newestDraft } = require('./helpers');

const roles = async (page) => ((await newestDraft(page))?.model?.items || []).filter((item) => item.role).map((item) => ({ id: item.id, role: item.role, hidden: Boolean(item.hidden), x: item.x, width: item.width, sticker: item.sticker }));

/** A word typed, so the draft is saved and the model can be read from it. */
async function touch(page) {
  const venue = page.locator('[data-card-field="venue"]');
  await venue.fill('Sudarshan Hall');
  await venue.blur();
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.length || 0).toBeGreaterThan(0);
}

test('two logos: removing the first leaves the second as the logo; the roundel widens for two and hides with none', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="regimental"]').click();
  await touch(page);
  const roundelOf = async () => (await roles(page)).find((item) => item.role === 'roundel');
  await expect.poll(async () => (await roundelOf())?.width).toBeGreaterThan(0);
  const single = await roundelOf();

  await page.locator('[data-card-logo="second"]').click();
  await expect(page.locator('[data-card-logo2-name]')).toContainText('Second logo');
  await expect.poll(async () => (await roles(page)).some((item) => item.role === 'logo2')).toBe(true);
  // The roundel is as wide as both logos.
  expect((await roundelOf()).width).toBeGreaterThan(single.width * 1.5);

  // The first is removed: the second takes its place.
  const second = (await roles(page)).find((item) => item.role === 'logo2');
  await page.locator('[data-card-logo-target="logo"]').click();
  await page.locator('[data-card-logo="remove"]').click();
  await expect.poll(async () => (await roles(page)).some((item) => item.role === 'logo2')).toBe(false);
  const logo = (await roles(page)).find((item) => item.role === 'logo');
  expect(logo).toBeTruthy();
  expect(logo.id).toBe(second.id);
  // Back in the middle, with a roundel of its own size.
  expect(Math.abs(logo.x - single.x)).toBeLessThan(1);
  expect(Math.abs((await roundelOf()).width - single.width)).toBeLessThan(1);
  await expect(page.locator('[data-card-logo2-name]')).toHaveCount(0);

  // No logo: the roundel hides; a logo picked brings it back.
  await page.locator('[data-card-logo="remove"]').click();
  await expect.poll(async () => (await roundelOf())?.hidden).toBe(true);
  await page.locator('[data-card-logo-use="logo:sdd"]').click();
  await expect.poll(async () => (await roundelOf())?.hidden).toBe(false);
  expect(errors).toEqual([]);
});

test('the second logo picked is the one Remove takes; the first goes back to the middle', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await touch(page);
  await expect.poll(async () => (await roles(page)).find((item) => item.role === 'logo')?.x).toBeGreaterThan(0);
  const middle = (await roles(page)).find((item) => item.role === 'logo').x;
  await page.locator('[data-card-logo="second"]').click();
  await expect.poll(async () => (await roles(page)).find((item) => item.role === 'logo').x).toBeLessThan(middle - 1);
  // Adding a second logo picks it: Remove takes the second.
  await expect(page.locator('[data-card-logo-target="logo2"]')).toHaveAttribute('aria-pressed', 'true');
  await page.locator('[data-card-logo="remove"]').click();
  await expect.poll(async () => (await roles(page)).some((item) => item.role === 'logo2')).toBe(false);
  expect(Math.abs((await roles(page)).find((item) => item.role === 'logo').x - middle)).toBeLessThan(1);
  await expect(page.locator('[data-card-logo="second"]')).toBeVisible();
  expect(errors).toEqual([]);
});

test('the Card panel starts with the words in the order the card reads; a poster leaves out the back and guest list', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  const fields = await page.locator('[data-card-field]').evaluateAll((inputs) => inputs.map((input) => input.dataset.cardField));
  expect(fields.slice(0, 5)).toEqual(['host', 'invite', 'guest', 'at', 'occasion']);
  // The words come before the logo, sides and guest list.
  const order = await page.locator('.clg-card-form .clg-section').evaluateAll((sections) => sections.map((section) => section.className));
  expect(order[0]).toContain('clg-card-words');
  await expect(page.locator('[data-card-guests]')).toHaveCount(1);
  await page.getByRole('button', { name: 'Save as…' }).click();
  await expect(page.locator('#clgSaveTitle')).toHaveText('Save the card');
  await page.keyboard.press('Escape');

  await openFresh(page);
  await page.locator('[data-start-poster="event"]').click();
  await expect(page.locator('#clgCardTitle')).toHaveText('Poster details');
  await expect(page.locator('[data-card-hint]')).toContainText('words on the poster');
  await expect(page.locator('[data-card-guests]')).toHaveCount(0);
  await expect(page.locator('[data-card-side="add"]')).toHaveCount(0);
  await expect(page.locator('[data-card-logo-name]')).toContainText('No logo chosen yet');
  await page.locator('[data-card-logo-use="logo:sdd"]').click();
  await page.getByRole('button', { name: 'Save as…' }).click();
  await expect(page.locator('#clgSaveTitle')).toHaveText('Save the poster');
  await page.keyboard.press('Escape');
  expect(errors).toEqual([]);
});

test('the custom card size box closes with Escape or Cancel, and the menu keeps its size', async ({ page }) => {
  const errors = await openFresh(page);
  const size = page.locator('[data-card-size]');
  const before = await size.inputValue();
  await size.selectOption('custom');
  const box = page.locator('[data-card-custom]');
  await expect(box).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(box).toBeHidden();
  await expect(size).toHaveValue(before);
  await expect(size).toBeFocused();
  await size.selectOption('custom');
  await page.locator('[data-card-custom-cancel]').click();
  await expect(box).toBeHidden();
  await expect(size).toHaveValue(before);
  expect(errors).toEqual([]);
});

test('a custom card smaller than A6 is warned of on the start screen; a larger one is not', async ({ page }) => {
  const errors = await openFresh(page);
  const size = page.locator('[data-card-size]');
  const hint = page.locator('[data-card-small]');
  await expect(hint).toBeHidden();
  const use = async (width, height) => {
    await size.selectOption('custom');
    await page.locator('[data-card-custom-size="width"]').fill(String(width));
    await page.locator('[data-card-custom-size="height"]').fill(String(height));
    await page.locator('[data-card-custom-use]').click();
  };
  await use(80, 120);
  await expect(hint).toBeVisible();
  await use(150, 200);
  await expect(hint).toBeHidden();
  expect(errors).toEqual([]);
});

/** The bytes of a download. */
async function bytesOf(download) {
  const chunks = [];
  for await (const chunk of await download.createReadStream()) chunks.push(chunk);
  return Buffer.concat(chunks);
}

/** Saves the page as a screen PNG and counts its dark (ink) pixels. */
async function saveAndCountInk(page) {
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'A slide or a screen' }).click();
  const download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const file = await download;
  await expect(page.locator('[data-save-result]')).toContainText('Saved');
  const result = await page.locator('[data-save-result]').textContent();
  await page.keyboard.press('Escape');
  const ink = await page.evaluate(async (base64) => {
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
    let dark = 0;
    for (let i = 0; i < data.length; i += 4) if (data[i] + data[i + 1] + data[i + 2] < 300) dark += 1;
    return dark;
  }, (await bytesOf(file)).toString('base64'));
  return { ink, result };
}

test('every card design saves with its words drawn, the Bilingual Hindi included', async ({ page }) => {
  test.setTimeout(180000);
  const errors = await openFresh(page);
  const keys = await page.locator('[data-start-design]').evaluateAll((buttons) => buttons.map((button) => button.dataset.startDesign));
  expect(keys.length).toBeGreaterThanOrEqual(6);
  await page.locator(`[data-start-design="${keys[0]}"]`).click();
  for (const key of keys) {
    await page.locator(`[data-card-design="${key}"]`).click();
    await expect(page.locator(`[data-card-design="${key}"]`)).toHaveAttribute('aria-pressed', 'true');
    const { ink, result } = await saveAndCountInk(page);
    expect(ink, `${key}: words drawn`).toBeGreaterThan(2000);
    expect(result, key).not.toMatch(/left out|could not/);
    if (key === 'bilingual') {
      // The Hindi is drawn too: emptied, the picture loses ink.
      const hindi = page.locator('[data-card-field="occasionHi"]');
      await hindi.fill('');
      await hindi.blur();
      const without = await saveAndCountInk(page);
      expect(without.ink).toBeLessThan(ink - 200);
      await page.keyboard.press('Control+z');
    }
  }
  expect(errors).toEqual([]);
});

test('a poster with No logo saves with nothing left out', async ({ page }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  await page.locator('[data-start-poster="event"]').click();
  await page.locator('.clg-card-question [data-card-logo="none"]').click();
  await expect(page.locator('[data-card-logo-name]')).toContainText('No logo on this poster');
  const { ink, result } = await saveAndCountInk(page);
  expect(ink).toBeGreaterThan(2000);
  expect(result).not.toMatch(/empty frame|left out|could not/);
  expect(errors).toEqual([]);
});
