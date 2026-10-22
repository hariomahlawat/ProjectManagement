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

function bootApp(handler) {
  const dom = new JSDOM(`<!doctype html><html><head><meta name="csrf-token" content="t"></head><body>
    <button data-chat-launcher></button>
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
