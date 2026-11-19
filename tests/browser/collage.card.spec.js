'use strict';

// Invitation cards (docs/ux/invitations-posters.md, PR 5): ready-made designs on the start screen,
// the Card details panel, and changing the design without losing a word.

const { test, expect } = require('./test');
const { openFresh, newestDraft } = require('./helpers');

/** The words of the field's text item in the newest draft, or null when it has none. */
async function fieldText(page, field) {
  const draft = await newestDraft(page);
  return draft?.model?.items?.find((item) => item.field === field)?.text ?? null;
}

test('a card starts from a design, its details are typed in the panel, and a new design keeps every word', async ({ page }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);

  // The start screen offers the designs at the chosen card size, each drawn.
  const size = page.locator('[data-card-size]');
  await expect(size).toHaveValue('a5p');
  const designs = page.locator('[data-start-design]');
  await expect(designs).toHaveCount(6);
  await expect.poll(() => designs.first().locator('canvas').evaluate((canvas) => {
    const data = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
    let inked = 0;
    for (let i = 0; i < data.length; i += 4) if (data[i + 3] > 0 && data[i] < 200) inked += 1;
    return inked;
  })).toBeGreaterThan(200);
  await size.selectOption('a6p');
  await expect(designs.first()).toHaveAttribute('aria-label', /A6 card/);

  await page.locator('[data-start-design="classic"]').click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await expect(page.locator('[data-size]')).toHaveValue('a6p');
  // The Card button and panel open with the card.
  const cardButton = page.locator('.clg-rail [data-panel="card"]');
  await expect(cardButton).toBeVisible();
  await expect(cardButton).toHaveAttribute('aria-pressed', 'true');
  const occasion = page.locator('[data-card-field="occasion"]');
  await expect(occasion).toHaveValue('Raising Day Dinner');
  await expect(page.locator('[data-card-design="classic"]')).toHaveAttribute('aria-pressed', 'true');

  // Typing in the panel changes the card's words, kept in the draft.
  await occasion.fill('Change of Command Parade');
  await occasion.blur();
  await expect.poll(() => fieldText(page, 'occasion')).toBe('Change of Command Parade');
  // A quick pick fills the field.
  await page.locator('[data-card-pick="dress"][data-value="Dress: Mess Dress"]').click();
  await expect(page.locator('[data-card-field="dress"]')).toHaveValue('Dress: Mess Dress');
  await expect.poll(() => fieldText(page, 'dress')).toBe('Dress: Mess Dress');

  // A field emptied leaves the card; filled again, it comes back.
  const note = page.locator('[data-card-field="note"]');
  await note.fill('');
  await note.blur();
  await expect.poll(() => fieldText(page, 'note')).toBe(null);
  await note.fill('Please be seated by 1915 h');
  await note.blur();
  await expect.poll(() => fieldText(page, 'note')).toBe('Please be seated by 1915 h');

  // Another design: every word stays, the look changes, and Undo brings the first one back.
  await page.locator('[data-card-design="navy"]').click();
  await expect(page.locator('[data-prism-toast]').filter({ hasText: 'Design: Navy and gold' })).toBeVisible();
  await expect.poll(async () => (await newestDraft(page))?.model?.card?.design).toBe('navy');
  const navy = (await newestDraft(page)).model;
  expect(navy.background).toEqual({ type: 'gradient', key: 'navy' });
  expect(navy.items.find((item) => item.field === 'occasion').text).toBe('Change of Command Parade');
  expect(navy.items.find((item) => item.field === 'note').text).toBe('Please be seated by 1915 h');
  await expect(occasion).toHaveValue('Change of Command Parade');
  await page.locator('[data-action="undo"]').click();
  await expect.poll(async () => (await newestDraft(page))?.model?.card?.design).toBe('classic');
  await expect(page.locator('[data-card-design="classic"]')).toHaveAttribute('aria-pressed', 'true');
  expect(errors).toEqual([]);
});

test('words typed on the card show in the Card details, and a plain collage has no Card button', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(page.locator('.clg-rail [data-panel="card"]')).toBeHidden();
  await page.getByRole('button', { name: 'Back to the start' }).click();

  await page.locator('[data-start-design="navy"]').click();
  await expect(page.locator('[data-card-field="venue"]')).toHaveValue("Officers' Mess, SDD Campus");
  // Select the venue on the page through Layers and type in its Words box.
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  await page.locator('[data-layer-select]').filter({ hasText: "Officers' Mess" }).click();
  await page.locator('.clg-props [data-props-tab="settings"]').click();
  const words = page.locator('[data-text]');
  await words.fill('Sudarshan Hall');
  await words.blur();
  // Back in the Card panel, the field shows the words typed on the card.
  await page.locator('.clg-rail [data-panel="card"]').click();
  await page.locator('.clg-rail [data-panel="card"]').click();
  await expect(page.locator('[data-card-field="venue"]')).toHaveValue('Sudarshan Hall');
  expect(errors).toEqual([]);
});

test('a design pressed straight after typing in Card details still changes the design', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  const occasion = page.locator('[data-card-field="occasion"]');
  // Type, and at once (well inside the pause that records the typing) press another design.
  await occasion.fill('Barakhana');
  await page.locator('[data-card-design="navy"]').click();
  await expect(page.locator('[data-card-design="navy"]')).toHaveAttribute('aria-pressed', 'true');
  await expect.poll(async () => (await newestDraft(page))?.model?.card?.design).toBe('navy');
  const model = (await newestDraft(page)).model;
  expect(model.items.find((item) => item.field === 'occasion').text).toBe('Barakhana');
  expect(errors).toEqual([]);
});

test('a quick pick pressed straight after typing words in Settings still fills its field', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  // Select the venue through Layers and type in its Words box, then at once press a quick pick.
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  await page.locator('[data-layer-select]').filter({ hasText: "Officers' Mess" }).click();
  await page.locator('.clg-props [data-props-tab="settings"]').click();
  await page.locator('[data-text]').fill('Sudarshan Hall');
  await page.locator('[data-card-pick="occasion"][data-value="Barakhana"]').click();
  await expect(page.locator('[data-card-field="occasion"]')).toHaveValue('Barakhana');
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.field === 'occasion')?.text).toBe('Barakhana');
  expect((await newestDraft(page)).model.items.find((item) => item.field === 'venue').text).toBe('Sudarshan Hall');
});

test('Regimental takes the unit\'s colours from Card details; Bilingual shows the Hindi fields, Classic does not', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="regimental"]').click();
  const top = page.locator('[data-card-colour="1"]');
  await expect(top).toHaveValue('#7a1f2b');
  await top.evaluate((input) => {
    input.value = '#1f5b3a';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  });
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.role === 'band1')?.fill).toBe('#1f5b3a');
  const model = (await newestDraft(page)).model;
  expect(model.items.find((item) => item.field === 'host').color).toBe('#1f5b3a');
  expect(model.card.details.colours.split(' ')[0]).toBe('#1f5b3a');
  await expect(page.locator('[data-card-field="occasionHi"]')).toHaveCount(0);
  // The colour change keeps the card's list of placed fields: a field deleted after it stays deleted.
  expect(Array.isArray(model.card.placed)).toBe(true);
  expect(model.card.placed).toContain('host');

  await page.locator('[data-card-design="bilingual"]').click();
  await expect(page.locator('[data-card-field="occasionHi"]')).toHaveValue('स्थापना दिवस रात्रिभोज');
  await expect(page.locator('[data-card-colour="1"]')).toHaveCount(0);
  await page.locator('[data-card-design="classic"]').click();
  await expect(page.locator('[data-card-field="occasionHi"]')).toHaveCount(0);
  await expect(page.locator('[data-card-field="at"]')).toBeVisible();
  expect(errors).toEqual([]);
});

test('words typed in Card details show in the selected item\'s Words box at once', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  await page.locator('[data-layer-select]').filter({ hasText: "Officers' Mess" }).click();
  await page.locator('.clg-props [data-props-tab="settings"]').click();
  await expect(page.locator('[data-text]')).toHaveValue("Officers' Mess, SDD Campus");
  await page.locator('[data-card-field="venue"]').fill('Sudarshan Hall');
  await expect(page.locator('[data-text]')).toHaveValue('Sudarshan Hall');
  expect(errors).toEqual([]);
});

test('emptying the field of a locked item does not delete it', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  await page.locator('[data-layer-lock]').locator('xpath=..').filter({ hasText: 'Kindly bring this card' }).locator('[data-layer-lock]').click();
  const note = page.locator('[data-card-field="note"]');
  await note.fill('');
  await expect(note).toHaveValue('Kindly bring this card');
  await expect(page.locator('[data-prism-toast]').filter({ hasText: 'It is locked' })).toBeVisible();
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.field === 'note')?.text).toBe('Kindly bring this card');
  expect(errors).toEqual([]);
});

test('Undo straight after a quick pick puts the field back too', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('[data-card-pick="occasion"][data-value="Barakhana"]').click();
  await expect(page.locator('[data-card-field="occasion"]')).toHaveValue('Barakhana');
  // The quick pick keeps the focus; Ctrl+Z undoes it.
  await page.keyboard.press('Control+z');
  await expect(page.locator('[data-card-field="occasion"]')).toHaveValue('Raising Day Dinner');
  expect(errors).toEqual([]);
});

test('a Card field clicked straight after typing words in Settings shows those words', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  await page.locator('[data-layer-select]').filter({ hasText: "Officers' Mess" }).click();
  await page.locator('.clg-props [data-props-tab="settings"]').click();
  await page.locator('[data-text]').fill('Sudarshan Hall');
  // At once, into the matching Card details field, and type one more letter.
  const venue = page.locator('[data-card-field="venue"]');
  await venue.click();
  await expect(venue).toHaveValue('Sudarshan Hall');
  await venue.press('End');
  await venue.type('s');
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.field === 'venue')?.text).toBe('Sudarshan Halls');
  expect(errors).toEqual([]);
});

test('the user\'s own logo: a see-through PNG keeps the card showing through it, and a removed logo stays off', async ({ page }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await expect(page.locator('[data-card-logo-name]')).toHaveText('SDD');

  // A 200 × 200 PNG: clear all round, a red disc in the middle.
  const png = await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.width = 200;
    canvas.height = 200;
    const context = canvas.getContext('2d');
    context.fillStyle = '#d01010';
    context.beginPath();
    context.arc(100, 100, 50, 0, Math.PI * 2);
    context.fill();
    return canvas.toDataURL('image/png').split(',')[1];
  });
  await page.locator('[data-card-logo-file]').setInputFiles({ name: 'unit-logo.png', mimeType: 'image/png', buffer: Buffer.from(png, 'base64') });
  await expect(page.locator('[data-card-logo-name]')).toHaveText('Your own logo (unit-logo.png)');
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.role === 'logo')?.kind).toBe('photo');
  const logo = (await newestDraft(page)).model.items.find((item) => item.role === 'logo');

  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('button', { name: 'A slide or a screen' }).click();
  const download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const file = await download;
  const pixels = await page.evaluate(async ([base64, item]) => {
    const text = atob(base64);
    const bytes = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i += 1) bytes[i] = text.charCodeAt(i);
    const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
    const canvas = document.createElement('canvas');
    canvas.width = bitmap.width;
    canvas.height = bitmap.height;
    const context = canvas.getContext('2d');
    context.drawImage(bitmap, 0, 0);
    const scale = bitmap.width / 1000;
    const at = (x, y) => [...context.getImageData(Math.round(x * scale), Math.round(y * scale), 1, 1).data].slice(0, 3);
    const half = item.width / 2;
    return { middle: at(item.x, item.y), corner: at(item.x - half * 0.85, item.y - half * 0.85) };
  }, [(await streamBytes(file)).toString('base64'), logo]);
  // The disc is drawn; round it, the card's ivory shows through.
  expect(pixels.middle[0]).toBeGreaterThan(180);
  expect(pixels.middle[1]).toBeLessThan(60);
  expect(pixels.corner).toEqual([0xfb, 0xf7, 0xee]);
  await page.keyboard.press('Escape');

  // Removed, it stays off when the design changes; the SDD logo can be put back.
  await page.locator('[data-card-logo="remove"]').click();
  await expect(page.locator('[data-card-logo-name]')).toHaveText('No logo');
  await page.locator('[data-card-design="navy"]').click();
  await expect.poll(async () => (await newestDraft(page))?.model?.card?.design).toBe('navy');
  expect((await newestDraft(page)).model.items.some((item) => item.role === 'logo')).toBe(false);
  await page.locator('[data-card-logo-use="logo:sdd"]').click();
  await expect(page.locator('[data-card-logo-name]')).toHaveText('SDD');
  expect(errors).toEqual([]);
});

async function streamBytes(download) {
  const chunks = [];
  for await (const chunk of await download.createReadStream()) chunks.push(chunk);
  return Buffer.concat(chunks);
}

test('a second logo can be changed: the choices change the logo picked above them', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('[data-card-logo="second"]').click();
  // Straight after adding it, the choices change the second logo.
  await expect(page.locator('[data-card-logo-target="logo2"]')).toHaveAttribute('aria-pressed', 'true');
  const png = await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.width = 120;
    canvas.height = 60;
    canvas.getContext('2d').fillRect(0, 0, 120, 60);
    return canvas.toDataURL('image/png').split(',')[1];
  });
  await page.locator('[data-card-logo-file]').setInputFiles({ name: 'second.png', mimeType: 'image/png', buffer: Buffer.from(png, 'base64') });
  await expect(page.locator('[data-card-logo-name]')).toHaveText('Your own logo (second.png)');
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.role === 'logo2')?.kind).toBe('photo');
  const items = (await newestDraft(page)).model.items;
  // The first logo is still the SDD logo.
  expect(items.find((item) => item.role === 'logo').sticker).toBe('logo:sdd');
  // First logo picked: the name shown is the first's.
  await page.locator('[data-card-logo-target="logo"]').click();
  await expect(page.locator('[data-card-logo-name]')).toHaveText('SDD');
  // Remove takes the logo picked: the second, then the one left.
  await page.locator('[data-card-logo-target="logo2"]').click();
  await page.locator('[data-card-logo="remove"]').click();
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.some((item) => item.role === 'logo2')).toBe(false);
  expect((await newestDraft(page)).model.items.find((item) => item.role === 'logo').sticker).toBe('logo:sdd');
  await expect(page.locator('[data-card-logo-target]')).toHaveCount(0);
  // With two again, removing the first leaves the second as the card's logo, still removable.
  await page.locator('[data-card-logo="second"]').click();
  await page.locator('[data-card-logo-file]').setInputFiles({ name: 'second.png', mimeType: 'image/png', buffer: Buffer.from(png, 'base64') });
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.role === 'logo2')?.kind).toBe('photo');
  await page.locator('[data-card-logo-target="logo"]').click();
  await page.locator('[data-card-logo="remove"]').click();
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.role === 'logo')?.kind).toBe('photo');
  await expect(page.locator('[data-card-logo="remove"]')).toHaveText(/Remove logo/);
  expect(errors).toEqual([]);
});

test('a locked logo is neither replaced nor removed from Card details', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  // A word typed, so the draft is saved and can be read.
  await page.locator('[data-card-field="venue"]').fill('Sudarshan Hall');
  await page.locator('[data-card-field="venue"]').blur();
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.some((item) => item.role === 'logo')).toBe(true);
  // Lock the logo through Layers.
  const logoId = (await newestDraft(page)).model.items.find((item) => item.role === 'logo').id;
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  await page.locator(`[data-layer-lock="${logoId}"]`).click();
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.role === 'logo')?.locked).toBe(true);
  const before = (await newestDraft(page)).model.items.find((item) => item.role === 'logo');
  await page.locator('[data-card-logo="remove"]').click();
  await expect(page.locator('[data-prism-toast]').filter({ hasText: 'locked' })).toBeVisible();
  await page.locator('[data-card-logo-use="logo:sdd"]').click();
  const after = (await newestDraft(page)).model.items.find((item) => item.role === 'logo');
  expect(after?.id).toBe(before.id);
  expect(errors).toEqual([]);
});

test('a logo is a photo of the collage: with 60 photos already, Your own logo is refused', async ({ page }) => {
  test.setTimeout(120000);
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  const png = await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.width = 8;
    canvas.height = 8;
    canvas.getContext('2d').fillRect(0, 0, 8, 8);
    return canvas.toDataURL('image/png').split(',')[1];
  });
  const files = Array.from({ length: 60 }, (_, i) => ({ name: `p${i}.png`, mimeType: 'image/png', buffer: Buffer.from(png, 'base64') }));
  await page.locator('[data-file-input]').setInputFiles(files);
  await expect(page.locator('[data-photo-count]')).toHaveText('60 of 60 photos');
  await page.locator('[data-card-logo-file]').setInputFiles({ name: 'logo.png', mimeType: 'image/png', buffer: Buffer.from(png, 'base64') });
  await expect(page.locator('[data-prism-toast]').filter({ hasText: 'the most it can hold' })).toBeVisible();
  await expect(page.locator('[data-photo-count]')).toHaveText('60 of 60 photos');
  await expect(page.locator('[data-card-logo-name]')).toHaveText('SDD');
  expect(errors).toEqual([]);
});

test('crests that arrive after a card is open appear in Card details as logos', async ({ page }) => {
  const samples = require('./fixtures/collage-api-samples.json');
  const crest = samples.stickers.stickers[0];
  // Hold back the crest list until the card is open.
  let release;
  const held = new Promise((resolve) => { release = resolve; });
  await page.route('**/api/collage/stickers', async (route) => { await held; await route.continue(); });
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await expect(page.locator('[data-card-logo-use="logo:sdd"]')).toBeVisible();
  await expect(page.locator(`[data-card-logo-use="unit:${crest.id}"]`)).toHaveCount(0);
  release();
  await expect(page.locator(`[data-card-logo-use="unit:${crest.id}"]`)).toBeVisible();
  expect(errors).toEqual([]);
});

test('a back with the programme: added and shown, typed from the front, and saved as a two-page PDF', async ({ page }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('[data-card-side="add"]').click();
  await expect(page.locator('[data-card-side="back"]')).toHaveAttribute('aria-pressed', 'true');
  await expect.poll(async () => (await newestDraft(page))?.model?.side).toBe('back');
  // The back's programme is typed in Card details, even with the front showing.
  await page.locator('[data-card-side="front"]').click();
  await expect(page.locator('[data-card-side="front"]')).toHaveAttribute('aria-pressed', 'true');
  const programme = page.locator('[data-card-field="programme"]');
  await programme.fill('1900 h · Arrival\n1930 h · Dinner');
  await programme.blur();
  await expect.poll(async () => (await newestDraft(page))?.model?.back?.items?.find((item) => item.field === 'programme')?.text).toBe('1900 h · Arrival\n1930 h · Dinner');

  // Save: both sides are a PDF of two pages, front first.
  await page.getByRole('button', { name: 'Save as…' }).click();
  await expect(page.locator('[data-save-sides]')).toBeVisible();
  await page.getByRole('button', { name: 'Printing' }).click();
  let download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  let file = await download;
  const pages = await page.evaluate(async (base64) => {
    const text = atob(base64);
    const bytes = new Uint8Array(text.length);
    for (let i = 0; i < text.length; i += 1) bytes[i] = text.charCodeAt(i);
    return (await window.PDFLib.PDFDocument.load(bytes)).getPageCount();
  }, (await streamBytes(file)).toString('base64'));
  expect(pages).toBe(2);
  await expect(page.locator('[data-save-result]')).toContainText('2 pages');
  // The front alone, as a picture: one file named for its side.
  await page.locator('input[name="sides"][value="front"]').check();
  await page.getByRole('button', { name: 'A slide or a screen' }).click();
  download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  file = await download;
  expect(file.suggestedFilename()).toMatch(/-front\.png$/);
  await page.keyboard.press('Escape');

  // Deleted, the card is one-sided again, with Undo.
  await page.locator('[data-card-side="delete"]').click();
  await expect(page.locator('[data-card-side="add"]')).toBeVisible();
  await expect.poll(async () => (await newestDraft(page))?.model?.back).toBe(undefined);
  expect(errors).toEqual([]);
});

test('a unit colour changed from the front reaches the back too', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="regimental"]').click();
  await page.locator('[data-card-side="add"]').click();
  await page.locator('[data-card-side="front"]').click();
  await page.locator('[data-card-colour="1"]').evaluate((input) => {
    input.value = '#1f5b3a';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  });
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.role === 'band1')?.fill).toBe('#1f5b3a');
  const model = (await newestDraft(page)).model;
  const back = model.side === 'back' ? model.items : model.back.items;
  const tinted = back.filter((item) => item.tint && Object.values(item.tint).includes(1));
  expect(tinted.length).toBeGreaterThan(0);
  for (const item of tinted) for (const [prop, which] of Object.entries(item.tint)) if (which === 1) expect(item[prop]).toBe('#1f5b3a');
  expect(errors).toEqual([]);
});

test('the back\'s small logo follows the front\'s: replaced, removed and put back', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('[data-card-side="add"]').click();
  await page.locator('[data-card-side="front"]').click();
  const backLogo = async () => {
    const model = (await newestDraft(page))?.model;
    const back = model?.side === 'back' ? model.items : model?.back?.items;
    return (back || []).find((item) => item.role === 'backlogo') || null;
  };
  await expect.poll(async () => (await backLogo())?.sticker).toBe('logo:sdd');
  const png = await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.width = 60;
    canvas.height = 60;
    canvas.getContext('2d').fillRect(0, 0, 60, 60);
    return canvas.toDataURL('image/png').split(',')[1];
  });
  await page.locator('[data-card-logo-file]').setInputFiles({ name: 'unit.png', mimeType: 'image/png', buffer: Buffer.from(png, 'base64') });
  await expect.poll(async () => (await backLogo())?.kind).toBe('photo');
  await page.locator('[data-card-logo="remove"]').click();
  await expect.poll(async () => await backLogo()).toBe(null);
  await page.locator('[data-card-logo-use="logo:sdd"]').click();
  await expect.poll(async () => (await backLogo())?.sticker).toBe('logo:sdd');
});

test('pocket sizes offer the pocket cards; a car pass shows its own fields and shares the occasion', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-card-size]').selectOption('passl');
  await expect(page.locator('[data-start-design]')).toHaveCount(4);
  await page.locator('[data-start-design="pass"]').click();
  await expect(page.locator('[data-card-field="passTitle"]')).toHaveValue('CAR PASS');
  await expect(page.locator('[data-card-field="vehicle"]')).toBeVisible();
  await expect(page.locator('[data-card-field="host"]')).toHaveCount(0);
  await expect(page.locator('[data-card-design]')).toHaveCount(4);
  // Another pocket design keeps the occasion typed for the pass.
  await page.locator('[data-card-field="occasion"]').fill('Barakhana');
  await page.locator('[data-card-field="occasion"]').blur();
  await page.locator('[data-card-design="pocketinvite"]').click();
  await expect.poll(async () => (await newestDraft(page))?.model?.card?.design).toBe('pocketinvite');
  expect((await newestDraft(page)).model.items.find((item) => item.field === 'occasion').text).toBe('Barakhana');
  expect(errors).toEqual([]);
});

test('turning to the back keeps the card\'s own title, from its front', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  const title = page.locator('[data-name]');
  // Once the card has its own name from its words.
  await expect(title).not.toHaveAttribute('placeholder', 'Untitled collage');
  const before = await title.getAttribute('placeholder');
  await page.locator('[data-card-side="add"]').click();
  await expect(page.locator('[data-card-side="back"]')).toHaveAttribute('aria-pressed', 'true');
  expect(await title.getAttribute('placeholder')).toBe(before);
  expect(before).not.toMatch(/Programme/);
});

// Posters (docs/ux/invitations-posters.md, PR 11).
test('a poster asks which logo; Decide later keeps its place; No logo moves the heading up; a logo picked brings its place back', async ({ page }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  await expect(page.locator('[data-start-poster]')).toHaveCount(4);
  await page.locator('[data-start-poster="notice"]').click();
  await expect(page.locator('#clgCardTitle')).toHaveText('Poster details');
  await expect(page.locator('.clg-rail [data-panel="card"] span')).toHaveText('Poster');
  await expect(page.locator('[data-card-field="title"]')).toHaveValue('Annual Sports Meet 2026');
  await expect(page.locator('.clg-card-question')).toContainText('Which logo goes on this poster?');
  await expect(page.locator('[data-card-design]')).toHaveCount(4);
  const titleY = async () => (await newestDraft(page))?.model?.items?.find((item) => item.field === 'title')?.y;
  // Decide later: the question goes, the logo's place stays (and the draft is written).
  await page.locator('.clg-card-question [data-card-logo="later"]').click();
  await expect(page.locator('.clg-card-question')).toHaveCount(0);
  await expect.poll(titleY).toBeGreaterThan(0);
  const before = await titleY();
  expect((await newestDraft(page)).model.items.find((item) => item.role === 'logo').kind).toBe('slot');

  // No logo: the heading moves up.
  await page.locator('[data-card-logo="none"]').click();
  await expect.poll(titleY).toBeLessThan(before);
  expect((await newestDraft(page)).model.items.some((item) => item.role === 'logo')).toBe(false);

  // The SDD logo picked: its place comes back and the heading goes back down.
  await page.locator('[data-card-logo-use="logo:sdd"]').click();
  await expect.poll(titleY).toBe(before);
  expect((await newestDraft(page)).model.items.find((item) => item.role === 'logo').sticker).toBe('logo:sdd');
  expect(errors).toEqual([]);
});

test('a card can be saved with an empty back showing, since its front has the invitation', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('[data-card-side="add"]').click();
  await expect(page.locator('[data-card-side="back"]')).toHaveAttribute('aria-pressed', 'true');
  // Empty the back: each of its items chosen in Layers and deleted.
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  for (let i = 0; i < 20 && await page.locator('[data-layer-select]').count(); i += 1) {
    await page.locator('[data-layer-select]').first().click();
    await page.keyboard.press('Delete');
  }
  await expect(page.locator('[data-layer-select]')).toHaveCount(0);
  await page.getByRole('button', { name: 'Save as…' }).click();
  await expect(page.locator('[data-save-dialog]')).toBeVisible();
  await expect(page.locator('[data-prism-toast]').filter({ hasText: 'nothing to save yet' })).toHaveCount(0);
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
  // A word typed, so the draft is saved and can be read.
  await page.locator('[data-card-field="venue"]').fill('Sudarshan Hall');
  await page.locator('[data-card-field="venue"]').blur();
  await expect.poll(async () => Boolean(await newestDraft(page))).toBe(true);
  const model = (await newestDraft(page)).model;
  const front = model.side === 'back' ? model.back.items : model.items;
  const back = model.side === 'back' ? model.items : model.back.items;
  expect(front.find((item) => item.role === 'logo').sticker).toBe('logo:sdd');
  expect(back.some((item) => item.role === 'logo')).toBe(false);
  expect(errors).toEqual([]);
});

test('a programme card\'s programme typed in Card details is the programme on the card', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-card-size]').selectOption('a7p');
  await page.locator('[data-start-design="programmecard"]').click();
  const programme = page.locator('[data-card-field="programme"]');
  await programme.fill('1900 h  Arrival\n1930 h  Dinner');
  await programme.blur();
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.field === 'programme')?.text).toBe('1900 h  Arrival\n1930 h  Dinner');
  await expect(programme).toHaveValue('1900 h  Arrival\n1930 h  Dinner');
  expect(errors).toEqual([]);
});

// Codex on PR 11: the contact line is in Poster details; a poster's back logo follows No logo.
test('Poster details changes the contact line; Remove logo takes the back logo too', async ({ page }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  await page.locator('[data-start-poster="notice"]').click();
  const contact = page.locator('[data-card-field="contact"]');
  await expect(contact).toHaveValue('Sports Officer · Ext 2210');
  await contact.fill('Adjutant · Ext 2201');
  await contact.blur();
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.find((item) => item.field === 'contact')?.text).toBe('Adjutant · Ext 2201');

  await page.locator('[data-card-logo-use="logo:sdd"]').click();
  await page.locator('[data-card-side="add"]').click();
  await expect(page.locator('[data-card-side="back"]')).toHaveAttribute('aria-pressed', 'true');
  const backLogo = async () => {
    const model = (await newestDraft(page))?.model;
    const back = model?.side === 'back' ? model.items : model?.back?.items;
    return (back || []).some((item) => item.role === 'backlogo');
  };
  await expect.poll(backLogo).toBe(true);
  await page.locator('[data-card-side="front"]').click();
  await page.locator('[data-card-logo="remove"]').click();
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.some((item) => item.role === 'logo')).toBe(false);
  await expect.poll(backLogo).toBe(false);
  expect(errors).toEqual([]);
});
