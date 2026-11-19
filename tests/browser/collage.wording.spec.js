'use strict';

// Round 2, PR 5 of docs/ux/collage-usability-plan.md: a poster's help speaks of Poster details,
// Tips lead with what is open, and after a batch of photos nothing is left chosen.

const { test, expect } = require('./test');
const { openFresh, startOn, photoFile, newestDraft } = require('./helpers');

test('a poster\'s line of help speaks of Poster details', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'poster');
  await page.locator('[data-start-poster="notice"]').click();
  await expect(page.locator('.clg-props [data-help-line]')).toHaveText('Type the words in Poster details, or click anything on the poster to move or restyle it.', { useInnerText: true });
  expect(errors).toEqual([]);
});

test('Tips lead with photos on a collage, and with cards and posters on a card', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('.clg-props').getByRole('button', { name: 'Tips' }).click();
  const headings = page.locator('[data-help-dialog] .clg-help__heading');
  await expect(headings.first()).toHaveText('Photos and the page');
  await page.keyboard.press('Escape');

  await page.getByRole('button', { name: 'Back to the start' }).click();
  await startOn(page, 'card');
  await page.locator('[data-start-design="classic"]').click();
  await page.locator('.clg-props').getByRole('button', { name: 'Tips' }).click();
  await expect(headings.first()).toHaveText('Cards and posters');
  expect(errors).toEqual([]);
});

test('after a batch of photos nothing is chosen; one photo added stays chosen', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^4 grid/ }).click();
  await page.locator('[data-file-input]').setInputFiles([
    await photoFile(page, { name: 'a.jpg' }),
    await photoFile(page, { name: 'b.jpg', colour: '#7a3b2e' }),
    await photoFile(page, { name: 'c.jpg', colour: '#2e4a7a' })
  ]);
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.filter((item) => item.kind === 'photo').length).toBe(3);
  await expect(page.locator('[data-toolbar]')).toBeHidden();
  await expect(page.locator('.clg-props [data-help-line]')).toBeVisible();

  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'd.jpg', colour: '#5a7a2e' }));
  await expect.poll(async () => (await newestDraft(page))?.model?.items?.filter((item) => item.kind === 'photo').length).toBe(4);
  await expect(page.locator('[data-toolbar]')).toBeVisible();
  expect(errors).toEqual([]);
});
