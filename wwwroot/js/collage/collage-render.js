// PRISM Collage Studio — drawing the collage for Save, from the original photos, with the same
// drawing as the page (collage-draw.js) so the saved picture matches the screen
// (docs/ux/collage-review-fixes.md, PR 12). It knows nothing of the page or the dialog.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageRender = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  /**
   * core and draw are collage-core.js and collage-draw.js; measureText and fontsReady come from
   * the page's text drawing; stickerImage(item, true) resolves a sticker's picture or null.
   * makeCanvas and decode default to the browser's and can be replaced in tests.
   */
  function createRenderer({
    core,
    draw,
    measureText,
    fontsReady,
    stickerImage,
    makeCanvas = () => document.createElement('canvas'),
    decode = (file, options) => createImageBitmap(file, options)
  }) {
    /**
     * The largest canvas this browser will really draw, up to `width` × `height`: some browsers
     * give a blank canvas past their limit instead of an error, so a pixel is drawn and read back.
     */
    function largestCanvas(width, height) {
      let size = { width, height, scaled: false };
      for (let attempt = 0; attempt < 10; attempt += 1) {
        const probe = makeCanvas();
        probe.width = size.width;
        probe.height = size.height;
        let works = false;
        try {
          const context = probe.getContext('2d');
          context.fillStyle = '#000';
          context.fillRect(size.width - 1, size.height - 1, 1, 1);
          works = context.getImageData(size.width - 1, size.height - 1, 1, 1).data[3] === 255;
        } catch {
          works = false;
        }
        probe.width = 0;
        probe.height = 0;
        if (works) return size;
        size = { ...core.fitArea(size.width, size.height, size.width * size.height * 0.7), scaled: true };
      }
      return null;
    }

    /**
     * Draws the collage from the original photos, each read only as large as its frame needs.
     * When drawing fails, the canvas is let go before the error is passed on.
     */
    async function drawCollage(collage, photos, width, height) {
      const canvas = makeCanvas();
      canvas.width = width;
      canvas.height = height;
      try {
        return await drawOnto(canvas, collage, photos, width, height);
      } catch (error) {
        release(canvas);
        throw error;
      }
    }

    async function drawOnto(canvas, collage, photos, width, height) {
      const context = canvas.getContext('2d');
      context.imageSmoothingQuality = 'high';
      const s = width / core.PAGE_WIDTH;
      let backPhoto = null;
      // What could not be drawn now: photos shown grey, stickers PRISM could not send.
      let leftOutPhotos = 0;
      let leftOutStickers = 0;
      if (collage.background?.type === 'photo' && photos.get(collage.background.photo)?.missing) leftOutPhotos += 1;
      else if (collage.background?.type === 'photo' && photos.get(collage.background.photo)) {
        const photo = photos.get(collage.background.photo);
        const bitmap = await decode(photo.file, { resizeWidth: Math.min(photo.width, width), resizeQuality: 'high' });
        backPhoto = { source: bitmap, width: bitmap.width, height: bitmap.height };
      }
      try {
        context.save();
        context.scale(s, s);
        draw.drawBackground(context, collage.background, core.PAGE_WIDTH, height / s, { photo: backPhoto, pixelsPerUnit: s });
        context.restore();
      } finally {
        backPhoto?.source.close?.();
      }
      await fontsReady();
      const stickerPictures = new Map();
      for (const item of core.itemsToSave(collage)) {
        if (item.kind === 'sticker') stickerPictures.set(item.id, await stickerImage(item, true));
      }
      for (const item of core.itemsToSave(collage)) {
        if (item.kind === 'sticker') {
          const image = stickerPictures.get(item.id);
          if (!image) { leftOutStickers += 1; continue; }
          context.save();
          context.globalAlpha = item.opacity / 100;
          context.translate(item.x * s, item.y * s);
          context.rotate(item.angle * Math.PI / 180);
          context.scale(s, s);
          context.translate(-item.width / 2, -core.itemHeight(item) / 2);
          draw.drawSticker(context, item, image, s);
          context.restore();
          continue;
        }
        if (item.kind === 'shape') {
          context.save();
          context.globalAlpha = item.opacity / 100;
          context.translate(item.x * s, item.y * s);
          context.rotate(item.angle * Math.PI / 180);
          context.scale(s, s);
          context.translate(-item.width / 2, -core.itemHeight(item) / 2);
          draw.drawShape(context, item);
          context.restore();
          continue;
        }
        if (item.kind === 'text') {
          const layout = core.layoutText(item, measureText);
          context.save();
          context.globalAlpha = item.opacity / 100;
          context.translate(item.x * s, item.y * s);
          context.rotate(item.angle * Math.PI / 180);
          context.scale(s, s);
          context.translate(-layout.width / 2, -layout.height / 2);
          draw.drawText(context, item, layout, s);
          context.restore();
          continue;
        }
        const photo = photos.get(item.photo);
        if (!photo) continue;
        if (photo.missing) { leftOutPhotos += 1; continue; }
        const inner = core.innerBox(item);
        const want = core.decodeSize(photo, inner.width / inner.height, item.crop, inner.width * s);
        // One original at a time, read at the size this frame needs and closed straight after.
        const bitmap = await decode(photo.file, { resizeWidth: want.width, resizeHeight: want.height, resizeQuality: 'high' });
        try {
          context.save();
          context.globalAlpha = item.opacity / 100;
          context.translate(item.x * s, item.y * s);
          context.rotate(item.angle * Math.PI / 180);
          context.scale(s, s);
          context.translate(-item.width / 2, -core.itemHeight(item) / 2);
          // The same drawing as on the page: frame, shape, look, caption and tape.
          draw.drawPhoto(context, item, bitmap, bitmap.width, bitmap.height, s);
          context.restore();
        } finally {
          bitmap.close?.();
        }
      }
      return { canvas, leftOutPhotos, leftOutStickers };
    }

    const toBytes = (canvas, type, quality) => new Promise((resolve, reject) => {
      canvas.toBlob((blob) => (blob ? blob.arrayBuffer().then((buffer) => resolve(new Uint8Array(buffer)), reject) : reject(new Error('encode'))), type, quality);
    });

    /** Lets go of a canvas's pixels at once, rather than when the browser next tidies up. */
    function release(canvas) {
      if (!canvas) return;
      canvas.width = 0;
      canvas.height = 0;
    }

    /**
     * Encodes the canvas drawn at this size; a smaller copy made for it is let go straight
     * after, so the size search for a JPG holds one copy at a time.
     */
    async function scaledBytes(source, width, height, type, quality) {
      const small = scaledCanvas(source, width, height);
      try {
        return await toBytes(small, type, quality);
      } finally {
        if (small !== source) release(small);
      }
    }

    /** The canvas drawn smaller, for the size search when a JPG must fit a file size. */
    function scaledCanvas(source, width, height) {
      if (width === source.width && height === source.height) return source;
      const small = makeCanvas();
      small.width = width;
      small.height = height;
      const context = small.getContext('2d');
      context.imageSmoothingQuality = 'high';
      context.drawImage(source, 0, 0, width, height);
      return small;
    }


    return Object.freeze({ largestCanvas, drawCollage, scaledCanvas, scaledBytes, toBytes, release });
  }

  return Object.freeze({ createRenderer });
});
