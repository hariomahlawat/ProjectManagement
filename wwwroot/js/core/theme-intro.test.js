const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const preferenceScript = fs.readFileSync(path.join(__dirname, 'theme-preference.js'), 'utf8');
const introScript = fs.readFileSync(path.join(__dirname, 'theme-intro.js'), 'utf8');

const card = `
  <section id="pmThemeIntro" hidden>
    <p data-theme-intro-text>Easier on the eyes in low light.</p>
    <div data-theme-intro-ask>
      <button id="tryDark" data-theme-intro-choose="dark"></button>
      <button id="useSystem" data-theme-intro-choose="system"></button>
      <button id="notNow" data-theme-intro-close></button>
    </div>
    <div data-theme-intro-done hidden>
      <button id="keep" data-theme-intro-close data-theme-intro-keep></button>
      <button id="undo" data-theme-intro-undo></button>
    </div>
  </section>
  <button id="menuDark" data-theme-preference-value="dark"></button>
  <input id="field" />`;

async function load({ intro, theme, storageThrows = false, focusField = false, modalOpen = false } = {}) {
  const dom = new JSDOM(`<!doctype html><html><body>${card}${modalOpen ? '<div class="modal show"></div>' : ''}</body></html>`, {
    runScripts: 'outside-only',
    url: 'https://prism.local/',
  });
  const { window } = dom;
  window.matchMedia = () => ({ matches: false, addEventListener() {} });
  if (storageThrows) {
    Object.defineProperty(window, 'localStorage', {
      configurable: true,
      get() { throw new window.DOMException('blocked', 'SecurityError'); },
    });
  } else {
    if (intro !== undefined) window.localStorage.setItem('prism.themeIntro', intro);
    if (theme !== undefined) window.localStorage.setItem('prism.theme', theme);
  }
  window.eval(preferenceScript);
  if (focusField) window.document.getElementById('field').focus();
  window.eval(introScript);
  await new Promise((resolve) => setTimeout(resolve, 1300));
  const doc = window.document;
  return {
    window,
    doc,
    cardEl: doc.getElementById('pmThemeIntro'),
    stored: (key) => window.localStorage.getItem(key),
  };
}

test('a first visit shows the card once the page settles, and counts the showing', async () => {
  const { cardEl, stored, doc } = await load();
  assert.equal(cardEl.hidden, false);
  assert.equal(stored('prism.themeIntro'), '1');
  assert.equal(doc.documentElement.getAttribute('data-bs-theme'), null, 'Light stays until the user chooses');
});

test('Try Dark switches the theme, confirms it and offers Back to Light', async () => {
  const { cardEl, stored, doc } = await load();
  doc.getElementById('tryDark').click();
  assert.equal(doc.documentElement.getAttribute('data-bs-theme'), 'dark');
  assert.equal(stored('prism.theme'), 'dark');
  assert.equal(stored('prism.themeIntro'), 'done');
  assert.equal(doc.querySelector('[data-theme-intro-text]').textContent, 'Dark theme is on.');
  assert.equal(doc.querySelector('[data-theme-intro-done]').hidden, false);
  assert.equal(doc.activeElement.id, 'keep');

  doc.getElementById('undo').click();
  assert.equal(doc.documentElement.getAttribute('data-bs-theme'), null);
  assert.equal(stored('prism.theme'), 'light');
  assert.equal(cardEl.hidden, true);
});

test('Use Windows setting chooses System', async () => {
  const { stored, doc } = await load();
  doc.getElementById('useSystem').click();
  assert.equal(stored('prism.theme'), 'system');
  assert.match(doc.querySelector('[data-theme-intro-text]').textContent, /Windows setting/);
});

test('Not now and Escape close it for good', async () => {
  let page = await load();
  page.doc.getElementById('notNow').click();
  assert.equal(page.cardEl.hidden, true);
  assert.equal(page.stored('prism.themeIntro'), 'done');
  assert.equal(page.stored('prism.theme'), null, 'Not now changes nothing');

  page = await load();
  page.cardEl.dispatchEvent(new page.window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  assert.equal(page.cardEl.hidden, true);
  assert.equal(page.stored('prism.themeIntro'), 'done');
});

test('choosing a theme in the user menu answers the card too', async () => {
  const { cardEl, stored, doc } = await load();
  doc.getElementById('menuDark').click();
  assert.equal(cardEl.hidden, true);
  assert.equal(stored('prism.themeIntro'), 'done');
});

test('never shown again once answered, after three showings, or to someone who used the switch', async () => {
  assert.equal((await load({ intro: 'done' })).cardEl.hidden, true);

  const third = await load({ intro: '2' });
  assert.equal(third.cardEl.hidden, false, 'the third showing still happens');
  assert.equal(third.stored('prism.themeIntro'), '3');
  const fourth = await load({ intro: '3' });
  assert.equal(fourth.cardEl.hidden, true);
  assert.equal(fourth.stored('prism.themeIntro'), 'done');

  const chose = await load({ theme: 'light' });
  assert.equal(chose.cardEl.hidden, true);
  assert.equal(chose.stored('prism.themeIntro'), 'done');
});

test('waits for a later page while a field has focus or a dialog is open', async () => {
  const typing = await load({ focusField: true });
  assert.equal(typing.cardEl.hidden, true);
  assert.equal(typing.stored('prism.themeIntro'), null, 'not counted as shown');

  const dialog = await load({ modalOpen: true });
  assert.equal(dialog.cardEl.hidden, true);
});

test('stays away when the browser cannot remember the answer', async () => {
  const { cardEl } = await load({ storageThrows: true });
  assert.equal(cardEl.hidden, true);
});

test('steps aside while a dropdown is open', async () => {
  const { cardEl, doc, window } = await load();
  doc.dispatchEvent(new window.Event('shown.bs.dropdown'));
  assert.ok(cardEl.classList.contains('is-stepped-aside'));
  doc.dispatchEvent(new window.Event('hidden.bs.dropdown'));
  assert.ok(!cardEl.classList.contains('is-stepped-aside'));
});
