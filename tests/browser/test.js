'use strict';

// The Collage Studio browser tests' own `test`: Playwright's, with a check after every test that
// the page broke none of PRISM's production security rules (docs/ux/collage-review-2-fixes.md,
// PR 12). The test page sends those rules (tests/browser/fixtures/production-headers.json), and
// a page that breaks them in a test would break them in PRISM too.
//
// A violation is caught two ways: the page's own `securitypolicyviolation` events, which an init
// script passes to the test at once with the directive, the blocked address and the script line
// that caused it (so a reload does not lose them), and the browser's console message, which also
// covers what the browser does outside any script.
//
// cspAllow lists violations a test may see, each with its reason where it is used; nothing else
// is let through. A pattern is matched against the console text and against an event's
// "<directive> <blocked address>" line.

const base = require('@playwright/test');

const REPORT = '__prismReportCspViolation';

const test = base.test.extend({
  cspAllow: [[], { option: true }],
  page: async ({ page, cspAllow }, use) => {
    const allowed = (text) => cspAllow.some((pattern) => pattern.test(text));
    const violations = [];
    await page.exposeFunction(REPORT, (event) => {
      const line = `${event.directive} ${event.blocked}`;
      if (!allowed(line)) violations.push(`securitypolicyviolation: ${line} (${event.source || 'no script'})`);
    });
    await page.addInitScript((report) => {
      document.addEventListener('securitypolicyviolation', (event) => {
        window[report]({
          directive: event.effectiveDirective || event.violatedDirective,
          blocked: event.blockedURI,
          source: event.sourceFile ? `${event.sourceFile}:${event.lineNumber}:${event.columnNumber}` : ''
        });
      }, true);
    }, REPORT);
    page.on('console', (message) => {
      const text = message.text();
      if (/Content Security Policy/i.test(text) && !allowed(text)) violations.push(text);
    });
    await use(page);
    base.expect(violations, 'the page broke PRISM\'s Content Security Policy').toEqual([]);
  }
});

module.exports = { test, expect: base.expect };
