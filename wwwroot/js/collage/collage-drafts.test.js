// Tests for the drafts store, collage-drafts.js (docs/ux/collage-review-3-fixes.md, PR 12): run
// against a small in-memory IndexedDB, and against browsers whose IndexedDB is missing, throws,
// or is full. Every call must fail softly (a rejection the editor turns into a status), never
// break the page.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const SOURCE = fs.readFileSync(path.join(__dirname, 'collage-drafts.js'), 'utf8');

/** A fresh copy of the store (it keeps its open database), with `self` as the window it sees. */
function loadDrafts(self) {
  vm.runInNewContext(SOURCE, { self, DOMException }, { filename: 'collage-drafts.js' });
  return self.PrismCollageDrafts;
}

const later = (fn) => setTimeout(fn, 0);
const quota = () => new DOMException('The quota has been exceeded.', 'QuotaExceededError');

/**
 * Just enough IndexedDB for collage-drafts.js: object stores with a key path, one index, get,
 * getAll, getAllKeys, put and delete; a transaction completes once no request is left, and aborts
 * when a request fails (later requests of an aborted transaction fail too). `failPuts` makes writes
 * fail as a full disk does: 'commit' when the transaction commits (as Chromium reports it), or
 * 'request' at the put itself.
 */
function memoryIndexedDB() {
  const databases = new Map();
  const options = { failPuts: false, opens: 0 };

  function makeRequest(tx, work) {
    const req = { result: undefined, error: null, onsuccess: null, onerror: null };
    if (tx) tx.pending += 1;
    later(() => {
      try {
        if (tx?.done) throw new DOMException('The transaction was aborted.', 'AbortError');
        req.result = work();
        req.onsuccess?.({ target: req });
      } catch (error) {
        req.error = error;
        req.onerror?.({ target: req });
        if (tx) tx.fail(error);
      }
      if (tx) { tx.pending -= 1; tx.settle(); }
    });
    return req;
  }

  function makeTransaction(db, names, mode) {
    const tx = {
      pending: 0, done: false, wrote: false, error: null, oncomplete: null, onerror: null, onabort: null,
      fail(error) {
        if (tx.done) return;
        tx.done = true;
        tx.error = error;
        tx.onerror?.({ target: tx });
        tx.onabort?.({ target: tx });
      },
      // Committed once the requests are answered and the code they woke has run.
      settle() {
        later(() => {
          if (tx.done || tx.pending) return;
          if (tx.wrote && options.failPuts === 'commit') { tx.fail(quota()); return; }
          tx.done = true;
          tx.oncomplete?.({ target: tx });
        });
      },
      objectStore(name) {
        if (![].concat(names).includes(name)) throw new DOMException('Not in this transaction', 'NotFoundError');
        const store = db.stores.get(name);
        const rows = store.rows;
        const read = (fn) => makeRequest(tx, fn);
        const write = (fn) => {
          if (mode !== 'readwrite') throw new DOMException('Read only', 'ReadOnlyError');
          tx.wrote = true;
          return makeRequest(tx, fn);
        };
        const byIndex = (indexPath, key) => [...rows.entries()].filter(([, row]) => row[indexPath] === key);
        return {
          put: (value) => write(() => {
            if (options.failPuts === 'request') throw quota();
            rows.set(value[store.keyPath], structuredClone(value));
            return value[store.keyPath];
          }),
          delete: (key) => write(() => { rows.delete(key); }),
          get: (key) => read(() => (rows.has(key) ? structuredClone(rows.get(key)) : undefined)),
          getAll: () => read(() => [...rows.values()].map((row) => structuredClone(row))),
          index: (indexName) => {
            const indexPath = store.indexes.get(indexName);
            return {
              getAll: (key) => read(() => byIndex(indexPath, key).map(([, row]) => structuredClone(row))),
              getAllKeys: (key) => read(() => byIndex(indexPath, key).map(([rowKey]) => rowKey))
            };
          }
        };
      }
    };
    tx.settle();
    return tx;
  }

  const factory = {
    options,
    databases,
    open(name, version) {
      options.opens += 1;
      const req = { result: null, error: null, onsuccess: null, onerror: null, onupgradeneeded: null, onblocked: null };
      later(() => {
        let db = databases.get(name);
        const upgrade = !db;
        if (!db) {
          db = {
            version,
            stores: new Map(),
            onversionchange: null,
            closed: false,
            objectStoreNames: { contains: (store) => db.stores.has(store) },
            createObjectStore(storeName, { keyPath }) {
              const store = { keyPath, rows: new Map(), indexes: new Map() };
              db.stores.set(storeName, store);
              return { createIndex: (indexName, indexPath) => { store.indexes.set(indexName, indexPath); } };
            },
            transaction: (names, mode = 'readonly') => {
              if (db.closed) throw new DOMException('The database connection is closing.', 'InvalidStateError');
              return makeTransaction(db, names, mode);
            },
            close() { db.closed = true; }
          };
          databases.set(name, db);
        }
        // Opened again after it was closed: a new connection to the same data.
        db.closed = false;
        req.result = db;
        if (upgrade) req.onupgradeneeded?.();
        req.onsuccess?.();
      });
      return req;
    }
  };
  return factory;
}

const photo = (id) => ({ id, name: `${id}.jpg`, file: `bytes of ${id}`, width: 1200, height: 900 });
const record = (id, extra = {}) => ({ id, title: `Draft ${id}`, saved: 1000, deletedAt: null, model: { items: [] }, photoCount: 0, ...extra });

test('a draft is saved with its photos, listed, read back and its photos found', async () => {
  const indexedDB = memoryIndexedDB();
  const drafts = loadDrafts({ indexedDB });
  await drafts.save(record('d1', { photoCount: 2 }), [photo('p1'), photo('p2')]);
  await drafts.save(record('d2'), [photo('p1')]);

  assert.deepEqual((await drafts.list()).map((d) => d.id).sort(), ['d1', 'd2']);
  assert.equal((await drafts.get('d1')).title, 'Draft d1');
  assert.equal(await drafts.get('nope'), undefined);
  const held = await drafts.photosOf('d1');
  assert.deepEqual(held.map((p) => p.id).sort(), ['p1', 'p2']);
  assert.deepEqual(held.find((p) => p.id === 'p1'), { key: 'd1/p1', draft: 'd1', id: 'p1', name: 'p1.jpg', file: 'bytes of p1', width: 1200, height: 900 });
  // Each draft holds its own copy of a photo.
  assert.deepEqual((await drafts.photosOf('d2')).map((p) => p.key), ['d2/p1']);
  // The database is opened once and reused.
  assert.equal(indexedDB.options.opens, 1);
});

test('saving again writes only new photos and lets go of photos the draft no longer has', async () => {
  const indexedDB = memoryIndexedDB();
  const drafts = loadDrafts({ indexedDB });
  await drafts.save(record('d1'), [photo('p1'), photo('p2')]);
  const rows = indexedDB.databases.get('prism-collage-studio').stores.get('photos').rows;
  rows.set('d1/p1', { ...rows.get('d1/p1'), file: 'kept as it was' });
  await drafts.save(record('d1', { title: 'Renamed' }), [photo('p1'), photo('p3')]);
  assert.deepEqual([...rows.keys()].sort(), ['d1/p1', 'd1/p3']);
  assert.equal(rows.get('d1/p1').file, 'kept as it was');
  assert.equal((await drafts.get('d1')).title, 'Renamed');
});

test('a draft marked deleted can be restored until it is purged with its photos', async () => {
  const indexedDB = memoryIndexedDB();
  const drafts = loadDrafts({ indexedDB });
  await drafts.save(record('d1'), [photo('p1')]);
  await drafts.save(record('d2'), [photo('p9')]);
  await drafts.markDeleted('d1', 5000);
  assert.equal((await drafts.get('d1')).deletedAt, 5000);
  await drafts.restore('d1');
  assert.equal((await drafts.get('d1')).deletedAt, null);
  // Marking or restoring a draft that is not there changes nothing.
  await drafts.markDeleted('gone', 1);
  await drafts.restore('gone');
  assert.equal(await drafts.get('gone'), undefined);

  await drafts.purge('d1');
  assert.equal(await drafts.get('d1'), undefined);
  assert.deepEqual(await drafts.photosOf('d1'), []);
  assert.deepEqual((await drafts.photosOf('d2')).map((p) => p.id), ['p9']);
});

test('a full browser rejects the save with a reason the editor reads as "full"', async () => {
  const indexedDB = memoryIndexedDB();
  const drafts = loadDrafts({ indexedDB });
  indexedDB.options.failPuts = 'commit';
  const error = await drafts.save(record('d1'), [photo('p1')]).then(() => null, (e) => e);
  assert.ok(error, 'the save is refused');
  assert.equal(drafts.reasonOf(error), 'full');
  // The store goes on working once there is room again.
  indexedDB.options.failPuts = false;
  await drafts.save(record('d1'), [photo('p1')]);
  assert.equal((await drafts.list()).length, 1);
});

test('a write refused at once is a refused save, not an unhandled rejection', async (t) => {
  const unhandled = [];
  const onUnhandled = (reason) => unhandled.push(reason);
  process.on('unhandledRejection', onUnhandled);
  t.after(() => process.off('unhandledRejection', onUnhandled));
  const indexedDB = memoryIndexedDB();
  const drafts = loadDrafts({ indexedDB });
  indexedDB.options.failPuts = 'request';
  const error = await drafts.save(record('d1'), [photo('p1')]).then(() => null, (e) => e);
  assert.ok(error, 'the save is refused');
  assert.equal(drafts.reasonOf(error), 'full');
  await new Promise((resolve) => setTimeout(resolve, 10));
  assert.deepEqual(unhandled, []);
});

test('every call fails softly when the browser has no IndexedDB', async () => {
  const drafts = loadDrafts({});
  for (const call of [() => drafts.list(), () => drafts.get('d1'), () => drafts.photosOf('d1'), () => drafts.save(record('d1'), []),
    () => drafts.markDeleted('d1', 1), () => drafts.restore('d1'), () => drafts.purge('d1')]) {
    const error = await call().then(() => null, (e) => e);
    assert.ok(error, 'the call is refused');
    assert.equal(drafts.reasonOf(error), 'blocked');
  }
  assert.equal(await drafts.usedBytes(), null);
  assert.equal(await drafts.keep(), false);
});

test('a browser whose IndexedDB throws (storage turned off) refuses softly, and is asked again next time', async () => {
  let throws = true;
  const memory = memoryIndexedDB();
  const indexedDB = {
    open: (...args) => {
      if (throws) throw new DOMException('The operation is insecure.', 'SecurityError');
      return memory.open(...args);
    }
  };
  const drafts = loadDrafts({ indexedDB });
  const error = await drafts.list().then(() => null, (e) => e);
  assert.equal(error.name, 'SecurityError');
  assert.equal(drafts.reasonOf(error), 'blocked');
  // The failed open is not kept: once the browser allows storage, drafts work.
  throws = false;
  await drafts.save(record('d1'), []);
  assert.equal((await drafts.list()).length, 1);
});

test('an open that errors or is blocked is refused, and a database another tab upgrades is let go', async () => {
  const errors = { open: () => { const req = {}; later(() => { req.error = new DOMException('Broken', 'UnknownError'); req.onerror(); }); return req; } };
  const failing = loadDrafts({ indexedDB: errors });
  assert.equal((await failing.get('d1').then(() => null, (e) => e)).name, 'UnknownError');

  const blocked = { open: () => { const req = {}; later(() => req.onblocked()); return req; } };
  assert.equal((await loadDrafts({ indexedDB: blocked }).list().then(() => null, (e) => e)).name, 'Blocked');

  const indexedDB = memoryIndexedDB();
  const drafts = loadDrafts({ indexedDB });
  const db = await drafts.open();
  db.onversionchange();
  assert.equal(db.closed, true);
  await drafts.list();
  assert.equal(indexedDB.options.opens, 2);
});

test('a database the browser closes is opened again by the next call', async () => {
  const indexedDB = memoryIndexedDB();
  const drafts = loadDrafts({ indexedDB });
  await drafts.save(record('d1'), []);
  const db = await drafts.open();
  // The browser closes the connection (storage cleared, its database process gone).
  db.close();
  db.onclose();
  assert.deepEqual((await drafts.list()).map((d) => d.id), ['d1']);
  await drafts.save(record('d2'), []);
  assert.equal(indexedDB.options.opens, 2);
});

test('the space used and keeping the drafts take the browser\'s answer, and never throw', async () => {
  const drafts = loadDrafts({ navigator: { storage: { estimate: async () => ({ usage: 4096 }), persist: async () => true } } });
  assert.equal(await drafts.usedBytes(), 4096);
  assert.equal(await drafts.keep(), true);
  const broken = loadDrafts({ navigator: { storage: { estimate: async () => { throw new Error('no'); }, persist: () => { throw new Error('no'); } } } });
  assert.equal(await broken.usedBytes(), null);
  assert.equal(await broken.keep(), false);
  assert.equal(await loadDrafts({ navigator: { storage: { estimate: async () => ({}) } } }).usedBytes(), null);
});

test('reasonOf: only a quota error is "full"', () => {
  const drafts = loadDrafts({});
  assert.equal(drafts.reasonOf(quota()), 'full');
  assert.equal(drafts.reasonOf({ target: { error: { name: 'QuotaExceededError' } } }), 'full');
  assert.equal(drafts.reasonOf(new DOMException('x', 'InvalidStateError')), 'blocked');
  assert.equal(drafts.reasonOf(null), 'blocked');
});
