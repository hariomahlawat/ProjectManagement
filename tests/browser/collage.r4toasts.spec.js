'use strict';

// PRISM's toasts in the editor (docs/ux/collage-review-4-fixes.md, PR 3): they never cover the
// selected item's toolbar or the phone's bottom bar, an Undo offer goes once its step is undone,
// the editor's toasts do not follow the user to the start screen, and they wait dimmed behind a
// dialog.

const { test, expect } = require('./test');
const { openFresh, photoFile } = require('./helpers');

const live = (page) => page.locator('[data-prism-toast]:not(.is-leaving)');

/** Every visible toast's box and the boxes it must stay clear of. */
async function boxes(page) {
  return page.evaluate(() => {
    const box = (el) => { const r = el.getBoundingClientRect(); return { left: r.left, right: r.right, top: r.top, bottom: r.bottom }; };
    const toasts = [...document.querySelectorAll('[data-prism-toast]:not(.is-leaving)')]
      .filter((el) => getComputedStyle(el).visibility !== 'hidden').map(box);
    const toolbar = document.querySelector('[data-toolbar]');
    const rail = document.querySelector('.clg-rail');
    return {
      toasts,
      toolbar: toolbar && !toolbar.hidden ? box(toolbar) : null,
      rail: rail && rail.getBoundingClientRect().top > window.innerHeight / 2 ? box(rail) : null
    };
  });
}

const overlaps = (a, b) => Boolean(a && b) && a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top;

async function expectClear(page) {
  // The toasts settle (their entry animation) before they are measured.
  await page.waitForTimeout(700);
  const { toasts, toolbar, rail } = await boxes(page);
  expect(toasts.length).toBeGreaterThan(0);
  expect(toolbar).not.toBeNull();
  for (const toast of toasts) {
    expect(overlaps(toast, toolbar), `toast ${JSON.stringify(toast)} covers the toolbar ${JSON.stringify(toolbar)}`).toBe(false);
    expect(overlaps(toast, rail), `toast ${JSON.stringify(toast)} covers the bottom bar ${JSON.stringify(rail)}`).toBe(false);
  }
}

/**
 * A heading at the foot of the page, selected, so its toolbar sits just above the bottom of the
 * screen; adding it shows the note that the collage is named after its words.
 */
async function alignBottom(page) {
  const button = page.locator('[data-align="bottom"]');
  if (!(await button.isVisible())) await page.locator('.clg-props summary').filter({ hasText: 'Line up on the page' }).click();
  await button.click();
}

async function headingAtTheBottom(page) {
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await page.locator('.clg-rail [data-panel="text"]').click();
  await page.locator('[data-add-text="heading"]').click();
  await page.locator('.clg-props [data-text]').fill('Picnic 2026');
  await expect(live(page).filter({ hasText: 'named after its first words' })).toBeVisible();
  // Closing the settings sheet (phones) leaves the page and its toolbar in view.
  const close = page.locator('[data-action="close-props"]');
  await alignBottom(page);
  if (await close.isVisible()) await close.click();
  await expect(page.locator('[data-toolbar]')).toBeVisible();
}

for (const size of [{ width: 1366, height: 768 }, { width: 1920, height: 1080 }]) {
  test.describe(`at ${size.width} wide`, () => {
    test.use({ viewport: size });

    test('a toast never covers the selected item\'s toolbar', async ({ page }) => {
      const errors = await openFresh(page);
      await headingAtTheBottom(page);
      await expectClear(page);
      // A second toast on top of the first: the stack is still clear of it.
      await page.locator('[data-toolbar] [data-action="duplicate"]').click();
      await alignBottom(page);
      await page.evaluate(() => window.PrismToast.show({ tone: 'info', text: 'A second message.' }));
      await expect(page.locator('[data-toolbar]')).toBeVisible();
      await expectClear(page);
      expect(errors).toEqual([]);
    });
  });
}

test.describe('on a 390 px phone', () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true });

  test('a toast covers neither the toolbar nor the bottom bar', async ({ page }) => {
    const errors = await openFresh(page);
    await headingAtTheBottom(page);
    await expectClear(page);
    expect(errors).toEqual([]);
  });
});

test('an Undo offer goes once its step is undone, from the toast, with Ctrl+Z or with the Undo button', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^4 grid/ }).click();
  const files = [];
  for (let i = 0; i < 4; i += 1) files.push(await photoFile(page, { name: `p${i}.jpg`, colour: `hsl(${i * 70}, 55%, 40%)` }));
  await page.locator('[data-file-input]').setInputFiles(files);
  await expect(page.locator('[data-photo-count]')).toHaveText('4 of 60 photos');

  // Make it casual, then Ctrl+Z: its Undo toast goes with the step.
  const casual = live(page).filter({ hasText: 'Photos tilted and scattered' });
  await page.locator('.clg-rail [data-panel="layouts"]').click();
  await page.locator('[data-action="casual"]').filter({ visible: true }).first().click();
  await expect(casual).toBeVisible();
  await page.keyboard.press('Control+z');
  await expect(casual).toHaveCount(0);

  // Again, undone with the Undo button in the top bar.
  await page.locator('[data-action="casual"]').filter({ visible: true }).first().click();
  await expect(casual).toBeVisible();
  await page.locator('[data-action="undo"]').filter({ visible: true }).first().click();
  await expect(casual).toHaveCount(0);

  // A delete's toast, undone from the toast itself.
  await page.locator('.clg-props [data-props-tab="layers"]').click();
  await page.locator('[data-layer-select]').first().click();
  await page.keyboard.press('Delete');
  const taken = live(page).filter({ hasText: 'Photo taken out' });
  await expect(taken).toBeVisible();
  await taken.getByRole('button', { name: 'Undo' }).click();
  await expect(taken).toHaveCount(0);
  expect(errors).toEqual([]);
});

test('the editor\'s toasts close when the user leaves for the start screen, and wait dimmed behind a dialog', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('.clg-rail [data-panel="text"]').click();
  await page.locator('[data-add-text="heading"]').click();
  await page.locator('.clg-props [data-text]').fill('Picnic 2026');
  const named = live(page).filter({ hasText: 'named after its first words' });
  await expect(named).toBeVisible();
  const opacity = () => page.locator('.prism-toasts').evaluate((el) => Number(getComputedStyle(el).opacity));

  // Behind the Save dialog the toasts are dimmed.
  await page.locator('[data-action="save-open"]').filter({ visible: true }).first().click();
  await expect(page.locator('[data-save-dialog]')).toBeVisible();
  await expect.poll(opacity).toBeLessThan(0.5);
  await page.keyboard.press('Escape');
  await expect(page.locator('[data-save-dialog]')).toBeHidden();
  await expect.poll(opacity).toBe(1);

  await page.locator('[data-action="home"]').filter({ visible: true }).first().click();
  await expect(page.locator('[data-view="start"]')).toBeVisible();
  await expect(named).toHaveCount(0);
  expect(errors).toEqual([]);
});
