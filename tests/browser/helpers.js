'use strict';

// Shared steps for the Collage Studio browser tests.

/**
 * Opens the page with an empty drafts database, and returns the list that collects page errors
 * and console errors, so every test can end by checking it is still empty.
 */
async function openFresh(page, path = '/Tools/Collage') {
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  page.on('console', (message) => { if (message.type() === 'error') errors.push(message.text()); });
  await page.goto(path);
  await page.evaluate(() => new Promise((resolve) => {
    const request = indexedDB.deleteDatabase('prism-collage-studio');
    request.onsuccess = request.onerror = request.onblocked = () => resolve();
  }));
  await page.reload();
  await page.locator('[data-view="start"]').waitFor();
  return errors;
}

/** A photo made in the page itself (a coloured JPEG), as a file Playwright can choose. */
async function photoFile(page, { width = 1200, height = 900, colour = '#2d6a4f', name = 'photo.jpg' } = {}) {
  const base64 = await page.evaluate(async ({ width, height, colour }) => {
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext('2d');
    context.fillStyle = colour;
    context.fillRect(0, 0, width, height);
    context.fillStyle = '#ffffff';
    context.fillRect(width * 0.1, height * 0.1, width * 0.2, height * 0.2);
    const blob = await new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.9));
    const bytes = new Uint8Array(await blob.arrayBuffer());
    let text = '';
    for (const byte of bytes) text += String.fromCharCode(byte);
    return btoa(text);
  }, { width, height, colour });
  return { name, mimeType: 'image/jpeg', buffer: Buffer.from(base64, 'base64') };
}

/** The collage as stored in the newest draft, read from the browser's drafts database. */
async function newestDraft(page) {
  return page.evaluate(() => new Promise((resolve, reject) => {
    const open = indexedDB.open('prism-collage-studio');
    open.onerror = () => reject(open.error);
    open.onsuccess = () => {
      const db = open.result;
      const all = db.transaction('drafts').objectStore('drafts').getAll();
      all.onsuccess = () => {
        db.close();
        resolve(all.result.sort((a, b) => b.saved - a.saved)[0] || null);
      };
      all.onerror = () => reject(all.error);
    };
  }));
}

/** Opens the Layers tab of the settings panel (docs/ux/collage-review-2-fixes.md, PR 8). */
async function showLayers(page) {
  await page.locator('.clg-props [data-props-tab="layers"]').click();
}

/** Opens the Settings tab of the settings panel. */
async function showSettings(page) {
  await page.locator('.clg-props [data-props-tab="settings"]').click();
}

// PRISM's production security headers, the same the test server sends on every answer.
const productionHeaders = JSON.parse(require('node:fs').readFileSync(require('node:path').join(__dirname, 'fixtures', 'production-headers.json'), 'utf8'));

/**
 * Answers a mocked request (page.route) as PRISM would: PRISM's production security headers, the
 * answer's own headers, and a JSON body (an object) or a raw one (a string or Buffer).
 * Every mocked answer goes through this, so no test passes on an answer PRISM would not send.
 */
function fulfillLikePrism(route, { status = 200, body = '', contentType, headers = {} } = {}) {
  const json = body !== null && typeof body === 'object' && !Buffer.isBuffer(body);
  const type = contentType || (json ? 'application/json; charset=utf-8' : undefined);
  return route.fulfill({
    status,
    headers: { ...productionHeaders, ...(json ? { 'Cache-Control': 'no-store' } : {}), ...(type ? { 'Content-Type': type } : {}), ...headers },
    body: json ? JSON.stringify(body) : body
  });
}

/**
 * Opens one section of the start screen (cards, posters or a collage), as a user does before
 * picking a design, a layout or a blank page (docs/ux/collage-usability-plan.md, PR 2). Waits
 * for the start screen first (after "Back to the start" it comes once the draft is kept), and
 * does nothing more when that section already shows.
 */
async function startOn(page, choice) {
  await page.locator('[data-view="start"]').waitFor();
  if (await page.locator(`[data-start-section="${choice}"]`).isVisible()) return;
  await page.locator(`[data-start-choice="${choice}"]`).click();
}

module.exports = { openFresh, startOn, photoFile, newestDraft, showLayers, showSettings, productionHeaders, fulfillLikePrism };
