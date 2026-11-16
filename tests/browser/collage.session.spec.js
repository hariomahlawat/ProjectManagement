'use strict';

// The edges where the browser harness used to differ from production
// (docs/ux/collage-review-4-fixes.md, PR 14): every mocked answer carries PRISM's production
// headers, a session that ends mid-edit (401 or the sign-in page) and a PRISM failure (500) each
// get a plain message, and the collage the user is making is kept.

const fs = require('node:fs');
const path = require('node:path');
const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft, fulfillLikePrism } = require('./helpers');
const samples = require('./fixtures/collage-api-samples.json');

const SIGNED_OUT = /Your PRISM session has ended\. Sign in again in another tab/;
const UNAVAILABLE = /PRISM Photos cannot be read right now/;
const LOGIN = '/Identity/Account/Login';

// The browser names each failed answer in the console; these tests make them on purpose.
const expectedFailure = (text) => /Failed to load resource: the server responded with a status of (401|500)/.test(text);

/** A collage with one photo from the computer: the work that must be kept. */
async function startWithOnePhoto(page) {
  await page.getByRole('button', { name: /^2 side by side/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'kept.jpg' }));
  await expect(page.locator('[data-photo-count]')).toHaveText('1 of 60 photos');
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((item) => item.kind === 'photo').length).toBe(1);
}

/** The work is still there: the editor is open with its one photo, in the page and in the draft. */
async function expectWorkKept(page) {
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await expect(page.locator('[data-photo-count]')).toHaveText('1 of 60 photos');
  const draft = await newestDraft(page);
  expect(draft.model.items.filter((item) => item.kind === 'photo')).toHaveLength(1);
}

test('every mocked answer in the browser tests carries PRISM\'s production headers', () => {
  // Answers made with route.fulfill go through fulfillLikePrism (helpers.js), never by hand.
  for (const file of fs.readdirSync(__dirname).filter((name) => name.endsWith('.spec.js'))) {
    const source = fs.readFileSync(path.join(__dirname, file), 'utf8');
    expect(source, `${file} answers a request without PRISM's headers`).not.toMatch(/route\.fulfill\(/);
  }
});

test('a mocked answer arrives with PRISM\'s production headers', async ({ page }) => {
  const errors = await openFresh(page);
  await page.route('**/api/collage/library-photos**', (route) => fulfillLikePrism(route, { body: samples.libraryOn }));
  const answer = page.waitForResponse('**/api/collage/library-photos**');
  await page.getByRole('button', { name: /^2 side by side/ }).click();
  await page.getByRole('button', { name: 'From PRISM Photos' }).click();
  const headers = (await answer).headers();
  expect(headers['content-security-policy']).toContain("default-src 'self'");
  expect(headers['cross-origin-resource-policy']).toBe('same-origin');
  expect(headers['x-content-type-options']).toBe('nosniff');
  expect(headers['cache-control']).toBe('no-store');
  expect(errors).toEqual([]);
});

test('PRISM Photos answering 401 mid-edit: the session message, and the collage is kept', async ({ page }) => {
  const errors = await openFresh(page);
  await startWithOnePhoto(page);
  await page.route('**/api/collage/library-photos**', (route) => fulfillLikePrism(route, { status: 401, body: '' }));

  await page.getByRole('button', { name: 'From PRISM Photos' }).click();
  const dialog = page.locator('[data-library-dialog]');
  await expect(dialog.locator('[data-library-status]')).toHaveText(SIGNED_OUT);
  await dialog.locator('[data-library-q]').press('Escape');
  await expectWorkKept(page);
  expect(errors.filter((text) => !expectedFailure(text))).toEqual([]);
});

test('PRISM Photos sending the sign-in page mid-edit: the session message, and the collage is kept', async ({ page }) => {
  const errors = await openFresh(page);
  await startWithOnePhoto(page);

  await page.route('**/api/collage/library-photos**', (route) => fulfillLikePrism(route, {
    status: 302,
    headers: { Location: `${LOGIN}?ReturnUrl=%2Fapi%2Fcollage%2Flibrary-photos` }
  }));

  await page.getByRole('button', { name: 'From PRISM Photos' }).click();
  await expect(page.locator('[data-library-dialog] [data-library-status]')).toHaveText(SIGNED_OUT);
  await page.locator('[data-library-dialog] [data-library-q]').press('Escape');
  await expectWorkKept(page);
  expect(errors.filter((text) => !expectedFailure(text))).toEqual([]);
});

test('PRISM Photos failing with 500: a plain message, and the collage is kept', async ({ page }) => {
  const errors = await openFresh(page);
  await startWithOnePhoto(page);
  await page.route('**/api/collage/library-photos**', (route) => fulfillLikePrism(route, {
    status: 500,
    contentType: 'text/html; charset=utf-8',
    body: '<!doctype html><title>Error</title>'
  }));

  await page.getByRole('button', { name: 'From PRISM Photos' }).click();
  await expect(page.locator('[data-library-dialog] [data-library-status]')).toHaveText(UNAVAILABLE);
  await page.locator('[data-library-dialog] [data-library-q]').press('Escape');
  await expectWorkKept(page);
  expect(errors.filter((text) => !expectedFailure(text))).toEqual([]);
});

test('the session ending while ticked photos are read: the session message, and nothing more is asked for', async ({ page }) => {
  const errors = await openFresh(page);
  await startWithOnePhoto(page);

  await page.route('**/api/collage/library-photos**', (route) => fulfillLikePrism(route, { body: samples.libraryOn }));
  const pictureRequests = [];
  // The session has ended by the time the pictures are read: each picture page sends the sign-in page.
  await page.route('**/Photos/Media/**', (route) => {
    if (/\/original\b/.test(route.request().url())) pictureRequests.push(route.request().url());
    return fulfillLikePrism(route, { status: 302, headers: { Location: `${LOGIN}?ReturnUrl=%2FPhotos` } });
  });

  await page.getByRole('button', { name: 'From PRISM Photos' }).click();
  const dialog = page.locator('[data-library-dialog]');
  await expect(dialog.locator('[data-library-photo]')).toHaveCount(3);
  // Two ticked: the first finds the session ended, and the second is not asked for.
  await dialog.locator('[data-library-photo="1"]').click();
  await dialog.locator('[data-library-photo="2"]').click();
  await dialog.locator('[data-action="library-add"]').click();

  await expect(page.locator('[data-alert-text]')).toHaveText(SIGNED_OUT);
  expect(pictureRequests).toHaveLength(1);
  await expectWorkKept(page);
  expect(errors.filter((text) => !expectedFailure(text))).toEqual([]);
});

test('the crests and logos answering 401: the session message in the Stickers panel, and the collage is kept', async ({ page }) => {
  await page.route('**/api/collage/stickers', (route) => fulfillLikePrism(route, { status: 401, body: '' }));
  const errors = await openFresh(page);
  await startWithOnePhoto(page);

  await page.locator('.clg-rail [data-panel="stickers"]').click();
  await page.locator('[data-sticker-group="crests"]').click();
  await expect(page.getByText(SIGNED_OUT)).toBeVisible();
  // The other stickers still work.
  await page.locator('[data-sticker-group="shapes"]').click();
  await expect(page.locator('[data-add-sticker^="draw:"]').first()).toBeVisible();
  await expectWorkKept(page);
  expect(errors.filter((text) => !expectedFailure(text))).toEqual([]);
});
