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
  // Save does not call the design's hidden roundel a hidden item of the user's.
  await page.getByRole('button', { name: 'Save as…' }).click();
  await expect(page.locator('[data-save-empty]')).not.toContainText('hidden item');
  await page.keyboard.press('Escape');
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

test('a colour set by hand stays when a unit colour changes', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="regimental"]').click();
  await touch(page);
  const host = (await newestDraft(page)).model.items.find((item) => item.field === 'host');
  expect(host.tint?.color).toBeTruthy();
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  await page.locator(`[data-layer-select="${host.id}"]`).click();
  await page.locator('.clg-props [data-props-tab="settings"]').click();
  await page.locator('[data-text-set="color"][data-value="#000000"]').click();
  await expect.poll(async () => (await newestDraft(page)).model.items.find((item) => item.id === host.id)?.color).toBe('#000000');
  await page.locator('[data-card-colour="1"]').evaluate((input) => {
    input.value = '#1f5b3a';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  });
  await expect.poll(async () => (await newestDraft(page)).model.items.find((item) => item.role === 'band1')?.fill).toBe('#1f5b3a');
  expect((await newestDraft(page)).model.items.find((item) => item.id === host.id).color).toBe('#000000');
  expect(errors).toEqual([]);
});

test('guests with only the side without names saved: one copy, and the result says why', async ({ page }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  await page.locator('[data-card-size]').selectOption('fold6');
  await page.locator('[data-start-design="classic"]').click();
  const list = page.locator('[data-card-guests]');
  await list.fill('Maj A\nCapt B\nLt C');
  await list.blur();
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'Printing' }).click();
  await page.locator('[data-save-sides] input[value="front"]').check();
  const download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const file = await download;
  await expect(page.locator('[data-save-result]')).toContainText('without names');
  const pages = await page.evaluate(async (base64) => {
    const text = atob(base64);
    const bytes = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i += 1) bytes[i] = text.charCodeAt(i);
    return (await window.PDFLib.PDFDocument.load(bytes)).getPageCount();
  }, (await bytesOf(file)).toString('base64'));
  expect(pages).toBe(1);
  expect(errors).toEqual([]);
});

/**
 * Each page of a PDF, read back from its content streams: the pictures drawn (where, how large,
 * turned or not) and how many lines (cut marks).
 */
async function sheetsOf(page, file) {
  return page.evaluate(async (base64) => {
    const { PDFDocument, PDFName, PDFArray, PDFRawStream, decodePDFRawStream } = window.PDFLib;
    const text = atob(base64);
    const bytes = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i += 1) bytes[i] = text.charCodeAt(i);
    const pdf = await PDFDocument.load(bytes);
    return pdf.getPages().map((p) => {
      const contents = p.node.get(PDFName.of('Contents'));
      const refs = contents instanceof PDFArray ? contents.asArray() : [contents];
      let ops = '';
      for (const ref of refs) {
        const stream = pdf.context.lookup(ref);
        ops += stream instanceof PDFRawStream ? new TextDecoder().decode(decodePDFRawStream(stream).decode()) : new TextDecoder().decode(stream.getContents());
      }
      // A picture: in "q … Do Q", the "a b c d e f cm" steps combined; a line: "x y m x y l S".
      const times = (m, n) => [m[0] * n[0] + m[2] * n[1], m[1] * n[0] + m[3] * n[1], m[0] * n[2] + m[2] * n[3], m[1] * n[2] + m[3] * n[3], m[0] * n[4] + m[2] * n[5] + m[4], m[1] * n[4] + m[3] * n[5] + m[5]];
      const pictures = [];
      for (const block of ops.split(/\bq\b/).slice(1)) {
        if (!/\sDo\b/.test(block)) continue;
        let m = [1, 0, 0, 1, 0, 0];
        for (const step of block.matchAll(/(-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) (-?[\d.]+) cm/g)) m = times(m, step.slice(1, 7).map(Number));
        pictures.push(m);
      }
      const lines = (ops.match(/ l\s/g) || []).length;
      return { size: [p.getWidth(), p.getHeight()], pictures, lines };
    });
  }, (await bytesOf(file)).toString('base64'));
}

test('an A6 card with a back on A4: two cards a sheet where the layout says, backs turned, cut marks on both', async ({ page }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  await page.locator('[data-card-size]').selectOption('a6p');
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('[data-card-side="add"]').click();
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'Printing' }).click();
  await page.locator('[data-save-sheet] input[value="a4"]').check();
  const download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const sheets = await sheetsOf(page, await download);
  const expected = await page.evaluate(() => {
    const print = window.PrismCollagePrint;
    const front = print.sheetLayout('a6p', 'a4');
    const back = print.sheetLayout('a6p', 'a4', { back: true });
    return { front: print.sheetPlacements(front, false), back: print.sheetPlacements(back, true), marks: print.cutMarks(front).length };
  });
  expect(sheets.length).toBe(2);
  const [fronts, backs] = sheets;
  for (const [sheet, places, turned] of [[fronts, expected.front, false], [backs, expected.back, true]]) {
    expect(sheet.pictures.length).toBe(places.length);
    sheet.pictures.forEach(([a, , , d, e, f], i) => {
      // The unit square mapped onto the card: upright, a = width and d = height; turned 180°,
      // both negative. e, f: the corner the picture starts from.
      expect(Math.sign(a)).toBe(turned ? -1 : 1);
      expect(Math.abs(Math.abs(a) - places[i].width)).toBeLessThan(0.5);
      expect(Math.abs(Math.abs(d) - places[i].height)).toBeLessThan(0.5);
      expect(Math.abs(e - places[i].x)).toBeLessThan(0.5);
      expect(Math.abs(f - places[i].y)).toBeLessThan(0.5);
    });
    expect(sheet.lines).toBe(expected.marks);
  }
  await page.keyboard.press('Escape');
  expect(errors).toEqual([]);
});

test('every pocket design and every poster saves with its words drawn', async ({ page }) => {
  test.setTimeout(240000);
  const errors = await openFresh(page);
  await page.locator('[data-card-size]').selectOption('a7p');
  const pocket = await page.locator('[data-start-design]').evaluateAll((buttons) => buttons.map((button) => button.dataset.startDesign));
  expect(pocket.length).toBeGreaterThanOrEqual(4);
  await page.locator(`[data-start-design="${pocket[0]}"]`).click();
  for (const key of pocket) {
    await page.locator(`[data-card-design="${key}"]`).click();
    await expect(page.locator(`[data-card-design="${key}"]`)).toHaveAttribute('aria-pressed', 'true');
    const { ink, result } = await saveAndCountInk(page);
    expect(ink, `${key}: words drawn`).toBeGreaterThan(500);
    expect(result, key).not.toMatch(/could not/);
  }
  await openFresh(page);
  const posters = await page.locator('[data-start-poster]').evaluateAll((buttons) => buttons.map((button) => button.dataset.startPoster));
  expect(posters.length).toBe(4);
  await page.locator(`[data-start-poster="${posters[0]}"]`).click();
  for (const key of posters) {
    await page.locator(`[data-card-design="${key}"]`).click();
    await expect(page.locator(`[data-card-design="${key}"]`)).toHaveAttribute('aria-pressed', 'true');
    const { ink, result } = await saveAndCountInk(page);
    expect(ink, `${key}: words drawn`).toBeGreaterThan(2000);
    expect(result, key).not.toMatch(/could not/);
  }
  expect(errors).toEqual([]);
});

test('a guest list from a .csv file: rank and name joined, past 300 counted, a file too large refused', async ({ page }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  const rows = ['Rank,Name,Unit', ...Array.from({ length: 302 }, (_, i) => `Maj,"Officer ${i + 1}, SM",5 Sikh`)].join('\n');
  await page.locator('[data-card-guest-file]').setInputFiles({ name: 'guests.csv', mimeType: 'text/csv', buffer: Buffer.from(rows) });
  await expect(page.locator('[data-card-guest-count]')).toContainText('300 guests');
  await expect(page.locator('[data-card-guest-count]')).toContainText('2 names were left out');
  await expect(page.locator('[data-prism-toast]').filter({ hasText: '2 names were left out' })).toBeVisible();
  await expect.poll(async () => (await newestDraft(page))?.model?.card?.guests?.[0]).toBe('Maj Officer 1, SM');
  await page.locator('[data-card-guest-file]').setInputFiles({ name: 'big.txt', mimeType: 'text/plain', buffer: Buffer.alloc(1000001, 65) });
  await expect(page.locator('[data-prism-toast]').filter({ hasText: 'too large for a guest list' })).toBeVisible();
  expect((await newestDraft(page)).model.card.guests.length).toBe(300);
  expect(errors).toEqual([]);
});

test('Undo after typing in Card details puts the words back, on the card and in the field', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await touch(page);
  const venue = page.locator('[data-card-field="venue"]');
  await venue.fill('Cariappa Hall');
  await venue.blur();
  await expect.poll(async () => (await newestDraft(page)).model.items.find((item) => item.field === 'venue')?.text).toBe('Cariappa Hall');
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await newestDraft(page)).model.items.find((item) => item.field === 'venue')?.text).toBe('Sudarshan Hall');
  await expect(venue).toHaveValue('Sudarshan Hall');
  expect(errors).toEqual([]);
});

test('a poster asks which logo first, at the top of its panel, and its picture on the start screen is its own', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-poster="talk"]').click();
  const first = await page.locator('.clg-card-form > *').first().getAttribute('class');
  expect(first).toContain('clg-card-question');
  // The talk poster opens with its own sample wording, as its start-screen picture shows.
  await expect(page.locator('[data-card-field="title"]')).not.toHaveValue('Annual Sports Meet 2026');
  await page.locator('.clg-card-question [data-card-logo="later"]').click();
  await expect(page.locator('.clg-card-question')).toHaveCount(0);
  expect(errors).toEqual([]);
});

test('Save offers A4 edge to edge where it fits more cards: 4 A6 touching, no cut marks', async ({ page }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  await page.locator('[data-card-size]').selectOption('a6p');
  await page.locator('[data-start-design="classic"]').click();
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'Printing' }).click();
  const edge = page.locator('[data-save-sheet] input[value="a4edge"]');
  await expect(edge).toBeVisible();
  await expect(page.locator('[data-sheet-note="a4edge"]')).toContainText('4 cards touching');
  await edge.check();
  const download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const [sheet] = await sheetsOf(page, await download);
  expect(sheet.pictures.length).toBe(4);
  expect(sheet.lines).toBe(0);
  await page.keyboard.press('Escape');
  expect(errors).toEqual([]);
});

test('a logo still being read when the card is turned over stays in the tray, not on the back', async ({ page }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('[data-card-side="add"]').click();
  await page.locator('[data-card-side="front"]').click();
  // A large picture, so it is still being read when the card is turned.
  const png = await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.width = 3000;
    canvas.height = 3000;
    const context = canvas.getContext('2d');
    for (let i = 0; i < 400; i += 1) { context.fillStyle = `hsl(${i}, 60%, 50%)`; context.fillRect((i * 37) % 3000, (i * 91) % 3000, 300, 300); }
    return canvas.toDataURL('image/png').split(',')[1];
  });
  const chosen = page.locator('[data-card-logo-file]').setInputFiles({ name: 'big-logo.png', mimeType: 'image/png', buffer: Buffer.from(png, 'base64') });
  await page.locator('[data-card-side="back"]').click();
  await chosen;
  await expect(page.locator('[data-prism-toast]').filter({ hasText: 'turned over while your logo was read' })).toBeVisible({ timeout: 20000 });
  await touch(page).catch(() => {});
  const draft = (await newestDraft(page)).model;
  // Stored front first: the front keeps the SDD logo, and the back has no logo in its place.
  expect(draft.items.find((item) => item.role === 'logo').sticker).toBe('logo:sdd');
  expect(draft.back.items.some((item) => item.role === 'logo')).toBe(false);
  expect(errors).toEqual([]);
});

test('Tab straight after typing in Card details goes to the next field, not to the top of the page', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  const active = () => page.evaluate(() => document.activeElement?.dataset?.cardField || document.activeElement?.tagName);
  for (const [field, next] of [['host', 'invite'], ['invite', 'guest'], ['when', 'venue']]) {
    await page.locator(`[data-card-field="${field}"]`).click();
    await page.keyboard.press('End');
    await page.keyboard.type('!');
    await page.keyboard.press('Tab');
    expect(await active()).toBe(next);
    await page.waitForTimeout(300);
    expect(await active(), `still on ${next} after the panel is drawn`).toBe(next);
  }
  expect(errors).toEqual([]);
});

test('on a Bilingual card an occasion quick pick sets the Hindi occasion too', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="bilingual"]').click();
  await page.locator('[data-card-pick="occasion"][data-value="Farewell"]').click();
  await expect(page.locator('[data-card-field="occasion"]')).toHaveValue('Farewell');
  await expect(page.locator('[data-card-field="occasionHi"]')).toHaveValue('विदाई समारोह');
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.field === 'occasionHi')?.text).toBe('विदाई समारोह');
  expect(errors).toEqual([]);
});

test('looking through designs keeps no drafts; the first change keeps one', async ({ page }) => {
  const errors = await openFresh(page);
  const draftCount = () => page.evaluate(() => new Promise((resolve) => {
    const open = indexedDB.open('prism-collage-studio');
    open.onsuccess = () => {
      const all = open.result.transaction('drafts').objectStore('drafts').getAll();
      all.onsuccess = () => resolve(all.result.filter((d) => !d.deletedAt).length);
    };
    open.onerror = () => resolve(0);
  }));
  for (const key of ['classic', 'navy', 'regimental']) {
    await page.locator(`[data-start-design="${key}"]`).click();
    await page.locator('[data-view="editor"]').waitFor();
    await page.waitForTimeout(800);
    await page.locator('.clg-editor [data-action="home"], [data-view="editor"] [data-action="home"]').first().click();
    await page.locator('[data-view="start"]').waitFor();
  }
  expect(await draftCount()).toBe(0);
  await page.locator('[data-start-design="classic"]').click();
  await touch(page);
  expect(await draftCount()).toBe(1);
  expect(errors).toEqual([]);
});
