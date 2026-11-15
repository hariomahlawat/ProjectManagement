/*
 * Collage Studio: what the page reads from PRISM (docs/ux/collage-studio-library-and-unit-stickers.md).
 * This is the only collage script that talks to the PRISM server, and only by GET to the fixed
 * addresses below. Nothing a user makes, and no photo from the computer, is ever sent.
 */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.PrismCollageLibrary = factory();
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  const STICKERS_URL = '/api/collage/stickers';

  /** The administrator's crests and logos, in picker order. Rejects when PRISM cannot be reached. */
  async function loadStickers() {
    const response = await fetch(STICKERS_URL, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
    if (!response.ok) throw new Error(`Crests and logos: ${response.status}`);
    const body = await response.json();
    return Array.isArray(body?.stickers) ? body.stickers : [];
  }

  return Object.freeze({ STICKERS_URL, loadStickers });
});
