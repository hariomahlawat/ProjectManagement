'use strict';

// The start screen's three choices (docs/ux/collage-usability-plan.md, PR 2): cards first, only
// the chosen section shows, and the choice is remembered in this browser.

const { test, expect } = require('./test');
const { openFresh } = require('./helpers');

const section = (page, key) => page.locator(`[data-start-section="${key}"]`);
const choice = (page, key) => page.locator(`[data-start-choice="${key}"]`);

test('a first visit shows the three choices with invitation cards open', async ({ page }) => {
  const errors = await openFresh(page);

  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Make a card, poster or collage');
  await expect(page.locator('[data-start-choice]')).toHaveText([/Invitation card/, /Poster/, /Collage/]);
  await expect(choice(page, 'card')).toHaveAttribute('aria-pressed', 'true');
  await expect(section(page, 'card')).toBeVisible();
  await expect(section(page, 'poster')).toBeHidden();
  await expect(section(page, 'collage')).toBeHidden();
  // The card designs and the size list are there at once.
  await expect(page.locator('[data-start-design="classic"]')).toBeVisible();
  await expect(page.locator('[data-card-size]')).toBeVisible();
  // Pocket cards come after the designs, not before them.
  const designsTop = await page.locator('[data-start-designs]').evaluate((el) => el.getBoundingClientRect().top);
  const pocketsTop = await page.locator('[data-card-pocket-hint]').evaluate((el) => el.getBoundingClientRect().top);
  expect(pocketsTop).toBeGreaterThan(designsTop);

  expect(errors).toEqual([]);
});

test('each choice shows only its own section, and every way in is still there', async ({ page }) => {
  const errors = await openFresh(page);

  await choice(page, 'poster').click();
  await expect(section(page, 'poster')).toBeVisible();
  await expect(section(page, 'card')).toBeHidden();
  await expect(page.locator('[data-start-poster]')).toHaveCount(4);
  await expect(page.locator('[data-poster-size]')).toBeVisible();

  await choice(page, 'collage').click();
  await expect(section(page, 'collage')).toBeVisible();
  await expect(section(page, 'poster')).toBeHidden();
  await expect(choice(page, 'collage')).toHaveAttribute('aria-pressed', 'true');
  await expect(choice(page, 'poster')).toHaveAttribute('aria-pressed', 'false');
  // 11 layouts and 5 row versions (docs/ux/collage-usability-plan.md, round 3, PR 2).
  await expect(page.locator('[data-start-layout]')).toHaveCount(16);
  await expect(page.locator('[data-start-size]')).toHaveCount(7);

  // A layout from the Collage section opens the editor as before.
  await page.getByRole('button', { name: /^2 side by side/ }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();

  expect(errors).toEqual([]);
});

test('the choice is remembered after a reload and after Back to the start', async ({ page }) => {
  const errors = await openFresh(page);

  await choice(page, 'collage').click();
  await page.reload();
  await page.locator('[data-view="start"]').waitFor();
  await expect(section(page, 'collage')).toBeVisible();
  await expect(section(page, 'card')).toBeHidden();

  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await page.getByRole('button', { name: 'Back to the start' }).click();
  await expect(page.locator('[data-view="start"]')).toBeVisible();
  await expect(section(page, 'collage')).toBeVisible();

  expect(errors).toEqual([]);
});

test('the choices work from the keyboard', async ({ page }) => {
  const errors = await openFresh(page);

  await choice(page, 'poster').focus();
  await page.keyboard.press('Enter');
  await expect(section(page, 'poster')).toBeVisible();
  await page.keyboard.press('Tab');
  await expect(choice(page, 'collage')).toBeFocused();
  await page.keyboard.press('Space');
  await expect(section(page, 'collage')).toBeVisible();
  // Focus stays on the choice, so the next Tab goes into its section.
  await expect(choice(page, 'collage')).toBeFocused();

  expect(errors).toEqual([]);
});
