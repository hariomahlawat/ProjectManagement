'use strict';

// The Collage Studio browser tests' own `test`: Playwright's, with a check after every test that
// the page broke none of PRISM's production security rules (docs/ux/collage-review-2-fixes.md,
// PR 12). The test page sends those rules (tests/browser/fixtures/production-headers.json), and
// a page that breaks them in a test would break them in PRISM too.
//
// cspAllow lists messages a test may see, each with its reason where it is used; nothing else is
// let through.

const base = require('@playwright/test');

const test = base.test.extend({
  cspAllow: [[], { option: true }],
  page: async ({ page, cspAllow }, use) => {
    const violations = [];
    page.on('console', (message) => {
      const text = message.text();
      if (/Content Security Policy/i.test(text) && !cspAllow.some((allowed) => allowed.test(text))) violations.push(text);
    });
    await use(page);
    base.expect(violations, 'the page broke PRISM\'s Content Security Policy').toEqual([]);
  }
});

module.exports = { test, expect: base.expect };
