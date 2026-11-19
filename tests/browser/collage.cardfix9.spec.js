'use strict';

// Invitation cards, cold review 9 (code and UX, 19 Nov): a long guest name laid out with the real
// fonts stays clear of the words round it; the guest list says what it took for headings; Save
// keeps its button in view and says when colour runs to the page's edge.

const { test, expect } = require('./test');
const { openFresh, newestDraft } = require('./helpers');

const LONG_GUEST = 'Lt Gen A. K. Singh, PVSM, AVSM, VSM, ADC and Mrs Sunita Singh';

/**
 * The guest's card laid out in the page exactly as Save makes it (PrismCollageSave.guestPage),
 * with the fonts PRISM ships measured by a canvas as the page measures them, and the boxes of the
 * name and of the parts round it, in page units.
 */
async function guestBoxes(page, model, guest) {
  return page.evaluate(async ({ model, guest }) => {
    const core = window.PrismCollage;
    const designs = window.PrismCollageDesigns;
    const collage = core.readDraftModel(model);
    const context = document.createElement('canvas').getContext('2d');
    const measureText = (text, font) => {
      context.font = font;
      if ('letterSpacing' in context) context.letterSpacing = '0px';
      return context.measureText(text).width;
    };
    // Every font the card's words use is loaded before anything is measured.
    const texts = [...collage.items, ...(collage.back?.items || [])].filter((item) => item.kind === 'text');
    await Promise.all(texts.map((item) => document.fonts.load(core.fontOf(item, 40), `${item.text || ''}${guest}`)));
    await document.fonts.ready;
    const front = collage.side === 'back' ? core.flipSide(collage) : collage;
    const onBack = designs.sideOf(front, 'guest') === 'back';
    const side = onBack ? { ...front.back, size: front.size } : front;
    const named = window.PrismCollageSave.guestPage(side, guest, { collage: front, core, designs, measureText });
    // The guest's name: the design's guest field with the name in it, or the one Save adds.
    const name = named.items.filter((item) => item.field === 'guest').pop();
    const boxOf = (item) => {
      const layout = item.kind === 'text' ? core.layoutText(item, measureText) : null;
      const width = layout ? layout.width : item.width;
      const height = layout ? layout.height : core.itemHeight(item);
      const half = core.turnedHalfBox(width, height, item.angle || 0);
      return { left: item.x - half.halfWidth, right: item.x + half.halfWidth, top: item.y - half.halfHeight, bottom: item.y + half.halfHeight, middle: item.y, size: layout?.size, lines: layout?.lines };
    };
    const around = {};
    for (const item of named.items) {
      if (item === name || item.hidden) continue;
      const key = item.field || item.role;
      if (['invite', 'at', 'occasion', 'host', 'guestline'].includes(key) && !(item.kind === 'text' && !String(item.text || '').trim())) around[key] = boxOf(item);
    }
    return { name: boxOf(name), text: name.text, around, page: { width: core.PAGE_WIDTH, height: core.pageHeight(collage.size) } };
  }, { model, guest });
}

/** How far two boxes overlap each way (a negative number is the gap between them). */
const overlap = (a, b) => ({ x: Math.min(a.right, b.right) - Math.max(a.left, b.left), y: Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) });

for (const size of ['a5l', 'a6p']) {
  // Code review 9, M3: the layout tests in Node measure every letter alike; this one uses the fonts.
  test(`a long guest name on Classic ${size} stays on the page and clear of the words and line round it`, async ({ page }) => {
    const errors = await openFresh(page);
    await page.locator('[data-card-size]').selectOption(size);
    await page.locator('[data-start-design="classic"]').click();
    await page.locator('[data-view="editor"]').waitFor();
    await page.locator('[data-card-guests]').fill(LONG_GUEST);
    await page.locator('[data-card-guests]').blur();
    await expect.poll(async () => (await newestDraft(page))?.model?.card?.guests).toEqual([LONG_GUEST]);
    const boxes = await guestBoxes(page, (await newestDraft(page)).model, LONG_GUEST);
    const { name, around, page: sheet } = boxes;
    // On the page, with room to spare.
    expect(name.left).toBeGreaterThanOrEqual(0);
    expect(name.right).toBeLessThanOrEqual(sheet.width);
    expect(name.top).toBeGreaterThanOrEqual(0);
    expect(name.bottom).toBeLessThanOrEqual(sheet.height);
    // Clear of the invitation line above it and of "at the" / the occasion below it.
    for (const key of ['invite', 'at', 'occasion', 'host']) {
      if (!around[key]) continue;
      const both = overlap(name, around[key]);
      expect(Math.min(both.x, both.y), `${size}: the name and the ${key} (${JSON.stringify({ name, [key]: around[key], text: boxes.text })})`).toBeLessThanOrEqual(0.5);
    }
    // The name is written on its line, never struck through by it: the rule (drawn across the
    // middle of its box) runs under the name's last line, close below it.
    if (around.guestline) {
      const rule = around.guestline.middle;
      const lineHeight = (name.bottom - name.top) / Math.max(1, name.lines);
      expect(rule, `${size}: the line under the name`).toBeGreaterThanOrEqual(name.bottom - lineHeight * 0.3);
      expect(rule, `${size}: the name sits on its line`).toBeLessThanOrEqual(name.bottom + lineHeight);
    }
    expect(errors).toEqual([]);
  });
}

// UX review 9, L4: with a back, a guest list and Printing, Save was below the fold at 1440 x 900.
test('Save stays in view at 1440 x 900 with a back, a guest list and Printing, and Tab still reaches it', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const errors = await openFresh(page);
  await page.locator('[data-start-design="navy"]').click();
  await page.locator('[data-view="editor"]').waitFor();
  await page.locator('[data-card-side="add"]').click();
  await page.locator('[data-card-side="front"]').click();
  await page.locator('[data-card-guests]').fill(['Brig A. Rao', 'Col B. Sen', 'Maj C. Das'].join('\n'));
  await page.locator('[data-card-guests]').blur();
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.locator('[data-save-purpose="print"]').click();
  await expect(page.locator('[data-save-guests]')).toBeVisible();
  await expect(page.locator('[data-save-sides]')).toBeVisible();
  await expect(page.locator('[data-save-sheet]')).toBeVisible();
  const dialog = page.locator('[data-save-dialog]');
  const go = page.locator('[data-save-go]');
  // The choices are taller than the dialog: it scrolls, and Save stays in view at its foot.
  expect(await dialog.evaluate((element) => element.scrollHeight > element.clientHeight)).toBe(true);
  const inView = async () => {
    const [box, frame] = await Promise.all([go.boundingBox(), dialog.boundingBox()]);
    return box.y >= frame.y && box.y + box.height <= frame.y + frame.height + 0.5 && box.y + box.height <= 900;
  };
  expect(await inView()).toBe(true);
  // Nothing hides the button: it is the element at its own middle.
  expect(await go.evaluate((button) => {
    const box = button.getBoundingClientRect();
    const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
    return button === hit || button.contains(hit);
  })).toBe(true);
  // Scrolled to the top and to the bottom of the choices, it is still there.
  await dialog.evaluate((element) => { element.scrollTop = 0; });
  expect(await inView()).toBe(true);
  await dialog.evaluate((element) => { element.scrollTop = element.scrollHeight; });
  expect(await inView()).toBe(true);
  // Colour runs to the edge on Navy and gold: Save says so for printing, not for WhatsApp.
  await expect(page.locator('[data-save-edge]')).toBeVisible();
  await expect(page.locator('[data-save-edge]')).toContainText('most office printers leave a white margin');
  await page.locator('[data-save-purpose="share"]').click();
  await expect(page.locator('[data-save-edge]')).toBeHidden();
  // The keyboard reaches Save from the last choice.
  await page.locator('[data-save-purpose="print"]').click();
  await page.locator('[data-action="save-close"]').first().focus();
  let reached = false;
  for (let i = 0; i < 30 && !reached; i += 1) {
    await page.keyboard.press('Tab');
    reached = await go.evaluate((button) => document.activeElement === button);
  }
  expect(reached).toBe(true);
  await page.keyboard.press('Escape');
  expect(errors).toEqual([]);
});

test('Bilingual: Save names every empty Hindi line, and gives no edge hint on its cream paper', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="bilingual"]').click();
  await page.locator('[data-view="editor"]').waitFor();
  for (const [field, words] of [['occasion', 'Change of Command Parade and Reception'], ['host', 'The Commandant and Officers, 50 Bde']]) {
    await page.locator(`[data-card-field="${field}"]`).fill(words);
    await page.locator(`[data-card-field="${field}"]`).blur();
  }
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.locator('[data-save-purpose="print"]').click();
  await expect(page.locator('[data-save-empty]')).toContainText('The Hindi host and occasion are empty');
  await expect(page.locator('[data-save-edge]')).toBeHidden();
  await page.keyboard.press('Escape');
  expect(errors).toEqual([]);
});

// Code review 9, M2: a first line taken for headings is said, so a guest it held shows.
test('the guest list says when its first line was read as headings, and when no column held a name', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('[data-view="editor"]').waitFor();
  const box = page.locator('[data-card-guests]');
  const hint = page.locator('[data-card-guest-count]');
  await box.fill(['Name', 'Brig A. Rao', 'Col B. Sen'].join('\n'));
  await expect(hint).toContainText('2 guests');
  await expect(hint).toContainText('First line read as column headings: “Name”.');
  await expect(hint).not.toContainText('serial');
  // A first guest with a rank is a guest, never a heading.
  await box.fill(['Col R. Names, VSM', 'Brig A. Rao'].join('\n'));
  await expect(hint).toContainText('2 guests');
  await expect(hint).toContainText('First on the cards: “Col R. Names, VSM”');
  await expect(hint).not.toContainText('headings');
  // A file headed with no name column: no guests made up from the headings.
  await page.locator('[data-card-guest-file]').setInputFiles({ name: 'guests.csv', mimeType: 'text/csv', buffer: Buffer.from(['Rank,Name of the Unit', 'Brig,50 Bde'].join('\n')) });
  await expect(page.locator('[data-card-guest-count]')).toContainText('No names were read. First line read as column headings: “Rank”, “Name of the Unit”.');
  // Serial numbers in the first column: the names are read from the next one, and it is said.
  await page.locator('[data-card-guest-file]').setInputFiles({ name: 'guests.csv', mimeType: 'text/csv', buffer: Buffer.from(['1,Brig A. Rao', '2,Col B. Sen'].join('\n')) });
  await expect(page.locator('[data-card-guest-count]')).toContainText('First on the cards: “Brig A. Rao”, “Col B. Sen”.');
  await expect(page.locator('[data-card-guest-count]')).toContainText('The first column holds only serial numbers, so the second column was used.');
  expect(errors).toEqual([]);
});
