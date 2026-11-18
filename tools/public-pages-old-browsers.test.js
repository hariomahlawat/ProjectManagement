'use strict';
// The sign-in page, the landing page and the status pages must stay usable in the older
// Chromium/Edge builds found on offline LAN machines (Nov 2026: an un-updated Edge showed the
// sign-in page with no card, borderless fields and an invisible Sign in button).
//
// - :has() (Chromium 105+): a browser without it drops the whole rule, so these stylesheets
//   select on the body classes the layout writes from ViewData["BodyClass"] instead.
// - color-mix() (Chromium 111+): a var() value using it becomes invalid when drawn and the
//   property resets (transparent background, no border). Each such background/border rule needs
//   a token-only fallback in an @supports not (color-mix) block, and so does each glass token.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { readRules } = require('./lib/css-rules');

const ROOT = path.resolve(__dirname, '..');
const read = (file) => fs.readFileSync(path.join(ROOT, file), 'utf8');

const SCENE_STYLESHEETS = [
  'wwwroot/css/landing.css',
  'wwwroot/css/auth-login.css',
  'wwwroot/css/public-shell.css',
  'wwwroot/css/status-page.css',
  'wwwroot/css/pages/developer.css',
];
// The sign-in and landing pages: every colour mix that would blank a surface has a fallback.
const FALLBACK_STYLESHEETS = ['wwwroot/css/landing.css', 'wwwroot/css/auth-login.css', 'wwwroot/css/public-shell.css'];
const SCENE_PAGES = [
  'Pages/Index.cshtml',
  'Pages/Error.cshtml',
  'Pages/Developer.cshtml',
  'Areas/Identity/Pages/Account/Login.cshtml',
  'Areas/Identity/Pages/Account/AccessDenied.cshtml',
  'Areas/Identity/Pages/Account/Logout.cshtml',
];
const NO_COLOR_MIX = /^@supports not \(color: color-mix\(/;
// Properties that leave a surface blank or edgeless when they reset.
const SURFACE = /^(background|border)(-|$)/;

test('the public scene stylesheets do not use :has()', () => {
  for (const file of SCENE_STYLESHEETS) {
    const css = read(file).replace(/\/\*[\s\S]*?\*\//g, '');
    assert.ok(!css.includes(':has('), `${file} uses :has(); select on the layout's body scene class instead.`);
  }
});

test('every page in the public scene sets its body scene class, and the layout writes it', () => {
  for (const file of SCENE_PAGES) {
    const source = read(file);
    assert.match(source, /<div class="lp[ "]/, `${file} is no longer a public scene page; update this list.`);
    assert.match(source, /ViewData\["BodyClass"\]\s*=[^;]*pm-scene-lp/, `${file} must set ViewData["BodyClass"] with pm-scene-lp.`);
  }
  const layout = read('Pages/Shared/_Layout.cshtml');
  assert.match(layout, /var sceneClass = ViewData\["BodyClass"\]/);
  assert.match(layout, /<body class="[^"]*@sceneClass"/);
  assert.match(layout, /<html lang="en" class="@sceneClass">/);
});

test('every colour mix that would blank a surface on the sign-in and landing pages has a fallback', () => {
  // The glass tokens are defined in landing.css and used by all three files.
  const tokenRules = readRules(read('wwwroot/css/landing.css'));
  const fallbackTokens = new Set(tokenRules
    .filter((rule) => rule.atRules.some((at) => NO_COLOR_MIX.test(at)))
    .flatMap((rule) => rule.declarations.map((d) => d.property).filter((p) => p.startsWith('--'))));
  const mixedTokens = tokenRules
    .filter((rule) => rule.atRules.length === 0)
    .flatMap((rule) => rule.declarations)
    .filter((d) => d.property.startsWith('--') && d.value.includes('color-mix'))
    .map((d) => d.property);
  assert.ok(mixedTokens.length > 0, 'expected the glass tokens to be found');
  for (const token of mixedTokens) assert.ok(fallbackTokens.has(token), `${token} uses color-mix() but has no fallback value.`);

  // A block hidden in a fallback (the aurora light, hidden in landing.css) covers its modifiers,
  // such as .lp-aurora--auth in auth-login.css.
  const hidden = FALLBACK_STYLESHEETS.flatMap((file) => readRules(read(file))
    .filter((rule) => rule.atRules.some((at) => NO_COLOR_MIX.test(at)))
    .filter((rule) => rule.declarations.some((d) => d.property === 'display' && d.value === 'none'))
    .flatMap((rule) => rule.selector.split(',').map((s) => s.trim())));
  const isHidden = (selector) => hidden.some((h) => selector === h || selector.startsWith(`${h}--`));

  for (const file of FALLBACK_STYLESHEETS) {
    const rules = readRules(read(file));
    const fallbacks = new Set();
    for (const rule of rules.filter((r) => r.atRules.some((at) => NO_COLOR_MIX.test(at)))) {
      for (const selector of rule.selector.split(',').map((s) => s.trim().replace(/\s+/g, ' '))) {
        for (const d of rule.declarations) {
          fallbacks.add(`${selector}|${d.property.split('-')[0]}`);
        }
      }
    }
    for (const rule of rules) {
      if (rule.atRules.some((at) => NO_COLOR_MIX.test(at) || /^@keyframes|prefers-reduced-transparency/.test(at))) continue;
      for (const d of rule.declarations) {
        if (!SURFACE.test(d.property) || !d.value.includes('color-mix') || !d.value.includes('var(')) continue;
        for (const selector of rule.selector.split(',').map((s) => s.trim().replace(/\s+/g, ' '))) {
          assert.ok(isHidden(selector) || fallbacks.has(`${selector}|${d.property.split('-')[0]}`),
            `${file}: "${selector} { ${d.property} }" mixes colours with color-mix() and has no fallback in the @supports not (color-mix) block.`);
        }
      }
    }
  }
});
