'use strict';

// The editor in a real browser where the unit tests cannot reach (docs/ux/collage-review-3-fixes.md,
// PR 12, finding E5): a draft with a photo that cannot be read, a browser that is full, the "Leave
// this collage?" question, a photo dropped on the page or pasted, and Undo after a drag or a resize.

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft, showLayers } = require('./helpers');

async function startBlank(page) {
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
}

async function addHeading(page) {
  await page.locator('.clg-rail [data-panel="text"]').click();
  await page.locator('[data-add-text="heading"]').click();
  await expect(page.locator('.clg-props [data-text]')).toBeVisible();
}

/** The draft's photo records, read from the drafts database. */
async function storedPhotos(page) {
  return page.evaluate(() => new Promise((resolve, reject) => {
    const open = indexedDB.open('prism-collage-studio');
    open.onerror = () => reject(open.error);
    open.onsuccess = () => {
      const db = open.result;
      const all = db.transaction('photos').objectStore('photos').getAll();
      all.onsuccess = () => { db.close(); resolve(all.result.map((p) => ({ id: p.id, name: p.name, size: p.file.size }))); };
      all.onerror = () => reject(all.error);
    };
  }));
}

/** Makes every write to the drafts database fail as a full disk does, or lets writes through again. */
async function fillTheBrowser(page, full = true) {
  await page.evaluate((on) => {
    const proto = IDBObjectStore.prototype;
    if (!proto.__realPut) proto.__realPut = proto.put;
    proto.put = on
      ? function () { throw new DOMException('The quota has been exceeded.', 'QuotaExceededError'); }
      : proto.__realPut;
  }, full);
}

/** The middle of an item on the page, in window pixels. */
async function itemCentre(page, id) {
  return page.evaluate((itemId) => {
    const stage = window.Konva.stages[0];
    const box = stage.findOne(`#${itemId}`).getClientRect();
    const host = stage.container().getBoundingClientRect();
    return { x: host.x + box.x + box.width / 2, y: host.y + box.y + box.height / 2 };
  }, id);
}

/** A JPEG made in the page and dispatched with a drop (at a point) or a paste, as the browser would. */
async function dropOrPaste(page, how, point = null) {
  await page.evaluate(async ({ kind, at }) => {
    const canvas = document.createElement('canvas');
    canvas.width = 900;
    canvas.height = 600;
    const context = canvas.getContext('2d');
    context.fillStyle = '#7b2d26';
    context.fillRect(0, 0, 900, 600);
    const blob = await new Promise((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.9));
    const data = new DataTransfer();
    data.items.add(new File([blob], kind === 'drop' ? 'dropped.jpg' : 'image.png', { type: 'image/jpeg' }));
    if (kind === 'paste') {
      document.body.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
      return;
    }
    const target = document.elementFromPoint(at.x, at.y);
    for (const type of ['dragenter', 'dragover', 'drop']) {
      target.dispatchEvent(new DragEvent(type, { dataTransfer: data, clientX: at.x, clientY: at.y, bubbles: true, cancelable: true }));
    }
  }, { kind: how, at: point });
}

const photoItems = async (page) => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo') || [];

test.describe('a draft photo that cannot be read', () => {
  // Chromium reports a connect-src violation for a blob: picture that fails to decode in an <img>
  // (see collage.flows.spec.js and collage.csp.spec.js); the page makes no such connection.
  test.use({ cspAllow: [/^(?:Connecting to 'blob:|connect-src blob$)/] });

  test('keeps its place and its file through edits, beside a photo that opens', async ({ page }) => {
    await openFresh(page);
    await startBlank(page);
    await page.locator('[data-file-input]').setInputFiles([
      await photoFile(page, { name: 'good.jpg', colour: '#1f6f43' }),
      await photoFile(page, { name: 'damaged.jpg', colour: '#0b2a8a' })
    ]);
    await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');
    await expect.poll(async () => (await storedPhotos(page)).length).toBe(2);

    // The stored copy of one photo is damaged.
    await page.evaluate(() => new Promise((resolve, reject) => {
      const open = indexedDB.open('prism-collage-studio');
      open.onerror = () => reject(open.error);
      open.onsuccess = () => {
        const db = open.result;
        const store = db.transaction('photos', 'readwrite').objectStore('photos');
        const all = store.getAll();
        all.onsuccess = () => {
          const photo = all.result.find((p) => p.name === 'damaged.jpg');
          store.put({ ...photo, file: new File([new Uint8Array([0xff, 0xd8, 0xff, 9, 9, 9, 9])], photo.name, { type: 'image/jpeg' }) });
          store.transaction.oncomplete = () => { db.close(); resolve(); };
        };
      };
    }));

    await page.reload();
    await page.locator('[data-open-draft]').first().click();
    await expect(page.locator('[data-alert-text]')).toContainText('One photo of this draft could not be opened just now and shows grey');
    await expect(page.locator('[data-photo-count]')).toHaveText('2 of 60 photos');

    // Edits go on; the draft keeps both photos on the page and the damaged file as it was, so the
    // photo can come back on a browser that reads it.
    await addHeading(page);
    await page.locator('.clg-props [data-text]').fill('Sports day');
    await expect.poll(async () => (await newestDraft(page))?.model.items.find((i) => i.kind === 'text')?.text).toBe('Sports day');
    expect((await photoItems(page)).length).toBe(2);
    const kept = await storedPhotos(page);
    expect(kept.map((p) => p.name).sort()).toEqual(['damaged.jpg', 'good.jpg']);
    expect(kept.find((p) => p.name === 'damaged.jpg').size).toBe(7);
  });
});

test('a full browser says so in the draft status, and the editor keeps working', async ({ page }) => {
  const errors = await openFresh(page);
  await startBlank(page);
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'team.jpg' }));
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');

  await fillTheBrowser(page);
  await addHeading(page);
  const status = page.locator('[data-draft-status]');
  await expect(status).toHaveText('Draft not saved: no room left');
  await expect(status).toHaveClass(/is-warning/);
  await expect(status).toHaveAttribute('title', /no room left/);

  // Still working: words typed show in Layers, and Undo takes the heading off.
  await page.locator('.clg-props [data-text]').fill('Farewell');
  await showLayers(page);
  await expect(page.locator('.clg-layer__name')).toContainText(['Farewell', 'team.jpg']);
  await page.locator('[data-stage]').click({ position: { x: 5, y: 5 } });
  await page.getByRole('button', { name: 'Undo' }).click();
  await page.getByRole('button', { name: 'Undo' }).click();
  await expect(page.locator('.clg-layer__name')).toHaveCount(1);

  // Room again: the next change is saved, and the status says so.
  await fillTheBrowser(page, false);
  await page.getByRole('button', { name: 'Redo' }).click();
  await expect(status).toHaveText('Draft saved in this browser');
  await expect(status).not.toHaveClass(/is-warning/);
  await expect.poll(async () => (await newestDraft(page))?.model.items.map((i) => i.kind)).toEqual(['photo', 'text']);
  expect(errors).toEqual([]);
});

test('a collage that cannot be kept asks before leaving, and the browser asks before closing', async ({ page }) => {
  const errors = await openFresh(page);
  await startBlank(page);
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'team.jpg' }));
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');

  // Saved: leaving the page asks nothing.
  const asksBeforeUnload = () => page.evaluate(() => {
    const event = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(event);
    return event.defaultPrevented;
  });
  expect(await asksBeforeUnload()).toBe(false);

  await fillTheBrowser(page);
  await addHeading(page);
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft not saved: no room left');
  expect(await asksBeforeUnload()).toBe(true);

  // Back to the start: the question says why, and Keep working stays in the editor.
  await page.getByRole('button', { name: 'Back to the start' }).click();
  const dialog = page.locator('[data-prism-confirm-dialog]');
  await expect(dialog).toContainText('Leave this collage?');
  await expect(dialog).toContainText('this browser has no room left');
  await dialog.getByRole('button', { name: 'Keep working' }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await expect(page.locator('.clg-props [data-text]')).toBeVisible();

  // Asked again, Leave collage goes to the start screen; the last draft kept is still there.
  await page.getByRole('button', { name: 'Back to the start' }).click();
  await dialog.getByRole('button', { name: 'Leave collage' }).click();
  await expect(page.locator('[data-view="start"]')).toBeVisible();
  await expect(page.locator('[data-open-draft]')).toHaveCount(1);
  expect(await asksBeforeUnload()).toBe(false);
  expect(errors).toEqual([]);
});

test('a photo dropped on the page goes where it is dropped, and one dropped on an empty frame fills it', async ({ page }) => {
  const errors = await openFresh(page);
  await startBlank(page);
  const host = await page.locator('[data-stage]').boundingBox();
  const point = { x: host.x + host.width * 0.3, y: host.y + host.height * 0.4 };
  await dropOrPaste(page, 'drop', point);
  await expect(page.locator('[data-photo-count]')).toHaveText('1 of 60 photos');
  await expect.poll(async () => (await photoItems(page)).length).toBe(1);
  const [dropped] = await photoItems(page);
  // The photo's middle is where it was dropped.
  const centre = await itemCentre(page, dropped.id);
  expect(Math.abs(centre.x - point.x)).toBeLessThan(4);
  expect(Math.abs(centre.y - point.y)).toBeLessThan(4);

  // On a layout, a photo dropped on its second empty frame fills that frame.
  await page.getByRole('button', { name: 'Back to the start' }).click();
  await expect(page.locator('[data-view="start"]')).toBeVisible();
  await page.getByRole('button', { name: /^1 big, 2 small/ }).click();
  const slots = await page.evaluate(() => window.Konva.stages[0].find('.item').filter((n) => n.getAttr('kind') === 'slot').map((n) => n.id()));
  expect(slots.length).toBe(3);
  await dropOrPaste(page, 'drop', await itemCentre(page, slots[1]));
  await expect.poll(async () => (await photoItems(page)).map((i) => i.id)).toEqual([slots[1]]);
  expect(errors).toEqual([]);
});

test('a pasted photo is added; a paste with no photo says so', async ({ page }) => {
  const errors = await openFresh(page);
  await startBlank(page);
  await dropOrPaste(page, 'paste');
  await expect(page.locator('[data-photo-count]')).toHaveText('1 of 60 photos');
  await expect.poll(async () => (await photoItems(page)).length).toBe(1);
  await expect(page.locator('[data-announce]')).toHaveText('Photo added.');

  await page.evaluate(() => {
    const data = new DataTransfer();
    data.setData('text/plain', 'just words');
    document.body.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
  });
  await expect(page.locator('[data-alert-text]')).toHaveText('There is no photo on the clipboard. Copy a photo or a screenshot first.');
  expect((await photoItems(page)).length).toBe(1);
  expect(errors).toEqual([]);
});

test('Undo takes back a drag with the mouse, and then a resize by a corner', async ({ page }) => {
  const errors = await openFresh(page);
  await startBlank(page);
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'team.jpg' }));
  await expect.poll(async () => (await photoItems(page)).length).toBe(1);
  const [start] = await photoItems(page);

  // Drag the photo by its middle (Alt: no lining up, so it goes exactly where it is put).
  const from = await itemCentre(page, start.id);
  await page.keyboard.down('Alt');
  await page.mouse.move(from.x, from.y);
  await page.mouse.down();
  await page.mouse.move(from.x + 60, from.y + 40, { steps: 8 });
  await page.mouse.up();
  await page.keyboard.up('Alt');
  await expect.poll(async () => (await photoItems(page))[0].x).toBeGreaterThan(start.x + 50);
  const moved = (await photoItems(page))[0];
  expect(moved.width).toBe(start.width);

  // Resize from the bottom-right corner.
  const corner = await page.evaluate(() => {
    const stage = window.Konva.stages[0];
    const box = stage.findOne('Transformer').findOne('.bottom-right').getClientRect();
    const host = stage.container().getBoundingClientRect();
    return { x: host.x + box.x + box.width / 2, y: host.y + box.y + box.height / 2 };
  });
  await page.mouse.move(corner.x, corner.y);
  await page.mouse.down();
  await page.mouse.move(corner.x + 50, corner.y + 30, { steps: 8 });
  await page.mouse.up();
  await expect.poll(async () => (await photoItems(page))[0].width).toBeGreaterThan(moved.width + 20);

  // Undo: first the resize, then the drag.
  await page.keyboard.press('Control+z');
  await expect.poll(async () => {
    const [item] = await photoItems(page);
    return [Math.round(item.x), Math.round(item.y), Math.round(item.width)];
  }).toEqual([Math.round(moved.x), Math.round(moved.y), Math.round(moved.width)]);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => {
    const [item] = await photoItems(page);
    return [Math.round(item.x), Math.round(item.y), Math.round(item.width)];
  }).toEqual([Math.round(start.x), Math.round(start.y), Math.round(start.width)]);
  expect(errors).toEqual([]);
});
