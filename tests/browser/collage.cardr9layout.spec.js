'use strict';

// Invitation cards and posters, cold review 9 (layout): words typed in Card details keep the
// place their design gives them, a folded card's inside reads as a whole invitation, a long
// guest's name stays readable, and design parts hidden by the design leave no empty outline.

const { test, expect } = require('./test');
const { openFresh, newestDraft } = require('./helpers');

const LONG_LINE = '1830 h · Guests arrive\n1845 h · Change of Command Parade, Main Parade Ground\n1930 h · Address by the outgoing Commandant\n2000 h · Reception\n2030 h · Dinner';

/** The draft's front and back items, whichever side shows. */
async function sides(page) {
  const model = (await newestDraft(page))?.model;
  if (!model) return { front: [], back: [] };
  const shown = model.items || [];
  const other = model.back?.items || [];
  return model.side === 'back' ? { front: other, back: shown } : { front: shown, back: other };
}

async function type(page, field, words) {
  const input = page.locator(`[data-card-field="${field}"]`);
  await input.fill(words);
  await input.blur();
}

test('H1: a long programme line typed on the back keeps the block centred and inside the border', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-card-size]').selectOption('a6p');
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('[data-card-side="add"]').click();
  await type(page, 'programme', LONG_LINE);
  await expect.poll(async () => (await sides(page)).back.find((item) => item.field === 'programme')?.text).toBe(LONG_LINE);
  const { back } = await sides(page);
  const programme = back.find((item) => item.field === 'programme');
  const border = back.find((item) => item.role === 'backborder');
  expect(Math.abs(programme.x - 500)).toBeLessThan(1);
  expect(programme.x + programme.width / 2).toBeLessThan(border.x + border.width / 2 - 8);
  expect(programme.x - programme.width / 2).toBeGreaterThan(border.x - border.width / 2 + 8);
  expect(errors).toEqual([]);
});

test('M2: a folded card asks for "at the" and a front cover title, and the cover follows the occasion', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-card-size]').selectOption('fold6');
  await page.locator('[data-start-design="classic"]').click();
  await expect(page.locator('[data-card-field="at"]')).toBeVisible();
  const cover = page.locator('[data-card-field="coverTitle"]');
  await expect(cover).toHaveValue('');
  await expect(cover).toHaveAttribute('placeholder', 'Same as the occasion');
  await type(page, 'occasion', 'Barakhana');
  await expect.poll(async () => (await sides(page)).front.find((item) => item.field === 'coverTitle')?.text).toBe('Barakhana');
  // Inside: the occasion ends the invitation, after "at the".
  const { back } = await sides(page);
  const y = (field) => back.find((item) => item.field === field).y;
  expect(y('at')).toBeGreaterThan(y('invite'));
  expect(y('occasion')).toBeGreaterThan(y('at'));
  // Its own words stay; emptied, it shows the occasion again.
  await type(page, 'coverTitle', 'You are invited');
  await type(page, 'occasion', 'At Home');
  await expect.poll(async () => (await sides(page)).back.find((item) => item.field === 'occasion')?.text).toBe('At Home');
  expect((await sides(page)).front.find((item) => item.field === 'coverTitle').text).toBe('You are invited');
  await type(page, 'coverTitle', '');
  await expect.poll(async () => (await sides(page)).front.find((item) => item.field === 'coverTitle')?.text).toBe('At Home');
  await expect(cover).toHaveValue('');
  expect(errors).toEqual([]);
});

test('M5: a long guest name typed in Card details stays readable on two lines, its last line on the line', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-card-size]').selectOption('a6p');
  await page.locator('[data-start-design="classic"]').click();
  await type(page, 'guest', 'Col P. Singh');
  await expect.poll(async () => (await sides(page)).front.find((item) => item.field === 'guest')?.text).toBe('Col P. Singh');
  const short = (await sides(page)).front.find((item) => item.field === 'guest');
  const bottom = short.y + short.width / short.aspect / 2;
  await type(page, 'guest', 'Lt Gen A. K. Singh, PVSM, AVSM, VSM, ADC and Mrs Sunita Singh');
  await expect.poll(async () => (await sides(page)).front.find((item) => item.field === 'guest')?.maxLines).toBe(2);
  const long = (await sides(page)).front.find((item) => item.field === 'guest');
  expect(long.size).toBeGreaterThanOrEqual(Math.round(short.size * 0.7));
  expect(Math.abs(long.y + long.width / long.aspect / 2 - bottom)).toBeLessThan(1);
  expect(errors).toEqual([]);
});

test('L5 and L6: one Your own logo button while a poster asks; no empty outline round the Navy logo', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-start-poster="notice"]').click();
  await page.locator('[data-view="editor"]').waitFor();
  await expect(page.locator('[data-card-logo="own"]')).toHaveCount(1);
  await page.locator('[data-card-logo="later"]').click();
  await expect(page.locator('[data-card-logo="own"]')).toHaveCount(1);
  await page.goto('/Tools/Collage');
  await page.locator('[data-view="start"]').waitFor();
  await page.locator('[data-start-design="navy"]').click();
  await page.locator('[data-view="editor"]').waitFor();
  await type(page, 'venue', 'Sudarshan Hall');
  await expect.poll(async () => (await sides(page)).front.find((item) => item.role === 'logotile')?.autoHidden).toBe(true);
  const outlines = await page.evaluate(() => window.Konva.stages[0].find('.hidden-outline').filter((shape) => shape.isVisible()).length);
  expect(outlines).toBe(0);
  expect(errors).toEqual([]);
});

// Codex on #3460: the occasion cleared and typed again: the cover, still following, follows it.
test('the cover title follows the occasion through clearing it and typing a new one', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-card-size]').selectOption('fold6');
  await page.locator('[data-start-design="classic"]').click();
  await type(page, 'occasion', 'Barakhana');
  await expect.poll(async () => (await sides(page)).front.find((item) => item.field === 'coverTitle')?.text).toBe('Barakhana');
  await type(page, 'occasion', '');
  await expect.poll(async () => (await sides(page)).front.some((item) => item.field === 'coverTitle')).toBe(false);
  await type(page, 'occasion', 'At Home');
  await expect.poll(async () => (await sides(page)).front.find((item) => item.field === 'coverTitle')?.text).toBe('At Home');
  await expect(page.locator('[data-card-field="coverTitle"]')).toHaveValue('');
  expect(errors).toEqual([]);
});

// Codex on #3460: the occasion typed in its Words box on the page (inside) takes the cover with it.
test('the occasion typed on the page in its Words box takes the following cover title with it', async ({ page }) => {
  const errors = await openFresh(page);
  await page.locator('[data-card-size]').selectOption('fold6');
  await page.locator('[data-start-design="classic"]').click();
  await type(page, 'occasion', 'Barakhana');
  await expect.poll(async () => (await sides(page)).front.find((item) => item.field === 'coverTitle')?.text).toBe('Barakhana');
  await page.locator('[data-card-side="back"]').click();
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  await page.locator('[data-layer-select]').filter({ hasText: 'Barakhana' }).click();
  await page.locator('.clg-props [data-props-tab="settings"]').click();
  await page.locator('[data-text]').fill('Regimental Dinner');
  await expect.poll(async () => (await sides(page)).back.find((item) => item.field === 'occasion')?.text).toBe('Regimental Dinner');
  await expect.poll(async () => (await sides(page)).front.find((item) => item.field === 'coverTitle')?.text).toBe('Regimental Dinner');
  expect(errors).toEqual([]);
});
