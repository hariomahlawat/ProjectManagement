'use strict';

// Phones (docs/ux/collage-review-3-fixes.md, PR 7): the page uses the width, a sheet takes only
// the room it needs, the toolbar is never behind a sheet or over the chosen item, and the handles
// are small but easy to touch.

const { test, expect } = require('./test');
const { openFresh, startOn, photoFile, newestDraft } = require('./helpers');

test.use({ viewport: { width: 390, height: 844 } });

/** The page, the toolbar, the open sheet and the selected item, in window pixels. */
async function measure(page) {
  return page.evaluate(() => {
    const stage = window.Konva.stages[0];
    const pageBox = stage.find('Rect').map((r) => r.getClientRect({ skipShadow: true, skipStroke: true })).sort((a, b) => b.width - a.width)[0];
    const host = stage.container().getBoundingClientRect();
    const toolbar = document.querySelector('[data-toolbar]');
    const sheet = [...document.querySelectorAll('.clg-drawer, .clg-props')].find((e) => getComputedStyle(e).position === 'fixed' && e.offsetHeight > 0);
    const selected = stage.findOne('Transformer')?.nodes()[0]?.getClientRect();
    return {
      page: { x: pageBox.x + host.x, y: pageBox.y + host.y, width: pageBox.width, height: pageBox.height },
      toolbar: toolbar.hidden ? null : toolbar.getBoundingClientRect().toJSON(),
      sheetTop: sheet ? sheet.getBoundingClientRect().top : null,
      item: selected ? { x: selected.x + host.x, y: selected.y + host.y, width: selected.width, height: selected.height } : null,
      anchor: stage.findOne('Transformer')?.findOne('.top-left')?.width()
    };
  });
}

test('at 390 px the page is wide, the toolbar is in view and clear of the chosen item', async ({ page }) => {
  const errors = await openFresh(page);
  await startOn(page, 'collage');
  await page.getByRole('button', { name: /^4 grid/ }).click();
  const files = [];
  for (let i = 0; i < 4; i += 1) files.push(await photoFile(page, { name: `p${i}.jpg`, colour: `hsl(${i * 60}, 55%, 40%)` }));
  // Three at once leave nothing chosen; the fourth, added on its own, stays chosen
  // (docs/ux/collage-usability-plan.md, round 2, PR 5).
  await page.locator('[data-file-input]').setInputFiles(files.slice(0, 3));
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(3);
  await page.locator('[data-file-input]').setInputFiles(files[3]);
  await expect.poll(async () => (await newestDraft(page))?.model.items.filter((i) => i.kind === 'photo').length).toBe(4);

  // With the Photos sheet open (the photo just added is selected).
  await expect.poll(async () => (await measure(page)).toolbar).not.toBeNull();
  let m = await measure(page);
  expect(m.sheetTop).not.toBeNull();
  expect(m.page.width).toBeGreaterThanOrEqual(340);
  expect(m.page.y + m.page.height).toBeLessThanOrEqual(m.sheetTop);
  expect(m.toolbar.bottom).toBeLessThanOrEqual(m.sheetTop);
  const clear = (t, i) => t.bottom <= i.y + 1 || t.top >= i.y + i.height - 1;
  expect(clear(m.toolbar, m.item)).toBe(true);
  // Small handles.
  expect(m.anchor).toBeLessThanOrEqual(14);

  // Sheet closed: the page keeps at least that width.
  await page.locator('.clg-drawer [data-action="close-drawer"]').click();
  await expect.poll(async () => (await measure(page)).sheetTop).toBeNull();
  m = await measure(page);
  expect(m.page.width).toBeGreaterThanOrEqual(340);
  expect(errors).toEqual([]);
});
