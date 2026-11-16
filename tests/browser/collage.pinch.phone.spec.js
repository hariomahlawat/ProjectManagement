'use strict';

// A second finger during a move of a photo in its frame cancels the move and zooms the page,
// through edit() like every other change (docs/ux/collage-review-4-fixes.md, PR 9).

const { test, expect } = require('./test');
const { openFresh, photoFile, newestDraft } = require('./helpers');

test('a second finger cancels moving a photo in its frame, with no error, and nothing is kept', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^A4 landscape/ }).click();
  await page.locator('[data-file-input]').setInputFiles(await photoFile(page, { name: 'one.jpg', width: 1600, height: 900 }));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(1);
  await page.locator('.clg-drawer [data-action="close-drawer"]').click().catch(() => {});
  await page.locator('[data-toolbar] [data-action="adjust"]').click();
  await expect(page.locator('[data-toolbar] [data-action="adjust-done"]')).toBeVisible();
  const before = (await newestDraft(page)).model.items[0].crop;
  const box = await page.evaluate(() => {
    const stage = window.Konva.stages[0];
    const node = stage.find((n) => n.getAttr('kind') === 'photo')[0];
    const r = node.getClientRect({ skipShadow: true, skipStroke: true });
    const host = stage.container().getBoundingClientRect();
    return { x: r.x + host.x + r.width / 2, y: r.y + host.y + r.height / 2 };
  });
  const cdp = await page.context().newCDPSession(page);
  const touch = (type, points) => cdp.send('Input.dispatchTouchEvent', { type, touchPoints: points.map(([x, y], id) => ({ x, y, id })) });
  await touch('touchStart', [[box.x, box.y]]);
  await touch('touchMove', [[box.x + 25, box.y]]);
  await touch('touchMove', [[box.x + 50, box.y]]);
  // A second finger: a pinch begins, and the move inside the frame is put back.
  await touch('touchStart', [[box.x + 50, box.y], [box.x - 60, box.y + 40]]);
  await touch('touchEnd', []);
  await page.waitForTimeout(400);
  const after = (await newestDraft(page)).model.items[0].crop;
  expect(after.x).toBeCloseTo(before.x, 5);
  expect(after.y).toBeCloseTo(before.y, 5);
  expect(errors).toEqual([]);
});
