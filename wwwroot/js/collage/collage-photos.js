// PRISM Collage Studio — reading photos for the editor: a working copy decoded straight at its
// working size, a small picture for the tray, and the grey stand-in for a draft photo this browser
// cannot open now (docs/ux/collage-review-fixes.md, PRs 2 and 13); working copies share a pixel
// budget and removed photos are let go (docs/ux/collage-review-3-fixes.md, PR 9). Uses the browser's own image
// decoding; nothing is sent anywhere.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollagePhotos = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  /**
   * Lets go of a photo's working copy at once, rather than when the browser next tidies up: its
   * canvas is emptied and its tray picture dropped. The original file is not touched.
   */
  function releasePhoto(photo) {
    if (!photo) return;
    if (photo.canvas) {
      photo.canvas.width = 0;
      photo.canvas.height = 0;
    }
    photo.canvas = null;
    photo.thumbnail = '';
    photo.released = true;
  }

  /**
   * Takes out of `photos` (a Map by id) and lets go of every removed photo that no Undo or Redo
   * step can bring back; `mentions(id)` is the change log's. Returns the ids let go.
   */
  function forgetUnreachable(photos, mentions) {
    const gone = [];
    for (const photo of [...photos.values()]) {
      if (!photo.removed || photo.importing || mentions(photo.id)) continue;
      photos.delete(photo.id);
      releasePhoto(photo);
      gone.push(photo.id);
    }
    return gone;
  }

  /**
   * core is collage-core.js; nextId(prefix) makes a new id. makeCanvas can be replaced in tests.
   */
  function createPhotoReader({ core, nextId, makeCanvas = () => document.createElement('canvas') }) {
    /** The grey stand-in for a draft photo this browser cannot open now; its file is kept. */
    function missingPhoto(entry) {
      const canvas = makeCanvas();
      canvas.width = 4;
      canvas.height = 3;
      const context = canvas.getContext('2d');
      if (context) {
        context.fillStyle = '#9aa3ad';
        context.fillRect(0, 0, 4, 3);
      }
      // The photo's own size, stored with it, so crops and frames are judged against the real photo
      // and the draft keeps them exactly; 4 by 3 only for a record from before sizes were stored.
      const known = Number(entry.width) > 0 && Number(entry.height) > 0;
      const width = known ? Number(entry.width) : 4;
      const height = known ? Number(entry.height) : 3;
      return {
        id: entry.id,
        name: entry.name,
        file: entry.file,
        canvas,
        thumbnail: canvas.toDataURL('image/png'),
        width,
        height,
        aspect: width / height,
        missing: true
      };
    }

    /**
     * The tray's picture of a photo: a small JPEG drawn once from the working copy, so the tray
     * never makes the browser decode the original file again.
     */
    function thumbnailOf(canvas) {
      const size = core.workingSize(canvas.width, canvas.height, core.PHOTO_LIMITS.thumbnailSide);
      const small = makeCanvas();
      small.width = size.width;
      small.height = size.height;
      const context = small.getContext('2d');
      context.imageSmoothingQuality = 'high';
      context.drawImage(canvas, 0, 0, size.width, size.height);
      return small.toDataURL('image/jpeg', 0.8);
    }

    /**
     * A photo's size as it is shown (turned by its camera orientation), read from the file
     * without decoding all its pixels.
     */
    async function pictureSize(file) {
      const address = URL.createObjectURL(file);
      try {
        const image = new Image();
        image.decoding = 'async';
        await new Promise((resolve, reject) => {
          image.onload = resolve;
          image.onerror = () => reject(new Error('unreadable'));
          image.src = address;
        });
        return { width: image.naturalWidth, height: image.naturalHeight };
      } finally {
        URL.revokeObjectURL(address);
      }
    }

    /**
     * A photo read for the editor: a working copy at most 2000 pixels on its long side, and
     * smaller when the collage keeps many photos (`count`, the photos it will hold with this one),
     * so all working copies stay within core.PHOTO_LIMITS.workingPixels. It is decoded straight at
     * that size so a large phone photo never sits in memory at full size. The original file is
     * kept for Save, which draws from it at full quality. Throws a reason ('unreadable',
     * 'too-many-pixels').
     */
    async function readPhoto(entry, keepId = null, { count = 1 } = {}) {
      const natural = await pictureSize(entry.file);
      if (!(natural.width > 0) || !(natural.height > 0)) throw Object.assign(new Error('unreadable'), { reason: 'unreadable' });
      if (natural.width * natural.height > core.PHOTO_LIMITS.maxPixels) {
        throw Object.assign(new Error('too many pixels'), { reason: 'too-many-pixels' });
      }
      const size = core.budgetedSize(natural.width, natural.height, count);
      const bitmap = await createImageBitmap(entry.file, { resizeWidth: size.width, resizeHeight: size.height, resizeQuality: 'high' });
      try {
        const canvas = makeCanvas();
        canvas.width = size.width;
        canvas.height = size.height;
        const context = canvas.getContext('2d');
        if (!context) throw Object.assign(new Error('no canvas'), { reason: 'unreadable' });
        context.drawImage(bitmap, 0, 0, size.width, size.height);
        return {
          id: keepId || nextId('p'),
          name: entry.name,
          file: entry.file,
          canvas,
          thumbnail: thumbnailOf(canvas),
          width: natural.width,
          height: natural.height,
          aspect: natural.width / natural.height
        };
      } finally {
        bitmap.close?.();
      }
    }

    /**
     * Keeps every working copy in `photos` (a Map by id) within its share of the budget for
     * `count` photos (by default the photos it holds now; before a batch is read, the photos it
     * will hold after it, so the budget holds while the batch is decoded): a copy made when the collage had fewer photos is drawn again
     * smaller from itself (no new decoding) and the larger one let go. A copy is never made larger
     * again when photos are removed; it already shows well. Returns the ids made smaller. The
     * page and the background draw from photos.get(id).canvas, so they pick up the new copy on
     * their next redraw.
     */
    function fitBudget(photos, count = photos.size) {
      const each = core.workingPixelsEach(count);
      const shrunk = [];
      for (const photo of photos.values()) {
        const canvas = photo.canvas;
        if (!canvas || photo.missing || canvas.width * canvas.height <= each) continue;
        const size = core.budgetedSize(canvas.width, canvas.height, count);
        const smaller = makeCanvas();
        smaller.width = size.width;
        smaller.height = size.height;
        const context = smaller.getContext('2d');
        if (!context) continue;
        context.imageSmoothingQuality = 'high';
        context.drawImage(canvas, 0, 0, size.width, size.height);
        photo.canvas = smaller;
        canvas.width = 0;
        canvas.height = 0;
        shrunk.push(photo.id);
      }
      return shrunk;
    }

    return Object.freeze({ readPhoto, missingPhoto, thumbnailOf, pictureSize, fitBudget });
  }

  return Object.freeze({ createPhotoReader, releasePhoto, forgetUnreachable });
});
