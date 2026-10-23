const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { webcrypto } = require('node:crypto');
const { JSDOM } = require('jsdom');

// The chat scripts, in the order the layout loads them (Pages/Shared/_ChatScripts.cshtml).
const chatScripts = Array.from(
  fs.readFileSync(path.join(__dirname, '..', '..', 'Pages', 'Shared', '_ChatScripts.cshtml'), 'utf8')
    .matchAll(/src="~\/js\/(chat\/[\w-]+\.js)"/g),
  (m) => m[1]);
const script = chatScripts.map((file) => fs.readFileSync(path.join(__dirname, file), 'utf8')).join(';\n');

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
      headers: { get: (name) => (result && result.headers ? result.headers[name] || null : null) },
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
    // Caught up: the held live message joins after the fetched page.
    assert.deepEqual(Array.from(win.messages, (m) => m.id), [14, 15, 16, 17, 99]);
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

test('a live message arriving while the last newer page loads is kept', async () => {
  let releaseNewer;
  const newerGate = new Promise((resolve) => { releaseNewer = resolve; });
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z' }];
    if (url.includes('before=16')) return { ...pageOf([14, 15], true), hasNewer: true };
    if (url.includes('after=15')) {
      await newerGate; // the server read the page before message 17 was saved
      return { ...pageOf([16], false), hasNewer: false };
    }
    return pageOf([], false);
  }, '<section data-chat-page data-open-with="peer" data-open-at="15"></section>');
  try {
    await tick(); await tick(); await tick();
    const app = window.PMChat;
    const win = app.page.current;
    const loading = app.loadNewer(win);
    app.onMessage({ id: 17, senderUserId: 'peer', recipientUserId: 'me', clientMessageId: 'c-17', body: 'live', sentUtc: new Date().toISOString() });
    releaseNewer();
    await loading;
    assert.deepEqual(Array.from(win.messages, (m) => m.id), [14, 15, 16, 17]);
    assert.equal(win.heldLive.length, 0);
  } finally {
    window.close();
  }
});

test('a live message arriving while a search hit loads is kept', async () => {
  let releaseHit;
  const hitGate = new Promise((resolve) => { releaseHit = resolve; });
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z' }];
    if (url.includes('before=16')) {
      await hitGate; // the server read the slice before message 16 was saved
      return { ...pageOf([14, 15], true), hasNewer: false };
    }
    return pageOf([], false);
  }, '<section data-chat-page data-open-with="peer" data-open-at="15"></section>');
  try {
    await tick(); await tick();
    const app = window.PMChat;
    app.onMessage({ id: 16, senderUserId: 'peer', recipientUserId: 'me', clientMessageId: 'c-16', body: 'live', sentUtc: new Date().toISOString() });
    releaseHit();
    await tick(); await tick(); await tick();
    const win = app.page.current;
    assert.deepEqual(Array.from(win.messages, (m) => m.id), [14, 15, 16]);
    assert.equal(win.hasNewer, false);
    assert.equal(win.heldLive.length, 0);
  } finally {
    window.close();
  }
});

test('a reply sent while a search hit is still loading stays, and the latest page loads', async () => {
  let releaseHit;
  const hitGate = new Promise((resolve) => { releaseHit = resolve; });
  const window = bootApp(async (url, init) => {
    if (url.endsWith('/contacts')) return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z' }];
    if (url.includes('before=16')) {
      await hitGate;
      return { ...pageOf([14, 15], true), hasNewer: true };
    }
    if (url.endsWith('/messages') && init.method === 'POST') {
      const body = JSON.parse(init.body);
      // Wrapped: a chat message has its own "body" field, which the fake fetch would unwrap.
      return { status: 200, body: { id: 100, senderUserId: 'me', recipientUserId: 'peer', clientMessageId: body.clientMessageId, body: body.body, sentUtc: new Date().toISOString() } };
    }
    if (url.includes('/messages?limit=')) return pageOf([98, 99], true);
    return { status: 204, body: null };
  }, '<section data-chat-page data-open-with="peer" data-open-at="15"></section>');
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.page.current;
    win.input.value = 'reply';
    app.send(win);
    await tick(); await tick(); await tick();
    releaseHit(); // the stale slice arrives after the reply was saved
    await tick(); await tick(); await tick();
    const ids = Array.from(win.messages, (m) => m.id);
    assert.ok(ids.includes(100), `reply missing from ${ids}`);
    assert.ok(ids.includes(99), `latest page missing from ${ids}`);
    assert.equal(ids.includes(14), false);
    assert.equal(win.hasNewer, false);
  } finally {
    window.close();
  }
});

test('switching conversations on the Messages page keeps drafts and unsent messages', async () => {
  const other = { ...contact, userId: 'other', displayName: 'Other Person', initials: 'OP', lastMessageUtc: '2026-10-21T09:00:00Z' };
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z' }, other];
    if (url.includes('/messages')) return pageOf([], false);
    return { status: 204, body: null };
  }, '<section data-chat-page data-open-with="peer"></section>');
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const page = app.page;
    const first = page.current;
    first.input.value = 'half-typed';
    first.messages.push({ id: 0, clientMessageId: 'c-x', senderUserId: 'me', recipientUserId: 'peer', body: 'failed one', failed: true });

    page.open('peer', 0); // clicking the open conversation again
    assert.equal(page.current, first);
    assert.equal(first.input.value, 'half-typed');

    page.open('other', 0);
    await tick();
    assert.notEqual(page.current, first);
    assert.equal(first.node.isConnected, false);

    page.open('peer', 0);
    await tick();
    assert.equal(page.current, first);
    assert.equal(first.input.value, 'half-typed');
    assert.ok(first.messages.some((m) => m.failed && m.body === 'failed one'));
  } finally {
    window.close();
  }
});

test('a docked draft moves into the Messages page with its conversation', async () => {
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z' }];
    if (url.includes('/messages')) return pageOf([], false);
    return { status: 204, body: null };
  }, '<section data-chat-page></section>');
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const docked = app.openWindow('peer', { focus: false });
    docked.input.value = 'started in the dock';
    app.page.open('peer', 0);
    assert.equal(app.windows.get('peer').embedded, true);
    assert.equal(app.page.current.input.value, 'started in the dock');
    assert.equal(docked.node.isConnected, false);
  } finally {
    window.close();
  }
});

test('an earlier page still loading for one search hit is dropped when another hit opens', async () => {
  let releaseEarlier;
  const earlierGate = new Promise((resolve) => { releaseEarlier = resolve; });
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z' }];
    if (url.includes('before=151')) return { ...pageOf([111, 150], true), hasNewer: true };
    if (url.includes('before=111')) {
      await earlierGate;
      return pageOf([71, 110], true);
    }
    if (url.includes('before=16')) return { ...pageOf([1, 15], false), hasNewer: true };
    return pageOf([], false);
  }, '<section data-chat-page data-open-with="peer" data-open-at="150"></section>');
  try {
    await tick(); await tick(); await tick();
    const app = window.PMChat;
    const win = app.page.current;
    const earlier = app.loadEarlier(win); // pending for the hit at 150
    app.page.open('peer', 15);
    await tick(); await tick();
    releaseEarlier();
    await earlier;
    assert.deepEqual(Array.from(win.messages, (m) => m.id), [1, 15]);
    assert.equal(win.loading, false);
  } finally {
    window.close();
  }
});

test('paging is blocked while a replacement search slice loads', async () => {
  let releaseHit;
  const hitGate = new Promise((resolve) => { releaseHit = resolve; });
  const requested = [];
  const window = bootApp(async (url) => {
    requested.push(url);
    if (url.endsWith('/contacts')) return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z' }];
    if (url.includes('before=151')) return { ...pageOf([111, 150], true), hasNewer: true };
    if (url.includes('before=16')) {
      await hitGate;
      return { ...pageOf([1, 15], false), hasNewer: true };
    }
    if (url.includes('before=111')) return pageOf([71, 110], true);
    return pageOf([], false);
  }, '<section data-chat-page data-open-with="peer" data-open-at="150"></section>');
  try {
    await tick(); await tick(); await tick();
    const app = window.PMChat;
    const win = app.page.current;
    app.page.open('peer', 15); // replacement slice in flight
    assert.equal(win.earlier.hidden, true);
    assert.equal(win.newer.hidden, true);
    await app.loadEarlier(win); // clicked from the old slice meanwhile
    releaseHit();
    await tick(); await tick(); await tick();
    assert.equal(requested.some((u) => u.includes('before=111')), false);
    assert.deepEqual(Array.from(win.messages, (m) => m.id), [1, 15]);
    assert.equal(win.loading, false);
    assert.equal(win.newer.hidden, false);
  } finally {
    window.close();
  }
});

test('retrying a failed message from a search slice jumps to the latest first', async () => {
  const window = bootApp(async (url, init) => {
    if (url.endsWith('/contacts')) return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z' }];
    if (url.includes('before=16')) return { ...pageOf([14, 15], true), hasNewer: true };
    if (url.endsWith('/messages') && init.method === 'POST') {
      const body = JSON.parse(init.body);
      return { status: 200, body: { id: 101, senderUserId: 'me', recipientUserId: 'peer', clientMessageId: body.clientMessageId, body: body.body, sentUtc: new Date().toISOString() } };
    }
    if (url.includes('/messages?limit=')) return pageOf([99, 100], true);
    return { status: 204, body: null };
  }, '<section data-chat-page data-open-with="peer"></section>');
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.page.current;
    const failed = { id: 0, clientMessageId: 'c-f', senderUserId: 'me', recipientUserId: 'peer', body: 'retry me', failed: true };
    win.messages.push(failed);
    app.page.open('peer', 15);
    await tick(); await tick();
    assert.equal(win.hasNewer, true);
    await app.deliver(win, failed); // the Retry link
    await tick(); await tick(); await tick();
    const ids = Array.from(win.messages, (m) => m.id);
    assert.equal(win.hasNewer, false);
    assert.equal(ids.includes(14), false);
    assert.deepEqual(ids.slice(-3), [99, 100, 101]);
  } finally {
    window.close();
  }
});

test('a send that completes after an older search slice opened does not land below the gap', async () => {
  let releaseSend;
  const sendGate = new Promise((resolve) => { releaseSend = resolve; });
  const requested = [];
  const window = bootApp(async (url, init) => {
    requested.push(url);
    if (url.endsWith('/contacts')) return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z' }];
    if (url.includes('before=16')) return { ...pageOf([14, 15], true), hasNewer: true };
    if (url.endsWith('/messages') && init.method === 'POST') {
      await sendGate;
      const body = JSON.parse(init.body);
      return { status: 200, body: { id: 101, senderUserId: 'me', recipientUserId: 'peer', clientMessageId: body.clientMessageId, body: body.body, sentUtc: new Date().toISOString() } };
    }
    if (url.includes('after=15')) return { ...pageOf([16, 100], false), hasNewer: false };
    if (url.includes('/messages?limit=')) return pageOf([99, 100], true);
    return { status: 204, body: null };
  }, '<section data-chat-page data-open-with="peer"></section>');
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.page.current;
    win.input.value = 'sent before searching';
    app.send(win);
    app.page.open('peer', 15); // opened while the send is still in flight
    await tick(); await tick();
    releaseSend();
    await tick(); await tick(); await tick();
    assert.deepEqual(Array.from(win.messages, (m) => m.id), [14, 15]);
    assert.equal(win.hasNewer, true);

    await app.loadNewer(win);
    assert.ok(requested.some((u) => u.includes('after=15')));
    assert.deepEqual(Array.from(win.messages, (m) => m.id), [14, 15, 16, 100, 101]);
  } finally {
    window.close();
  }
});

test('a send in flight when its conversation moves to the Messages page reports there', async () => {
  let releaseSend;
  const sendGate = new Promise((resolve) => { releaseSend = resolve; });
  const window = bootApp(async (url, init) => {
    if (url.endsWith('/contacts')) return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z' }];
    if (url.endsWith('/messages') && init.method === 'POST') {
      await sendGate;
      return { status: 500, body: { detail: 'Server error' } };
    }
    if (url.includes('/messages')) return pageOf([], false);
    return { status: 204, body: null };
  }, '<section data-chat-page></section>');
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const docked = app.openWindow('peer', { focus: false });
    await tick();
    docked.input.value = 'from the dock';
    app.send(docked);
    app.page.open('peer', 0); // moves the conversation, unsent message included
    await tick();
    releaseSend();
    await tick(); await tick(); await tick();
    const view = app.page.current;
    const retry = view.log.querySelector('.pm-chat__retry');
    assert.ok(retry, 'the visible view should offer Retry');
    assert.equal(view.log.textContent.includes('Sending…'), false);
  } finally {
    window.close();
  }
});

// --- Status messages, unsend, chat buttons next to names ------------------------------------

test('status messages show for others and my own can be set from the contact list', async () => {
  const posted = [];
  const window = bootApp(async (url, init) => {
    if (url.endsWith('/contacts')) return [{ ...contact, statusMessage: 'In review meeting till 4' }];
    if (url.endsWith('/status') && init.method === 'POST') {
      posted.push(JSON.parse(init.body).message);
      return { userId: 'me', message: JSON.parse(init.body).message };
    }
    if (url.endsWith('/status')) return { userId: 'me', message: 'At my desk' };
    return pageOf([], false);
  });
  try {
    await tick(); await tick();
    const app = window.PMChat;
    assert.equal(app.statusInput.value, 'At my desk');
    assert.equal(app.list.querySelector('.pm-chat__contact-note').textContent, 'In review meeting till 4');

    const win = app.openWindow('peer', { focus: false });
    assert.match(win.status.textContent, /In review meeting till 4/);

    app.onStatus({ userId: 'peer', message: null });
    assert.equal(app.list.querySelector('.pm-chat__contact-note'), null);

    app.statusInput.value = '  Out   for lunch ';
    await app.saveMyStatus();
    assert.deepEqual(posted, ['Out for lunch']);
    await app.saveMyStatus(); // unchanged: no second request
    assert.equal(posted.length, 1);
  } finally {
    window.close();
  }
});

test('my recent messages offer Unsend until the window passes, and unsent ones show a placeholder', async () => {
  const now = Date.now();
  const window = bootApp(async (url, init) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/unsend') && init.method === 'POST') {
      return { status: 200, body: { id: 2, senderUserId: 'me', recipientUserId: 'peer', clientMessageId: 'c-2', body: '', sentUtc: new Date(now).toISOString(), isUnsent: true } };
    }
    if (url.includes('/messages')) {
      return {
        messages: [
          { id: 1, senderUserId: 'me', recipientUserId: 'peer', clientMessageId: 'c-1', body: 'old', sentUtc: new Date(now - 10 * 60000).toISOString() },
          { id: 2, senderUserId: 'me', recipientUserId: 'peer', clientMessageId: 'c-2', body: 'fresh', sentUtc: new Date(now).toISOString() },
          { id: 3, senderUserId: 'peer', recipientUserId: 'me', clientMessageId: 'c-3', body: 'theirs', sentUtc: new Date(now).toISOString() },
        ],
        hasMore: false,
      };
    }
    return { status: 204, body: null };
  });
  try {
    await tick();
    const app = window.PMChat;
    app.unsendMs = 5 * 60000;
    const win = app.openWindow('peer', { focus: false });
    await tick(); await tick();
    const links = win.log.querySelectorAll('.pm-chat__unsend');
    assert.equal(links.length, 1, 'only my message from the last five minutes');

    await app.unsend(win, win.messages.find((m) => m.id === 2));
    const unsent = win.log.querySelector('.is-unsent .pm-chat__bubble');
    assert.equal(unsent.textContent, 'You unsent a message');
    assert.equal(win.log.querySelectorAll('.pm-chat__unsend').length, 0);

    app.onUnsent({ id: 3, senderUserId: 'peer', recipientUserId: 'me', clientMessageId: 'c-3', body: '', sentUtc: new Date(now).toISOString(), isUnsent: true });
    const texts = Array.from(win.log.querySelectorAll('.is-unsent .pm-chat__bubble'), (b) => b.textContent);
    assert.deepEqual(texts, ['You unsent a message', 'Message unsent']);
    assert.equal(win.log.textContent.includes('theirs'), false);
  } finally {
    window.close();
  }
});

test('a refused unsend explains why', async () => {
  const window = bootApp(async (url, init) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/unsend') && init.method === 'POST') return { status: 409, body: { detail: 'A message can only be unsent within 5 minutes of sending it.' } };
    if (url.includes('/messages')) return { messages: [{ id: 2, senderUserId: 'me', recipientUserId: 'peer', clientMessageId: 'c-2', body: 'fresh', sentUtc: new Date().toISOString() }], hasMore: false };
    return { status: 204, body: null };
  });
  try {
    await tick();
    const app = window.PMChat;
    app.unsendMs = 5 * 60000;
    const win = app.openWindow('peer', { focus: false });
    await tick(); await tick();
    await app.unsend(win, win.messages[0]);
    assert.match(win.log.querySelector('.pm-chat__unsend-error').textContent, /within 5 minutes/);
  } finally {
    window.close();
  }
});

test('names marked data-chat-user get one chat button, never for myself', async () => {
  const window = bootApp(async (url) => (url.endsWith('/contacts') ? [contact] : pageOf([], false)),
    '<p><span id="po" data-chat-user="peer" data-chat-name="Peer Person">PO Peer Person</span>'
    + '<span id="me" data-chat-user="me">Me</span><span id="none" data-chat-user="">Unassigned</span></p>');
  try {
    await tick();
    const app = window.PMChat;
    const doc = window.document;
    app.attachPersonButtons(doc); // again: must not duplicate
    assert.equal(doc.querySelectorAll('#po .pm-chat-person').length, 1);
    assert.equal(doc.querySelector('#po .pm-chat-person').getAttribute('aria-label'), 'Chat with Peer Person');
    assert.equal(doc.querySelectorAll('#me .pm-chat-person, #none .pm-chat-person').length, 0);

    doc.querySelector('#po .pm-chat-person').click();
    assert.ok(app.windows.get('peer'));
    assert.equal(doc.activeElement, app.windows.get('peer').input);
  } finally {
    window.close();
  }
});

test('an unsend that arrives while history is loading is not undone by the response', async () => {
  let releaseHistory;
  const historyGate = new Promise((resolve) => { releaseHistory = resolve; });
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/messages')) {
      await historyGate; // read on the server before the unsend
      return { messages: [{ id: 7, senderUserId: 'peer', recipientUserId: 'me', clientMessageId: 'c-7', body: 'withdrawn text', sentUtc: new Date().toISOString() }], hasMore: false };
    }
    return { status: 204, body: null };
  });
  try {
    await tick();
    const app = window.PMChat;
    const win = app.openWindow('peer', { focus: false });
    app.onUnsent({ id: 7, senderUserId: 'peer', recipientUserId: 'me', clientMessageId: 'c-7', body: '', sentUtc: new Date().toISOString(), isUnsent: true });
    releaseHistory();
    await tick(); await tick(); await tick();
    assert.equal(win.log.textContent.includes('withdrawn text'), false);
    assert.equal(win.messages[0].isUnsent, true);

    // A stale copy merged later cannot restore it either.
    win.messages = window.PMChatInternals.mergeMessages(win.messages, [{ id: 7, senderUserId: 'peer', recipientUserId: 'me', clientMessageId: 'c-7', body: 'withdrawn text' }]);
    assert.equal(win.messages[0].isUnsent, true);
  } finally {
    window.close();
  }
});

test('unsends missed while offline are applied on reconnect, also to older loaded messages', async () => {
  const asked = [];
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/unsent?since=')) {
      asked.push(url);
      return { ids: [1], hasMore: false };
    }
    if (url.includes('/messages')) return pageOf([1, 2], false);
    return { status: 204, body: null };
  });
  try {
    await tick();
    const app = window.PMChat;
    const win = app.openWindow('peer', { focus: false });
    await tick(); await tick();
    app.markOffline();
    app.resync();
    await tick(); await tick(); await tick();
    assert.equal(asked.length, 1);
    assert.equal(win.messages.find((m) => m.id === 1).isUnsent, true);
    assert.equal(app.offlineSince, null);
  } finally {
    window.close();
  }
});

test('an unsent message leaves the search results, also from a late response', async () => {
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [{ ...contact, lastMessageUtc: '2026-10-22T09:00:00Z' }];
    if (url.includes('/search')) {
      return [
        { messageId: 15, peerUserId: 'peer', peerDisplayName: 'Peer Person', senderUserId: 'peer', body: 'secret minutes', sentUtc: '2026-10-01T09:00:00Z' },
        { messageId: 16, peerUserId: 'peer', peerDisplayName: 'Peer Person', senderUserId: 'peer', body: 'other minutes', sentUtc: '2026-10-01T09:00:00Z' },
      ];
    }
    return pageOf([], false);
  }, '<section data-chat-page></section>');
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const page = app.page;
    page.searchInput.value = 'minutes';
    await page.runSearch();
    assert.equal(page.results.querySelectorAll('.pm-chat-page__hit').length, 2);

    app.onUnsent({ id: 15, senderUserId: 'peer', recipientUserId: 'me', clientMessageId: 'c-15', body: '', sentUtc: new Date().toISOString(), isUnsent: true });
    assert.equal(page.results.textContent.includes('secret minutes'), false);
    assert.equal(page.results.querySelectorAll('.pm-chat-page__hit').length, 1);

    await page.runSearch(); // the server copy still lists it (in flight before the unsend)
    assert.equal(page.results.textContent.includes('secret minutes'), false);
  } finally {
    window.close();
  }
});

test('a failed unsend catch-up after reconnecting is retried until it succeeds', async () => {
  let failures = 1;
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/unsent?since=')) {
      if (failures > 0) { failures -= 1; return { status: 500, body: {} }; }
      return { ids: [1], hasMore: false };
    }
    if (url.includes('/messages')) return pageOf([1, 2], false);
    return { status: 204, body: null };
  });
  try {
    await tick();
    const app = window.PMChat;
    const win = app.openWindow('peer', { focus: false });
    await tick(); await tick();
    app.markOffline();
    await app.syncUnsent(true); // fails
    assert.ok(app.unsentRetry, 'a retry is scheduled');
    assert.notEqual(app.offlineSince, null);
    window.clearTimeout(app.unsentRetry);
    await app.syncUnsent(true); // the retry
    assert.equal(app.unsentRetry, null);
    assert.equal(app.offlineSince, null);
    assert.equal(win.messages.find((m) => m.id === 1).isUnsent, true);
  } finally {
    window.close();
  }
});

test('unsends between the first history load and the first connection are caught up', async () => {
  const asked = [];
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/unsent?since=')) {
      asked.push(url);
      return { ids: [1], hasMore: false };
    }
    if (url.includes('/messages')) return pageOf([1, 2], false);
    return { status: 204, body: null };
  });
  try {
    await tick();
    const app = window.PMChat;
    assert.ok(app.offlineSince, 'tracking starts with the page, before any connection');
    const win = app.openWindow('peer', { focus: false });
    await tick(); await tick();
    app.resync(); // what the first successful connection does
    await tick(); await tick(); await tick();
    assert.equal(asked.length, 1);
    assert.equal(win.messages.find((m) => m.id === 1).isUnsent, true);
    assert.equal(app.offlineSince, null);
  } finally {
    window.close();
  }
});

test('unsend catch-up reads every page before counting itself done', async () => {
  const asked = [];
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/unsent?since=')) {
      asked.push(url);
      return url.includes('before=') ? { ids: [3, 1], hasMore: false } : { ids: [9, 7], hasMore: true };
    }
    if (url.includes('/messages')) return pageOf([1, 2, 3], false);
    return { status: 204, body: null };
  });
  try {
    await tick();
    const app = window.PMChat;
    const win = app.openWindow('peer', { focus: false });
    await tick(); await tick();
    await app.syncUnsent(true);
    assert.equal(asked.length, 2);
    assert.ok(asked[1].includes('before=7'));
    assert.deepEqual(Array.from(win.messages.filter((m) => m.isUnsent), (m) => m.id), [1, 3]);
    assert.equal(app.offlineSince, null);
  } finally {
    window.close();
  }
});

test('unsend catch-up looks back over the whole drop-detection window', async () => {
  const asked = [];
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/unsent?since=')) { asked.push(url); return { ids: [], hasMore: false }; }
    return pageOf([], false);
  });
  try {
    await tick();
    const app = window.PMChat;
    const noticed = new Date('2026-10-22T10:00:00Z');
    app.offlineSince = noticed;
    await app.syncUnsent(true);
    const since = new Date(decodeURIComponent(asked[0].split('since=')[1]));
    assert.ok(noticed.getTime() - since.getTime() >= 150000, 'covers the 90 s timeout plus skew');
  } finally {
    window.close();
  }
});

test('a drop during catch-up keeps the watermark for the next catch-up', async () => {
  let release;
  const gate = new Promise((resolve) => { release = resolve; });
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/unsent?since=')) { await gate; return { ids: [], hasMore: false }; }
    return pageOf([], false);
  });
  try {
    await tick();
    const app = window.PMChat;
    const running = app.syncUnsent(true);
    app.markOffline(); // dropped again while the request was in flight
    release();
    await running;
    assert.notEqual(app.offlineSince, null);
  } finally {
    window.close();
  }
});

// --- Group chats ------------------------------------------------------------------------------

const desk = { id: 5, title: 'TEC desk', managerUserId: 'me', memberCount: 3, unreadCount: 2, lastMessageUtc: '2026-10-22T09:00:00Z' };
const deskMembers = [
  { userId: 'me', displayName: 'Me Myself', initials: 'MM', isActive: true, lastReadMessageId: 12 },
  { userId: 'peer', displayName: 'Peer Person', initials: 'PP', isActive: true, lastReadMessageId: 11 },
  { userId: 'third', displayName: 'Third Officer', initials: 'TO', isActive: true, lastReadMessageId: 10 },
];

function groupMessage(id, sender, body, extra) {
  return { id, senderUserId: sender, recipientUserId: '', clientMessageId: `c-${id}`, body, sentUtc: new Date().toISOString(), groupId: 5, ...extra };
}

function groupApp(extra, extraBody) {
  return bootApp(async (url, init) => {
    if (extra) {
      const answer = await extra(url, init);
      if (answer !== undefined) return answer;
    }
    // Fresh copies: the app mutates what it is given (unread counts), and other tests share these.
    if (url.endsWith('/contacts')) return [{ ...contact, unreadCount: 0 }, { ...contact, userId: 'third', displayName: 'Third Officer', initials: 'TO', unreadCount: 0 }];
    if (url.endsWith('/groups')) return [{ ...desk }];
    if (url.includes('/groups/5/messages')) {
      return {
        groupId: 5,
        messages: [
          groupMessage(10, 'me', '', { eventKind: 'Created', eventData: 'TEC desk' }),
          groupMessage(11, 'peer', 'Minutes attached'),
          groupMessage(12, 'me', 'Thanks'),
        ],
        hasMore: false,
        hasNewer: false,
        members: deskMembers.map((m) => ({ ...m })),
      };
    }
    return { status: 204, body: null };
  }, extraBody);
}

test('groups are listed with their unread count, which joins the launcher total', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const row = app.groupList.querySelector('.pm-chat__contact');
    assert.match(row.textContent, /TEC desk/);
    assert.match(row.textContent, /3 people/);
    assert.equal(app.unreadTotal(), 2);
    assert.equal(window.document.querySelector('[data-chat-launcher]').getAttribute('aria-label'), 'Open chat, 2 unread');
  } finally {
    window.close();
  }
});

test('a group window shows notices, who said what, and "Seen by" ticks', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow(window.PMChatInternals.groupKey(5), { focus: false });
    await tick(); await tick();
    assert.equal(win.name.textContent, 'TEC desk');
    assert.equal(win.log.querySelector('.pm-chat__event').textContent, 'You created the group “TEC desk”');
    assert.equal(win.log.querySelector('.pm-chat__sender').textContent, 'Peer Person');
    const tick12 = win.log.querySelector('.is-mine .pm-chat__tick');
    assert.equal(tick12.getAttribute('aria-label'), 'Sent'); // nobody else has read message 12 yet

    app.onGroupRead({ groupId: 5, userId: 'peer', upToMessageId: 12 });
    assert.equal(win.log.querySelector('.is-mine .pm-chat__tick').getAttribute('aria-label'), 'Seen by Peer Person');
    assert.equal(win.log.querySelector('.is-mine .pm-chat__tick').classList.contains('is-seen'), false);

    app.onGroupRead({ groupId: 5, userId: 'third', upToMessageId: 12 });
    const both = win.log.querySelector('.is-mine .pm-chat__tick');
    assert.equal(both.getAttribute('aria-label'), 'Seen by Peer Person, Third Officer');
    assert.equal(both.classList.contains('is-seen'), true);
  } finally {
    window.close();
  }
});

test('a group message pops the group open and counts as unread; notices do not', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    app.onMessage(groupMessage(20, 'peer', '', { eventKind: 'Added', eventData: 'third' }));
    assert.equal(app.windows.has('g:5'), false, 'a notice does not pop a window');
    assert.equal(app.groups.get(5).unreadCount, 2);

    app.onMessage(groupMessage(21, 'peer', 'Agenda for Friday'));
    assert.ok(app.windows.has('g:5'));
    assert.equal(app.groups.get(5).unreadCount, 3);
  } finally {
    window.close();
  }
});

test('typing in a group names who is typing and signals TypingInGroup', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const calls = fakeHub(app);
    const win = app.openWindow('g:5', { focus: false });
    await tick(); await tick();
    app.onGroupTyping({ groupId: 5, userId: 'peer' });
    assert.equal(win.status.textContent, 'Peer Person is typing…');
    app.onGroupTyping({ groupId: 5, userId: 'third' });
    assert.equal(win.status.textContent, 'Peer Person and Third Officer are typing…');

    win.input.value = 'x';
    win.input.dispatchEvent(new window.Event('input'));
    assert.deepEqual(calls.filter(([m]) => m === 'TypingInGroup'), [['TypingInGroup', 5]]);
  } finally {
    window.close();
  }
});

test('sending in a group posts to the group and shows the saved message', async () => {
  const posts = [];
  const window = groupApp(async (url, init) => {
    if (url.endsWith('/groups/5/messages') && init.method === 'POST') {
      const body = JSON.parse(init.body);
      posts.push(body);
      return { status: 200, body: groupMessage(30, 'me', body.body, { clientMessageId: body.clientMessageId }) };
    }
    return undefined;
  });
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: false });
    await tick(); await tick();
    win.input.value = 'Noted';
    app.send(win);
    await tick(); await tick();
    assert.equal(posts.length, 1);
    assert.equal(posts[0].body, 'Noted');
    assert.equal(posts[0].recipientUserId, undefined);
    assert.ok(win.messages.some((m) => m.id === 30));
  } finally {
    window.close();
  }
});

test('being removed from a group closes its reply box and drops it from the list', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: false });
    await tick(); await tick();
    app.onGroupRemoved(5);
    assert.equal(win.form.hidden, true);
    assert.match(win.notice.textContent, /no longer in this group/);
    assert.equal(app.groupList.children.length, 0);
    assert.equal(app.unreadTotal(), 0);
  } finally {
    window.close();
  }
});

test('a 410 from a group means the user was removed meanwhile', async () => {
  const window = groupApp(async (url, init) => {
    if (url.endsWith('/groups/5/messages') && init.method === 'POST') return { status: 410, body: { detail: 'You are not in this group.' } };
    return undefined;
  });
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: false });
    await tick(); await tick();
    win.input.value = 'Hello?';
    app.send(win);
    await tick(); await tick(); await tick();
    assert.equal(app.groups.has(5), false);
    assert.equal(win.form.hidden, true);
  } finally {
    window.close();
  }
});

test('the manager sees Remove and Rename; leaving needs a second click', async () => {
  const actions = [];
  const window = groupApp(async (url, init) => {
    if (url.includes('/groups/5/') && init.method === 'POST' && !url.endsWith('/read') && !url.endsWith('/messages')) {
      actions.push(url.replace(/^.*\/groups\/5/, ''));
      return { status: 200, body: { id: 5, title: 'TEC desk', managerUserId: 'peer', members: deskMembers.filter((m) => m.userId !== 'me') } };
    }
    return undefined;
  });
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: false });
    await tick(); await tick();
    win.people.click();
    const panel = win.panel;
    assert.equal(panel.hidden, false);
    assert.equal(panel.querySelectorAll('[data-focus-key^="remove:"]').length, 2, 'remove buttons for the two others');
    assert.equal(panel.querySelectorAll('[data-focus-key^="chat:"]').length, 2, 'a chat button for each of the others');
    assert.ok(panel.querySelector('.pm-chat__members-rename'));

    panel.querySelector('.pm-chat__leave').click();
    assert.deepEqual(actions, []);
    panel.querySelector('.pm-chat__leave').click(); // the armed button is a fresh one after the redraw
    await tick(); await tick();
    assert.deepEqual(actions, ['/leave']);
    assert.equal(app.groups.has(5), false);

    // Someone who does not manage the group gets neither Remove nor Rename.
    app.onGroupChanged({ id: 5, title: 'TEC desk', managerUserId: 'peer', members: deskMembers });
    win.panelOpen = true;
    app.renderMembersPanel(win);
    assert.equal(panel.querySelectorAll('[data-focus-key^="remove:"]').length, 0);
    assert.equal(panel.querySelector('.pm-chat__members-rename'), null);
  } finally {
    window.close();
  }
});

test('New group posts the name and the chosen people, then opens the group', async () => {
  const created = [];
  const window = groupApp(async (url, init) => {
    if (url.endsWith('/groups') && init.method === 'POST') {
      created.push(JSON.parse(init.body));
      return { status: 200, body: { id: 9, title: 'Night shift', managerUserId: 'me', members: deskMembers } };
    }
    return undefined;
  });
  try {
    await tick(); await tick();
    const app = window.PMChat;
    app.openGroupForm();
    const form = app.groupForm;
    form.querySelector('input[type="text"]').value = 'Night shift';
    form.querySelector('.pm-chat__picker-list input').click();
    form.dispatchEvent(new window.Event('submit', { cancelable: true }));
    await tick(); await tick();
    assert.equal(created.length, 1);
    assert.equal(created[0].title, 'Night shift');
    assert.equal(created[0].memberUserIds.length, 1);
    assert.ok(app.windows.has('g:9'));
    assert.equal(form.hidden, true);
  } finally {
    window.close();
  }
});

test('the Messages page lists groups and opens a group search hit', async () => {
  const window = groupApp(async (url) => {
    if (url.includes('/search')) {
      assert.ok(url.includes('groups=true'), 'the page asks for group hits (a bool the endpoint can bind)');
      return [{ messageId: 11, peerUserId: '', peerDisplayName: 'TEC desk', senderUserId: 'peer', body: 'Minutes attached', sentUtc: '2026-10-22T08:00:00Z', groupId: 5 }];
    }
    if (url.includes('/groups/5/messages') && url.includes('before=12')) {
      return { groupId: 5, messages: [groupMessage(11, 'peer', 'Minutes attached')], hasMore: true, hasNewer: true, members: deskMembers };
    }
    return undefined;
  }, '<section data-chat-page></section>');
  try {
    await tick(); await tick(); await tick();
    const page = window.PMChat.page;
    assert.match(page.list.textContent, /TEC desk/);
    page.searchInput.value = 'minutes';
    await page.runSearch();
    const hit = page.results.querySelector('.pm-chat-page__hit');
    assert.match(hit.textContent, /TEC desk \(group\)/);
    assert.match(hit.textContent, /Peer Person: /);
    hit.click();
    await tick(); await tick();
    assert.equal(page.current.groupId, 5);
    assert.equal(page.current.highlightId, 11);
  } finally {
    window.close();
  }
});

test('member counts read naturally', () => {
  const { PMChatInternals } = load();
  assert.equal(PMChatInternals.peopleCount(1), '1 person');
  assert.equal(PMChatInternals.peopleCount(3), '3 people');
});

test('group messages arrive on their own hub events, never on ReceiveMessage', async () => {
  const handlers = {};
  const window = groupApp();
  try {
    await tick();
    function Builder() {}
    Builder.prototype.withUrl = function () { return this; };
    Builder.prototype.withAutomaticReconnect = function () { return this; };
    Builder.prototype.build = function () {
      return {
        on: (name, fn) => { handlers[name] = fn; },
        onreconnecting() {}, onreconnected() {}, onclose() {},
        start: () => new Promise(() => {}),
        state: 'Disconnected',
      };
    };
    window.signalR = { HubConnectionBuilder: Builder };
    window.PMChat.hubUrl = '/hubs/chat';
    window.PMChat.connect();
    assert.ok(handlers.ReceiveGroupMessage, 'listens for ReceiveGroupMessage');
    assert.ok(handlers.GroupMessageUnsent, 'listens for GroupMessageUnsent');
    await tick();
    handlers.ReceiveGroupMessage(groupMessage(40, 'peer', 'On the group channel'));
    assert.ok(window.PMChat.windows.get('g:5').messages.some((m) => m.id === 40));
  } finally {
    window.close();
  }
});

// --- Review fixes: links, 404s, ticks, the people panel, drafts --------------------------------

test('group links are normalised and malformed ones are refused', () => {
  const { PMChatInternals } = load();
  assert.equal(PMChatInternals.conversationKey('g:05'), 'g:5');
  assert.equal(PMChatInternals.conversationKey('g:5.5'), '');
  assert.equal(PMChatInternals.conversationKey('g:-3'), '');
  assert.equal(PMChatInternals.conversationKey('g:abc'), '');
  assert.equal(PMChatInternals.conversationKey('g:'), '');
  assert.equal(PMChatInternals.conversationKey('peer'), 'peer');
  assert.equal(PMChatInternals.groupIdOf('g:5.5'), 0);
  assert.equal(PMChatInternals.groupIdOf('g:99999999999999999'), 0, 'beyond a safe integer');
});

test('an ordinary 404 is an error for that request; only "chat is off" stands chat down', async () => {
  let off = false;
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/groups/77')) {
      return off ? { status: 404, headers: { 'X-Chat-Disabled': 'true' }, body: null } : { status: 404, body: null };
    }
    return { status: 204, body: null };
  });
  try {
    await tick();
    const app = window.PMChat;
    await assert.rejects(app.request('GET', '/groups/77/messages'));
    assert.notEqual(app.stopped, true);
    assert.equal(app.root.hidden, false);

    off = true;
    await assert.rejects(app.request('GET', '/groups/77/messages'));
    assert.equal(app.stopped, true);
    assert.equal(app.root.hidden, true);
  } finally {
    window.close();
  }
});

test('"Seen" in a group waits only for members who can read the message', () => {
  const { PMChatInternals } = load();
  const message = { id: 12, sentUtc: '2026-10-22T09:00:00Z' };
  const members = [
    { userId: 'me', isActive: true, lastReadMessageId: 12 },
    { userId: 'peer', isActive: true, lastReadMessageId: 12, joinedUtc: '2026-10-01T00:00:00Z' },
    { userId: 'gone', isActive: false, lastReadMessageId: 3 }, // inactive: never reads
    { userId: 'late', isActive: true, lastReadMessageId: null, joinedUtc: '2026-10-22T10:00:00Z' }, // joined after it
  ];
  assert.equal(PMChatInternals.seenByEveryone(members, 'me', message), true);
  // The label never names someone who joined after the message, however far they have read.
  members[3].lastReadMessageId = 40;
  assert.deepEqual(Array.from(PMChatInternals.seenBy(members, 'me', message), (m) => m.userId), ['peer']);
  members[1].lastReadMessageId = 11;
  assert.equal(PMChatInternals.seenByEveryone(members, 'me', message), false);
  assert.equal(PMChatInternals.seenByEveryone([{ userId: 'me', isActive: true }], 'me', message), false, 'nobody else');
});

test('the people panel covers the window, not the scrolled messages, and keeps work across changes', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: false });
    await tick(); await tick();
    win.people.click();
    const panel = win.panel;
    assert.notEqual(panel.parentNode, win.body, 'not inside the scrolling body');
    assert.ok(panel.parentNode.classList.contains('pm-chat__window-stage'));

    // Start adding someone and renaming, arm Leave, then another member changes the group.
    const details = panel.querySelector('.pm-chat__members-add');
    details.open = true;
    details.dispatchEvent(new window.Event('toggle'));
    const rename = panel.querySelector('.pm-chat__members-rename input');
    rename.value = 'TEC desk (Nov)';
    rename.dispatchEvent(new window.Event('input'));
    panel.querySelector('.pm-chat__leave').click();
    panel.querySelector('.pm-chat__members-rename input').focus();

    app.onGroupChanged({ id: 5, title: 'TEC desk', managerUserId: 'me', members: deskMembers.map((m) => ({ ...m })) });

    const renameAgain = panel.querySelector('.pm-chat__members-rename input');
    assert.equal(panel.querySelector('.pm-chat__members-add').open, true);
    assert.equal(renameAgain.value, 'TEC desk (Nov)');
    assert.equal(window.document.activeElement, renameAgain, 'focus survives');
    assert.match(panel.querySelector('.pm-chat__leave').textContent, /Leave\?/);

    // Closing the panel forgets it and returns focus to the people button.
    panel.querySelector('.pm-chat__members-head button').click();
    assert.equal(panel.hidden, true);
    assert.equal(window.document.activeElement, win.people);
    win.people.click();
    assert.equal(panel.querySelector('.pm-chat__members-rename input').value, 'TEC desk');
  } finally {
    window.close();
  }
});

test('ticked people survive a group change, except anyone who joined meanwhile', async () => {
  const window = groupApp(async (url) => {
    if (url.endsWith('/contacts')) {
      return ['peer', 'third', 'fourth', 'fifth'].map((id) => ({ ...contact, userId: id, displayName: 'Person ' + id, unreadCount: 0 }));
    }
    return undefined;
  });
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: false });
    await tick(); await tick();
    win.people.click();
    const tickFor = (id) => win.panel.querySelector('[data-focus-key="pick:' + id + '"]');
    tickFor('fourth').click();
    tickFor('fifth').click();
    assert.deepEqual(Array.from(win.panelState.chosen).sort(), ['fifth', 'fourth']);

    // Someone else adds "fourth".
    app.onGroupChanged({
      id: 5,
      title: 'TEC desk',
      managerUserId: 'me',
      members: deskMembers.concat([{ userId: 'fourth', displayName: 'Person fourth', initials: 'PF', isActive: true, lastReadMessageId: null }]),
    });
    assert.deepEqual(Array.from(win.panelState.chosen), ['fifth']);
    assert.equal(tickFor('fifth').checked, true);
    assert.equal(tickFor('fourth'), null);
  } finally {
    window.close();
  }
});

test('the people panel waits for members, then fills in when they arrive', async () => {
  let release;
  const gate = new Promise((resolve) => { release = resolve; });
  const window = groupApp(async (url) => {
    if (url.includes('/groups/5/messages')) await gate;
    return undefined;
  });
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: false });
    win.people.click();
    assert.match(win.panel.textContent, /Loading/);
    assert.equal(win.panel.querySelector('.pm-chat__members-add'), null);

    // Codex review on #2734: the open panel must not stay on "Loading…".
    release();
    await tick(); await tick(); await tick();
    assert.doesNotMatch(win.panel.textContent, /Loading/);
    assert.equal(win.panel.querySelectorAll('.pm-chat__members-list li').length, 3);
    assert.ok(win.panel.querySelector('.pm-chat__members-add'));
  } finally {
    release();
    window.close();
  }
});

test('being added back to a group reloads it from the new membership', async () => {
  let loads = 0;
  const window = groupApp(async (url, init) => {
    if (url.includes('/groups/5/messages') && (!init.method || init.method === 'GET')) {
      loads += 1;
      if (loads > 1) {
        return {
          groupId: 5,
          messages: [groupMessage(50, 'peer', '', { eventKind: 'Added', eventData: 'me' })],
          hasMore: false,
          hasNewer: false,
          members: deskMembers.map((m) => ({ ...m })),
        };
      }
    }
    return undefined;
  });
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: false });
    await tick(); await tick();
    assert.equal(loads, 1);
    app.onGroupRemoved(5);
    app.onGroupChanged({ id: 5, title: 'TEC desk', managerUserId: 'peer', members: deskMembers.map((m) => ({ ...m })) });
    await tick(); await tick();
    assert.equal(loads, 2);
    assert.deepEqual(Array.from(win.messages, (m) => m.id), [50]);
    assert.equal(win.form.hidden, false);
  } finally {
    window.close();
  }
});

test('being added back while an older search slice is open still loads the new membership', async () => {
  let latest = 0;
  const window = groupApp(async (url, init) => {
    if (url.includes('/groups/5/messages') && (!init.method || init.method === 'GET') && !url.includes('before=') && !url.includes('after=')) {
      latest += 1;
      if (latest > 1) {
        return {
          groupId: 5,
          messages: [groupMessage(50, 'peer', '', { eventKind: 'Added', eventData: 'me' })],
          hasMore: false,
          hasNewer: false,
          members: deskMembers.map((m) => ({ ...m })),
        };
      }
    }
    return undefined;
  });
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: false });
    await tick(); await tick();
    // As if a search hit had opened an older slice of the conversation.
    win.hasNewer = true;
    win.highlightId = 11;

    app.onGroupRemoved(5);
    app.onGroupChanged({ id: 5, title: 'TEC desk', managerUserId: 'peer', members: deskMembers.map((m) => ({ ...m })) });
    await tick(); await tick();

    assert.equal(latest, 2);
    assert.equal(win.hasNewer, false);
    assert.deepEqual(Array.from(win.messages, (m) => m.id), [50]);
  } finally {
    window.close();
  }
});

function narrow(window) {
  Object.defineProperty(window, 'innerWidth', { value: 390, configurable: true });
}

test('a message does not push aside a window someone is typing in; it waits as unread', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    narrow(window);
    const app = window.PMChat;
    const win = app.openWindow('peer', { focus: true });
    await tick(); await tick();
    win.input.value = 'Half a thought';
    win.input.blur();

    app.onMessage({ id: 70, senderUserId: 'third', recipientUserId: 'me', clientMessageId: 'c-70', body: 'Ping', sentUtc: new Date().toISOString() });
    assert.equal(app.windows.has('third'), false);
    assert.equal(app.windows.get('peer'), win);
    assert.equal(app.contacts.get('third').unreadCount, 1);
  } finally {
    window.close();
  }
});

test('a draft and a failed message come back when a closed window is reopened', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    narrow(window);
    const app = window.PMChat;
    const first = app.openWindow('peer', { focus: true });
    await tick(); await tick();
    first.input.value = 'Draft for peer';
    first.messages.push({ id: 0, clientMessageId: 'c-x', senderUserId: 'me', body: 'did not go', failed: true, sentUtc: new Date().toISOString() });

    app.openWindow('third', { focus: true }); // one window fits: peer's closes
    await tick(); await tick();
    assert.equal(app.windows.has('peer'), false);

    const again = app.openWindow('peer', { focus: true });
    assert.equal(again.input.value, 'Draft for peer');
    assert.ok(again.messages.some((m) => m.clientMessageId === 'c-x' && m.failed));
    assert.equal(app.kept.has('peer'), false);
  } finally {
    window.close();
  }
});

test('the Messages page opens a normalised group link', async () => {
  const window = groupApp(undefined, '<section data-chat-page data-open-with="g:05"></section>');
  try {
    await tick(); await tick(); await tick();
    const app = window.PMChat;
    assert.ok(app.windows.has('g:5'));
    assert.equal(app.windows.has('g:05'), false);
  } finally {
    window.close();
  }
});

test('a group title, sender name or notice containing markup is shown as text', async () => {
  const evil = '<img src=x onerror="window.__pwned=1">';
  const window = groupApp(async (url) => {
    if (url.endsWith('/groups')) return [{ ...desk, title: evil }];
    if (url.endsWith('/contacts')) return [{ ...contact, displayName: evil, unreadCount: 0 }];
    return undefined;
  });
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: false });
    await tick(); await tick();
    app.onMessage(groupMessage(60, 'peer', evil));
    app.onMessage(groupMessage(61, 'peer', '', { eventKind: 'Renamed', eventData: evil }));
    assert.equal(window.document.querySelector('img'), null);
    assert.equal(window.__pwned, undefined);
    assert.ok(win.name.textContent.includes('<img'));
  } finally {
    window.close();
  }
});

// --- Accessibility: focus that survives redraws, Esc and focus return, what screen readers hear --

const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function directMessage(id, sender, body, extra) {
  const recipient = sender === 'me' ? 'peer' : 'me';
  return { id, senderUserId: sender, recipientUserId: recipient, clientMessageId: 'c-' + id, body, sentUtc: new Date().toISOString(), ...extra };
}

function focusKeyOf(window) {
  const active = window.document.activeElement;
  return active && active.dataset ? active.dataset.focusKey || '' : '';
}

test('keyboard focus in the contact list survives presence and unread updates', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    app.toggleBuddy(true);
    await tick(); await tick();
    app.list.querySelector('[data-focus-key="contact:third"]').focus();

    app.onPresence({ userId: 'peer', isOnline: false, isAway: false });
    app.contacts.get('third').unreadCount = 4;
    app.renderContacts();

    assert.equal(focusKeyOf(window), 'contact:third');
    assert.match(window.document.activeElement.getAttribute('aria-label'), /4 unread/);

    app.groupList.querySelector('[data-focus-key="group:5"]').focus();
    app.groups.get(5).unreadCount = 7;
    app.renderContacts();
    assert.equal(focusKeyOf(window), 'group:5');
  } finally {
    window.close();
  }
});

test('focus on Unsend survives a redraw, and moves to the message box when the link goes', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    app.unsendMs = 5 * 60000;
    const win = app.openWindow('peer', { focus: false });
    await tick(); await tick();
    app.acceptSaved(win, [directMessage(30, 'me', 'Sent just now')]);
    win.log.querySelector('[data-focus-key="unsend:30"]').focus();

    app.onPeerRead({ peerUserId: 'peer', upToMessageId: 30 }); // read ticks redraw the log
    app.renderMessages(win, { stickToBottom: false });
    assert.equal(focusKeyOf(window), 'unsend:30');

    app.unsendMs = 0; // the window has passed: the link disappears on the next redraw
    app.renderMessages(win, { stickToBottom: false });
    assert.equal(win.log.querySelector('[data-focus-key="unsend:30"]'), null);
    assert.equal(window.document.activeElement, win.input);
  } finally {
    window.close();
  }
});

test('Esc minimises a window and leaves focus on its title bar; the minimise button does too', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('peer', { focus: true });
    await tick(); await tick();
    win.input.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    assert.equal(win.minimized, true);
    assert.equal(window.document.activeElement, win.toggle);

    win.toggle.click();
    assert.equal(win.minimized, false);
    win.minimize.click();
    assert.equal(window.document.activeElement, win.toggle);
  } finally {
    window.close();
  }
});

test('closing a window or the contact list hands focus back to the chat button', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const launcher = window.document.querySelector('[data-chat-launcher]');
    const win = app.openWindow('peer', { focus: true });
    await tick(); await tick();
    win.node.querySelector('[aria-label="Close chat"]').click();
    assert.equal(app.windows.has('peer'), false);
    assert.equal(window.document.activeElement, launcher);

    app.toggleBuddy(true);
    app.buddy.querySelector('[aria-label="Close contacts"]').click();
    assert.equal(app.buddy.hidden, true);
    assert.equal(window.document.activeElement, launcher);
  } finally {
    window.close();
  }
});

test('Esc in the contact list clears the search first, then closes the list', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const launcher = window.document.querySelector('[data-chat-launcher]');
    app.toggleBuddy(true);
    const esc = (target) => target.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));

    // Esc while editing the status message cancels the edit only.
    app.statusInput.focus();
    app.statusInput.value = 'half typed';
    esc(app.statusInput);
    assert.equal(app.buddy.hidden, false);
    assert.equal(app.statusInput.value, '');

    app.search.value = 'thi';
    app.renderContacts();
    esc(app.search);
    assert.equal(app.search.value, '');
    assert.equal(app.buddy.hidden, false);

    esc(app.search);
    assert.equal(app.buddy.hidden, true);
    assert.equal(window.document.activeElement, launcher);
  } finally {
    window.close();
  }
});

test('the people panel is announced, takes focus, and Esc closes it before the window', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: true });
    await tick(); await tick();
    assert.equal(win.people.getAttribute('aria-expanded'), 'false');
    assert.equal(win.people.getAttribute('aria-controls'), win.panel.id);
    assert.equal(win.panel.getAttribute('role'), 'region');

    win.people.click();
    assert.equal(win.people.getAttribute('aria-expanded'), 'true');
    assert.equal(window.document.activeElement, win.panel);

    win.panel.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    assert.equal(win.panel.hidden, true);
    assert.equal(win.minimized, false, 'the first Esc only closes the panel');
    assert.equal(window.document.activeElement, win.people);
  } finally {
    window.close();
  }
});

test('a failed group change is announced', async () => {
  const window = groupApp(async (url, init) => {
    if (url.endsWith('/groups/5/rename') && init.method === 'POST') return { status: 400, body: { detail: 'That is already the group’s name.' } };
    return undefined;
  });
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: false });
    await tick(); await tick();
    win.people.click();
    await app.groupAction(win, '/rename', { title: 'TEC desk' });
    await wait(50);
    assert.match(app.announcer.textContent, /already the group/);
    assert.match(win.panel.querySelector('.pm-chat__form-error').textContent, /already the group/);
  } finally {
    window.close();
  }
});

test('screen readers hear who wrote each message, and new messages once, not the whole log', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('peer', { focus: false });
    await tick(); await tick();
    assert.equal(win.log.getAttribute('aria-live'), 'off');

    app.onMessage(directMessage(40, 'peer', 'Are you free at three?'));
    await wait(50);
    assert.equal(app.announcer.textContent, 'New message from Peer Person', 'not in the window: who, not what');

    win.input.focus(); // now in the conversation
    app.onMessage(directMessage(41, 'peer', 'Room 4'));
    await wait(50);
    assert.equal(app.announcer.textContent, 'Peer Person: Room 4');

    app.acceptSaved(win, [directMessage(42, 'me', 'Yes')]);
    const hidden = Array.from(win.log.querySelectorAll('.pm-chat__msg .visually-hidden'), (n) => n.textContent);
    assert.deepEqual(hidden, ['Peer Person: ', 'Peer Person: ', 'You: ']);
  } finally {
    window.close();
  }
});

test('in a group, every message names its sender for screen readers, not just the first of a run', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: false });
    await tick(); await tick();
    app.onMessage(groupMessage(20, 'peer', 'One'));
    app.onMessage(groupMessage(21, 'peer', 'Two'));
    const lastTwo = Array.from(win.log.querySelectorAll('.pm-chat__msg')).slice(-2);
    assert.equal(lastTwo[0].querySelector('.pm-chat__sender').textContent, 'Peer Person');
    assert.equal(lastTwo[1].querySelector('.pm-chat__sender'), null);
    assert.equal(lastTwo[1].querySelector('.visually-hidden').textContent, 'Peer Person: ');
  } finally {
    window.close();
  }
});

test('the message box label follows a group rename', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    const win = app.openWindow('g:5', { focus: false });
    await tick(); await tick();
    assert.equal(win.input.getAttribute('aria-label'), 'Message TEC desk');
    app.onGroupChanged({ id: 5, title: 'TEC desk (Nov)', managerUserId: 'me', members: deskMembers.map((m) => ({ ...m })) });
    assert.equal(win.input.getAttribute('aria-label'), 'Message TEC desk (Nov)');
  } finally {
    window.close();
  }
});

test('a message search announces how many it found', async () => {
  const window = groupApp(async (url) => {
    if (url.includes('/search')) {
      return [{ messageId: 11, peerUserId: '', peerDisplayName: 'TEC desk', senderUserId: 'peer', body: 'Minutes attached', sentUtc: '2026-10-22T08:00:00Z', groupId: 5 }];
    }
    return undefined;
  }, '<section data-chat-page></section>');
  try {
    await tick(); await tick(); await tick();
    const page = window.PMChat.page;
    assert.equal(page.results.getAttribute('aria-live'), 'off');
    page.searchInput.value = 'minutes';
    await page.runSearch();
    await wait(50);
    assert.equal(window.PMChat.announcer.textContent, '1 message found');
  } finally {
    window.close();
  }
});

test('messages fetched while the live connection is down are announced, one or several', async () => {
  let page = [directMessage(1, 'peer', 'Hello')];
  const window = bootApp(async (url) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.includes('/with/peer/messages')) return { messages: page, hasMore: false };
    return { status: 204, body: null };
  });
  try {
    await tick();
    const app = window.PMChat;
    const win = app.openWindow('peer', { focus: false });
    await tick(); await tick();
    assert.equal(win.historyLoaded, true);

    page = [directMessage(1, 'peer', 'Hello'), directMessage(2, 'peer', 'Are you there?')];
    await app.loadLatest(win); // what polling does
    await wait(50);
    assert.equal(app.announcer.textContent, 'New message from Peer Person');

    page = page.concat([directMessage(3, 'peer', 'Ping'), directMessage(4, 'me', 'Yes'), directMessage(5, 'peer', 'Good')]);
    await app.loadLatest(win);
    await wait(50);
    assert.equal(app.announcer.textContent, '2 new messages from Peer Person');
  } finally {
    window.close();
  }
});

test('a message that could not be sent is announced', async () => {
  const window = bootApp(async (url, init) => {
    if (url.endsWith('/contacts')) return [contact];
    if (url.endsWith('/messages') && init.method === 'POST') return { status: 500, body: { detail: 'The server is busy.' } };
    if (url.includes('/messages')) return { messages: [], hasMore: false };
    return { status: 204, body: null };
  });
  try {
    await tick();
    const app = window.PMChat;
    const win = app.openWindow('peer', { focus: false });
    await tick(); await tick();
    win.input.value = 'Hello';
    app.send(win);
    await tick(); await tick();
    await wait(50);
    assert.equal(app.announcer.textContent, 'Message not sent. The server is busy.');
    assert.ok(win.log.querySelector('.pm-chat__retry'));
  } finally {
    window.close();
  }
});

// --- Group management: confirmations, feedback, member list, mute -----------------------------

/** Runs the app's timers by hand: returns run(), which fires those scheduled so far. */
function manualTimers(window) {
  const pending = [];
  const real = window.setTimeout;
  window.setTimeout = (fn, ms) => {
    if (ms >= 1000) {
      pending.push(fn);
      return pending.length;
    }
    return real(fn, ms);
  };
  return () => pending.splice(0).forEach((fn) => fn());
}

async function openDeskPanel(window) {
  const app = window.PMChat;
  const win = app.openWindow('g:5', { focus: false });
  await tick(); await tick();
  win.people.click();
  return { app, win, panel: win.panel };
}

test('Remove asks for confirmation, and both confirmations disarm themselves', async () => {
  const actions = [];
  const window = groupApp(async (url, init) => {
    if (url.includes('/remove') && init.method === 'POST') {
      actions.push(url.replace(/^.*\/groups\/5/, ''));
      return { status: 200, body: { id: 5, title: 'TEC desk', managerUserId: 'me', members: deskMembers.filter((m) => m.userId !== 'third') } };
    }
    return undefined;
  });
  try {
    await tick(); await tick();
    const runTimers = manualTimers(window);
    const { panel } = await openDeskPanel(window);
    const removeThird = () => panel.querySelector('[data-focus-key="remove:third"]');

    removeThird().click();
    assert.deepEqual(actions, []);
    assert.equal(removeThird().textContent, 'Confirm remove');
    assert.equal(removeThird().getAttribute('aria-label'), 'Confirm removing Third Officer');

    runTimers(); // five seconds later
    assert.equal(removeThird().textContent, 'Remove');

    panel.querySelector('.pm-chat__leave').click();
    assert.match(panel.querySelector('.pm-chat__leave').textContent, /Leave\?/);
    runTimers();
    assert.equal(panel.querySelector('.pm-chat__leave').textContent, 'Leave group');

    removeThird().click();
    removeThird().click();
    await tick(); await tick();
    assert.deepEqual(actions, ['/members/third/remove']);
    assert.equal(panel.querySelector('.pm-chat__form-notice').textContent, 'Removed Third Officer.');
  } finally {
    window.close();
  }
});

test('adding people confirms who was added, and says so to screen readers', async () => {
  const window = groupApp(async (url, init) => {
    if (url.endsWith('/contacts')) {
      return ['peer', 'third', 'fourth'].map((id) => ({ ...contact, userId: id, displayName: id === 'peer' ? 'Peer Person' : id === 'third' ? 'Third Officer' : 'Fourth Officer', unreadCount: 0 }));
    }
    if (url.endsWith('/groups/5/members') && init.method === 'POST') {
      return { status: 200, body: { id: 5, title: 'TEC desk', managerUserId: 'me', members: deskMembers.concat([{ userId: 'fourth', displayName: 'Fourth Officer', initials: 'FO', isActive: true, lastReadMessageId: null }]) } };
    }
    return undefined;
  });
  try {
    await tick(); await tick();
    const { app, panel } = await openDeskPanel(window);

    panel.querySelector('.pm-chat__members-add .btn-primary').click(); // nothing ticked
    assert.equal(panel.querySelector('.pm-chat__form-notice').textContent, 'Tick the people to add first.');

    panel.querySelector('[data-focus-key="pick:fourth"]').click();
    panel.querySelector('.pm-chat__members-add .btn-primary').click();
    await tick(); await tick();
    await wait(50);
    assert.equal(panel.querySelector('.pm-chat__form-notice').textContent, 'Added Fourth Officer.');
    assert.equal(app.announcer.textContent, 'Added Fourth Officer.');
    assert.equal(panel.querySelector('.pm-chat__members-add').open, false);
  } finally {
    window.close();
  }
});

test('the member list shows who is online and offers a direct chat', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    // Set here: other tests change the shared contact fixture's presence.
    Object.assign(window.PMChat.contacts.get('peer'), { isOnline: true, isAway: false });
    const { app, panel } = await openDeskPanel(window);
    const rows = Array.from(panel.querySelectorAll('.pm-chat__members-list li'));
    const peerRow = rows.find((r) => r.textContent.includes('Peer Person'));
    assert.ok(peerRow.querySelector('.pm-chat__presence.is-online'));
    assert.match(peerRow.textContent, /Online/);
    assert.equal(rows.find((r) => r.textContent.includes('(you)')).querySelector('[data-focus-key^="chat:"]'), null, 'no chat with yourself');

    peerRow.querySelector('[data-focus-key="chat:peer"]').click();
    assert.ok(app.windows.has('peer'));
  } finally {
    window.close();
  }
});

test('a muted group counts unread but neither pops open nor alerts, and stays muted on the next page', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    let alerts = 0;
    app.alert = () => { alerts += 1; };
    app.setMuted(5, true);
    const row = app.groupList.querySelector('[data-focus-key="group:5"]');
    assert.match(row.getAttribute('aria-label'), /muted/);
    assert.ok(row.querySelector('.pm-chat__muted'));

    app.onMessage(groupMessage(60, 'peer', 'Quiet please'));
    assert.equal(app.windows.has('g:5'), false);
    assert.equal(alerts, 0);
    assert.equal(app.groups.get(5).unreadCount, 3);

    assert.deepEqual(JSON.parse(window.localStorage.getItem('prism.chat.muted.me')), [5]);
    assert.equal(app.readMuted().has(5), true);

    app.setMuted(5, false);
    app.onMessage(groupMessage(61, 'peer', 'Back to normal'));
    assert.ok(app.windows.has('g:5'));
    assert.equal(alerts, 1);
  } finally {
    window.close();
  }
});

test('the mute switch in the people panel mutes the group and says so in the window', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const { app, win, panel } = await openDeskPanel(window);
    const box = panel.querySelector('[data-focus-key="mute"]');
    assert.equal(box.checked, false);
    box.click();
    assert.equal(app.isMuted(5), true);
    assert.match(win.status.textContent, /Muted/);
  } finally {
    window.close();
  }
});

test('a full group says it is full instead of offering people to add', async () => {
  const many = Array.from({ length: 50 }, (_, i) => ({ userId: i === 0 ? 'me' : 'u' + i, displayName: 'User ' + i, initials: 'U', isActive: true, lastReadMessageId: 0 }));
  const window = groupApp(async (url) => {
    if (url.includes('/groups/5/messages')) {
      return { groupId: 5, messages: [], hasMore: false, hasNewer: false, members: many };
    }
    return undefined;
  });
  try {
    await tick(); await tick();
    const { panel } = await openDeskPanel(window);
    panel.querySelector('.pm-chat__members-add').open = true;
    assert.match(panel.querySelector('.pm-chat__members-add').textContent, /This group is full \(50 people\)/);
    assert.equal(panel.querySelectorAll('.pm-chat__picker-list').length, 0);
  } finally {
    window.close();
  }
});

test('New group checks the name and the people before sending, and points at the fix', async () => {
  let posted = 0;
  const window = groupApp(async (url, init) => {
    if (url.endsWith('/groups') && init.method === 'POST') posted += 1;
    return undefined;
  });
  try {
    await tick(); await tick();
    const app = window.PMChat;
    app.toggleBuddy(true);
    app.openGroupForm();
    const form = app.groupForm;
    form.dispatchEvent(new window.Event('submit', { cancelable: true }));
    assert.equal(form.querySelector('.pm-chat__form-error').textContent, 'Give the group a name.');
    assert.equal(window.document.activeElement, form.querySelector('input[type="text"]'));

    form.querySelector('input[type="text"]').value = 'Desk';
    form.dispatchEvent(new window.Event('submit', { cancelable: true }));
    assert.equal(form.querySelector('.pm-chat__form-error').textContent, 'Tick at least one other person.');
    assert.equal(window.document.activeElement, form.querySelector('input[type="search"]'));
    assert.equal(posted, 0);
  } finally {
    window.close();
  }
});

test('muting in another tab applies here, and a change here keeps what the other tab stored', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    let alerts = 0;
    app.alert = () => { alerts += 1; };

    // Another tab of the same user mutes group 5 and also group 9.
    window.localStorage.setItem('prism.chat.muted.me', JSON.stringify([5, 9]));
    window.dispatchEvent(new window.StorageEvent('storage', { key: 'prism.chat.muted.me' }));
    assert.equal(app.isMuted(5), true);
    app.onMessage(groupMessage(70, 'peer', 'Hush'));
    assert.equal(alerts, 0);
    assert.equal(app.windows.has('g:5'), false);

    // Unmuting 5 here must not drop the 9 the other tab stored.
    app.setMuted(5, false);
    assert.deepEqual(JSON.parse(window.localStorage.getItem('prism.chat.muted.me')), [9]);
  } finally {
    window.close();
  }
});

test('the people panel follows presence changes while it is open', async () => {
  const window = groupApp();
  try {
    await tick(); await tick();
    const app = window.PMChat;
    Object.assign(app.contacts.get('third'), { isOnline: false, isAway: false });
    const { panel } = await openDeskPanel(window);
    const thirdRow = () => Array.from(panel.querySelectorAll('.pm-chat__members-list li')).find((r) => r.textContent.includes('Third Officer'));
    assert.match(thirdRow().textContent, /Offline/);

    app.onPresence({ userId: 'third', isOnline: true, isAway: false });
    assert.match(thirdRow().textContent, /Online/);
    assert.ok(thirdRow().querySelector('.pm-chat__presence.is-online'));
  } finally {
    window.close();
  }
});
