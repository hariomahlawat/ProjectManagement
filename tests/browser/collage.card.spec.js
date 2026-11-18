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
  await expect(designs).toHaveCount(2);
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
