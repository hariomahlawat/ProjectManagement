'use strict';

// A draft made by a newer PRISM keeps what this page does not understand
// (docs/ux/invitations-posters.md, PR 2): parts of kinds it does not draw, an unknown logo sticker and its back page
// are not shown here, the user is told so once, and saving the draft here writes them back.

const { test, expect } = require('./test');
const { openFresh, photoFile } = require('./helpers');

/** Runs fn(record, store) on the one draft record in the drafts database, inside the page. */
function withDraft(page, fn, arg) {
  return page.evaluate(([source, value]) => new Promise((resolve, reject) => {
    const change = new Function('record', 'value', `return (${source})(record, value);`);
    const open = indexedDB.open('prism-collage-studio');
    open.onerror = () => reject(open.error);
    open.onsuccess = () => {
      const db = open.result;
      const tx = db.transaction(['drafts', 'photos'], 'readwrite');
      const drafts = tx.objectStore('drafts');
      const photos = tx.objectStore('photos');
      const all = drafts.getAll();
      const kept = photos.getAll();
      let answer;
      all.onsuccess = () => {
        const [record] = all.result.filter((d) => !d.deletedAt);
        answer = change(record, value);
        if (answer && answer.write) drafts.put(record);
      };
      tx.oncomplete = () => { db.close(); resolve({ answer, photos: kept.result.length }); };
      tx.onerror = () => reject(tx.error);
    };
  }), [fn.toString(), arg]);
}

test('a draft keeps the parts a newer PRISM made when it is edited and saved here', async ({ page }) => {
  test.setTimeout(90000);
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await page.locator('[data-file-input]').setInputFiles([await photoFile(page, { name: 'front.jpg', colour: 'hsl(20, 60%, 40%)' })]);
  await expect(page.locator('[data-photo-count]')).toHaveText('1 of 60 photos');
  await expect(page.locator('[data-draft-status]')).toHaveText('Draft saved in this browser');
  await page.getByRole('button', { name: 'Back to the start' }).click();
  await expect(page.locator('[data-open-draft]')).toHaveCount(1);

  // A newer PRISM's parts, as it would write them: a part of a kind this page does not draw, a logo sticker this page
  // does not know, and a back page that shows the same photo.
  const newer = {
    shape: { id: 'zshape', kind: 'seal', seal: 'rect', x: 10, y: 10, width: 980, aspect: 1.4, stroke: '#a8842f' },
    logo: { id: 'zlogo', kind: 'sticker', sticker: 'logo:sdd', x: 440, y: 30, width: 120, aspect: 0.59 }
  };
  await withDraft(page, (record, parts) => {
    const photo = record.model.items.find((i) => i.kind === 'photo').photo;
    record.model.items.splice(0, 0, parts.shape);
    record.model.items.push(parts.logo);
    record.model.back = { background: { type: 'color', value: '#fbf7ee' }, items: [{ id: 'zback', kind: 'photo', photo, x: 0, y: 0, width: 1000, aspect: 1.4 }] };
    record.model.design = 'classic';
    return { write: true };
  }, newer);

  await page.locator('[data-open-draft]').click();
  await expect(page.locator('[data-view="editor"]')).toBeVisible();
  await expect(page.locator('[data-prism-toast]').filter({ hasText: '4 parts were made by a newer PRISM' })).toBeVisible();
  // Only what this page draws is on the page.
  const kinds = await page.evaluate(() => window.Konva.stages[0].find('.item').map((n) => n.getAttr('kind')));
  expect(kinds).toEqual(['photo']);

  // An edit here, saved to the draft: the newer parts go back in their places, unchanged.
  await page.locator('[data-size]').selectOption('a4p');
  await expect.poll(async () => (await withDraft(page, (record) => record.model.size)).answer).toBe('a4p');
  const { answer: model, photos } = await withDraft(page, (record) => record.model);
  expect(model.items.map((i) => i.id)).toEqual(['zshape', expect.any(String), 'zlogo']);
  expect(model.items[0]).toEqual(newer.shape);
  expect(model.items[2]).toEqual(newer.logo);
  expect(model.design).toBe('classic');
  expect(model.back.items[0].photo).toBe(model.items[1].photo);
  expect(model.kept).toBeUndefined();
  // The photo stays in the draft, used on the page and on the kept back page.
  expect(photos).toBe(1);
  expect(errors).toEqual([]);
});
