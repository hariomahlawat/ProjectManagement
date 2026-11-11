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

  /** JPG, PNG and WEBP are supported; the name is checked too because some systems give no type. */
  function isSupportedPhoto(mime, fileName) {
    return /^image\/(jpeg|png|webp)$/.test(String(mime || '')) || /\.(jpe?g|png|webp)$/i.test(String(fileName || ''));
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
  // Lighting
  // ---------------------------------------------------------------------------

  /**
   * Measures a photo for the one-click lighting fix: the darkest and brightest levels that
   * hold real detail (the extreme half percent at each end is ignored), and how far each
   * colour is from neutral (a yellow indoor cast, a blue shade cast). Measured once on the
   * whole photo, then applied to the preview and the saved photo alike.
   */
  function measureLighting(pixels) {
    const histogram = new Uint32Array(256);
    const sums = [0, 0, 0];
    let count = 0;
    for (let i = 0; i < pixels.length; i += 4) {
      if (pixels[i + 3] === 0) continue;
      const light = Math.round(0.299 * pixels[i] + 0.587 * pixels[i + 1] + 0.114 * pixels[i + 2]);
      histogram[light] += 1;
      sums[0] += pixels[i];
      sums[1] += pixels[i + 1];
      sums[2] += pixels[i + 2];
      count += 1;
    }
    if (!count) return { low: 0, high: 255, gains: [1, 1, 1] };

    const cut = count * 0.005;
    let low = 0;
    for (let seen = 0; low < 255; low += 1) {
      seen += histogram[low];
      if (seen > cut) break;
    }
    let high = 255;
    for (let seen = 0; high > 0; high -= 1) {
      seen += histogram[high];
      if (seen > cut) break;
    }
    // A flat photo (fog, a plain wall) would be stretched into noise; leave its levels alone.
    if (high - low < 48) {
      low = 0;
      high = 255;
    }

    const mean = (sums[0] + sums[1] + sums[2]) / 3;
    // Correct most of the cast, not all: a photo that is truly mostly one colour stays natural.
    const gains = sums.map(sum => (sum > 0 ? Math.min(1.25, Math.max(0.8, 1 + (mean / sum - 1) * 0.6)) : 1));
    return { low, high, gains };
  }

  /**
   * Applies the lighting fix (when lighting is given) and a brightness change (-50 to 50)
   * to RGBA pixels in place.
   */
  function applyLighting(pixels, lighting, brightness) {
    const low = lighting ? lighting.low : 0;
    const high = lighting ? lighting.high : 255;
    const gains = lighting ? lighting.gains : [1, 1, 1];
    const lift = (Number(brightness) || 0) * 1.28;
    const stretch = 255 / Math.max(1, high - low);
    const tables = gains.map(gain => {
      const table = new Uint8ClampedArray(256);
      for (let value = 0; value < 256; value += 1) {
        table[value] = (value * gain - low) * stretch + lift;
      }
      return table;
    });
    for (let i = 0; i < pixels.length; i += 4) {
      pixels[i] = tables[0][pixels[i]];
      pixels[i + 1] = tables[1][pixels[i + 1]];
      pixels[i + 2] = tables[2][pixels[i + 2]];
    }
    return pixels;
  }

  // ---------------------------------------------------------------------------
  // Passport photo
  // ---------------------------------------------------------------------------

  const PRINT_DPI = 300;

  // Sizes in pixels at 300 dpi, so the saved photo prints at its true size.
  const PASSPORT_SIZES = Object.freeze([
    Object.freeze({ key: '35x45', label: '35 × 45 mm', note: 'Passport size, most forms', width: 413, height: 531 }),
    Object.freeze({ key: '2x2', label: '2 × 2 inch', note: 'Indian passport, visa', width: 600, height: 600 }),
    Object.freeze({ key: '20x25', label: '20 × 25 mm', note: 'Stamp size', width: 236, height: 295 })
  ]);

  /** A length in mm or inches as pixels at the print resolution. */
  function lengthToPixels(value, unit, dpi) {
    const number = Number(String(value ?? '').trim().replace(',', '.'));
    if (!Number.isFinite(number) || number <= 0) return null;
    const inches = unit === 'inch' ? number : number / 25.4;
    return Math.max(1, Math.round(inches * (dpi || PRINT_DPI)));
  }

  /** The smallest zoom at which a photo, turned by angle (radians), still fills the crop frame. */
  function coverScale(imageWidth, imageHeight, frameWidth, frameHeight, angle) {
    const c = Math.abs(Math.cos(angle));
    const s = Math.abs(Math.sin(angle));
    return Math.max((frameWidth * c + frameHeight * s) / imageWidth, (frameWidth * s + frameHeight * c) / imageHeight);
  }

  /**
   * Keeps the crop frame inside the photo. x and y are the photo centre's distance from the
   * frame centre on screen; the result is the nearest position with no empty corner.
   */
  function clampOffset(place) {
    const { imageWidth, imageHeight, frameWidth, frameHeight, angle, scale } = place;
    const cos = Math.cos(angle);
    const sin = Math.sin(angle);
    const c = Math.abs(cos);
    const s = Math.abs(sin);
    const limitX = Math.max(0, (scale * imageWidth - (frameWidth * c + frameHeight * s)) / 2);
    const limitY = Math.max(0, (scale * imageHeight - (frameWidth * s + frameHeight * c)) / 2);
    // Work along the photo's own sides, where the limits are simple.
    const alongX = Math.min(limitX, Math.max(-limitX, place.x * cos + place.y * sin));
    const alongY = Math.min(limitY, Math.max(-limitY, -place.x * sin + place.y * cos));
    return { x: alongX * cos - alongY * sin, y: alongX * sin + alongY * cos };
  }

  /**
   * Writes the print resolution into a JPG's header so it prints at its true size (a
   * 413 x 531 px photo at 300 dpi is 35 x 45 mm). Files without the usual JFIF header are
   * returned unchanged.
   */
  function setJpegDpi(bytes, dpi) {
    const isJfif = bytes.length > 18
      && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff && bytes[3] === 0xe0
      && bytes[6] === 0x4a && bytes[7] === 0x46 && bytes[8] === 0x49 && bytes[9] === 0x46 && bytes[10] === 0;
    if (!isJfif) return bytes;
    const output = new Uint8Array(bytes);
    output[13] = 1; // density is in dots per inch
    output[14] = (dpi >> 8) & 0xff;
    output[15] = dpi & 0xff;
    output[16] = (dpi >> 8) & 0xff;
    output[17] = dpi & 0xff;
    return output;
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
   * fixedSize keeps the dimensions exactly as given (a passport photo must be an exact
   * number of pixels), so only the quality is searched, down to lowestQuality.
   *
   * Returns { bytes, width, height, quality, reached }. reached is false when even the
   * smallest allowed photo is over the target, so callers never claim a size they missed.
   */
  async function encodeToTarget(options) {
    const { encode, lossy } = options;
    const target = options.targetBytes > 0 ? options.targetBytes : null;
    const lowest = options.lowestQuality > 0 ? options.lowestQuality : LOWEST_QUALITY;
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
        floorBytes = await encode(width, height, lowest);
        if (floorBytes.length <= target) {
          let best = { bytes: floorBytes, quality: lowest };
          let low = lowest;
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

      smallest = { bytes: floorBytes, width, height, quality: lossy ? lowest : 1, reached: false };
      if (options.fixedSize || Math.min(width, height) <= SMALLEST_SIDE) break;

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
    isSupportedPhoto,
    baseName,
    outputNames,
    toGreyscale,
    measureLighting,
    applyLighting,
    PRINT_DPI,
    PASSPORT_SIZES,
    lengthToPixels,
    coverScale,
    clampOffset,
    setJpegDpi,
    encodeToTarget
  });
});
