// Tests for the draft session (docs/ux/collage-review-2-fixes.md, PR 2): when drafts are written,
// when the user is asked about room, deleting with Undo, and that no draft loses its photos when
// it is deleted or purged while open.
const test = require('node:test');
const assert = require('node:assert/strict');
const core = require('./collage-core.js');
const { createDraftSession } = require('./collage-draft-session.js');

/** The drafts database in memory, with the same calls as collage-drafts.js. */
function memoryDrafts() {
  const records = new Map();
  const photos = new Map();
  const calls = [];
  return {
    records,
    photos,
    calls,
    list: async () => [...records.values()].map((r) => ({ ...r })),
    get: async (id) => (records.has(id) ? { ...records.get(id) } : undefined),
    photosOf: async (id) => [...photos.values()].filter((p) => p.draft === id),
    save: async (record, all) => {
      calls.push(['save', record.id]);
      records.set(record.id, { ...record });
      const wanted = new Set();
      for (const photo of all || []) {
        const key = `${record.id}/${photo.id}`;
        wanted.add(key);
        if (!photos.has(key)) photos.set(key, { key, draft: record.id, ...photo });
      }
      for (const key of [...photos.keys()]) if (key.startsWith(`${record.id}/`) && !wanted.has(key)) photos.delete(key);
    },
    markDeleted: async (id, at) => { if (records.has(id)) records.set(id, { ...records.get(id), deletedAt: at }); },
    restore: async (id) => { if (records.has(id)) records.set(id, { ...records.get(id), deletedAt: null }); },
    purge: async (id) => {
      calls.push(['purge', id]);
      records.delete(id);
      for (const key of [...photos.keys()]) if (key.startsWith(`${id}/`)) photos.delete(key);
    },
    keep: () => {},
    reasonOf: () => 'blocked'
  };
}

function fakeTimers() {
  let next = 1;
  const waiting = new Map();
  return {
    set: (fn) => { const id = next++; waiting.set(id, fn); return id; },
    clear: (id) => waiting.delete(id),
    fire: () => { const all = [...waiting.values()]; waiting.clear(); all.forEach((fn) => fn()); },
    waiting
  };
}

function fakeChannel() {
  return { onmessage: null, sent: [], postMessage(message) { this.sent.push(message); } };
}

function setUp({ answer = async () => true, photos = [{ id: 'p1', name: 'a.jpg', file: 'A' }] } = {}) {
  const drafts = memoryDrafts();
  const timers = fakeTimers();
  const channel = fakeChannel();
  const asked = [];
  const statuses = [];
  let clock = 1_000_000;
  const shot = { photos };
  const session = createDraftSession({
    drafts,
    core,
    ask: async (options) => { asked.push(options.title); return answer(options); },
    snapshot: () => ({
      title: 'Trip', autoTitle: false, model: { v: 1 }, size: 'square', photos: shot.photos,
      thumbnail: '', hasContent: shot.photos.length > 0
    }),
    onStatus: (text, warning) => statuses.push([text, warning]),
    now: () => (clock += 1000),
    setTimer: timers.set,
    clearTimer: timers.clear,
    channel
  });
  return { drafts, timers, channel, asked, statuses, session, shot };
}

/** Fills the database with as many drafts as it can keep. */
function fill(drafts, count = core.MAX_DRAFTS) {
  for (let i = 0; i < count; i += 1) {
    drafts.records.set(`old${i}`, { id: `old${i}`, title: `Old ${i}`, saved: i + 1, deletedAt: null });
  }
}

const settle = () => new Promise((resolve) => setImmediate(resolve));

test('a change is written half a second later, with the photos', async () => {
  const { drafts, timers, session } = setUp();
  session.begin({ id: 'd1' });
  session.changed();
  session.changed();
  assert.equal(timers.waiting.size, 1);
  timers.fire();
  await settle();
  assert.equal(drafts.records.get('d1').title, 'Trip');
  assert.ok(drafts.photos.has('d1/p1'));
  assert.equal(session.draft.created, true);
  assert.equal(session.pending, false);
});

test('an empty page is not kept as a draft', async () => {
  const { drafts, session, shot } = setUp();
  shot.photos = [];
  session.begin({ id: 'd1' });
  session.changed();
  assert.equal(await session.saveNow(), true);
  assert.equal(drafts.records.size, 0);
});

test('a draft purged by another tab while open here is written back with its photos', async () => {
  const { drafts, channel, session } = setUp();
  session.begin({ id: 'd1' });
  session.changed();
  await session.saveNow();
  await drafts.purge('d1');
  assert.equal(drafts.photos.size, 0);
  channel.onmessage({ data: { type: 'gone', id: 'd1' } });
  await session.saveNow();
  assert.ok(drafts.records.has('d1'));
  assert.ok(drafts.photos.has('d1/p1'), 'the photo is stored again');
  assert.equal(drafts.records.get('d1').deletedAt, null);
});

test('a message about another draft changes nothing here', async () => {
  const { drafts, channel, session } = setUp();
  session.begin({ id: 'd1' });
  session.changed();
  await session.saveNow();
  const saves = drafts.calls.filter(([c]) => c === 'save').length;
  channel.onmessage({ data: { type: 'gone', id: 'other' } });
  await session.saveNow();
  assert.equal(drafts.calls.filter(([c]) => c === 'save').length, saves);
});

test('the open draft is never purged or deleted from this tab', async () => {
  const { drafts, session } = setUp();
  session.begin({ id: 'd1' });
  session.changed();
  await session.saveNow();
  await session.purge('d1');
  assert.equal(await session.remove('d1'), null);
  assert.ok(drafts.records.has('d1'));
  assert.ok(!drafts.calls.some(([c, id]) => c === 'purge' && id === 'd1'));
});

test('autosave never asks to delete the oldest draft; the collage is left out until the user acts', async () => {
  const { drafts, asked, statuses, session } = setUp();
  session.begin({ id: 'new' });
  fill(drafts);
  session.changed();
  assert.equal(await session.saveNow(), false);
  assert.deepEqual(asked, []);
  assert.equal(drafts.records.has('new'), false);
  assert.equal(session.draft.off, 'full');
  assert.match(session.problem(), /30 drafts are kept already/);
  assert.deepEqual(statuses.at(-1), ['Not kept as a draft', true]);
  // "Keep as a draft" is the user acting: now they are asked, and the oldest makes room.
  assert.equal(await session.keepAgain(), true);
  assert.equal(asked.length, 1);
  assert.equal(drafts.records.has('old0'), false);
  assert.ok(drafts.records.has('new'));
});

test('starting a collage with no room asks first; declining keeps every draft', async () => {
  const { drafts, asked, session } = setUp({ answer: async () => false });
  fill(drafts);
  assert.equal(await session.prepareNew(), false);
  assert.equal(asked.length, 1);
  assert.equal(drafts.records.size, core.MAX_DRAFTS);
});

test('starting a collage with room asks nothing', async () => {
  const { drafts, asked, session } = setUp();
  fill(drafts, core.MAX_DRAFTS - 1);
  assert.equal(await session.prepareNew(), true);
  assert.deepEqual(asked, []);
});

test('a deleted draft can be brought back until Undo has gone, then it is purged', async () => {
  const { drafts, timers, channel, session } = setUp();
  drafts.records.set('a', { id: 'a', title: 'A', saved: 1, deletedAt: null });
  drafts.photos.set('a/p', { key: 'a/p', draft: 'a', id: 'p' });
  const undos = [];
  const record = await session.remove('a');
  assert.equal(record.title, 'A');
  assert.ok(drafts.records.get('a').deletedAt);
  assert.deepEqual(channel.sent.at(-1), { type: 'gone', id: 'a' });
  assert.equal(session.undoing.id, 'a');
  // The start screen does not purge it while Undo is shown.
  const shown = await session.list();
  assert.equal(shown.length, 0);
  assert.ok(drafts.records.has('a'));
  assert.equal(await session.restore(), true);
  assert.equal(drafts.records.get('a').deletedAt, null);
  undos.push(session.undoing);
  assert.equal(undos[0], null);
  // Deleted again and left: purged once the Undo time is over.
  await session.remove('a');
  timers.fire();
  await settle();
  assert.equal(drafts.records.has('a'), false);
  assert.equal(drafts.photos.size, 0);
});

test('a draft deleted while it is being opened is not opened', async () => {
  const { drafts, session } = setUp();
  drafts.records.set('a', { id: 'a', title: 'A', saved: 1, deletedAt: null });
  const found = await session.read('a');
  assert.equal(found.record.id, 'a');
  assert.equal(await session.stillThere('a'), true);
  await drafts.markDeleted('a', 5);
  assert.equal(await session.stillThere('a'), false);
  assert.equal(await session.read('a'), null);
});

test('a failed write is reported and tried again on the next change', async () => {
  const { drafts, statuses, session } = setUp();
  const save = drafts.save;
  drafts.save = async () => { throw new Error('nope'); };
  session.begin({ id: 'd1' });
  session.changed();
  assert.equal(await session.saveNow(), false);
  assert.equal(session.failed, 'blocked');
  assert.deepEqual(statuses.at(-1), ['Draft not saved in this browser', true]);
  drafts.save = save;
  session.changed();
  assert.equal(await session.saveNow(), true);
  assert.equal(session.failed, '');
});

test('a draft finished writing after the editor moved on does not change the new collage', async () => {
  const { drafts, statuses, session } = setUp();
  const save = drafts.save;
  let release;
  drafts.save = (...args) => new Promise((resolve) => { release = () => resolve(save(...args)); });
  session.begin({ id: 'd1' });
  session.changed();
  const writing = session.saveNow();
  await settle();
  assert.equal(session.writing, true);
  session.end();
  session.begin({ id: 'd2' });
  const count = statuses.length;
  release();
  assert.equal(await writing, true);
  assert.ok(drafts.records.has('d1'));
  assert.equal(statuses.length, count, 'no status for the old draft');
  assert.equal(session.draft.id, 'd2');
});

test('a photo removed from the collage leaves the draft on its next save', async () => {
  const { drafts, session, shot } = setUp({ photos: [{ id: 'p1' }, { id: 'p2' }] });
  session.begin({ id: 'd1' });
  session.changed();
  await session.saveNow();
  assert.deepEqual([...drafts.photos.keys()].sort(), ['d1/p1', 'd1/p2']);
  shot.photos = [{ id: 'p1' }];
  session.changed();
  await session.saveNow();
  assert.deepEqual([...drafts.photos.keys()], ['d1/p1']);
  assert.equal(drafts.records.get('d1').photoCount, 1);
});
