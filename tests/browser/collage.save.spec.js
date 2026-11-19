'use strict';

// Save as… (docs/ux/collage-review-3-fixes.md, PR 6): what it is for comes first, the type and
// quality are under More options (closed until the user opens it, then remembered), the chosen
// purpose is pressed, Tab stays inside the dialog, and on a phone the dialog fits one screen.

const fs = require('node:fs');
const { test, expect } = require('./test');
const { openFresh, photoFile, startOn } = require('./helpers');

async function startWithPhoto(page) {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'picnic.jpg' }));
  await expect(page.locator('[data-photo-count]')).toHaveText('1 of 60 photos');
  return errors;
}

const openSave = (page) => page.getByRole('button', { name: 'Save as…' }).click();
const closeSave = (page) => page.locator('[data-save-dialog] .clg-dialog__head [data-action="save-close"]').click();

test('More options starts closed, and stays open next time once opened', async ({ page }) => {
  const errors = await startWithPhoto(page);
  const more = page.locator('[data-save-more]');
  await openSave(page);
  await expect(more).not.toHaveAttribute('open', '');
  await expect(page.locator('input[name="format"][value="png"]')).toBeHidden();
  // What Save will make is said with the fold closed.
  await expect(page.locator('[data-save-summary]')).toHaveText(`A JPG picture at screen size: 1,920 × ${await screenHeight(page)} pixels.`);

  await more.locator('summary').click();
  await expect(page.locator('input[name="format"][value="png"]')).toBeVisible();
  await closeSave(page);
  await openSave(page);
  await expect(more).toHaveAttribute('open', '');

  // Closed again, it starts closed again.
  await more.locator('summary').click();
  await closeSave(page);
  await page.reload();
  await page.locator('[data-view="start"]').waitFor();
  expect(await page.evaluate(() => localStorage.getItem('prism-collage-save-more'))).toBe('closed');
  expect(errors).toEqual([]);
});

/** The screen-size height of an A4 landscape collage, as Save works it out. */
async function screenHeight(page) {
  const pixels = await page.evaluate(() => window.PrismCollage.savePixels('a4l', 'screen'));
  return pixels.height.toLocaleString('en-IN');
}

test('the chosen purpose is pressed, and a choice under More options still drives Save', async ({ page }) => {
  const errors = await startWithPhoto(page);
  await openSave(page);
  const pressed = page.locator('[data-save-purpose][aria-pressed="true"]');
  await expect(pressed).toHaveText('WhatsApp or email');
  await page.getByRole('button', { name: 'Printing' }).click();
  await expect(pressed).toHaveText('Printing');
  await expect(page.locator('[data-save-summary]')).toContainText('A PDF at print quality: A4 landscape at its true size');
  // Pressed reads as more than a colour: a tick and a thick edge.
  const look = await page.getByRole('button', { name: 'Printing' }).evaluate((button) => ({
    tick: getComputedStyle(button.querySelector('.bi')).display,
    edge: getComputedStyle(button).boxShadow
  }));
  expect(look.tick).not.toBe('none');
  expect(look.edge).not.toBe('none');
  const unpressedTick = await page.getByRole('button', { name: 'WhatsApp or email' })
    .evaluate((button) => getComputedStyle(button.querySelector('.bi')).display);
  expect(unpressedTick).toBe('none');

  // A PNG at print quality, chosen by hand, is what Save makes; no purpose is pressed.
  await page.locator('[data-save-more] > summary').click();
  await page.locator('input[name="format"][value="png"]').check();
  await expect(pressed).toHaveCount(0);
  await expect(page.locator('[data-save-summary]')).toContainText('A PNG picture at print quality');
  const download = page.waitForEvent('download');
  await page.locator('[data-action="save-go"]').click();
  const file = await download;
  expect(file.suggestedFilename()).toMatch(/\.png$/);
  const bytes = fs.readFileSync(await file.path());
  expect(bytes.subarray(1, 4).toString('latin1')).toBe('PNG');
  // Print quality: the A4 page at 300 dots per inch (the width is in the PNG header).
  expect(bytes.readUInt32BE(16)).toBe(3508);
  expect(errors).toEqual([]);
});

test('Tab stays inside the Save dialog, both ways', async ({ page }) => {
  const errors = await startWithPhoto(page);
  await openSave(page);
  const insideDialog = () => page.evaluate(() => document.querySelector('[data-save-dialog]').contains(document.activeElement));
  const close = page.locator('[data-save-dialog] .clg-dialog__head [data-action="save-close"]');
  const go = page.locator('[data-action="save-go"]');

  await close.focus();
  // Close, three purposes, More options, Close, Save: then back to the first Close.
  for (let step = 0; step < 6; step += 1) {
    await page.keyboard.press('Tab');
    expect(await insideDialog()).toBe(true);
  }
  await expect(go).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(close).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(go).toBeFocused();

  // With More options open, every stop is still inside.
  await page.locator('[data-save-more] > summary').click();
  for (let step = 0; step < 12; step += 1) {
    await page.keyboard.press('Tab');
    expect(await insideDialog()).toBe(true);
  }
  expect(errors).toEqual([]);
});

test('Save makes the right file for each purpose', async ({ page }) => {
  const errors = await startWithPhoto(page);
  const cases = [
    ['WhatsApp or email', /\.jpg$/, (bytes) => bytes[0] === 0xff && bytes[1] === 0xd8],
    ['Printing', /\.pdf$/, (bytes) => bytes.subarray(0, 4).toString('latin1') === '%PDF'],
    ['A slide or a screen', /\.png$/, (bytes) => bytes.subarray(1, 4).toString('latin1') === 'PNG']
  ];
  for (const [purpose, ending, isRight] of cases) {
    await openSave(page);
    await page.getByRole('button', { name: purpose }).click();
    const download = page.waitForEvent('download');
    await page.locator('[data-action="save-go"]').click();
    const file = await download;
    expect(file.suggestedFilename()).toMatch(ending);
    expect(isRight(fs.readFileSync(await file.path()))).toBe(true);
    await closeSave(page);
  }
  expect(errors).toEqual([]);
});

test.describe('on a 390 × 844 phone', () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true });

  test('Save fits one screen with More options closed', async ({ page }) => {
    const errors = await startWithPhoto(page);
    await openSave(page);
    const dialog = page.locator('[data-save-dialog]');
    await expect(dialog).toBeVisible();
    await expect(page.locator('[data-save-more]')).not.toHaveAttribute('open', '');
    const fit = await dialog.evaluate((element) => {
      const box = element.getBoundingClientRect();
      return {
        scrolls: element.scrollHeight > element.clientHeight,
        top: box.top,
        bottom: box.bottom,
        screen: window.innerHeight
      };
    });
    expect(fit.scrolls).toBe(false);
    expect(fit.top).toBeGreaterThanOrEqual(0);
    expect(fit.bottom).toBeLessThanOrEqual(fit.screen);
    await expect(page.locator('[data-action="save-go"]')).toBeInViewport();
    expect(errors).toEqual([]);
  });
});
