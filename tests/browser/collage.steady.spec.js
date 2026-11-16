'use strict';

// The page stays still after photos are added (a fix after the fourth review): on a full-screen
// window with real scrollbars, fitting the page could make the work surface scroll, the
// scrollbars took room, and the page was fitted again, many times a second.

const { test, expect } = require('./test');
const { openFresh, photoFile } = require('./helpers');

// Real scrollbars, as in Edge and Chrome on Windows (the test browser hides them by default).
test.use({ launchOptions: { ignoreDefaultArgs: ['--hide-scrollbars'] }, viewport: { width: 1920, height: 945 } });

test('with "3 columns" and tall photos on a full-screen window, the page settles and stays still', async ({ page }) => {
  const errors = await openFresh(page);
  await page.getByRole('button', { name: /^3 columns/ }).click();
  // The shapes of the photos that showed the problem (camera photos, mostly tall).
  const sizes = [[1920, 2460], [1920, 2481], [1920, 1239], [1920, 2954], [1920, 2518], [1920, 1509]];
  const files = [];
  for (const [i, [width, height]] of sizes.entries()) files.push(await photoFile(page, { name: `p${i}.jpg`, width, height, colour: `hsl(${i * 60}, 45%, 45%)` }));
  await page.locator('[data-file-input]').setInputFiles(files);
  await expect(page.locator('[data-photo-count]')).toHaveText('6 of 60 photos');
  await page.waitForTimeout(800);
  const changes = await page.evaluate(() => new Promise((resolve) => {
    const box = document.querySelector('[data-stage]');
    const stage = window.Konva.stages[0];
    const seen = [];
    const tick = () => {
      seen.push([stage.width(), stage.height(), box.clientWidth, box.clientHeight].join(','));
      if (seen.length < 60) requestAnimationFrame(tick);
      else resolve(seen.filter((s, i) => i && s !== seen[i - 1]).length);
    };
    requestAnimationFrame(tick);
  }));
  expect(changes, 'the work surface changed size while nothing was done').toBe(0);
  expect(errors).toEqual([]);
});
