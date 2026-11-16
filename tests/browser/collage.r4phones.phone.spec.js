'use strict';

// Phones (docs/ux/collage-review-4-fixes.md, PR 5): the toolbar is docked where it covers no
// photo, adding words does not open the keyboard over the page, and the Hindi hint fits a phone.

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft } = require('./helpers');

test.use({ viewport: { width: 390, height: 844 } });

test('on a phone the toolbar covers no photo, and a photo under where it used to float can be tapped', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^4 grid/ }).click();
  const files = [];
  for (let i = 0; i < 4; i += 1) files.push(await photoFile(page, { name: `p${i}.jpg`, colour: `hsl(${i * 70}, 55%, 40%)` }));
  await page.locator('[data-file-input]').setInputFiles(files);
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(4);
  const boxes = async () => page.evaluate(() => {
    const stage = window.Konva.stages[0];
    const host = stage.container().getBoundingClientRect();
    const photos = stage.find((n) => n.getAttr('kind') === 'photo').map((n) => {
      const r = n.getClientRect();
      return { x: r.x + host.x, y: r.y + host.y, w: r.width, h: r.height, id: n.id() };
    });
    const tb = document.querySelector('[data-toolbar]');
    return { photos, toolbar: tb.hidden ? null : tb.getBoundingClientRect().toJSON() };
  });
  // With the Photos sheet open (as the reviewer had it), select the bottom-right photo: the
  // toolbar is shown and lies over no photo.
  let m = await boxes();
  const last = m.photos.sort((a, b) => (b.y + b.x) - (a.y + a.x))[0];
  await page.mouse.click(last.x + last.w / 2, last.y + last.h / 2);
  await expect.poll(async () => (await boxes()).toolbar).not.toBeNull();
  m = await boxes();
  for (const p of m.photos) {
    const overlap = Math.min(m.toolbar.right, p.x + p.w) - Math.max(m.toolbar.left, p.x) > 0
      && Math.min(m.toolbar.bottom, p.y + p.h) - Math.max(m.toolbar.top, p.y) > 0;
    expect(overlap, `toolbar over ${p.id}`).toBe(false);
  }
  // The top-left photo can be tapped while the toolbar shows.
  const first = m.photos.sort((a, b) => (a.y + a.x) - (b.y + b.x))[0];
  await page.mouse.click(first.x + 12, first.y + 12);
  await expect.poll(async () => page.evaluate(() => window.Konva.stages[0].findOne('Transformer').nodes()[0]?.id())).toBe(first.id);
  expect(errors).toEqual([]);
});

test('on a phone, adding words does not put the keyboard over the page, and the Hindi hint speaks of the phone', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('.clg-rail [data-panel="text"]').click();
  // The phone's wording shows, the Windows wording does not.
  await expect(page.locator('[data-drawer-panel="text"] .clg-for-touch', { hasText: 'phone' })).toHaveCSS('display', 'inline');
  await expect(page.locator('[data-drawer-panel="text"] .clg-for-mouse', { hasText: 'Windows' })).toHaveCSS('display', 'none');
  await page.locator('[data-add-text="heading"]').click();
  await expect(page.locator('.clg-props [data-text]')).toBeVisible();
  await page.waitForTimeout(300);
  expect(await page.evaluate(() => document.activeElement?.matches('[data-text]'))).toBe(false);
  expect(errors).toEqual([]);
});
