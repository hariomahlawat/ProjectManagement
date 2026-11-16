'use strict';

// The policy check every browser test ends with (tests/browser/test.js), checked itself, and the
// one browser behaviour a test is allowed (docs/ux/collage-review-3-fixes.md, PR 14).

const { test, expect } = require('./test');

test.describe('the policy check', () => {
  // The check runs after the test body, so a violation fails the test there. This test passes
  // only if that happens.
  test.fail();

  test('fails a test whose page breaks the policy', async ({ page }) => {
    await page.goto('/Tools/Collage');
    // connect-src is 'self': the browser blocks this before any network request is made.
    await page.evaluate(() => fetch('https://example.invalid/blocked').catch(() => null));
    await expect.poll(() => page.evaluate(() => document.readyState)).toBe('complete');
  });
});

test.describe('a picture the browser cannot decode', () => {
  // Chromium reports a connect-src violation for a blob: picture that fails to decode in an
  // <img>, with no script behind it, even though img-src allows blob:. The page itself makes no
  // such connection. Collage Studio meets this when a draft's photo is damaged
  // (collage.flows.spec.js); production sends the same policy, so it shows the same console line
  // there, and nothing is blocked that the page needed: the picture had already failed.
  // One pattern: the console line and the event's "<directive> <blocked>" line.
  test.use({ cspAllow: [/^(?:Connecting to 'blob:|connect-src blob$)/] });

  test('reports connect-src blob only when the picture fails, and nothing for a good one', async ({ page }) => {
    await page.goto('/Tools/Collage');
    const result = await page.evaluate(async () => {
      const seen = [];
      document.addEventListener('securitypolicyviolation', (event) => seen.push({ directive: event.effectiveDirective, blocked: event.blockedURI, source: event.sourceFile }));
      const load = async (bytes, type) => {
        const address = URL.createObjectURL(new Blob([bytes], { type }));
        const before = seen.length;
        const outcome = await new Promise((resolve) => {
          const image = new Image();
          image.onload = () => resolve('loaded');
          image.onerror = () => resolve('failed');
          image.src = address;
        });
        await new Promise((resolve) => setTimeout(resolve, 200));
        URL.revokeObjectURL(address);
        return { outcome, reports: seen.slice(before) };
      };
      const canvas = document.createElement('canvas');
      canvas.width = 4;
      canvas.height = 4;
      const good = new Uint8Array(await (await new Promise((resolve) => canvas.toBlob(resolve, 'image/png'))).arrayBuffer());
      return {
        good: await load(good, 'image/png'),
        damaged: await load(new Uint8Array([0xff, 0xd8, 0xff, 1, 2, 3]), 'image/jpeg')
      };
    });

    expect(result.good).toEqual({ outcome: 'loaded', reports: [] });
    expect(result.damaged.outcome).toBe('failed');
    // Every report is this one, from the browser and not from a script.
    for (const report of result.damaged.reports) expect(report).toEqual({ directive: 'connect-src', blocked: 'blob', source: '' });
  });
});
