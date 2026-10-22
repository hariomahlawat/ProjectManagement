const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { webcrypto } = require('node:crypto');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'chat.js'), 'utf8');

function load(body = '') {
  const dom = new JSDOM(`<!doctype html><html><body>${body}</body></html>`, {
    runScripts: 'outside-only',
    url: 'https://prism.local/',
  });
  Object.defineProperty(dom.window, 'crypto', { value: webcrypto, configurable: true });
  dom.window.eval(script);
  return dom.window;
}

test('client ids are RFC 4122 v4 and unique without crypto.randomUUID', () => {
  const { PMChatInternals } = load();
  const a = PMChatInternals.newClientId(webcrypto);
  const b = PMChatInternals.newClientId(webcrypto);
  assert.match(a, /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  assert.notEqual(a, b);
});

test('contacts sort unread first, then active, then online, then by name', () => {
  const { PMChatInternals } = load();
  const c = (displayName, extra) => ({ displayName, unreadCount: 0, isActive: true, isOnline: false, ...extra });
  const sorted = PMChatInternals.sortContacts([
    c('Zed'),
    c('Amit', { isActive: false, isOnline: true }),
    c('Bela', { isOnline: true }),
    c('Chen', { unreadCount: 2 }),
    c('arun'),
  ]);
  assert.deepEqual(sorted.map((x) => x.displayName), ['Chen', 'Bela', 'arun', 'Zed', 'Amit']);
});

test('contact search matches name or rank, case-insensitively', () => {
  const { PMChatInternals } = load();
  const list = [{ displayName: 'Ravi Kumar', rank: 'Col' }, { displayName: 'Sita Rao', rank: 'Maj' }];
  assert.deepEqual(PMChatInternals.filterContacts(list, 'kum').map((x) => x.displayName), ['Ravi Kumar']);
  assert.deepEqual(PMChatInternals.filterContacts(list, 'MAJ').map((x) => x.displayName), ['Sita Rao']);
  assert.equal(PMChatInternals.filterContacts(list, '  ').length, 2);
});

test('window capacity leaves room for the buddy list and is one on phones', () => {
  const { PMChatInternals } = load();
  assert.equal(PMChatInternals.windowCapacity(390), 1);
  assert.equal(PMChatInternals.windowCapacity(800), 1);
  assert.equal(PMChatInternals.windowCapacity(1280), 3);
  assert.equal(PMChatInternals.windowCapacity(1920), 5);
});

test('merging replaces a pending message with its saved copy and de-duplicates pushes', () => {
  const { PMChatInternals } = load();
  const pending = { id: 0, clientMessageId: 'c-3', body: 'three' };
  const existing = [{ id: 2, clientMessageId: 'c-2' }, { id: 1, clientMessageId: 'c-1' }, pending];
  const merged = PMChatInternals.mergeMessages(existing, [{ id: 3, clientMessageId: 'c-3', body: 'three' }, { id: 2, clientMessageId: 'c-2' }]);
  assert.deepEqual(Array.from(merged, (m) => m.id), [1, 2, 3]);
});

test('day labels say Today and Yesterday in India time', () => {
  const { PMChatInternals } = load();
  const now = new Date('2026-10-22T10:00:00Z');
  assert.equal(PMChatInternals.dayLabel(new Date('2026-10-22T01:00:00Z'), now), 'Today');
  // 20:00 UTC on the 21st is already the 22nd in IST.
  assert.equal(PMChatInternals.dayLabel(new Date('2026-10-21T20:00:00Z'), now), 'Today');
  assert.equal(PMChatInternals.dayLabel(new Date('2026-10-21T10:00:00Z'), now), 'Yesterday');
  assert.match(PMChatInternals.dayLabel(new Date('2026-10-01T10:00:00Z'), now), /1 Oct 2026/);
});

test('does nothing on pages without the chat root', () => {
  const window = load('<p>Signed out</p>');
  assert.equal(window.PMChat, undefined);
});

// --- Catch-up and read-marker behaviour (Codex review on #2730) ------------------------------

function pageOf(ids, hasMore) {
  return { messages: ids.map((id) => ({ id, clientMessageId: `c-${id}`, senderUserId: 'peer' })), hasMore };
}

test('catch-up pages back until it reaches the newest message already shown', async () => {
  const { PMChatInternals } = load();
  const calls = [];
  // Server holds 1..101; the window already shows message 1.
  const fetchPage = async (before) => {
    calls.push(before);
    const top = before ? before - 1 : 101;
    const ids = [];
    for (let id = Math.max(1, top - 39); id <= top; id += 1) ids.push(id);
    return pageOf(ids, ids[0] > 1);
  };
  const result = await PMChatInternals.fetchSince(fetchPage, 1, 25);
  assert.equal(result.overlaps, true);
  assert.deepEqual(calls, [null, 62, 22]);
  const ids = Array.from(result.messages, (m) => m.id);
  for (let id = 2; id <= 101; id += 1) assert.ok(ids.includes(id), `missing ${id}`);
});

test('catch-up reports no overlap when the page cap runs out', async () => {
  const { PMChatInternals } = load();
  let next = 1000;
  const fetchPage = async () => {
    const ids = [next - 1, next];
    next -= 2;
    return pageOf(ids, true);
  };
  const result = await PMChatInternals.fetchSince(fetchPage, 1, 3);
  assert.equal(result.overlaps, false);
  assert.equal(result.messages.length, 6);
});

function bootApp(handler, extraBody = '') {
  const dom = new JSDOM(`<!doctype html><html><head><meta name="csrf-token" content="t"></head><body>
    <button data-chat-launcher></button>
    ${extraBody}
    <div data-chat-root data-current-user-id="me" data-api-base="/api/chat" data-max-length="4000"></div>
  </body></html>`, { runScripts: 'outside-only', url: 'https://prism.local/', pretendToBeVisual: true });
  const { window } = dom;
  Object.defineProperty(window, 'crypto', { value: webcrypto, configurable: true });
  window.fetch = async (url, init = {}) => {
    const result = await handler(String(url), init);
    const status = result && result.status ? result.status : 200;
    const body = result && 'body' in result ? result.body : result;
    return {
      ok: status >= 200 && status < 300,
      status,
      json: async () => body,
    };
  };
  window.eval(script);
  return window;
}

const tick = () => new Promise((resolve) => setTimeout(resolve, 0));
const contact = { userId: 'peer', displayName: 'Peer Person', rank: null, initials: 'PP', isActive: true, isOnline: true, unreadCount: 0, lastMessageUtc: null };

test('a window popped open by a pushed message still offers earlier history', async () => {
  let releaseHistory;
  const historyGate = new Promise((resolve) => { releaseHistory = resolve; });
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/messages')) {
      await historyGate;
      return pageOf(Array.from({ length: 40 }, (_, i) => i + 11), true);
    }
    return { status: 204, body: null };
  });
  try {
    await tick();
    window.PMChat.onMessage({ id: 60, senderUserId: 'peer', recipientUserId: 'me', clientMessageId: 'c-60', body: 'hi', sentUtc: new Date().toISOString() });
    releaseHistory();
    await tick(); await tick();
    const win = window.PMChat.windows.get('peer');
    assert.equal(win.hasMore, true);
    assert.equal(win.earlier.hidden, false);
  } finally {
    window.close();
  }
});

test('a failed mark-read is retried on the next focus', async () => {
  const reads = [];
  let failNext = true;
  const window = bootApp(async (url, init) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/messages')) return pageOf([5], false);
    if (url.endsWith('/read')) {
      reads.push(JSON.parse(init.body).upToMessageId);
      if (failNext) { failNext = false; return { status: 500, body: {} }; }
      return { status: 204, body: null };
    }
    return { status: 204, body: null };
  });
  try {
    await tick();
    const win = window.PMChat.openWindow('peer', { focus: true });
    await tick(); await tick(); await tick();
    assert.deepEqual(reads, [5]);
    assert.equal(win.lastReadId, 0);
    win.input.blur();
    win.input.focus();
    await tick(); await tick();
    assert.deepEqual(reads, [5, 5]);
    assert.equal(win.lastReadId, 5);
  } finally {
    window.close();
  }
});

test('a message from someone not yet in the contact list refreshes it so they can be answered', async () => {
  let contactCalls = 0;
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) {
      contactCalls += 1;
      return contactCalls === 1 ? [] : [{ ...contact, unreadCount: 1 }];
    }
    if (url.includes('/messages')) return pageOf([], false);
    return { status: 204, body: null };
  });
  try {
    await tick();
    window.PMChat.onMessage({ id: 9, senderUserId: 'peer', recipientUserId: 'me', clientMessageId: 'c-9', body: 'hello', sentUtc: new Date().toISOString() });
    await tick(); await tick(); await tick();
    const win = window.PMChat.windows.get('peer');
    assert.equal(contactCalls, 2);
    assert.equal(win.name.textContent, 'Peer Person');
    assert.equal(win.form.hidden, false);
    assert.equal(window.PMChat.contacts.get('peer').unreadCount, 1);
  } finally {
    window.close();
  }
});

test('a reactivated contact coming online or writing is refreshed so the reply box returns', async () => {
  let active = false;
  let contactCalls = 0;
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) {
      contactCalls += 1;
      return [{ ...contact, isActive: active, isOnline: false }];
    }
    if (url.includes('/messages')) return pageOf([], false);
    return { status: 204, body: null };
  });
  try {
    await tick();
    const win = window.PMChat.openWindow('peer', { focus: false });
    await tick(); await tick();
    assert.equal(win.form.hidden, true);

    active = true;
    window.PMChat.onPresence({ userId: 'peer', isOnline: true });
    await tick(); await tick(); await tick();
    assert.equal(contactCalls, 2);
    assert.equal(win.form.hidden, false);

    // The same holds when the first sign is a message rather than a presence change.
    window.PMChat.contacts.get('peer').isActive = false;
    window.PMChat.onMessage({ id: 3, senderUserId: 'peer', recipientUserId: 'me', clientMessageId: 'c-3', body: 'back', sentUtc: new Date().toISOString() });
    await tick(); await tick(); await tick();
    assert.equal(contactCalls, 3);
    assert.equal(win.form.hidden, false);
  } finally {
    window.close();
  }
});

// --- Phase 2: ticks, typing, away, alerts, Messages page -------------------------------------

function fakeHub(app) {
  const calls = [];
  app.connection = { state: 'Connected', invoke: (method, arg) => { calls.push([method, arg]); return Promise.resolve(); }, stop: () => Promise.resolve() };
  return calls;
}

test('highlightParts marks every case-insensitive match', () => {
  const { PMChatInternals } = load();
  const parts = Array.from(PMChatInternals.highlightParts('TEC minutes; tec again', 'tec'), (p) => `${p.match ? '[' : ''}${p.text}${p.match ? ']' : ''}`);
  assert.deepEqual(parts, ['[TEC]', ' minutes; ', '[tec]', ' again']);
  assert.equal(PMChatInternals.highlightParts('abc', '').length, 1);
});

test('conversation list keeps people with history, most recent first', () => {
  const { PMChatInternals } = load();
  const list = PMChatInternals.conversationList([
    { userId: 'a', lastMessageUtc: '2026-10-20T10:00:00Z' },
    { userId: 'b', lastMessageUtc: null },
    { userId: 'c', lastMessageUtc: '2026-10-22T10:00:00Z' },
  ]);
  assert.deepEqual(Array.from(list, (c) => c.userId), ['c', 'a']);
});

test('away sorts between online and offline', () => {
  const { PMChatInternals } = load();
  const c = (displayName, extra) => ({ displayName, unreadCount: 0, isActive: true, isOnline: false, isAway: false, ...extra });
  const sorted = PMChatInternals.sortContacts([c('Off'), c('Away', { isOnline: true, isAway: true }), c('On', { isOnline: true })]);
  assert.deepEqual(Array.from(sorted, (x) => x.displayName), ['On', 'Away', 'Off']);
});

test('title flasher alternates and restores the page title', () => {
  const { PMChatInternals } = load();
  const doc = { title: 'Dashboard | PRISM ERP' };
  let tickFn = null;
  const flasher = new PMChatInternals.TitleFlasher(doc, (fn) => { tickFn = fn; return 1; }, () => { tickFn = null; });
  flasher.start('Ravi says…');
  assert.equal(doc.title, 'Ravi says…');
  tickFn();
  assert.equal(doc.title, 'Dashboard | PRISM ERP');
  tickFn();
  assert.equal(doc.title, 'Ravi says…');
  flasher.start('Sita says…'); // a second sender keeps the original to restore
  flasher.stop();
  assert.equal(doc.title, 'Dashboard | PRISM ERP');
  assert.equal(flasher.active(), false);
});

test('my messages get one tick when sent and two once the other person has read them', async () => {
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/messages')) {
      return {
        messages: [
          { id: 1, senderUserId: 'me', recipientUserId: 'peer', clientMessageId: 'c-1', body: 'one', sentUtc: new Date().toISOString() },
          { id: 2, senderUserId: 'me', recipientUserId: 'peer', clientMessageId: 'c-2', body: 'two', sentUtc: new Date().toISOString() },
        ],
        hasMore: false,
        peerLastReadMessageId: 1,
      };
    }
    return { status: 204, body: null };
  });
  try {
    await tick();
    const win = window.PMChat.openWindow('peer', { focus: false });
    await tick(); await tick();
    const labels = () => Array.from(win.log.querySelectorAll('.pm-chat__tick'), (t) => t.getAttribute('aria-label'));
    assert.deepEqual(labels(), ['Seen', 'Sent']);
    window.PMChat.onPeerRead({ peerUserId: 'peer', upToMessageId: 2 });
    assert.deepEqual(labels(), ['Seen', 'Seen']);
  } finally {
    window.close();
  }
});

test('typing shows in the window header, is throttled when sent, and clears on the message', async () => {
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/messages')) return pageOf([], false);
    return { status: 204, body: null };
  });
  try {
    await tick();
    const calls = fakeHub(window.PMChat);
    const win = window.PMChat.openWindow('peer', { focus: false });
    await tick();

    window.PMChat.onTyping({ userId: 'peer' });
    assert.equal(win.status.textContent, 'typing…');

    window.PMChat.onMessage({ id: 7, senderUserId: 'peer', recipientUserId: 'me', clientMessageId: 'c-7', body: 'done', sentUtc: new Date().toISOString() });
    assert.notEqual(win.status.textContent, 'typing…');

    win.input.value = 'h';
    win.input.dispatchEvent(new window.Event('input'));
    win.input.value = 'hi';
    win.input.dispatchEvent(new window.Event('input'));
    assert.deepEqual(calls.filter(([m]) => m === 'Typing'), [['Typing', 'peer']]);
  } finally {
    window.close();
  }
});

test('an idle tab reports away and activity brings it back', async () => {
  const window = bootApp(async (url) => (url.endsWith('/contacts') ? [contact] : { status: 204, body: null }));
  try {
    await tick();
    const app = window.PMChat;
    const calls = fakeHub(app);
    app.checkIdle(app.lastActivity + 4 * 60 * 1000);
    assert.equal(app.away, false);
    app.checkIdle(app.lastActivity + 5 * 60 * 1000);
    assert.equal(app.away, true);
    app.noteActivity();
    assert.equal(app.away, false);
    assert.deepEqual(calls, [['SetAway', true], ['SetAway', false]]);

    app.onPresence({ userId: 'peer', isOnline: true, isAway: true });
    assert.equal(app.contacts.get('peer').isAway, true);
  } finally {
    window.close();
  }
});

test('only one tab chimes for a given message', async () => {
  const window = bootApp(async (url) => (url.endsWith('/contacts') ? [contact] : pageOf([], false)));
  try {
    await tick();
    const app = window.PMChat;
    let plays = 0;
    app.chime.play = () => { plays += 1; };
    app.alert({ id: 41, senderUserId: 'peer' }, contact);
    app.alert({ id: 41, senderUserId: 'peer' }, contact); // the same push reaching a second tab
    app.alert({ id: 42, senderUserId: 'peer' }, contact);
    assert.equal(plays, 2);

    app.setSound(false);
    app.alert({ id: 43, senderUserId: 'peer' }, contact);
    assert.equal(plays, 2);
  } finally {
    window.close();
  }
});

test('the Messages page opens a search hit in the middle of a conversation', async () => {
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z' }];
    if (url.includes('/search')) {
      return [{ messageId: 15, peerUserId: 'peer', peerDisplayName: 'Peer Person', senderUserId: 'peer', body: 'the TEC minutes', sentUtc: '2026-10-01T09:00:00Z' }];
    }
    if (url.includes('before=16')) return { ...pageOf([14, 15], true), hasNewer: true };
    if (url.includes('after=15')) return { ...pageOf([16, 17], false), hasNewer: false };
    return pageOf([], false);
  }, '<section data-chat-page></section>');
  try {
    await tick(); await tick();
    const page = window.PMChat.page;
    assert.equal(page.list.querySelectorAll('.pm-chat-page__item').length, 1);

    page.searchInput.value = 'tec';
    await page.runSearch();
    const hit = page.results.querySelector('.pm-chat-page__hit');
    assert.equal(hit.querySelector('mark').textContent, 'TEC');

    hit.click();
    await tick(); await tick();
    const win = page.current;
    assert.equal(win.embedded, true);
    assert.equal(win.highlightId, 15);
    assert.equal(win.newer.hidden, false);
    assert.ok(win.log.querySelector('.is-highlight'));

    // A live message must not be pasted below the gap while an old slice is shown.
    window.PMChat.onMessage({ id: 99, senderUserId: 'peer', recipientUserId: 'me', clientMessageId: 'c-99', body: 'new', sentUtc: new Date().toISOString() });
    assert.deepEqual(Array.from(win.messages, (m) => m.id), [14, 15]);

    win.newer.focus();
    await window.PMChat.loadNewer(win);
    assert.deepEqual(Array.from(win.messages, (m) => m.id), [14, 15, 16, 17]);
    assert.equal(win.newer.hidden, true);
    // The button hid itself; focus stays in the conversation so it still counts as read.
    assert.equal(window.document.activeElement, win.input);
  } finally {
    window.close();
  }
});

test('reading an older slice asks the server for unread instead of zeroing it', async () => {
  let contactCalls = 0;
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) {
      contactCalls += 1;
      return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z', unreadCount: 3 }];
    }
    if (url.includes('before=16')) return { ...pageOf([14, 15], true), hasNewer: true };
    return pageOf([], false);
  }, '<section data-chat-page data-open-with="peer" data-open-at="15"></section>');
  try {
    await tick(); await tick(); await tick();
    const app = window.PMChat;
    const before = contactCalls;
    app.onRead({ peerUserId: 'peer', upToMessageId: 15 });
    await tick(); await tick();
    assert.ok(contactCalls > before);
    assert.equal(app.contacts.get('peer').unreadCount, 3);
  } finally {
    window.close();
  }
});

test('choosing a person already open on the Messages page brings the conversation back on a phone', async () => {
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z' }];
    return pageOf([], false);
  }, '<section data-chat-page data-open-with="peer"></section>');
  try {
    Object.defineProperty(window, 'innerWidth', { value: 375, configurable: true });
    await tick(); await tick();
    const app = window.PMChat;
    const page = app.page;
    page.layout.classList.remove('has-thread'); // "All conversations"
    app.toggleBuddy(true); // "New chat"
    app.openWindow('peer', { focus: true });
    assert.equal(page.layout.classList.contains('has-thread'), true);
    assert.equal(app.buddyOpen, false);
    assert.equal(window.document.activeElement, page.current.input);
  } finally {
    window.close();
  }
});
