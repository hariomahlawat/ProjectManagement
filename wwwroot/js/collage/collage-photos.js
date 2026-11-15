// PRISM Collage Studio — reading photos for the editor: a working copy decoded straight at its
// working size, a small picture for the tray, and the grey stand-in for a draft photo this browser
// cannot open now (docs/ux/collage-review-fixes.md, PRs 2 and 13). Uses the browser's own image
// decoding; nothing is sent anywhere.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollagePhotos = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  /** core is collage-core.js; nextId(prefix) makes a new id. */
  function createPhotoReader({ core, nextId }) {
    /**
     * The tray's picture of a photo: a small JPEG drawn once from the working copy, so the tray
     * never makes the browser decode the original file again.
     */
    /** The grey stand-in for a draft photo this browser cannot open now; its file is kept. */
    function missingPhoto(entry) {
      const canvas = document.createElement('canvas');
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

    function thumbnailOf(canvas) {
      const size = core.workingSize(canvas.width, canvas.height, core.PHOTO_LIMITS.thumbnailSide);
      const small = document.createElement('canvas');
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
     * A photo read for the editor: a working copy at most 2000 pixels on its long side, decoded
     * straight at that size so a large phone photo never sits in memory at full size. The
     * original file is kept for Save. Throws a reason ('unreadable', 'too-many-pixels').
     */
    async function readPhoto(entry, keepId = null) {
      const natural = await pictureSize(entry.file);
      if (!(natural.width > 0) || !(natural.height > 0)) throw Object.assign(new Error('unreadable'), { reason: 'unreadable' });
      if (natural.width * natural.height > core.PHOTO_LIMITS.maxPixels) {
        throw Object.assign(new Error('too many pixels'), { reason: 'too-many-pixels' });
      }
      const size = core.workingSize(natural.width, natural.height);
      const bitmap = await createImageBitmap(entry.file, { resizeWidth: size.width, resizeHeight: size.height, resizeQuality: 'high' });
      try {
        const canvas = document.createElement('canvas');
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

    return Object.freeze({ readPhoto, missingPhoto, thumbnailOf, pictureSize });
  }

  return Object.freeze({ createPhotoReader });
});
