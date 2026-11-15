// PRISM Collage Studio — drafts kept in this browser (docs/ux/collage-studio.md).
//
// Drafts live in the browser's own database (IndexedDB) on this computer: the collage, a small
// picture of it, and the original photo files, so a draft opens again after the browser is
// closed. Nothing is sent to PRISM. Every call fails softly: when the browser keeps no data (a
// private window, storage turned off) or is full, the editor goes on working and says why its
// draft was not kept.
(function (root) {
  'use strict';

  const DB_NAME = 'prism-collage-studio';
  const DB_VERSION = 1;
  let opening = null;

  /** 'full' when the browser has no room left, otherwise 'blocked'. */
  function reasonOf(error) {
    const name = error && (error.name || error.target?.error?.name);
    return name === 'QuotaExceededError' ? 'full' : 'blocked';
  }

  function request(req) {
    return new Promise((resolve, reject) => {
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
    });
  }

  function done(tx) {
    return new Promise((resolve, reject) => {
      tx.oncomplete = () => resolve();
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error || new DOMException('Aborted', 'AbortError'));
    });
  }

  /** The database, or a rejection with a reason when this browser keeps no data. */
  function open() {
    if (opening) return opening;
    opening = new Promise((resolve, reject) => {
      if (!root.indexedDB) { reject(Object.assign(new Error('No IndexedDB'), { name: 'NotSupported' })); return; }
      let req;
      try {
        req = root.indexedDB.open(DB_NAME, DB_VERSION);
      } catch (error) {
        reject(error);
        return;
      }
      req.onupgradeneeded = () => {
        const db = req.result;
        if (!db.objectStoreNames.contains('drafts')) db.createObjectStore('drafts', { keyPath: 'id' });
        if (!db.objectStoreNames.contains('photos')) {
          const photos = db.createObjectStore('photos', { keyPath: 'key' });
          photos.createIndex('draft', 'draft', { unique: false });
        }
      };
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => reject(req.error);
      req.onblocked = () => reject(Object.assign(new Error('Blocked'), { name: 'Blocked' }));
    });
    opening.catch(() => { opening = null; });
    return opening;
  }

  /** Every draft record (without its photos), deleted ones included. */
  async function list() {
    const db = await open();
    return request(db.transaction('drafts').objectStore('drafts').getAll());
  }

  async function get(id) {
    const db = await open();
    return request(db.transaction('drafts').objectStore('drafts').get(id));
  }

  /** The photos a draft holds: { id, name, file, width, height }. */
  async function photosOf(id) {
    const db = await open();
    return request(db.transaction('photos').objectStore('photos').index('draft').getAll(id));
  }

  /**
   * Saves a draft in one go: the draft record and the photos it did not hold yet, so a draft is
   * never stored half written. Photos already stored are not written again.
   */
  async function save(draft, newPhotos) {
    const db = await open();
    const tx = db.transaction(['drafts', 'photos'], 'readwrite');
    tx.objectStore('drafts').put(draft);
    const store = tx.objectStore('photos');
    for (const photo of newPhotos || []) {
      store.put({ key: `${draft.id}/${photo.id}`, draft: draft.id, id: photo.id, name: photo.name, file: photo.file, width: photo.width, height: photo.height });
    }
    await done(tx);
  }

  /** Hides a draft at once; it can be brought back with restore() until it is purged. */
  async function markDeleted(id, at) {
    const db = await open();
    const tx = db.transaction('drafts', 'readwrite');
    const store = tx.objectStore('drafts');
    const draft = await request(store.get(id));
    if (draft) store.put({ ...draft, deletedAt: at });
    await done(tx);
  }

  async function restore(id) {
    const db = await open();
    const tx = db.transaction('drafts', 'readwrite');
    const store = tx.objectStore('drafts');
    const draft = await request(store.get(id));
    if (draft) store.put({ ...draft, deletedAt: null });
    await done(tx);
  }

  /** Removes a draft and its photos for good. */
  async function purge(id) {
    const db = await open();
    const tx = db.transaction(['drafts', 'photos'], 'readwrite');
    tx.objectStore('drafts').delete(id);
    const photos = tx.objectStore('photos');
    const keys = await request(photos.index('draft').getAllKeys(id));
    for (const key of keys) photos.delete(key);
    await done(tx);
  }

  /** Bytes this site uses in the browser, or null when the browser does not say. */
  async function usedBytes() {
    try {
      const estimate = await root.navigator?.storage?.estimate?.();
      return estimate && Number.isFinite(estimate.usage) ? estimate.usage : null;
    } catch {
      return null;
    }
  }

  /** Asks the browser to keep the drafts when it clears space; the answer is the browser's. */
  async function keep() {
    try {
      return Boolean(await root.navigator?.storage?.persist?.());
    } catch {
      return false;
    }
  }

  root.PrismCollageDrafts = Object.freeze({ open, list, get, photosOf, save, markDeleted, restore, purge, usedBytes, keep, reasonOf });
})(typeof self !== 'undefined' ? self : this);
