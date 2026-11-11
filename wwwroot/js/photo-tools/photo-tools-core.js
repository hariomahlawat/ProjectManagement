// PRISM Photo Tools — core operations.
//
// Everything in this file runs in the user's browser (and in Node for tests). Photo Tools
// never uploads photos to PRISM: users change their own photos locally and download the
// result. Drawing and encoding are passed in, so the rules stay testable without a canvas.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) {
    module.exports = api;
  } else {
    root.PrismPhotoTools = api;
  }
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  class PhotoToolError extends Error {
    constructor(code, message) {
      super(message);
      this.name = 'PhotoToolError';
      this.code = code;
    }
  }

  const TYPES = Object.freeze({
    jpeg: Object.freeze({ mime: 'image/jpeg', extension: 'jpg', label: 'JPG', lossy: true }),
    png: Object.freeze({ mime: 'image/png', extension: 'png', label: 'PNG', lossy: false }),
    webp: Object.freeze({ mime: 'image/webp', extension: 'webp', label: 'WEBP', lossy: true })
  });

  // Quality the search starts from and the lowest it will go before making the photo smaller.
  const BEST_QUALITY = 0.92;
  const LOWEST_QUALITY = 0.4;
  const SMALLEST_SIDE = 16;

  // ---------------------------------------------------------------------------
  // Sizes and names
  // ---------------------------------------------------------------------------

  function formatBytes(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  /** "100" + "KB" → 102400. Returns null when the value is not a positive number. */
  function parseTargetBytes(value, unit) {
    const number = Number(String(value ?? '').trim().replace(',', '.'));
    if (!Number.isFinite(number) || number <= 0) return null;
    const factor = String(unit).toUpperCase() === 'MB' ? 1024 * 1024 : 1024;
    return Math.round(number * factor);
  }

  /**
   * New width and height for a resize choice. Photos are never made larger.
   * mode: { type: 'keep' } | { type: 'longest', value: pixels } | { type: 'percent', value: 1-100 }
   */
  function resizeDimensions(width, height, mode) {
    let scale = 1;
    if (mode && mode.type === 'longest' && mode.value > 0) {
      scale = Math.min(1, mode.value / Math.max(width, height));
    } else if (mode && mode.type === 'percent' && mode.value > 0) {
      scale = Math.min(1, mode.value / 100);
    }
    return scaleDimensions(width, height, scale);
  }

  function scaleDimensions(width, height, scale) {
    return {
      width: Math.max(1, Math.round(width * scale)),
      height: Math.max(1, Math.round(height * scale))
    };
  }

  /** The type to save as: 'jpeg' | 'png' | 'webp' | 'same' (same as the original where possible). */
  function outputType(sourceMime, choice) {
    if (choice && choice !== 'same' && TYPES[choice]) return TYPES[choice];
    const same = Object.values(TYPES).find(type => type.mime === sourceMime);
    return same || TYPES.jpeg;
  }

  function baseName(fileName, fallback) {
    const name = String(fileName || '').replace(/\.[^.\\/]+$/, '').trim();
    return name || fallback || 'photo';
  }

  /** Output file names for a batch; repeated names get " (2)", " (3)" so a ZIP has no clashes. */
  function outputNames(items, suffix) {
    const used = new Map();
    return items.map(item => {
      const stem = `${baseName(item.name)}${suffix || ''}`;
      const key = `${stem}.${item.extension}`.toLowerCase();
      const count = (used.get(key) || 0) + 1;
      used.set(key, count);
      return count === 1 ? `${stem}.${item.extension}` : `${stem} (${count}).${item.extension}`;
    });
  }

  // ---------------------------------------------------------------------------
  // Pixels
  // ---------------------------------------------------------------------------

  /** Turns RGBA pixels grey in place (Rec. 601 weights, as a black-and-white print would). */
  function toGreyscale(pixels) {
    for (let i = 0; i < pixels.length; i += 4) {
      const grey = Math.round(0.299 * pixels[i] + 0.587 * pixels[i + 1] + 0.114 * pixels[i + 2]);
      pixels[i] = grey;
      pixels[i + 1] = grey;
      pixels[i + 2] = grey;
    }
    return pixels;
  }

  // ---------------------------------------------------------------------------
  // Target file size
  // ---------------------------------------------------------------------------

  /**
   * Finds the best-looking encoding that fits a file size.
   *
   * encode(width, height, quality) → Promise<Uint8Array>. For JPG and WEBP the quality is
   * searched first; only when the lowest acceptable quality is still too big are the
   * dimensions stepped down. PNG has no quality, so only its dimensions change.
   *
   * Returns { bytes, width, height, quality, reached }. reached is false when even the
   * smallest allowed photo is over the target, so callers never claim a size they missed.
   */
  async function encodeToTarget(options) {
    const { encode, lossy } = options;
    const target = options.targetBytes > 0 ? options.targetBytes : null;
    let { width, height } = options;

    if (!target) {
      const quality = lossy ? 0.9 : 1;
      return { bytes: await encode(width, height, quality), width, height, quality, reached: true };
    }

    let smallest = null;
    for (let round = 0; round < 12; round += 1) {
      const top = lossy ? BEST_QUALITY : 1;
      let bytes = await encode(width, height, top);
      if (bytes.length <= target) {
        return { bytes, width, height, quality: top, reached: true };
      }

      let floorBytes = bytes;
      if (lossy) {
        floorBytes = await encode(width, height, LOWEST_QUALITY);
        if (floorBytes.length <= target) {
          let best = { bytes: floorBytes, quality: LOWEST_QUALITY };
          let low = LOWEST_QUALITY;
          let high = top;
          for (let step = 0; step < 6; step += 1) {
            const quality = (low + high) / 2;
            bytes = await encode(width, height, quality);
            if (bytes.length <= target) {
              best = { bytes, quality };
              low = quality;
            } else {
              high = quality;
            }
          }
          return { bytes: best.bytes, width, height, quality: best.quality, reached: true };
        }
      }

      smallest = { bytes: floorBytes, width, height, quality: lossy ? LOWEST_QUALITY : 1, reached: false };
      if (Math.min(width, height) <= SMALLEST_SIDE) break;

      // File size follows the pixel count, so aim a little under the target and try again.
      const scale = Math.min(0.9, Math.max(0.5, Math.sqrt(target / floorBytes.length) * 0.95));
      const next = scaleDimensions(width, height, scale);
      if (next.width === width && next.height === height) break;
      width = next.width;
      height = next.height;
    }
    return smallest;
  }

  return Object.freeze({
    PhotoToolError,
    TYPES,
    BEST_QUALITY,
    LOWEST_QUALITY,
    formatBytes,
    parseTargetBytes,
    resizeDimensions,
    scaleDimensions,
    outputType,
    baseName,
    outputNames,
    toGreyscale,
    encodeToTarget
  });
});
