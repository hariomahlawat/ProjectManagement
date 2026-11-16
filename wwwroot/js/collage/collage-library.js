/*
 * Collage Studio: what the page reads from PRISM (docs/ux/collage-studio-library-and-unit-stickers.md).
 * This is the only collage script that talks to the PRISM server, and only by GET to the fixed
 * addresses below: the crests and logos list, the PRISM Photos list, and a listed photo's
 * original picture. Nothing a user makes, and no photo from the computer, is ever sent.
 */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else {
    root.PrismCollageLibrary = factory();
    // PRISM may be served under a sub-path; the page names it (Url.Content("~/")).
    root.PrismCollageLibrary.setBasePath(root.document?.querySelector('[data-collage]')?.dataset?.appBase);
  }
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  // Read under the app's path base (setBasePath); these are the addresses at the site root.
  const STICKERS_URL = '/api/collage/stickers';
  const LIBRARY_URL = '/api/collage/library-photos';
  // The only pictures fetched: a listed photo's original, from the same page PRISM Photos uses
  // for its kind (MediaLibraryItemAddressResolver on the server), so that page checks access.
  // Same-origin paths only, each with exactly its own query string and nothing more.
  const ID = '[1-9][0-9]{0,17}';
  const GUID = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';
  const VERSION = '[A-Za-z0-9]{1,128}';
  const pictureAddress = (sizes) => new RegExp('^(?:' + [
    // A project photo: the project's photo page.
    `\\/Projects\\/${ID}\\/Photos\\/${ID}\\/View\\/(?:${sizes.project})(?:\\?v=${VERSION})?`,
    // A visit or social media photo: that record's photo page.
    `\\/ProjectOfficeReports\\/(?:Visits|SocialMedia)\\/ViewPhoto\\/${GUID}\\/Photos\\/${GUID}\\?size=(?:${sizes.report})(?:&v=${VERSION})?`,
    // An activity photo or a photo from another folder: the library's own page.
    `\\/Photos\\/Media\\/${ID}\\/(?:${sizes.media})\\?v=[0-9]{1,9}`
  ].join('|') + ')$');
  // A project photo's page has no "original"; xl is its largest size.
  const LIBRARY_PICTURE = pictureAddress({ project: 'xl', report: 'original', media: 'original' });
  const LIBRARY_THUMB = pictureAddress({ project: 'md', report: 'md|feed', media: 'thumb' });
  /**
   * The app's path base ("" at the site root, "/prism" under a sub-path), from the page's
   * data-app-base. Anything that is not a plain path is read as the site root.
   */
  function cleanBase(value) {
    const text = String(value || '').trim().replace(/\/+$/, '');
    return /^(?:\/[A-Za-z0-9._~%!$&'()*+,;=:@-]+)*$/.test(text) ? text : '';
  }

  let base = '';

  /** Sets the app's path base the addresses here are read under; returns it as used. */
  function setBasePath(value) {
    base = cleanBase(value);
    return base;
  }

  /** True when the address is PRISM's own, under the path base, and below it matches the pattern. */
  function isPrismAddress(pattern, address) {
    const text = String(address || '');
    return text.startsWith(`${base}/`) && pattern.test(text.slice(base.length));
  }

  const TYPE_ENDINGS = { 'image/jpeg': '.jpg', 'image/png': '.png', 'image/webp': '.webp', 'image/gif': '.gif', 'image/bmp': '.bmp' };

  /** The administrator's crests and logos, in picker order. Rejects when PRISM cannot be reached. */
  async function loadStickers() {
    const response = await fetch(`${base}${STICKERS_URL}`, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
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
    const response = await fetch(`${base}${LIBRARY_URL}?${query}`, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
    if (!response.ok) throw new Error(`PRISM Photos: ${response.status}`);
    const body = await response.json();
    const photos = (Array.isArray(body?.photos) ? body.photos : []).filter((photo) =>
      Number.isInteger(photo?.id) && photo.id > 0
      && isPrismAddress(LIBRARY_PICTURE, photo.url) && isPrismAddress(LIBRARY_THUMB, photo.thumbUrl));
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

  /**
   * A listed photo's original, read into the browser as a File, ready for the usual checks. With
   * maxBytes, a photo the server says is larger is refused before its body is read (an error whose
   * reason is 'too-large').
   */
  async function readLibraryPhoto(photo, { maxBytes = 0 } = {}) {
    const address = String(photo?.url || '');
    if (!isPrismAddress(LIBRARY_PICTURE, address)) throw new Error('Not a PRISM Photos picture');
    const response = await fetch(address, { credentials: 'same-origin' });
    if (!response.ok) throw new Error(`PRISM Photos picture: ${response.status}`);
    const tooLarge = () => Object.assign(new Error('Larger than a collage takes'), { reason: 'too-large' });
    const declared = Number(response.headers?.get?.('Content-Length'));
    if (maxBytes && declared > maxBytes) {
      try { await response.body?.cancel(); } catch { /* nothing more to read */ }
      throw tooLarge();
    }
    const blob = await response.blob();
    if (maxBytes && blob.size > maxBytes) throw tooLarge();
    return new File([blob], fileNameFor(photo.title, blob.type), { type: blob.type });
  }

  return Object.freeze({ STICKERS_URL, LIBRARY_URL, LIBRARY_PICTURE, LIBRARY_THUMB, setBasePath, isPrismAddress, loadStickers, libraryQuery, loadLibraryPage, fileNameFor, readLibraryPhoto });
});
