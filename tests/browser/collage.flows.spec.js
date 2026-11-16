'use strict';

// The main Collage Studio flows in a real browser (docs/ux/collage-review-fixes.md, PR 11).

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft } = require('./helpers');

async function startBlank(page) {
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
}

async function addHeading(page) {
  await page.locator('.clg-rail [data-panel="text"]').click();
  await page.locator('[data-add-text="heading"]').click();
  await expect(page.locator('.clg-props [data-text]')).toBeVisible();
}

test('a draft reopens after a reload with its photo and the words typed', async ({ page }) => {
  const errors = await openFresh(page);
  await startBlank(page);
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'team.jpg' }));
  await expect(page.locator('[data-photo-count]')).toHaveText('1 of 60 photos');
  await addHeading(page);
  await page.locator('.clg-props [data-text]').fill('Farewell Col Rao');
  // Typed words are kept in the draft without waiting for the field to lose focus.
  await expect.poll(async () => (await newestDraft(page))?.model.items.find((item) => item.kind === 'text')?.text).toBe('Farewell Col Rao');

  await page.reload();
  const card = page.locator('[data-open-draft]').first();
  await expect(card).toContainText('Farewell Col Rao');
  await card.click();
  await expect(page.locator('[data-photo-count]')).toHaveText('1 of 60 photos');
  await expect(page.locator('.clg-layer__name')).toContainText(['Farewell Col Rao', 'team.jpg']);
  expect(errors).toEqual([]);
});

test.describe('a damaged draft photo', () => {
  // Chromium itself reports "Connecting to blob:" when it fails to load a damaged picture from
  // the drafts database; the page makes no such request (only image loads, which are allowed).
  test.use({ cspAllow: [/^Connecting to 'blob:/] });

  test('a draft whose photo cannot be opened keeps it as a grey stand-in, and Save says so', async ({ page }) => {
    await openFresh(page);
    await startBlank(page);
    await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'damaged.jpg' }));
    await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');

    // The stored photo is damaged, as can happen when a browser runs out of memory or storage.
    await page.evaluate(() => new Promise((resolve, reject) => {
      const open = indexedDB.open('prism-collage-studio');
      open.onerror = () => reject(open.error);
      open.onsuccess = () => {
        const db = open.result;
        const store = db.transaction('photos', 'readwrite').objectStore('photos');
        const all = store.getAll();
        all.onsuccess = () => {
          const photo = all.result[0];
          store.put({ ...photo, file: new File([new Uint8Array([0xff, 0xd8, 0xff, 1, 2, 3])], photo.name, { type: 'image/jpeg' }) });
          store.transaction.oncomplete = () => { db.close(); resolve(); };
        };
      };
    }));

    await page.reload();
    await page.locator('[data-open-draft]').first().click();
    await expect(page.locator('[data-alert-text]')).toContainText('could not be opened just now and shows grey');
    await addHeading(page);
    await expect.poll(async () => (await newestDraft(page))?.model.items.map((item) => item.kind)).toEqual(['photo', 'text']);

    await page.getByRole('button', { name: 'Save as…' }).click();
    const download = page.waitForEvent('download');
    await page.locator('[data-action="save-go"]').click();
    await download;
    await expect(page.locator('[data-save-result]')).toContainText('One photo shown grey could not be opened');
  });
});

test('keyboard only: Enter presses the focused button, focus stays in Settings, Undo takes back one step', async ({ page }) => {
  const errors = await openFresh(page);
  await startBlank(page);
  await addHeading(page);
  const bold = page.locator('.clg-props [data-text-toggle="bold"]');
  const before = await bold.getAttribute('aria-pressed');
  await bold.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('.clg-props [data-text-toggle="bold"]')).not.toHaveAttribute('aria-pressed', before);
  // The panel was drawn again, and focus is on the new Bold button, not lost to the page.
  await expect(page.locator('.clg-props [data-text-toggle="bold"]')).toBeFocused();

  const lock = page.locator('.clg-props [data-action="lock"]');
  await lock.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('.clg-props [data-action="lock"]')).toBeFocused();
  await expect(page.locator('.clg-props [data-text-set="font"]').first()).toBeDisabled();

  await page.locator('body').click({ position: { x: 5, y: 5 } }).catch(() => {});
  await page.keyboard.press('Control+z');
  await expect(page.locator('.clg-props [data-action="lock"]')).toHaveAttribute('aria-pressed', 'false');
  expect(errors).toEqual([]);
});

test('Save by purpose: WhatsApp makes a JPG, printing makes a PDF', async ({ page }) => {
  await openFresh(page);
  await startBlank(page);
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'picnic.jpg' }));
  await expect(page.locator('[data-photo-count]')).toHaveText('1 of 60 photos');

  for (const [purpose, ending] of [['WhatsApp or email', /\.jpg$/], ['Printing', /\.pdf$/]]) {
    await page.getByRole('button', { name: 'Save as…' }).click();
    await page.getByRole('button', { name: purpose }).click();
    const download = page.waitForEvent('download');
    await page.locator('[data-action="save-go"]').click();
    expect((await download).suggestedFilename()).toMatch(ending);
    await page.locator('[data-action="save-close"]').first().click();
  }
});

test('a wide photo goes into the wide frame of "1 big, 2 small"', async ({ page }) => {
  await openFresh(page);
  await page.getByRole('button', { name: /^1 big, 2 small/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { width: 1600, height: 1000, name: 'wide.jpg' }));
  await expect.poll(async () => {
    const draft = await newestDraft(page);
    const photo = draft?.model.items.find((item) => item.kind === 'photo');
    return photo ? photo.aspect > 1 : null;
  }).toBe(true);
});

test('the Dark theme colours the editor panels', async ({ page }) => {
  await openFresh(page);
  await page.evaluate(() => document.documentElement.setAttribute('data-bs-theme', 'dark'));
  await startBlank(page);
  const background = await page.locator('.clg-props').evaluate((element) => getComputedStyle(element).backgroundColor);
  const [r, g, b] = background.match(/\d+/g).map(Number);
  expect(r + g + b).toBeLessThan(3 * 100);
});

test('a draft can be deleted and brought back with Undo', async ({ page }) => {
  const errors = await openFresh(page);
  await startBlank(page);
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'keep.jpg' }));
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');
  await page.getByRole('button', { name: 'Back to the start' }).click();
  await expect(page.locator('[data-open-draft]')).toHaveCount(1);

  // Delete may ask first; accept the PRISM confirmation if it does.
  await page.locator('[data-delete-draft]').first().click();
  const confirm = page.locator('.prism-confirm-modal.show [data-confirm-accept], .prism-confirm-modal.show .btn-danger, .prism-confirm-modal.show .btn-primary');
  if (await confirm.count()) await confirm.first().click();
  await expect(page.locator('[data-open-draft]')).toHaveCount(0);
  await expect(page.locator('[data-draft-undo]')).toBeVisible();

  await page.locator('[data-action="draft-undo"]').click();
  await expect(page.locator('[data-open-draft]')).toHaveCount(1);
  expect(errors).toEqual([]);
});

test('From PRISM Photos: ticked photos are read in and fill the empty frames', async ({ page }) => {
  const picture = (await photoFile(page, { width: 1200, height: 900 })).buffer;
  // PRISM's own answer, kept equal to the API by CollageBrowserFixtureTests.
  const samples = JSON.parse(require('node:fs').readFileSync(require('node:path').join(__dirname, 'fixtures', 'collage-api-samples.json'), 'utf8'));
  await page.route('**/api/collage/library-photos**', (route) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(samples.libraryOn) }));

  await page.route('**/Photos/Media/**', (route) => route.fulfill({ contentType: 'image/jpeg', body: picture }));

  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^2 side by side/ }).click();
  await page.getByRole('button', { name: 'From PRISM Photos' }).click();
  const dialog = page.locator('[data-library-dialog]');
  await expect(dialog.locator('[data-library-photo]')).toHaveCount(3);
  await expect(dialog.locator('[data-library-project] option')).toHaveCount(2);
  await dialog.locator('[data-library-photo="1"]').click();
  await dialog.locator('[data-library-photo="2"]').click();
  await expect(dialog.locator('[data-library-chosen]')).toHaveText('2 ticked');
  await dialog.locator('[data-action="library-add"]').click();
  await expect(page.locator('[data-photo-count]')).toHaveText('2 of 60 photos');
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((item) => item.kind === 'photo').length).toBe(2);
  expect(errors).toEqual([]);
});

test('typing and then at once clicking Bold are two Undo steps', async ({ page }) => {
  const errors = await openFresh(page);
  await startBlank(page);
  await addHeading(page);
  const bold = page.locator('.clg-props [data-text-toggle="bold"]');
  const before = (await bold.getAttribute('aria-pressed')) === 'true';
  await page.locator('.clg-props [data-text]').fill('Welcome home');
  // Bold straight after typing, well inside the pause that would end the typing on its own.
  await bold.click();
  await expect(page.locator('.clg-props [data-text-toggle="bold"]')).toHaveAttribute('aria-pressed', String(!before));
  await page.locator('body').click({ position: { x: 5, y: 5 } }).catch(() => {});
  await page.keyboard.press('Control+z');
  // Undo takes back Bold only; the words typed stay.
  await expect.poll(async () => (await newestDraft(page))?.model.items.find((item) => item.kind === 'text'))
    .toMatchObject({ text: 'Welcome home', bold: before });
  expect(errors).toEqual([]);
});
