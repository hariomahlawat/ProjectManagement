'use strict';

// Invitation cards, UX review 8 (19 Nov): pocket cards found from the start screen, a second
// logo that is never a copy of the first, and a light tile behind a crest on a dark design.

const { test, expect } = require('./test');
const { openFresh, newestDraft } = require('./helpers');

const roles = async (page) => ((await newestDraft(page))?.model?.items || []).filter((item) => item.role);

/** Your own logo… with a plain picture of the given shape. */
async function ownLogo(page, { name = 'crest.png', width = 173, height = 294 } = {}) {
  const png = await page.evaluate(([w, h]) => {
    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    const context = canvas.getContext('2d');
    context.fillStyle = '#1d2c55';
    context.fillRect(0, 0, w, h);
    return canvas.toDataURL('image/png').split(',')[1];
  }, [width, height]);
  await page.locator('[data-card-logo-file]').setInputFiles({ name, mimeType: 'image/png', buffer: Buffer.from(png, 'base64') });
}

/** A word typed, so the draft is saved and the model can be read from it. */
async function touch(page) {
  const venue = page.locator('[data-card-field="venue"]');
  await venue.fill('Sudarshan Hall');
  await venue.blur();
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.length || 0).toBeGreaterThan(0);
}

test('M6: the pocket cards are on the start screen and each opens on its own pocket size', async ({ page }) => {
  const errors = await openFresh(page);
  const row = page.locator('[data-card-pocket-hint]');
  await expect(row).toBeVisible();
  await expect(row.locator('[data-start-pocket]')).toHaveCount(4);
  await expect(row.locator('[data-start-pocket="pass"]')).toContainText('Entry card or car pass');
  // With a pocket size chosen, the designs themselves are the pocket cards: the row goes.
  await page.locator('[data-card-size]').selectOption('a7p');
  await expect(row).toBeHidden();
  await page.locator('[data-card-size]').selectOption('a5p');
  await expect(row).toBeVisible();
  // The RSVP card opens on the RSVP card size, in its own design.
  await row.locator('[data-start-pocket="reply"]').click();
  await page.locator('[data-view="editor"]').waitFor();
  await expect(page.locator('[data-size]')).toHaveValue('in355p');
  await expect(page.locator('[data-card-field="replyTitle"]')).toBeVisible();
  await page.locator('[data-card-field="occasion"]').fill('Raising Day Dinner');
  await page.locator('[data-card-field="occasion"]').blur();
  await expect.poll(async () => (await newestDraft(page))?.model?.card?.design).toBe('reply');
  expect((await newestDraft(page)).model.size).toBe('in355p');
  expect(errors).toEqual([]);
});

test('M4: a second logo beside your own is the SDD logo; the logo on the card cannot be picked for the other', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await ownLogo(page);
  await expect.poll(async () => (await roles(page)).find((item) => item.role === 'logo')?.kind).toBe('photo');
  await page.locator('[data-card-logo="second"]').click();
  await expect.poll(async () => (await roles(page)).find((item) => item.role === 'logo2')?.sticker).toBe('logo:sdd');
  const [first, second] = ['logo', 'logo2'].map(async (role) => (await roles(page)).find((item) => item.role === role));
  const a = await first;
  const b = await second;
  // Side by side, as tall as each other, sharing the middle.
  expect(b.x).toBeGreaterThan(a.x + a.width / 2);
  expect(Math.abs(a.width / a.aspect - b.width / b.aspect)).toBeLessThan(1);
  await expect(page.locator('[data-card-logo-name]')).toHaveText('SDD');
  // The first logo picked: the SDD logo, already the second, cannot be chosen for it.
  await page.locator('[data-card-logo-target="logo"]').click();
  await expect(page.locator('[data-card-logo-use="logo:sdd"]')).toBeDisabled();
  expect(errors).toEqual([]);
});

test('M4: with the SDD logo first, Add a second logo opens the choice; Cancel leaves one logo', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await touch(page);
  await page.locator('[data-card-logo="second"]').click();
  await expect(page.locator('[data-card-logo-pick]')).toContainText('Choose the second logo');
  await expect(page.locator('[data-card-logo-use="logo:sdd"]')).toBeDisabled();
  await expect(page.locator('[data-card-logo="second"]')).toHaveCount(0);
  await page.locator('[data-card-logo="cancel-second"]').click();
  await expect(page.locator('[data-card-logo-pick]')).toHaveCount(0);
  await expect(page.locator('[data-card-logo="second"]')).toBeFocused();
  expect((await roles(page)).filter((item) => /^logo2?$/.test(item.role)).map((item) => item.role)).toEqual(['logo']);
  expect(errors).toEqual([]);
});

test('M3: a dark crest on the navy card gets a light tile behind it, gone again with the SDD logo', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="navy"]').click();
  await touch(page);
  const tile = async () => (await roles(page)).find((item) => item.role === 'logotile');
  expect((await tile()).hidden).toBe(true);
  await ownLogo(page);
  await expect.poll(async () => { const each = await tile(); return Boolean(each) && each.hidden !== true; }).toBe(true);
  const items = (await newestDraft(page)).model.items;
  const logoAt = items.findIndex((item) => item.role === 'logo');
  const tileAt = items.findIndex((item) => item.role === 'logotile');
  expect(tileAt).toBeLessThan(logoAt);
  expect(items[tileAt].width).toBeGreaterThan(items[logoAt].width);
  // The SDD logo again: its gold reads on navy, so the tile hides.
  await page.locator('[data-card-logo-use="logo:sdd"]').click();
  await expect.poll(async () => (await tile())?.hidden).toBe(true);
  // Save does not count the design's hidden tile as an item the user hid.
  await page.getByRole('button', { name: 'Save as…' }).click();
  await expect(page.locator('[data-save-empty]')).not.toContainText('hidden item');
  await page.keyboard.press('Escape');
  expect(errors).toEqual([]);
});

// Codex on #3456: a wide picture as the second logo beside the SDD logo still fits on the page.
test('a wide second logo beside the SDD logo is narrowed so both stay on the page', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-design="classic"]').click();
  await touch(page);
  await page.locator('[data-card-logo="second"]').click();
  await ownLogo(page, { name: 'wide.png', width: 500, height: 100 });
  await expect.poll(async () => (await roles(page)).find((item) => item.role === 'logo2')?.kind).toBe('photo');
  const items = await roles(page);
  for (const role of ['logo', 'logo2']) {
    const item = items.find((each) => each.role === role);
    expect(item.x - item.width / 2, role).toBeGreaterThanOrEqual(0);
    expect(item.x + item.width / 2, role).toBeLessThanOrEqual(1000);
  }
  expect(errors).toEqual([]);
});
