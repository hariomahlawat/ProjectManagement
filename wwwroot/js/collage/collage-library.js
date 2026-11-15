/*
 * Collage Studio: what the page reads from PRISM (docs/ux/collage-studio-library-and-unit-stickers.md).
 * This is the only collage script that talks to the PRISM server, and only by GET to the fixed
 * addresses below: the crests and logos list, the PRISM Photos list, and a listed photo's
 * original picture. Nothing a user makes, and no photo from the computer, is ever sent.
 */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.PrismCollageLibrary = factory();
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  const STICKERS_URL = '/api/collage/stickers';
  const LIBRARY_URL = '/api/collage/library-photos';
  // The only pictures fetched: a listed photo's original from the library's own page.
  const LIBRARY_PICTURE = /^\/Photos\/Media\/[1-9][0-9]{0,17}\/original\?v=[0-9]{1,9}$/;
  const TYPE_ENDINGS = { 'image/jpeg': '.jpg', 'image/png': '.png', 'image/webp': '.webp', 'image/gif': '.gif', 'image/bmp': '.bmp' };

  /** The administrator's crests and logos, in picker order. Rejects when PRISM cannot be reached. */
  async function loadStickers() {
    const response = await fetch(STICKERS_URL, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
    if (!response.ok) throw new Error(`Crests and logos: ${response.status}`);
    const body = await response.json();
    return Array.isArray(body?.stickers) ? body.stickers : [];
  }

  /** The query string of one page of PRISM Photos: words, project, year and page, nothing else. */
  function libraryQuery({ q = '', projectId = '', year = '', page = 1 } = {}) {
    const query = new URLSearchParams();
    const words = String(q || '').trim().slice(0, 200);
    if (words) query.set('q', words);
    if (/^[1-9][0-9]{0,8}$/.test(String(projectId))) query.set('projectId', String(projectId));
    if (/^[0-9]{4}$/.test(String(year))) query.set('year', String(year));
    const pageNumber = Number.isInteger(page) && page > 1 ? page : 1;
    query.set('page', String(pageNumber));
    return query.toString();
  }

  /**
   * One page of PRISM Photos: { available, message, photos, page, hasMore, projects, years }.
   * Only photos with PRISM's own picture addresses are kept.
   */
  async function loadLibraryPage(filters) {
    const query = libraryQuery(filters);
    const response = await fetch(`${LIBRARY_URL}?${query}`, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
    if (!response.ok) throw new Error(`PRISM Photos: ${response.status}`);
    const body = await response.json();
    const photos = (Array.isArray(body?.photos) ? body.photos : []).filter((photo) =>
      Number.isInteger(photo?.id) && photo.id > 0 && LIBRARY_PICTURE.test(String(photo.url || '')));
    return {
      available: body?.available === true,
      message: typeof body?.message === 'string' ? body.message : '',
      photos,
      page: Number.isInteger(body?.page) ? body.page : 1,
      hasMore: body?.hasMore === true,
      projects: Array.isArray(body?.projects) ? body.projects : null,
      years: Array.isArray(body?.years) ? body.years : null
    };
  }

  /** A file name for a library photo: its title, made safe, with the ending of its type. */
  function fileNameFor(title, type) {
    const stem = String(title || 'PRISM photo').replace(/[\\/:*?"<>|]+/g, ' ').trim().slice(0, 80) || 'PRISM photo';
    const ending = TYPE_ENDINGS[type] || '';
    return stem.toLowerCase().endsWith(ending) ? stem : `${stem}${ending}`;
  }

  /** A listed photo's original, read into the browser as a File, ready for the usual checks. */
  async function readLibraryPhoto(photo) {
    const address = String(photo?.url || '');
    if (!LIBRARY_PICTURE.test(address)) throw new Error('Not a PRISM Photos picture');
    const response = await fetch(address, { credentials: 'same-origin' });
    if (!response.ok) throw new Error(`PRISM Photos picture: ${response.status}`);
    const blob = await response.blob();
    return new File([blob], fileNameFor(photo.title, blob.type), { type: blob.type });
  }

  return Object.freeze({ STICKERS_URL, LIBRARY_URL, LIBRARY_PICTURE, loadStickers, libraryQuery, loadLibraryPage, fileNameFor, readLibraryPhoto });
});
