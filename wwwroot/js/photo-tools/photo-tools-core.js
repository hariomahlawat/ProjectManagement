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

  // A signature box is wide and low. The last one is for online forms that ask for pixels.
  const SIGNATURE_SIZES = Object.freeze([
    Object.freeze({ key: 'sig-35x15', label: '35 × 15 mm', note: 'Signature box on most forms', width: 413, height: 177 }),
    Object.freeze({ key: 'sig-50x20', label: '50 × 20 mm', note: 'Larger signature box', width: 591, height: 236 }),
    Object.freeze({ key: 'sig-140x60', label: '140 × 60 px', note: 'Online forms that ask for pixels', width: 140, height: 60 })
  ]);

  // Paper in pixels at 300 dpi. The margin is the edge a printer may not reach.
  const SHEET_SIZES = Object.freeze([
    Object.freeze({ key: '4x6', label: '4 × 6 inch photo paper', width: 1200, height: 1800, margin: 35 }),
    Object.freeze({ key: 'a4', label: 'A4 paper', width: 2480, height: 3508, margin: 59 })
  ]);

  const SHEET_GAP = 24; // 2 mm between copies, room for the scissors

  /**
   * Places as many copies of a photo as fit on a sheet of paper, in a centred grid. The paper is
   * turned on its side when that fits more. All measures are pixels at the print resolution.
   */
  function sheetLayout(sheet, photoWidth, photoHeight) {
    const gap = SHEET_GAP;
    const fit = (width, height) => {
      const columns = Math.max(0, Math.floor((width - sheet.margin * 2 + gap) / (photoWidth + gap)));
      const rows = Math.max(0, Math.floor((height - sheet.margin * 2 + gap) / (photoHeight + gap)));
      return { width, height, columns, rows, count: columns * rows };
    };
    const upright = fit(sheet.width, sheet.height);
    const sideways = fit(sheet.height, sheet.width);
    const best = sideways.count > upright.count ? sideways : upright;
    const positions = [];
    if (best.count > 0) {
      const left = Math.round((best.width - (best.columns * (photoWidth + gap) - gap)) / 2);
      const top = Math.round((best.height - (best.rows * (photoHeight + gap) - gap)) / 2);
      for (let row = 0; row < best.rows; row += 1) {
        for (let column = 0; column < best.columns; column += 1) {
          positions.push({ x: left + column * (photoWidth + gap), y: top + row * (photoHeight + gap) });
        }
      }
    }
    return { ...best, positions };
  }

  /**
   * Finds the paper and the ink in a photographed signature: the paper is the usual brightness
   * (most of the picture), the ink the darkest half percent. Null when there is no clear ink.
   */
  function measurePaper(pixels) {
    const histogram = new Uint32Array(256);
    const count = pixels.length / 4;
    if (!count) return null;
    for (let i = 0; i < pixels.length; i += 4) {
      histogram[Math.round(0.299 * pixels[i] + 0.587 * pixels[i + 1] + 0.114 * pixels[i + 2])] += 1;
    }
    const level = (share) => {
      let seen = 0;
      for (let value = 0; value < 256; value += 1) {
        seen += histogram[value];
        if (seen > count * share) return value;
      }
      return 255;
    };
    const ink = level(0.005);
    const paper = level(0.5);
    return paper - ink >= 40 ? { ink, paper } : null;
  }

  /**
   * Turns a photographed signature into dark ink on white paper, in place. Brightness near the
   * paper becomes white, near the ink black, and the soft edge of each stroke is kept.
   */
  function cleanPaper(pixels, levels) {
    if (!levels) return pixels;
    const range = levels.paper - levels.ink;
    const black = levels.ink + range * 0.15;
    const white = levels.paper - range * 0.25;
    const table = new Uint8ClampedArray(256);
    for (let value = 0; value < 256; value += 1) {
      const t = Math.min(1, Math.max(0, (value - black) / (white - black)));
      table[value] = Math.round(255 * t * t * (3 - 2 * t));
    }
    for (let i = 0; i < pixels.length; i += 4) {
      const shade = table[Math.round(0.299 * pixels[i] + 0.587 * pixels[i + 1] + 0.114 * pixels[i + 2])];
      pixels[i] = shade;
      pixels[i + 1] = shade;
      pixels[i + 2] = shade;
    }
    return pixels;
  }

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
  // Edit photo
  // ---------------------------------------------------------------------------

  // Shapes the crop box can be held to. A ratio is width divided by height.
  const CROP_SHAPES = Object.freeze([
    Object.freeze({ key: 'free', label: 'Any shape', ratio: null }),
    Object.freeze({ key: 'original', label: 'Same shape as the photo', ratio: 'original' }),
    Object.freeze({ key: '1:1', label: 'Square', ratio: 1 }),
    Object.freeze({ key: '4:3', label: '4 : 3 (wide)', ratio: 4 / 3 }),
    Object.freeze({ key: '3:4', label: '3 : 4 (tall)', ratio: 3 / 4 }),
    Object.freeze({ key: '3:2', label: '3 : 2 (wide)', ratio: 3 / 2 }),
    Object.freeze({ key: '2:3', label: '2 : 3 (tall)', ratio: 2 / 3 }),
    Object.freeze({ key: '16:9', label: '16 : 9 (wide)', ratio: 16 / 9 }),
    Object.freeze({ key: '9:16', label: '9 : 16 (tall)', ratio: 9 / 16 })
  ]);

  const EDIT_MIN_SIDE = 16;
  const EDIT_MAX_SIDE = 10000;

  /**
   * The size left after a photo is turned by a few degrees and trimmed to the largest upright
   * piece of the same shape, so no empty corner shows.
   */
  function straightenedSize(width, height, degrees) {
    const cover = coverScale(width, height, width, height, (Number(degrees) || 0) * Math.PI / 180);
    return { width: Math.max(1, Math.floor(width / cover)), height: Math.max(1, Math.floor(height / cover)) };
  }

  /** The size of a photo after a list of edit steps. */
  function editedSize(width, height, steps) {
    let size = { width, height };
    for (const step of steps) {
      if (step.type === 'rotate' && Math.abs(step.turns) % 2 === 1) {
        size = { width: size.height, height: size.width };
      } else if (step.type === 'straighten') {
        size = straightenedSize(size.width, size.height, step.degrees);
      } else if (step.type === 'crop' || step.type === 'resize') {
        size = { width: step.width, height: step.height };
      }
    }
    return size;
  }

  function tidyCrop(x, y, width, height, bounds) {
    const left = Math.min(Math.max(0, Math.round(x)), bounds.width - 1);
    const top = Math.min(Math.max(0, Math.round(y)), bounds.height - 1);
    return {
      x: left,
      y: top,
      width: Math.max(1, Math.min(Math.round(width), bounds.width - left)),
      height: Math.max(1, Math.min(Math.round(height), bounds.height - top))
    };
  }

  /** The starting crop box: the whole photo, or the largest centred box of a fixed shape. */
  function defaultCrop(bounds, ratio) {
    if (!ratio) return { x: 0, y: 0, width: bounds.width, height: bounds.height };
    const width = Math.min(bounds.width, bounds.height * ratio);
    const height = width / ratio;
    return tidyCrop((bounds.width - width) / 2, (bounds.height - height) / 2, width, height, bounds);
  }

  /** Slides the crop box without letting it leave the photo. */
  function moveCrop(rect, dx, dy, bounds) {
    return {
      ...rect,
      x: Math.round(Math.min(Math.max(0, rect.x + dx), bounds.width - rect.width)),
      y: Math.round(Math.min(Math.max(0, rect.y + dy), bounds.height - rect.height))
    };
  }

  /**
   * Drags one handle of the crop box (n, s, e, w or a corner such as "se") to a point on the
   * photo. The opposite side stays where it is. With a ratio the box keeps its shape.
   */
  function resizeCrop(rect, handle, point, ratio, bounds, minimum) {
    const min = minimum || EDIT_MIN_SIDE;
    const west = handle.includes('w');
    const east = handle.includes('e');
    const north = handle.includes('n');
    const south = handle.includes('s');
    let left = rect.x;
    let right = rect.x + rect.width;
    let top = rect.y;
    let bottom = rect.y + rect.height;
    const px = Math.min(bounds.width, Math.max(0, point.x));
    const py = Math.min(bounds.height, Math.max(0, point.y));

    if (!ratio) {
      if (west) left = Math.min(px, right - min);
      if (east) right = Math.max(px, left + min);
      if (north) top = Math.min(py, bottom - min);
      if (south) bottom = Math.max(py, top + min);
      return tidyCrop(left, top, right - left, bottom - top, bounds);
    }

    const anchorX = west ? right : east ? left : (left + right) / 2;
    const anchorY = north ? bottom : south ? top : (top + bottom) / 2;
    const roomX = west ? anchorX : east ? bounds.width - anchorX : 2 * Math.min(anchorX, bounds.width - anchorX);
    const roomY = north ? anchorY : south ? bounds.height - anchorY : 2 * Math.min(anchorY, bounds.height - anchorY);
    const wantX = west ? anchorX - px : east ? px - anchorX : 0;
    const wantY = (north ? anchorY - py : south ? py - anchorY : 0) * ratio;
    const most = Math.min(roomX, roomY * ratio);
    const least = Math.min(most, min * Math.max(1, ratio));
    const width = Math.min(most, Math.max(least, wantX, wantY));
    const height = width / ratio;
    const x = west ? anchorX - width : east ? anchorX : anchorX - width / 2;
    const y = north ? anchorY - height : south ? anchorY : anchorY - height / 2;
    return tidyCrop(x, y, width, height, bounds);
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
    SIGNATURE_SIZES,
    SHEET_SIZES,
    sheetLayout,
    measurePaper,
    cleanPaper,
    lengthToPixels,
    coverScale,
    clampOffset,
    setJpegDpi,
    CROP_SHAPES,
    EDIT_MIN_SIDE,
    EDIT_MAX_SIDE,
    straightenedSize,
    editedSize,
    defaultCrop,
    moveCrop,
    resizeCrop,
    encodeToTarget
  });
});
