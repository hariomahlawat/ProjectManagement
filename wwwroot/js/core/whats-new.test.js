const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const preferenceScript = fs.readFileSync(path.join(__dirname, 'theme-preference.js'), 'utf8');
const whatsNewScript = fs.readFileSync(path.join(__dirname, 'whats-new.js'), 'utf8');

// As Pages/Shared/_WhatsNew.cshtml renders them: Chat first (only when chat is on), then the theme.
const chatCard = `
  <section id="chatCard" hidden
           data-whats-new="prism.whatsNew.chat"
           data-whats-new-skip-if-stored="prism.chat.dock.me"
           data-whats-new-answered-by="[data-chat-launcher]">
    <button id="showMe" data-whats-new-open-chat></button>
    <button id="chatNotNow" data-whats-new-close></button>
  </section>`;

const themeCard = `
  <section id="themeCard" hidden
           data-whats-new="prism.themeIntro"
           data-whats-new-skip-if-stored="prism.theme"
           data-whats-new-answered-by="[data-theme-preference-value]">
    <p data-whats-new-text>Easier on the eyes in low light.</p>
    <div data-whats-new-ask>
      <button id="tryDark" data-whats-new-theme="dark" data-whats-new-confirm="Dark theme is on."></button>
      <button id="useSystem" data-whats-new-theme="system" data-whats-new-confirm="PRISM now follows your Windows setting (System)."></button>
      <button id="notNow" data-whats-new-close></button>
    </div>
    <div data-whats-new-done hidden>
      <button id="keep" data-whats-new-close data-whats-new-keep></button>
      <button id="undo" data-whats-new-undo></button>
    </div>
  </section>`;

const pageControls = `
  <button id="menuDark" data-theme-preference-value="dark"></button>
  <button id="launcher" data-chat-launcher></button>
  <input id="field" />`;

async function load({ chat = false, stored = {}, storageThrows = false, focusField = false, modalOpen = false, beforeShow } = {}) {
  const body = `${chat ? chatCard : ''}${themeCard}${pageControls}${modalOpen ? '<div class="modal show"></div>' : ''}`;
  const dom = new JSDOM(`<!doctype html><html><body>${body}</body></html>`, {
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
    Object.entries(stored).forEach(([key, value]) => window.localStorage.setItem(key, value));
  }
  window.eval(preferenceScript);
  if (focusField) window.document.getElementById('field').focus();
  window.eval(whatsNewScript);
  if (beforeShow) beforeShow(window);
  await new Promise((resolve) => setTimeout(resolve, 1300));
  const doc = window.document;
  return {
    window,
    doc,
    cardEl: doc.getElementById('themeCard'),
    chatEl: doc.getElementById('chatCard'),
    stored: (key) => window.localStorage.getItem(key),
  };
}

// ----- the theme card ------------------------------------------------------------

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
  assert.equal(doc.querySelector('[data-whats-new-text]').textContent, 'Dark theme is on.');
  assert.equal(doc.querySelector('[data-whats-new-done]').hidden, false);
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
  assert.match(doc.querySelector('[data-whats-new-text]').textContent, /Windows setting/);
});

test('Not now and Escape close it for good', async () => {
  let page = await load();
  page.doc.getElementById('notNow').click();
  assert.equal(page.cardEl.hidden, true);
  assert.equal(page.stored('prism.themeIntro'), 'done');
  assert.equal(page.stored('prism.theme'), null, 'Not now changes nothing');

  // Focus stays on the page when the card appears; Escape there closes it.
  page = await load();
  assert.equal(page.doc.activeElement, page.doc.body);
  page.doc.body.dispatchEvent(new page.window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  assert.equal(page.cardEl.hidden, true);
  assert.equal(page.stored('prism.themeIntro'), 'done');

  // From inside the card too.
  page = await load();
  page.doc.getElementById('tryDark').focus();
  page.doc.activeElement.dispatchEvent(new page.window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  assert.equal(page.cardEl.hidden, true);

  // Escape in a form field belongs to the field.
  page = await load();
  page.doc.getElementById('field').focus();
  page.doc.activeElement.dispatchEvent(new page.window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  assert.equal(page.cardEl.hidden, false);
});

test('choosing a theme in the user menu answers the card too', async () => {
  const { cardEl, stored, doc } = await load();
  doc.getElementById('menuDark').click();
  assert.equal(cardEl.hidden, true);
  assert.equal(stored('prism.themeIntro'), 'done');
});

test('a theme chosen in the profile menu before the card appears keeps it away', async () => {
  const { cardEl, stored } = await load({
    beforeShow: (window) => window.document.getElementById('menuDark').click(),
  });
  assert.equal(cardEl.hidden, true);
  assert.equal(stored('prism.themeIntro'), 'done');
  assert.equal(stored('prism.theme'), 'dark');
});

test('never shown again once answered, after three showings, or to someone who used the switch', async () => {
  assert.equal((await load({ stored: { 'prism.themeIntro': 'done' } })).cardEl.hidden, true);

  const third = await load({ stored: { 'prism.themeIntro': '2' } });
  assert.equal(third.cardEl.hidden, false, 'the third showing still happens');
  assert.equal(third.stored('prism.themeIntro'), '3');
  const fourth = await load({ stored: { 'prism.themeIntro': '3' } });
  assert.equal(fourth.cardEl.hidden, true);
  assert.equal(fourth.stored('prism.themeIntro'), 'done');

  const chose = await load({ stored: { 'prism.theme': 'light' } });
  assert.equal(chose.cardEl.hidden, true);
  assert.equal(chose.stored('prism.themeIntro'), 'done');
});

test('waits while the chat contact list is open, since it sits in the same corner', async () => {
  const page = await load({ beforeShow: (window) => {
    const root = window.document.createElement('div');
    root.setAttribute('data-chat-root', '');
    root.className = 'has-buddy';
    window.document.body.append(root);
  } });
  assert.equal(page.cardEl.hidden, true);
  assert.equal(page.stored('prism.themeIntro'), null);
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

// ----- the chat card and the queue ------------------------------------------------

test('Chat comes first; the theme card waits until Chat is answered', async () => {
  const first = await load({ chat: true });
  assert.equal(first.chatEl.hidden, false);
  assert.equal(first.cardEl.hidden, true, 'one card at a time');
  assert.equal(first.stored('prism.themeIntro'), null, 'the theme card is not counted while it waits');

  first.doc.getElementById('chatNotNow').click();
  assert.equal(first.stored('prism.whatsNew.chat'), 'done');

  assert.ok(Number(first.stored('prism.whatsNew.lastAnswered')) > 0);

  // The next page, the same day: nothing new.
  const sameDay = await load({ chat: true, stored: { 'prism.whatsNew.chat': 'done', 'prism.whatsNew.lastAnswered': String(Date.now()) } });
  assert.equal(sameDay.chatEl.hidden, true);
  assert.equal(sameDay.cardEl.hidden, true, 'one introduction a day');

  // A day later: the theme card takes its turn.
  const dayLater = String(Date.now() - 21 * 60 * 60 * 1000);
  const next = await load({ chat: true, stored: { 'prism.whatsNew.chat': 'done', 'prism.whatsNew.lastAnswered': dayLater } });
  assert.equal(next.chatEl.hidden, true);
  assert.equal(next.cardEl.hidden, false, 'the theme card follows on a later day');
});

test('Show me opens the contact list and points at the chat button', async () => {
  let opened = null;
  const page = await load({
    chat: true,
    beforeShow: (window) => { window.PMChat = { toggleBuddy: (force) => { opened = force; } }; },
  });
  page.doc.getElementById('showMe').click();
  assert.equal(opened, true);
  assert.equal(page.chatEl.hidden, true);
  assert.equal(page.stored('prism.whatsNew.chat'), 'done');
  assert.ok(page.doc.getElementById('launcher').classList.contains('is-pointed'));
});

test('someone who has already used chat, or opens it now, is not told about it', async () => {
  // Chat writes its dock state the first time someone opens it.
  const used = await load({ chat: true, stored: { 'prism.chat.dock.me': '{"buddyOpen":true}' } });
  assert.equal(used.chatEl.hidden, true);
  assert.equal(used.stored('prism.whatsNew.chat'), 'done');
  assert.equal(used.cardEl.hidden, false, 'the next card takes its turn');

  const now = await load({ chat: true });
  now.doc.getElementById('launcher').click();
  assert.equal(now.chatEl.hidden, true);
  assert.equal(now.stored('prism.whatsNew.chat'), 'done');
});
