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
