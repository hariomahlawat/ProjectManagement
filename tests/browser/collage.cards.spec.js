'use strict';

// Card sizes and the SDD logo (docs/ux/invitations-posters.md, PR 4): the size menu has cards and
// pocket cards in their own groups, and PRISM's own SDD logo is first in Crests and logos and
// goes into the saved picture.

const { test, expect } = require('./test');
const { openFresh } = require('./helpers');

test('a car pass size and the SDD logo: the menu groups sizes, and Save draws the logo at the card shape', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();

  const size = page.locator('[data-size]');
  expect(await size.locator('optgroup').evaluateAll((groups) => groups.map((g) => g.label))).toEqual(['Pages and screens', 'Cards', 'Pocket cards', 'Custom']);
  await size.selectOption('passl');
  await expect(size).toHaveValue('passl');

  await page.locator('.clg-rail [data-panel="stickers"]').click();
  await page.locator('[data-sticker-group="crests"]').click();
  const sdd = page.locator('[data-add-sticker="logo:sdd"]');
  await expect(sdd).toHaveAttribute('aria-label', 'Add SDD');
  // The picker shows the logo itself, from the picture shipped with PRISM.
  await expect.poll(() => sdd.locator('img').evaluate((img) => img.complete && img.naturalWidth)).toBe(1732);
  await sdd.click();
  await expect(page.locator('[data-props] .clg-panel-title')).toContainText('SDD');

  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'A slide or a screen' }).click();
  const download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const file = await download;
  await expect(page.locator('[data-save-result]')).toContainText('Saved');
  await expect(page.locator('[data-save-result]')).not.toContainText('could not be loaded');

  const picture = await page.evaluate(async (base64) => {
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
    // The archer's gold: red well above blue.
    let gold = 0;
    for (let i = 0; i < data.length; i += 4) if (data[i] > 150 && data[i] - data[i + 2] > 70) gold += 1;
    return { ratio: bitmap.width / bitmap.height, gold };
  }, (await streamBytes(file)).toString('base64'));
  // A visiting card, 89 × 51 mm.
  expect(Math.abs(picture.ratio - 88.9 / 50.8)).toBeLessThan(0.01);
  expect(picture.gold).toBeGreaterThan(500);
  expect(errors).toEqual([]);
});

async function streamBytes(download) {
  const chunks = [];
  for await (const chunk of await download.createReadStream()) chunks.push(chunk);
  return Buffer.concat(chunks);
}

test('a custom card size typed in mm: refused out of range, then the card saves a PDF of that paper size', async ({ page }) => {
  const errors = await openFresh(page);
  const size = page.locator('[data-card-size]');
  await size.selectOption('custom');
  await expect(size).toHaveValue('a5p');
  const box = page.locator('[data-card-custom]');
  await expect(box).toBeVisible();
  await box.locator('[data-card-custom-size="width"]').fill('30');
  await box.locator('[data-card-custom-size="height"]').fill('180');
  await box.locator('[data-card-custom-use]').click();
  await expect(box.locator('[data-card-custom-error]')).toHaveText('Each side must be from 50 to 300 mm.');
  await box.locator('[data-card-custom-size="width"]').fill('120');
  await box.locator('[data-card-custom-size="height"]').press('Enter');
  await expect(size).toHaveValue('mm:120x180');
  await expect(box).toBeHidden();
  await expect(page.locator('[data-start-design]').first()).toHaveAttribute('aria-label', /Custom 120 × 180 mm/);

  await page.locator('[data-start-design="classic"]').click();
  await expect(page.locator('[data-size]')).toHaveValue('mm:120x180');
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'Printing' }).click();
  const download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const file = await download;
  expect(file.suggestedFilename()).toMatch(/\.pdf$/);
  // Read with the pdf-lib PRISM ships, in the page: one page, 120 × 180 mm.
  const pageSize = await page.evaluate(async (base64) => {
    const text = atob(base64);
    const bytes = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i += 1) bytes[i] = text.charCodeAt(i);
    const doc = await window.PDFLib.PDFDocument.load(bytes);
    return { pages: doc.getPageCount(), ...doc.getPage(0).getSize() };
  }, (await streamBytes(file)).toString('base64'));
  expect(pageSize.pages).toBe(1);
  expect(Math.abs(pageSize.width - 120 / 25.4 * 72)).toBeLessThan(0.5);
  expect(Math.abs(pageSize.height - 180 / 25.4 * 72)).toBeLessThan(0.5);

  // In the editor, Custom size… opens Page settings to type another size, even from Layers.
  await page.keyboard.press('Escape');
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  await page.locator('[data-size]').selectOption('custom');
  await expect(page.locator('[data-size]')).toHaveValue('mm:120x180');
  const width = page.locator('[data-props] [data-custom-size="width"]');
  await expect(width).toBeFocused();
  await width.fill('200');
  await page.locator('[data-props] [data-custom-size="height"]').fill('100');
  await page.locator('[data-props] [data-action-custom-size]').click();
  await expect(page.locator('[data-size]')).toHaveValue('mm:200x100');
  expect(errors).toEqual([]);
});

test('A6 cards with a back print two to a turned A4 sheet, fronts then backs, with cut marks', async ({ page }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  await page.locator('[data-card-size]').selectOption('a6p');
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('[data-card-side="add"]').click();
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'Printing' }).click();
  const sheet = page.locator('[data-save-sheet]');
  await expect(sheet).toBeVisible();
  await expect(sheet.locator('[data-sheet-note="a4"]')).toContainText('2 cards to a sheet');
  await sheet.locator('input[value="a4"]').check();
  const download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const file = await download;
  const pdf = await page.evaluate(async (base64) => {
    const text = atob(base64);
    const bytes = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i += 1) bytes[i] = text.charCodeAt(i);
    const doc = await window.PDFLib.PDFDocument.load(bytes);
    return doc.getPages().map((p) => p.getSize());
  }, (await streamBytes(file)).toString('base64'));
  // Two A4 sheets turned on their side: the fronts, then the backs.
  expect(pdf.length).toBe(2);
  for (const size of pdf) {
    expect(Math.abs(size.width - 297 / 25.4 * 72)).toBeLessThan(0.5);
    expect(Math.abs(size.height - 210 / 25.4 * 72)).toBeLessThan(0.5);
  }
  // A screen picture hides the sheet choice.
  await page.getByRole('button', { name: 'A slide or a screen' }).click();
  await expect(sheet).toBeHidden();
  expect(errors).toEqual([]);
});

test('a guest list makes one card for each guest: on their own pages, or filling A4 sheets', async ({ page }) => {
  test.setTimeout(120000);
  const errors = await openFresh(page);
  await page.locator('[data-card-size]').selectOption('a6p');
  await page.locator('[data-start-design="classic"]').click();
  const list = page.locator('[data-card-guests]');
  await list.fill('Lt Gen R. K. Mehta, AVSM\nBrig P. Singh, SM and Mrs Singh\nCol D. Nair');
  await list.blur();
  await expect(page.locator('[data-card-guest-count]')).toContainText('3 guests');
  await expect.poll(async () => {
    const draft = await page.evaluate(() => new Promise((resolve) => {
      const open = indexedDB.open('prism-collage-studio');
      open.onsuccess = () => {
        const all = open.result.transaction('drafts').objectStore('drafts').getAll();
        all.onsuccess = () => resolve(all.result.sort((a, b) => b.saved - a.saved)[0]);
      };
    }));
    return draft?.model?.card?.guests?.length;
  }).toBe(3);

  const pagesOf = async (file) => page.evaluate(async (base64) => {
    const text = atob(base64);
    const bytes = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i += 1) bytes[i] = text.charCodeAt(i);
    return (await window.PDFLib.PDFDocument.load(bytes)).getPages().map((p) => Math.round(p.getSize().width));
  }, (await streamBytes(file)).toString('base64'));

  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'Printing' }).click();
  await expect(page.locator('[data-save-guests]')).toBeVisible();
  await expect(page.locator('[data-guest-label]')).toHaveText('One card for each guest (3)');
  // Each on its own A6 page.
  let download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  expect((await pagesOf(await download)).length).toBe(3);
  await expect(page.locator('[data-save-result]')).toContainText('3 guest cards on 3 pages');
  // Two to a turned A4 sheet: two sheets for three guests.
  await page.locator('[data-save-sheet] input[value="a4"]').check();
  download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const sheets = await pagesOf(await download);
  expect(sheets).toEqual([842, 842]);
  await expect(page.locator('[data-save-result]')).toContainText('3 guest cards on 2 pages');
  await page.keyboard.press('Escape');

  // Cleared, with Undo; Save then offers no guest cards.
  await page.locator('[data-card-guest="clear"]').click();
  await expect(page.locator('[data-card-guests]')).toHaveValue('');
  expect(errors).toEqual([]);
});
