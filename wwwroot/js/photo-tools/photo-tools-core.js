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

  /**
   * "100" + "KB" → 102400. Returns null when the value is not a positive number. A comma is a
   * decimal point ("1,5" MB), except in a number written with thousands ("1,000" KB), which
   * must not be read as 1 KB.
   */
  function parseTargetBytes(value, unit) {
    const text = String(value ?? '').trim();
    const plain = /^\d{1,3}(,\d{3})+$/.test(text) ? text.replace(/,/g, '') : text.replace(',', '.');
    const number = Number(plain);
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

  // Sizes in pixels at 300 dpi, so the saved photo prints at its true size. A passport applied
  // for in India takes 4.5 x 3.5 cm (Passport Seva); 2 x 2 inch is what Indian missions abroad
  // ask for. See docs/photo-tools.md before changing the notes.
  const PASSPORT_SIZES = Object.freeze([
    Object.freeze({ key: '35x45', label: '35 × 45 mm', note: 'Indian passport, most forms', width: 413, height: 531 }),
    Object.freeze({ key: '2x2', label: '2 × 2 inch', note: 'Passport from an embassy abroad, visa', width: 600, height: 600 }),
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
    Object.freeze({ key: '4x6', label: '4 × 6 inch', note: 'Photo paper', width: 1200, height: 1800, margin: 35 }),
    Object.freeze({ key: '5x7', label: '5 × 7 inch', note: 'Photo paper', width: 1500, height: 2100, margin: 35 }),
    Object.freeze({ key: 'a5', label: 'A5', note: '148 × 210 mm', width: 1748, height: 2480, margin: 59 }),
    Object.freeze({ key: 'a4', label: 'A4', note: '210 × 297 mm', width: 2480, height: 3508, margin: 59 })
  ]);

  const SHEET_GAP = 24; // 2 mm between copies, room for the scissors

  /**
   * Places copies of a photo on a sheet of paper, in a centred grid. The paper is turned on its
   * side when that fits more. `count` is how many fit; `positions` holds as many as were asked
   * for (all that fit when no number is given), filled row by row from the top left so the
   * unused paper stays in one piece. All measures are pixels at the print resolution.
   */
  function sheetLayout(sheet, photoWidth, photoHeight, copies) {
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
    const wanted = Number.isInteger(copies) && copies >= 1 ? Math.min(copies, best.count) : best.count;
    return { ...best, positions: positions.slice(0, wanted) };
  }

  /**
   * A sheet layout as one PDF page. PDF measures in points (72 to the inch) from the bottom left
   * corner, so the page is the paper's true size and each photo its true size. `cuts` are the
   * rectangles to stroke for the cut lines: the line lies outside the photo, not over its edge.
   */
  function sheetPdfPlan(layout, photoWidth, photoHeight) {
    const point = 72 / PRINT_DPI;
    const pageHeight = layout.height * point;
    const lineWidth = 2 * point;
    const photos = layout.positions.map(({ x, y }) => ({
      x: x * point,
      y: pageHeight - (y + photoHeight) * point,
      width: photoWidth * point,
      height: photoHeight * point
    }));
    const cuts = photos.map(box => ({
      x: box.x - lineWidth / 2,
      y: box.y - lineWidth / 2,
      width: box.width + lineWidth,
      height: box.height + lineWidth
    }));
    return { pageWidth: layout.width * point, pageHeight, lineWidth, photos, cuts };
  }

  const COVER_MAX_ZOOM = 3;

  /**
   * The part of a photo that fills a frame of another shape: the largest piece of that shape.
   * `whole` says the photo already had the shape, so at no zoom nothing is lost.
   *
   * `place` moves it: zoom (1 to 3) takes a smaller piece, and x and y (0 to 1) say where in the
   * room that is left the piece sits, 0.5 being the middle. Without it the middle is taken.
   */
  function coverCrop(sourceWidth, sourceHeight, frameWidth, frameHeight, place) {
    const shape = frameWidth / frameHeight;
    let width = sourceWidth;
    let height = Math.round(sourceWidth / shape);
    if (height > sourceHeight) {
      height = sourceHeight;
      width = Math.round(sourceHeight * shape);
    }
    width = Math.max(1, Math.min(sourceWidth, width));
    height = Math.max(1, Math.min(sourceHeight, height));
    // A pixel or two of rounding is not a different shape.
    const whole = sourceWidth - width <= Math.max(2, sourceWidth * 0.01)
      && sourceHeight - height <= Math.max(2, sourceHeight * 0.01);

    const share = value => (Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : 0.5);
    const zoom = place && Number.isFinite(place.zoom) ? Math.min(COVER_MAX_ZOOM, Math.max(1, place.zoom)) : 1;
    width = Math.max(1, Math.round(width / zoom));
    height = Math.max(1, Math.round(height / zoom));
    return {
      x: Math.floor((sourceWidth - width) * share(place ? place.x : 0.5)),
      y: Math.floor((sourceHeight - height) * share(place ? place.y : 0.5)),
      width,
      height,
      whole
    };
  }

  /**
   * Where the piece sits after the photo is dragged under the frame by (dx, dy), given as shares
   * of the frame's width and height. Dragging the photo right shows more of its left.
   */
  function dragCover(sourceWidth, sourceHeight, frameWidth, frameHeight, place, dx, dy) {
    const piece = coverCrop(sourceWidth, sourceHeight, frameWidth, frameHeight, place);
    const roomX = sourceWidth - piece.width;
    const roomY = sourceHeight - piece.height;
    const clamp = value => Math.min(1, Math.max(0, value));
    return {
      zoom: place.zoom,
      x: roomX > 0 ? clamp((piece.x - dx * piece.width) / roomX) : 0.5,
      y: roomY > 0 ? clamp((piece.y - dy * piece.height) / roomY) : 0.5
    };
  }

  /** Pixels at the print resolution as whole millimetres, for naming a size. */
  function pixelsToMillimetres(pixels, dpi) {
    return Math.round(pixels / (dpi || PRINT_DPI) * 25.4);
  }

  // ---------------------------------------------------------------------------
  // Background colour
  // ---------------------------------------------------------------------------
  //
  // The background of a passport photo is found by colour, starting from the edges of the photo.
  // It works on a plain wall or curtain and refuses anything else; there is no model that knows
  // what a person looks like. The numbers below were set on real photographs; see
  // docs/photo-tools.md before changing them.

  // When deciding whether a background is plain, brightness counts for much less than colour,
  // so a shadow or uneven light on a wall is still a plain wall. Colour differences count double.
  const BACKGROUND_LUMA_WEIGHT = 0.4;
  const BACKGROUND_COLOUR_WEIGHT = 2;
  // Above this spread along the edges the background is patterned, not plain.
  const BACKGROUND_BUSY_SPREAD = 22;
  // Against the colour expected at a pixel, brightness counts nearly in full: there the uneven
  // light is already allowed for, and brightness is what tells a white shirt from a white wall.
  const BACKGROUND_STEP_LUMA = 0.8;
  // How far a pixel may be from the colour expected there, at Less and at More.
  const BACKGROUND_LIMIT_LESS = 7;
  const BACKGROUND_LIMIT_MORE = 21;
  // How far the colour of one square may move from the edge colour before it is not believed.
  const BACKGROUND_DRIFT = 45;
  const BACKGROUND_ROUNDS = 12;
  // A boundary pixel this far from the wall colour is all person; nearer, it is part wall.
  const BACKGROUND_EDGE_SOFTNESS = 36;
  // How quickly the brightness may change from pixel to pixel and still be wall, at Less and More.
  const BACKGROUND_EDGE_LESS = 6;
  const BACKGROUND_EDGE_MORE = 13;
  const BACKGROUND_STATUS = Object.freeze({
    changed: 'changed',
    busy: 'busy',
    none: 'none',
    tooMuch: 'too-much'
  });

  function backgroundDistance(r, g, b, colour, lumaWeight) {
    const dr = r - colour[0];
    const dg = g - colour[1];
    const db = b - colour[2];
    const luma = 0.299 * dr + 0.587 * dg + 0.114 * db;
    const blue = (db - luma) * 0.564 * BACKGROUND_COLOUR_WEIGHT;
    const red = (dr - luma) * 0.713 * BACKGROUND_COLOUR_WEIGHT;
    const light = luma * lumaWeight;
    return Math.sqrt(light * light + blue * blue + red * red);
  }

  /**
   * The pixels where the background is expected: a thin band along the top edge and down the
   * upper part of both sides. The bottom is left out because the shoulders reach it.
   */
  function backgroundEdge(width, height) {
    const band = Math.max(1, Math.round(Math.min(width, height) * 0.015));
    const sideEnd = Math.floor(height * 0.6);
    const edge = [];
    for (let y = 0; y < Math.min(band, height); y += 1) {
      for (let x = 0; x < width; x += 1) edge.push(y * width + x);
    }
    for (let y = band; y < sideEnd; y += 1) {
      for (let x = 0; x < Math.min(band, width); x += 1) {
        edge.push(y * width + x);
        if (width - 1 - x >= band) edge.push(y * width + width - 1 - x);
      }
    }
    return edge;
  }

  /**
   * Reads the background from the edges of RGBA pixels: its usual colour, how much the edge
   * pixels differ from it, and whether that is little enough to call the background plain.
   */
  function measureBackground(pixels, width, height) {
    const edge = backgroundEdge(width, height);
    const histograms = [new Uint32Array(256), new Uint32Array(256), new Uint32Array(256)];
    for (const index of edge) {
      histograms[0][pixels[index * 4]] += 1;
      histograms[1][pixels[index * 4 + 1]] += 1;
      histograms[2][pixels[index * 4 + 2]] += 1;
    }
    const colour = histograms.map((histogram) => {
      let seen = 0;
      for (let value = 0; value < 256; value += 1) {
        seen += histogram[value];
        if (seen * 2 >= edge.length) return value;
      }
      return 255;
    });
    const distances = new Float32Array(edge.length);
    edge.forEach((index, at) => {
      distances[at] = backgroundDistance(pixels[index * 4], pixels[index * 4 + 1], pixels[index * 4 + 2], colour, BACKGROUND_LUMA_WEIGHT);
    });
    distances.sort();
    // The eighth decile: a little hair or a shoulder at the edge does not make a wall busy.
    const spread = distances.length ? distances[Math.floor((distances.length - 1) * 0.8)] : 0;
    return { colour, spread, plain: spread <= BACKGROUND_BUSY_SPREAD, edge };
  }

  /**
   * Gives the background of RGBA pixels a new colour, in place. `amount` (0 to 100) is how
   * readily a pixel counts as background: how far it may be from the colour expected at that
   * spot, and how quickly the brightness may change there.
   *
   * Returns a status and leaves the pixels alone unless it is "changed":
   *   busy      the background is not plain;
   *   none      hardly any background was found;
   *   too-much  nearly everything, or the middle of the face, was taken as background.
   * Alpha is not touched.
   */
  function changeBackground(pixels, width, height, colour, amount) {
    const total = width * height;
    const measured = measureBackground(pixels, width, height);
    if (!measured.plain) return { status: BACKGROUND_STATUS.busy, share: 0 };

    const old = measured.colour;
    const limit = BACKGROUND_LIMIT_LESS
      + Math.min(100, Math.max(0, Number(amount) || 0)) / 100 * (BACKGROUND_LIMIT_MORE - BACKGROUND_LIMIT_LESS);

    // The background colour is not the same everywhere: light falls off across a wall and a
    // backdrop is mottled. So the colour is kept per square of a coarse grid, starting as the
    // one colour read from the edges and corrected from the background found so far.
    const cell = Math.max(6, Math.round(Math.min(width, height) / 24));
    const columns = Math.ceil(width / cell);
    const rows = Math.ceil(height / cell);
    const field = new Float32Array(columns * rows * 3);
    for (let i = 0; i < columns * rows; i += 1) field.set(old, i * 3);
    const local = [0, 0, 0];
    const wall = { field, cell, columns, rows, limit };
    /** The background colour expected at a pixel, blended between the squares around it. */
    const colourAt = (x, y) => wallColourAt(wall, x, y, local);

    // A wall is smooth; the outline of a person, and cloth, are not. Where the brightness
    // changes quickly from one pixel to the next the background does not spread, even when
    // the colour there is close to the wall's (a blue jacket against a blue backdrop).
    const strength = Math.min(100, Math.max(0, Number(amount) || 0)) / 100;
    const edgeLimit = BACKGROUND_EDGE_LESS + strength * (BACKGROUND_EDGE_MORE - BACKGROUND_EDGE_LESS);
    const light = new Float32Array(total);
    for (let i = 0; i < total; i += 1) {
      light[i] = 0.299 * pixels[i * 4] + 0.587 * pixels[i * 4 + 1] + 0.114 * pixels[i * 4 + 2];
    }
    // Averaged over three by three pixels first, so film grain and noise are not taken for edges.
    const smooth = new Float32Array(total);
    for (let y = 0; y < height; y += 1) {
      for (let x = 0; x < width; x += 1) {
        let sum = 0;
        let count = 0;
        for (let ny = Math.max(0, y - 1); ny <= Math.min(height - 1, y + 1); ny += 1) {
          for (let nx = Math.max(0, x - 1); nx <= Math.min(width - 1, x + 1); nx += 1) {
            sum += light[ny * width + nx];
            count += 1;
          }
        }
        smooth[y * width + x] = sum / count;
      }
    }
    const steep = new Uint8Array(total);
    for (let y = 0; y < height; y += 1) {
      for (let x = 0; x < width; x += 1) {
        const i = y * width + x;
        const across = smooth[y * width + Math.min(width - 1, x + 1)] - smooth[y * width + Math.max(0, x - 1)];
        const down = smooth[Math.min(height - 1, y + 1) * width + x] - smooth[Math.max(0, y - 1) * width + x];
        steep[i] = Math.abs(across) + Math.abs(down) > edgeLimit ? 1 : 0;
      }
    }

    const rowColours = new Float32Array(columns * 3);
    const distance = new Float32Array(total);
    const mask = new Uint8Array(total);
    const queue = new Int32Array(total);
    let head = 0;
    let tail = 0;
    const visit = (index) => {
      if (!mask[index] && !steep[index] && distance[index] < limit) {
        mask[index] = 1;
        queue[tail++] = index;
      }
    };
    let reached = -1;
    for (let round = 0; round < BACKGROUND_ROUNDS; round += 1) {
      for (let y = 0, i = 0; y < height; y += 1) {
        // The colours of this row of pixels, one per column of squares; a pixel then only
        // blends the two beside it.
        const fy = Math.min(rows - 1, Math.max(0, (y + 0.5) / cell - 0.5));
        const y0 = Math.floor(fy);
        const y1 = Math.min(rows - 1, y0 + 1);
        const ty = fy - y0;
        for (let at = 0; at < columns * 3; at += 1) {
          rowColours[at] = field[y0 * columns * 3 + at] * (1 - ty) + field[y1 * columns * 3 + at] * ty;
        }
        for (let x = 0; x < width; x += 1, i += 1) {
          const fx = Math.min(columns - 1, Math.max(0, (x + 0.5) / cell - 0.5));
          const x0 = Math.floor(fx);
          const x1 = Math.min(columns - 1, x0 + 1);
          const tx = fx - x0;
          local[0] = rowColours[x0 * 3] * (1 - tx) + rowColours[x1 * 3] * tx;
          local[1] = rowColours[x0 * 3 + 1] * (1 - tx) + rowColours[x1 * 3 + 1] * tx;
          local[2] = rowColours[x0 * 3 + 2] * (1 - tx) + rowColours[x1 * 3 + 2] * tx;
          distance[i] = backgroundDistance(pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2], local, BACKGROUND_STEP_LUMA);
        }
      }
      // Spread from the edges to every neighbour that is close enough to the colour expected there.
      mask.fill(0);
      head = 0;
      tail = 0;
      for (const index of measured.edge) visit(index);
      while (head < tail) {
        const index = queue[head++];
        const x = index % width;
        if (x > 0) visit(index - 1);
        if (x < width - 1) visit(index + 1);
        if (index >= width) visit(index - width);
        if (index < total - width) visit(index + width);
      }
      // Nothing more was found with the corrected colours: done.
      if (tail - reached <= total * 0.002) break;
      reached = tail;
      if (round === BACKGROUND_ROUNDS - 1) break;

      // Correct each square from the background found in it. A square with little background,
      // or whose colour has moved far from the edge colour (it has crept onto the person), takes
      // the colour of the nearest squares that are sure.
      const sums = new Float64Array(columns * rows * 4);
      for (let y = 0, i = 0; y < height; y += 1) {
        const row = Math.floor(y / cell) * columns;
        for (let x = 0; x < width; x += 1, i += 1) {
          if (!mask[i]) continue;
          const at = (row + Math.floor(x / cell)) * 4;
          sums[at] += pixels[i * 4];
          sums[at + 1] += pixels[i * 4 + 1];
          sums[at + 2] += pixels[i * 4 + 2];
          sums[at + 3] += 1;
        }
      }
      const known = new Uint8Array(columns * rows);
      let waiting = [];
      for (let i = 0; i < columns * rows; i += 1) {
        const count = sums[i * 4 + 3];
        if (count >= cell * cell * 0.1) {
          const r = sums[i * 4] / count;
          const g = sums[i * 4 + 1] / count;
          const b = sums[i * 4 + 2] / count;
          if (backgroundDistance(r, g, b, old, BACKGROUND_LUMA_WEIGHT) < BACKGROUND_DRIFT) {
            field[i * 3] = r;
            field[i * 3 + 1] = g;
            field[i * 3 + 2] = b;
            known[i] = 1;
            continue;
          }
        }
        waiting.push(i);
      }
      while (waiting.length && waiting.length < columns * rows) {
        const filled = [];
        const still = [];
        for (const i of waiting) {
          const cx = i % columns;
          const cy = Math.floor(i / columns);
          let r = 0;
          let g = 0;
          let b = 0;
          let count = 0;
          for (const [dx, dy] of [[-1, 0], [1, 0], [0, -1], [0, 1]]) {
            const nx = cx + dx;
            const ny = cy + dy;
            if (nx < 0 || ny < 0 || nx >= columns || ny >= rows || !known[ny * columns + nx]) continue;
            r += field[(ny * columns + nx) * 3];
            g += field[(ny * columns + nx) * 3 + 1];
            b += field[(ny * columns + nx) * 3 + 2];
            count += 1;
          }
          if (count) {
            filled.push([i, r / count, g / count, b / count]);
          } else {
            still.push(i);
          }
        }
        if (!filled.length) break;
        for (const [i, r, g, b] of filled) {
          field[i * 3] = r;
          field[i * 3 + 1] = g;
          field[i * 3 + 2] = b;
          known[i] = 1;
        }
        waiting = still;
      }
    }

    // Islands left standing inside the background (a mark or a darker patch on the wall, noise)
    // go with it. The person is one large piece that reaches the bottom of the photo, so a
    // piece that reaches the bottom is never an island.
    const speck = Math.max(4, Math.round(total * 0.005));
    const seen = new Uint8Array(total);
    const gather = (index) => {
      if (!mask[index] && !seen[index]) {
        seen[index] = 1;
        queue[tail++] = index;
      }
    };
    for (let start = 0; start < total; start += 1) {
      if (mask[start] || seen[start]) continue;
      head = 0;
      tail = 0;
      gather(start);
      while (head < tail) {
        const index = queue[head++];
        const x = index % width;
        if (x > 0) gather(index - 1);
        if (x < width - 1) gather(index + 1);
        if (index >= width) gather(index - width);
        if (index < total - width) gather(index + width);
      }
      if (tail <= speck) {
        let standing = false;
        for (let i = 0; i < tail && !standing; i += 1) standing = queue[i] >= total - width;
        if (!standing) {
          for (let i = 0; i < tail; i += 1) mask[queue[i]] = 1;
        }
      }
    }

    let found = 0;
    for (let i = 0; i < total; i += 1) found += mask[i];
    const share = found / total;
    if (share < 0.03) return { status: BACKGROUND_STATUS.none, share };
    if (share > 0.85) return { status: BACKGROUND_STATUS.tooMuch, share };

    // The middle of the face must not be background.
    const left = Math.floor(width * 0.4);
    const right = Math.max(left + 1, Math.ceil(width * 0.6));
    const top = Math.floor(height * 0.35);
    const bottom = Math.max(top + 1, Math.ceil(height * 0.55));
    let inFace = 0;
    for (let y = top; y < bottom; y += 1) {
      for (let x = left; x < right; x += 1) inFace += mask[y * width + x];
    }
    if (inFace > (right - left) * (bottom - top) * 0.2) return { status: BACKGROUND_STATUS.tooMuch, share };
    // Asked only where the background is: nothing is painted.
    if (!colour) return { status: BACKGROUND_STATUS.changed, share, mask, wall };

    // The three rows of pixels on the person's side of the boundary: 1 touches the background,
    // 2 touches those, 3 touches those. They are a mix of person and wall (hair, a soft edge).
    const ring = new Uint8Array(total);
    for (let pass = 1; pass <= 3; pass += 1) {
      for (let y = 0; y < height; y += 1) {
        for (let x = 0; x < width; x += 1) {
          const index = y * width + x;
          if (mask[index] || ring[index]) continue;
          let touches = false;
          for (let dy = -1; dy <= 1 && !touches; dy += 1) {
            const ny = y + dy;
            if (ny < 0 || ny >= height) continue;
            for (let dx = -1; dx <= 1; dx += 1) {
              const nx = x + dx;
              if (nx < 0 || nx >= width) continue;
              const near = ny * width + nx;
              if (pass === 1 ? mask[near] : ring[near] === pass - 1) {
                touches = true;
                break;
              }
            }
          }
          // Marked after the pass, so a pixel found in this pass does not pull in its neighbours.
          if (touches) ring[index] = pass + 100;
        }
      }
      for (let i = 0; i < total; i += 1) {
        if (ring[i] === pass + 100) ring[i] = pass;
      }
    }

    for (let i = 0; i < total; i += 1) {
      const at = i * 4;
      if (mask[i]) {
        pixels[at] = colour[0];
        pixels[at + 1] = colour[1];
        pixels[at + 2] = colour[2];
      } else if (ring[i]) {
        // How much of this pixel is person: nothing near the wall colour, all of it far from it.
        // The further in from the boundary, the more of it is person whatever its colour.
        let person = Math.min(1, Math.max(0, (distance[i] - limit * 0.5) / BACKGROUND_EDGE_SOFTNESS));
        if (ring[i] === 2) person = 0.35 + person * 0.65;
        if (ring[i] === 3) person = 0.7 + person * 0.3;
        // The wall's share of the pixel changes colour; the person's share stays. This also
        // takes the old wall colour out of the edge, so no fringe of it is left.
        const wall = 1 - person;
        const was = colourAt(i % width, Math.floor(i / width));
        pixels[at] = Math.min(255, Math.max(0, Math.round(pixels[at] + wall * (colour[0] - was[0]))));
        pixels[at + 1] = Math.min(255, Math.max(0, Math.round(pixels[at + 1] + wall * (colour[1] - was[1]))));
        pixels[at + 2] = Math.min(255, Math.max(0, Math.round(pixels[at + 2] + wall * (colour[2] - was[2]))));
      }
    }
    return { status: BACKGROUND_STATUS.changed, share, mask, wall };
  }

  /**
   * The old background colour expected at a pixel, from the `wall` that changeBackground returns:
   * a coarse grid of colours that follows the light across the wall, blended between the squares
   * around the pixel. Written into `out` ([r, g, b]) and returned.
   */
  function wallColourAt(wall, x, y, out) {
    const { field, cell, columns, rows } = wall;
    const fx = Math.min(columns - 1, Math.max(0, (x + 0.5) / cell - 0.5));
    const fy = Math.min(rows - 1, Math.max(0, (y + 0.5) / cell - 0.5));
    const x0 = Math.floor(fx);
    const y0 = Math.floor(fy);
    const x1 = Math.min(columns - 1, x0 + 1);
    const y1 = Math.min(rows - 1, y0 + 1);
    const tx = fx - x0;
    const ty = fy - y0;
    for (let channel = 0; channel < 3; channel += 1) {
      const top = field[(y0 * columns + x0) * 3 + channel] * (1 - tx) + field[(y0 * columns + x1) * 3 + channel] * tx;
      const bottom = field[(y1 * columns + x0) * 3 + channel] * (1 - tx) + field[(y1 * columns + x1) * 3 + channel] * tx;
      out[channel] = top * (1 - ty) + bottom * ty;
    }
    return out;
  }

  /**
   * Finds the background without changing the photo. The result is that of changeBackground;
   * when the status is "changed" (here: found), `mask` holds 1 for each background pixel.
   */
  function findBackground(pixels, width, height, amount) {
    return changeBackground(pixels, width, height, null, amount);
  }

  // ---------------------------------------------------------------------------
  // Passport photo checks
  // ---------------------------------------------------------------------------
  //
  // A few things that can be measured without knowing what a face looks like. They are a guide
  // for the user, not a check against any authority's rules, and the screen says so.

  // Where the face guide on the screen draws its lines, as shares of the photo's height.
  const PASSPORT_GUIDE = Object.freeze({ top: 0.10, chin: 0.78 });

  /**
   * Where the person's outline starts and how wide the head is, from a background mask:
   * `top` is the first row (as a share of the height) where the middle of the photo is person,
   * and `width` the widest run of person (as a share of the width) across the rows where the
   * face is. Null when the middle of the photo is never person.
   */
  function headOutline(mask, width, height) {
    const from = Math.floor(width * 0.3);
    const to = Math.ceil(width * 0.7);
    // The head starts where the middle stays person for a run of rows (1% of the height): a
    // line along the edge of a scan, or a mark on the wall, is not the head.
    const run = Math.max(2, Math.round(height * 0.01));
    let top = null;
    let rows = 0;
    for (let y = 0; y < height && top === null; y += 1) {
      let person = 0;
      for (let x = from; x < to; x += 1) person += mask[y * width + x] ? 0 : 1;
      rows = person >= (to - from) * 0.25 ? rows + 1 : 0;
      if (rows >= run) top = y - run + 1;
    }
    if (top === null) return null;

    // The face is between the guide's two lines; shoulders are below it.
    const firstRow = Math.floor(height * 0.3);
    const lastRow = Math.ceil(height * 0.55);
    const runs = [];
    for (let y = firstRow; y < lastRow; y += 1) {
      // The run of person that holds the middle column, so a second person or a plant at the
      // side is not counted.
      const middle = y * width + Math.floor(width / 2);
      if (mask[middle]) continue;
      let left = Math.floor(width / 2);
      let right = left;
      while (left > 0 && !mask[y * width + left - 1]) left -= 1;
      while (right < width - 1 && !mask[y * width + right + 1]) right += 1;
      runs.push(right - left + 1);
    }
    runs.sort((a, b) => a - b);
    return {
      top: top / height,
      width: runs.length ? runs[Math.floor(runs.length / 2)] / width : null
    };
  }

  /**
   * Looks at a finished passport photo for things a form is likely to reject. Returns a list
   * of { key, text }: an empty list means none of the checks found a problem.
   *
   *   pixels, width, height  the finished photo (RGBA)
   *   found                  findBackground's result for the photo before any background change
   *   plain, colour          measureBackground's verdict and colour for that photo
   *   replaced               the background was given a new colour
   *   enlarged               how many times the photo is enlarged to fill the frame (1 or less is none)
   */
  function passportChecks({ pixels, width, height, found, plain, colour, replaced, enlarged }) {
    const problems = [];
    const guide = PASSPORT_GUIDE;

    if (enlarged > 1.2) {
      problems.push({
        key: 'enlarged',
        text: `This part of the photo is small, so it is enlarged about ${(Math.round(enlarged * 10) / 10).toFixed(1)} times and may look soft. Zoom out, or use a photo with more pixels.`
      });
    }

    const outline = found && found.status === BACKGROUND_STATUS.changed ? headOutline(found.mask, width, height) : null;
    if (outline) {
      if (outline.top <= 0.01) {
        problems.push({ key: 'head-cut', text: 'The top of the head touches the edge or is cut off. Move the photo down or zoom out.' });
      } else if (outline.top < guide.top - 0.05) {
        problems.push({ key: 'head-high', text: 'The head is high in the frame. Move the photo down so the top of the head is near the line.' });
      } else if (outline.top > guide.top + 0.08) {
        problems.push({ key: 'head-low', text: 'The head is low or small in the frame. Move the photo up, or zoom in, so the top of the head is near the line.' });
      }
      if (outline.width !== null && outline.width > 0.85) {
        problems.push({ key: 'head-large', text: 'The head fills the frame from side to side. Zoom out so the face sits inside the oval.' });
      } else if (outline.width !== null && outline.width < 0.33) {
        problems.push({ key: 'head-small', text: 'The head looks small in the frame. Zoom in so the face fills the oval.' });
      }
    }

    if (!replaced) {
      if (!plain) {
        problems.push({ key: 'background-busy', text: 'The edges of the photo are not a plain background. Most forms ask for a plain white or light background with some space around the head.' });
      } else if (0.299 * colour[0] + 0.587 * colour[1] + 0.114 * colour[2] < 150) {
        problems.push({ key: 'background-dark', text: 'The background is not white or light, which most forms ask for. Background, above, can change it.' });
      }
    }

    // The middle of the face, where the guide's oval is.
    const cx = width / 2;
    const cy = height * (guide.top + guide.chin) / 2;
    const rx = width * 0.2;
    const ry = height * (guide.chin - guide.top) * 0.36;
    let sum = 0;
    let count = 0;
    for (let y = Math.max(0, Math.floor(cy - ry)); y < Math.min(height, Math.ceil(cy + ry)); y += 1) {
      for (let x = Math.max(0, Math.floor(cx - rx)); x < Math.min(width, Math.ceil(cx + rx)); x += 1) {
        if (((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 > 1) continue;
        const at = (y * width + x) * 4;
        sum += 0.299 * pixels[at] + 0.587 * pixels[at + 1] + 0.114 * pixels[at + 2];
        count += 1;
      }
    }
    const light = count ? sum / count : 128;
    if (light < 80) {
      problems.push({ key: 'face-dark', text: 'The face looks dark. Try Fix lighting, or raise Brightness.' });
    } else if (light > 222) {
      problems.push({ key: 'face-bright', text: 'The face looks washed out. Lower Brightness, or use a photo with less light on the face.' });
    }
    return problems;
  }

  // The three brushes for touching up a changed background, named as in common photo editors.
  // A stroke is drawn into a mask picture in its brush's colour channel, so a later stroke lies
  // over an earlier one.
  //   refine   Refine Edge: only what still looks like the old background takes the new colour;
  //            hair, skin and clothes under the brush stay. For the rim left around hair.
  //   erase    Erase: everything under the brush takes the new colour. For something left
  //            standing in the background that does not look like the wall.
  //   restore  Restore: the photo as it was before the change comes back.
  const BACKGROUND_BRUSHES = Object.freeze({
    restore: Object.freeze({ key: 'restore', channel: 0, mark: '#ff0000' }),
    refine: Object.freeze({ key: 'refine', channel: 1, mark: '#00ff00' }),
    erase: Object.freeze({ key: 'erase', channel: 2, mark: '#0000ff' })
  });

  /**
   * Softens one plane of strengths (0 to 1) inside a box of the picture: a box blur of the given
   * radius, run twice across and twice down, which is close to a Gaussian. In place.
   */
  function featherPlane(plane, width, box, radius) {
    if (radius < 1) return plane;
    const { left, top, right, bottom } = box;
    const span = Math.max(right - left, bottom - top);
    const line = new Float32Array(span);
    const blurLine = (start, step, count) => {
      for (let n = 0; n < count; n += 1) line[n] = plane[start + n * step];
      const size = radius * 2 + 1;
      let sum = 0;
      for (let n = -radius; n <= radius; n += 1) sum += line[Math.min(count - 1, Math.max(0, n))];
      for (let n = 0; n < count; n += 1) {
        plane[start + n * step] = sum / size;
        sum += line[Math.min(count - 1, n + radius + 1)] - line[Math.max(0, n - radius)];
      }
    };
    for (let pass = 0; pass < 2; pass += 1) {
      for (let y = top; y < bottom; y += 1) blurLine(y * width + left, 1, right - left);
      for (let x = left; x < right; x += 1) blurLine(top * width + x, width, bottom - top);
    }
    return plane;
  }

  /**
   * Lays the touch-up strokes over a photo whose background was changed, in place. All pictures
   * are RGBA of one size:
   *   pixels    the changed photo
   *   original  the photo before the change
   *   mask      the strokes: red Restore, green Refine Edge, blue Erase; alpha is how strongly
   *   colour    the new background colour [r, g, b]
   * options:
   *   width     the pictures' width in pixels
   *   wall      the `wall` changeBackground returned; Refine Edge needs it to tell the old
   *             background from the person (without it, Refine Edge acts as Erase)
   *   feather   the radius in pixels over which a stroke's edge fades out (default none)
   *
   * Refine Edge treats each pixel as a mix of the person and the wall behind them, as hair over a
   * wall is. The wall's colour there is known (`wall`); the person's colour is taken from the
   * nearby pixels that are clearly person (a small square around the pixel). How far the pixel
   * lies from the wall colour towards the person's colour is the person's share; the wall's share
   * is moved to the new colour. So old wall becomes the new colour, a strand of hair keeps its
   * colour on the new background, and solid hair or skin is not changed.
   * Alpha of the photo is not touched.
   */
  /**
   * The person's colours near each pixel of a box. The box is cut into small squares; in each,
   * the pixels that are clearly not wall are split by brightness into a dark group and a light
   * group (hair and skin, say), and each group's average is kept. `at(x, y)` gives the colours
   * of the pixel's square and the eight around it, as a flat list [r, g, b, group, ...], where
   * group numbers the square so the two colours of one square can be paired; empty when no
   * person is near.
   */
  function personColours(original, width, box, wall, cell) {
    const columns = Math.ceil((box.right - box.left) / cell);
    const rows = Math.ceil((box.bottom - box.top) / cell);
    const clear = wall.limit + BACKGROUND_EDGE_SOFTNESS;
    const was = [0, 0, 0];
    const squareOf = (x, y) => Math.floor((y - box.top) / cell) * columns + Math.floor((x - box.left) / cell);
    // Which pixels are person, and the average brightness of the person in each square.
    const isPerson = new Uint8Array((box.right - box.left) * (box.bottom - box.top));
    const light = new Float64Array(columns * rows * 2);
    for (let y = box.top; y < box.bottom; y += 1) {
      for (let x = box.left; x < box.right; x += 1) {
        const at = (y * width + x) * 4;
        wallColourAt(wall, x, y, was);
        if (backgroundDistance(original[at], original[at + 1], original[at + 2], was, BACKGROUND_STEP_LUMA) < clear) continue;
        isPerson[(y - box.top) * (box.right - box.left) + (x - box.left)] = 1;
        const square = squareOf(x, y) * 2;
        light[square] += luminance(original, at);
        light[square + 1] += 1;
      }
    }
    // The dark and the light group of each square.
    const sums = new Float64Array(columns * rows * 8);
    for (let y = box.top; y < box.bottom; y += 1) {
      for (let x = box.left; x < box.right; x += 1) {
        if (!isPerson[(y - box.top) * (box.right - box.left) + (x - box.left)]) continue;
        const at = (y * width + x) * 4;
        const square = squareOf(x, y);
        const middle = light[square * 2] / light[square * 2 + 1];
        const group = (square * 2 + (luminance(original, at) > middle ? 1 : 0)) * 4;
        sums[group] += original[at];
        sums[group + 1] += original[at + 1];
        sums[group + 2] += original[at + 2];
        sums[group + 3] += 1;
      }
    }
    const found = [];
    return {
      at(x, y) {
        found.length = 0;
        const cx = Math.floor((x - box.left) / cell);
        const cy = Math.floor((y - box.top) / cell);
        for (let ny = cy - 1; ny <= cy + 1; ny += 1) {
          for (let nx = cx - 1; nx <= cx + 1; nx += 1) {
            if (nx < 0 || ny < 0 || nx >= columns || ny >= rows) continue;
            const square = ny * columns + nx;
            for (let half = 0; half < 2; half += 1) {
              const group = (square * 2 + half) * 4;
              const count = sums[group + 3];
              if (count) found.push(sums[group] / count, sums[group + 1] / count, sums[group + 2] / count, square);
            }
          }
        }
        return found;
      }
    };
  }

  // Two fits of a pixel this close (in RGB levels) are equally good.
  const FIT_TOLERANCE = 1;

  /** How far a point (dr, dg, db from a start) is from the segment to (fr, fg, fb), and where along it (0 to 1) it is nearest. */
  function segmentFit(dr, dg, db, fr, fg, fb) {
    const length = fr * fr + fg * fg + fb * fb;
    const along = length < 1 ? 0 : Math.min(1, Math.max(0, (dr * fr + dg * fg + db * fb) / length));
    const er = dr - along * fr;
    const eg = dg - along * fg;
    const eb = db - along * fb;
    return { along, off: Math.sqrt(er * er + eg * eg + eb * eb) };
  }

  /**
   * How much of a pixel is person (0 to 1). The pixel is explained as the best-fitting mix of:
   * the wall colour `was` and one of the person's colours near it (a strand of hair over the
   * wall: the person's share is how far along from the wall the pixel lies), or two colours of
   * the person (where skin meets hair: all person). A pixel that none of these explains well is a
   * colour of its own, and person. Without a person colour near, only a pixel well away from
   * the wall counts as person.
   */
  function personShare(original, at, was, candidates, limit) {
    const r = original[at];
    const g = original[at + 1];
    const b = original[at + 2];
    if (!candidates || !candidates.length) {
      const distance = backgroundDistance(r, g, b, was, BACKGROUND_STEP_LUMA);
      return Math.min(1, Math.max(0, (distance - limit) / BACKGROUND_EDGE_SOFTNESS));
    }
    // The best mix of wall and one person colour. Of mixes that fit about as well, the one that
    // leaves more of the pixel to the person is taken: when unsure, the person is kept.
    let best = null;
    for (let i = 0; i < candidates.length; i += 4) {
      const fit = segmentFit(r - was[0], g - was[1], b - was[2], candidates[i] - was[0], candidates[i + 1] - was[1], candidates[i + 2] - was[2]);
      if (!best || fit.off < best.off - FIT_TOLERANCE
        || (fit.off <= best.off + FIT_TOLERANCE && fit.along > best.along)) best = fit;
    }
    // The best mix of the two colours of one square of the person.
    let inside = Infinity;
    for (let i = 0; i < candidates.length; i += 4) {
      for (let j = i + 4; j < candidates.length; j += 4) {
        if (candidates[i + 3] !== candidates[j + 3]) continue;
        const fit = segmentFit(r - candidates[i], g - candidates[i + 1], b - candidates[i + 2],
          candidates[j] - candidates[i], candidates[j + 1] - candidates[i + 1], candidates[j + 2] - candidates[i + 2]);
        if (fit.off < inside) inside = fit.off;
      }
    }
    // Two person colours explain it as well as wall and person do (grey hair against a grey wall,
    // where every colour lies on one line): it is person.
    if (inside <= best.off + FIT_TOLERANCE) return 1;
    // Grain on the wall is not person: the first tenth of the way counts as wall.
    const mixed = Math.min(1, Math.max(0, (best.along - 0.1) / 0.9));
    const own = Math.min(1, Math.max(0, (best.off - limit) / (BACKGROUND_EDGE_SOFTNESS / 2)));
    return Math.max(mixed, own);
  }

  function applyBackgroundFixes(pixels, original, mask, colour, options) {
    const total = pixels.length / 4;
    const width = (options && options.width) || total;
    const height = total / width;
    const wall = options && options.wall;
    const feather = Math.max(0, Math.round(Number(options && options.feather) || 0));

    // The box the strokes cover, widened by the soft edge. Only that box is worked on, so a
    // stroke on a large photo costs little.
    let left = Infinity;
    let top = Infinity;
    let right = -1;
    let bottom = -1;
    for (let i = 0; i < total; i += 1) {
      if (!mask[i * 4 + 3]) continue;
      const x = i % width;
      const y = (i - x) / width;
      if (x < left) left = x;
      if (x > right) right = x;
      if (y < top) top = y;
      if (y > bottom) bottom = y;
    }
    if (right < 0) return pixels;
    const box = {
      left: Math.max(0, left - feather * 2),
      top: Math.max(0, top - feather * 2),
      right: Math.min(width, right + 1 + feather * 2),
      bottom: Math.min(height, bottom + 1 + feather * 2)
    };
    const boxWidth = box.right - box.left;
    const boxHeight = box.bottom - box.top;

    // Strength of each brush per pixel of the box: red Restore, green Refine Edge, blue Erase.
    const planes = [0, 1, 2].map(() => new Float32Array(boxWidth * boxHeight));
    for (let y = box.top; y < box.bottom; y += 1) {
      for (let x = box.left; x < box.right; x += 1) {
        const at = (y * width + x) * 4;
        const strength = mask[at + 3] / 255;
        if (!strength) continue;
        const local = (y - box.top) * boxWidth + (x - box.left);
        for (let plane = 0; plane < 3; plane += 1) planes[plane][local] = mask[at + plane] / 255 * strength;
      }
    }
    if (feather) {
      const whole = { left: 0, top: 0, right: boxWidth, bottom: boxHeight };
      for (const plane of planes) featherPlane(plane, boxWidth, whole, feather);
    }

    const person = wall && planes[1].some(value => value > 0)
      ? personColours(original, width, box, wall, Math.max(4, feather * 2))
      : null;
    const was = [0, 0, 0];
    for (let y = box.top; y < box.bottom; y += 1) {
      for (let x = box.left; x < box.right; x += 1) {
        const local = (y - box.top) * boxWidth + (x - box.left);
        let restore = planes[0][local];
        let refine = planes[1][local];
        let erase = planes[2][local];
        const sum = restore + refine + erase;
        if (sum < 0.002) continue;
        if (sum > 1) {
          restore /= sum;
          refine /= sum;
          erase /= sum;
        }
        const stay = Math.max(0, 1 - restore - refine - erase);
        const at = (y * width + x) * 4;
        // Refine Edge: how much of the photo here was old wall. 1 is all wall, 0 all person.
        // Without the wall's colours it acts as Erase.
        let wallShare = 1;
        if (refine && wall) {
          wallColourAt(wall, x, y, was);
          wallShare = 1 - personShare(original, at, was, person && person.at(x, y), wall.limit);
        } else {
          was[0] = original[at];
          was[1] = original[at + 1];
          was[2] = original[at + 2];
        }
        for (let channel = 0; channel < 3; channel += 1) {
          const refined = original[at + channel] + wallShare * (colour[channel] - was[channel]);
          pixels[at + channel] = Math.round(
            pixels[at + channel] * stay
            + original[at + channel] * restore
            + Math.min(255, Math.max(0, refined)) * refine
            + colour[channel] * erase);
        }
      }
    }
    return pixels;
  }

  /**
   * Finds the paper and the ink in a photographed signature: the paper is the usual brightness
   * (most of the picture), the ink the darkest half percent. Null when there is no clear ink.
   */
  function measurePaper(pixels) {
    const histogram = new Uint32Array(256);
    let count = 0;
    for (let i = 0; i < pixels.length; i += 4) {
      if (pixels[i + 3] === 0) continue; // see-through pixels are neither paper nor ink
      histogram[Math.round(0.299 * pixels[i] + 0.587 * pixels[i + 1] + 0.114 * pixels[i + 2])] += 1;
      count += 1;
    }
    if (!count) return null;
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
  // Clean up, adjust and filters (Edit photo)
  // ---------------------------------------------------------------------------

  const luminance = (pixels, i) => 0.299 * pixels[i] + 0.587 * pixels[i + 1] + 0.114 * pixels[i + 2];

  /**
   * Brightness, contrast, saturation and warmth, each from -50 to 50, applied to RGBA pixels
   * in place. Zero leaves a photo exactly as it is.
   */
  function adjustPixels(pixels, settings) {
    const brightness = Number(settings.brightness) || 0;
    const contrast = Number(settings.contrast) || 0;
    const saturation = Number(settings.saturation) || 0;
    const warmth = Number(settings.warmth) || 0;
    const gain = 1 + contrast / 50 * 0.6;
    const lift = brightness * 1.28;
    const colour = 1 + saturation / 50;
    const warm = warmth * 0.6;
    const table = new Float32Array(256);
    for (let value = 0; value < 256; value += 1) {
      table[value] = (value - 128) * gain + 128 + lift;
    }
    for (let i = 0; i < pixels.length; i += 4) {
      let red = table[pixels[i]] + warm;
      let green = table[pixels[i + 1]];
      let blue = table[pixels[i + 2]] - warm;
      if (saturation) {
        const grey = 0.299 * red + 0.587 * green + 0.114 * blue;
        red = grey + (red - grey) * colour;
        green = grey + (green - grey) * colour;
        blue = grey + (blue - grey) * colour;
      }
      pixels[i] = red;
      pixels[i + 1] = green;
      pixels[i + 2] = blue;
    }
    return pixels;
  }

  // The six filters. Each is a fixed recipe, so a filter always looks the same.
  const FILTERS = Object.freeze([
    Object.freeze({ key: 'bw', label: 'Black and white' }),
    Object.freeze({ key: 'sepia', label: 'Sepia' }),
    Object.freeze({ key: 'warm', label: 'Warm', adjust: Object.freeze({ warmth: 25, saturation: 8 }) }),
    Object.freeze({ key: 'cool', label: 'Cool', adjust: Object.freeze({ warmth: -25 }) }),
    Object.freeze({ key: 'vivid', label: 'Vivid', adjust: Object.freeze({ contrast: 15, saturation: 30 }) }),
    Object.freeze({ key: 'faded', label: 'Faded', adjust: Object.freeze({ contrast: -20, brightness: 8, saturation: -25 }) })
  ]);

  function filterPixels(pixels, key) {
    const filter = FILTERS.find(item => item.key === key);
    if (!filter) return pixels;
    if (filter.adjust) return adjustPixels(pixels, filter.adjust);
    toGreyscale(pixels);
    if (key === 'sepia') {
      for (let i = 0; i < pixels.length; i += 4) {
        const grey = pixels[i];
        pixels[i] = grey * 1.07 + 12;
        pixels[i + 1] = grey * 0.95 + 4;
        pixels[i + 2] = grey * 0.74;
      }
    }
    return pixels;
  }

  /**
   * Runs `change(i, red, green, blue)` for every pixel, in place, with the average of that pixel
   * and its eight neighbours (alpha untouched). Each neighbour counts by how solid it is, so the
   * see-through part of a PNG or WEBP does not darken the edge of what is next to it.
   *
   * Only three rows of sums are held at a time, so a large photo needs little extra memory. Each
   * row's sums are taken before that row or the one above it is changed, so every average comes
   * from the pixels as they were.
   */
  function withNeighbourAverage(pixels, width, height, change) {
    // Colour times alpha in the first three places, alpha in the fourth.
    const rowSums = [0, 1, 2].map(() => new Float32Array(width * 4));
    const sumRow = (y, into) => {
      into.fill(0);
      const start = y * width;
      for (let x = 0; x < width; x += 1) {
        const i = x * 4;
        for (const j of [(start + Math.max(0, x - 1)) * 4, (start + x) * 4, (start + Math.min(width - 1, x + 1)) * 4]) {
          const alpha = pixels[j + 3];
          into[i] += pixels[j] * alpha;
          into[i + 1] += pixels[j + 1] * alpha;
          into[i + 2] += pixels[j + 2] * alpha;
          into[i + 3] += alpha;
        }
      }
      return into;
    };
    // Row r's sums live in buffer r % 3. Past the top or bottom edge, the edge row stands in.
    const row = (r) => rowSums[Math.min(height - 1, Math.max(0, r)) % 3];
    sumRow(0, rowSums[0]);
    for (let y = 0; y < height; y += 1) {
      // Row y+1 takes the buffer of row y-2, which is no longer needed. It is still unchanged.
      if (y + 1 < height) sumRow(y + 1, rowSums[(y + 1) % 3]);
      const above = row(y - 1);
      const here = row(y);
      const below = row(y + 1);
      for (let x = 0; x < width; x += 1) {
        const k = x * 4;
        const weight = above[k + 3] + here[k + 3] + below[k + 3];
        // With nothing visible around it, a pixel is left as it is.
        if (weight <= 0) continue;
        change(
          (y * width + x) * 4,
          (above[k] + here[k] + below[k]) / weight,
          (above[k + 1] + here[k + 1] + below[k + 1]) / weight,
          (above[k + 2] + here[k + 2] + below[k + 2]) / weight);
      }
    }
    return pixels;
  }

  /** Makes edges crisper: each pixel is pushed away from the average of its neighbours. */
  function sharpenPixels(pixels, width, height, amount) {
    const strength = amount === undefined ? 0.7 : amount;
    return withNeighbourAverage(pixels, width, height, (i, red, green, blue) => {
      pixels[i] += (pixels[i] - red) * strength;
      pixels[i + 1] += (pixels[i + 1] - green) * strength;
      pixels[i + 2] += (pixels[i + 2] - blue) * strength;
    });
  }

  /** Calms grain: each pixel is moved halfway towards the average of its neighbours. */
  function softenPixels(pixels, width, height) {
    return withNeighbourAverage(pixels, width, height, (i, red, green, blue) => {
      pixels[i] = (pixels[i] + red) / 2;
      pixels[i + 1] = (pixels[i + 1] + green) / 2;
      pixels[i + 2] = (pixels[i + 2] + blue) / 2;
    });
  }

  /**
   * Evens out light that falls off across a photo (a shadow on one side, a dark corner). The
   * photo is divided into a coarse grid; each area is pulled towards the overall brightness, and
   * the correction is blended smoothly between areas so no blocks show. Strength 0 to 1.
   */
  function evenLighting(pixels, width, height, strength) {
    const blend = strength === undefined ? 0.7 : strength;
    const cell = Math.max(1, Math.ceil(Math.max(width, height) / 24));
    const columns = Math.ceil(width / cell);
    const rows = Math.ceil(height / cell);
    // Every pixel counts by how solid it is, so the see-through part of a PNG or WEBP is not
    // measured as darkness. An area with nothing visible in it takes no part at all.
    const sums = new Float64Array(columns * rows);
    const weights = new Float64Array(columns * rows);
    let total = 0;
    let weight = 0;
    for (let y = 0; y < height; y += 1) {
      const row = Math.floor(y / cell) * columns;
      for (let x = 0; x < width; x += 1) {
        const i = (y * width + x) * 4;
        const alpha = pixels[i + 3];
        if (!alpha) continue;
        const at = row + Math.floor(x / cell);
        const light = luminance(pixels, i) * alpha;
        sums[at] += light;
        weights[at] += alpha;
        total += light;
        weight += alpha;
      }
    }
    if (!weight) return pixels;
    const target = total / weight;
    const raw = new Float64Array(columns * rows);
    for (let at = 0; at < raw.length; at += 1) {
      raw[at] = weights[at] ? sums[at] / weights[at] : NaN;
    }
    // Each area is averaged with its neighbours, so one dark object does not drag its area up.
    const grid = new Float64Array(raw.length);
    for (let r = 0; r < rows; r += 1) {
      for (let c = 0; c < columns; c += 1) {
        let sum = 0;
        let seen = 0;
        for (let dr = -1; dr <= 1; dr += 1) {
          for (let dc = -1; dc <= 1; dc += 1) {
            const rr = r + dr;
            const cc = c + dc;
            if (rr >= 0 && rr < rows && cc >= 0 && cc < columns && !Number.isNaN(raw[rr * columns + cc])) {
              sum += raw[rr * columns + cc];
              seen += 1;
            }
          }
        }
        // With nothing visible nearby there is nothing to correct: the area is left as it is.
        grid[r * columns + c] = seen ? sum / seen : target;
      }
    }
    for (let y = 0; y < height; y += 1) {
      const gy = Math.min(rows - 1, Math.max(0, (y + 0.5) / cell - 0.5));
      const r0 = Math.floor(gy);
      const r1 = Math.min(rows - 1, r0 + 1);
      const fy = gy - r0;
      for (let x = 0; x < width; x += 1) {
        const gx = Math.min(columns - 1, Math.max(0, (x + 0.5) / cell - 0.5));
        const c0 = Math.floor(gx);
        const c1 = Math.min(columns - 1, c0 + 1);
        const fx = gx - c0;
        const local = (grid[r0 * columns + c0] * (1 - fx) + grid[r0 * columns + c1] * fx) * (1 - fy)
          + (grid[r1 * columns + c0] * (1 - fx) + grid[r1 * columns + c1] * fx) * fy;
        const full = Math.min(1.8, Math.max(0.6, target / Math.max(1, local)));
        const gain = 1 + (full - 1) * blend;
        const i = (y * width + x) * 4;
        pixels[i] *= gain;
        pixels[i + 1] *= gain;
        pixels[i + 2] *= gain;
      }
    }
    return pixels;
  }

  /**
   * A photographed page made to look scanned: the light is evened out, then the paper is taken
   * to white and the ink to dark. Each colour has its own white point (the usual level of that
   * colour, which is the paper), so yellowish paper under a lamp still comes out white, while
   * coloured ink is kept (a red stamp stays red).
   */
  function cleanDocument(pixels, width, height) {
    evenLighting(pixels, width, height, 1);
    const levels = measurePaper(pixels);
    if (!levels) return pixels;
    const tables = [0, 1, 2].map(channel => {
      const histogram = new Uint32Array(256);
      let visible = 0;
      for (let i = channel; i < pixels.length; i += 4) {
        if (pixels[i - channel + 3] === 0) continue;
        histogram[pixels[i]] += 1;
        visible += 1;
      }
      let paper = 0;
      for (let seen = 0; paper < 255; paper += 1) {
        seen += histogram[paper];
        if (seen > visible / 2) break;
      }
      const range = Math.max(40, paper - levels.ink);
      const black = levels.ink + range * 0.1;
      const white = Math.max(black + 1, paper - range * 0.12);
      const table = new Uint8ClampedArray(256);
      for (let value = 0; value < 256; value += 1) {
        table[value] = (value - black) / (white - black) * 255;
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

  // The one-click clean-ups, in the order the page shows them.
  const CLEAN_UPS = Object.freeze([
    Object.freeze({ key: 'enhance', label: 'Auto enhance', note: 'Fixes dull light and a colour cast in one step.' }),
    Object.freeze({ key: 'cast', label: 'Fix colour cast', note: 'Removes a yellow or blue tint and leaves the light alone.' }),
    Object.freeze({ key: 'even', label: 'Even out lighting', note: 'Lifts a shadow on one side or a dark corner.' }),
    Object.freeze({ key: 'document', label: 'Clean document', note: 'White paper and dark text, for a photographed page.' }),
    Object.freeze({ key: 'denoise', label: 'Reduce noise', note: 'Calms grain from a dark room. Softens fine detail a little.' }),
    Object.freeze({ key: 'sharpen', label: 'Sharpen', note: 'Makes edges crisper.' })
  ]);

  /** True for a step that changes the pixels and keeps the size (a clean-up, adjust or filter). */
  function isPixelStep(step) {
    return step.type === 'cleanup' || step.type === 'adjust' || step.type === 'filter';
  }

  /** Applies a clean-up, adjust or filter step to RGBA pixels in place. */
  function applyPixelStep(pixels, width, height, step) {
    if (step.type === 'adjust') return adjustPixels(pixels, step);
    if (step.type === 'filter') return filterPixels(pixels, step.key);
    if (step.type !== 'cleanup') return pixels;
    if (step.key === 'enhance') return applyLighting(pixels, measureLighting(pixels), 0);
    if (step.key === 'cast') return applyLighting(pixels, { low: 0, high: 255, gains: measureLighting(pixels).gains }, 0);
    if (step.key === 'even') return evenLighting(pixels, width, height);
    if (step.key === 'document') return cleanDocument(pixels, width, height);
    if (step.key === 'denoise') return softenPixels(pixels, width, height);
    if (step.key === 'sharpen') return sharpenPixels(pixels, width, height);
    return pixels;
  }

  // ---------------------------------------------------------------------------
  // Mark up and hide (Edit photo)
  // ---------------------------------------------------------------------------

  // The ways an area can be hidden. Cover is the one that is safe for writing and numbers.
  const HIDE_MODES = Object.freeze(['cover', 'blur', 'pixelate']);

  const MARK_KINDS = Object.freeze([
    Object.freeze({ key: 'box', label: 'Box', icon: 'bi-square', drag: true }),
    Object.freeze({ key: 'circle', label: 'Circle', icon: 'bi-circle', drag: true }),
    Object.freeze({ key: 'arrow', label: 'Arrow', icon: 'bi-arrow-up-right', drag: true }),
    Object.freeze({ key: 'pen', label: 'Pen', icon: 'bi-pencil', drag: true }),
    Object.freeze({ key: 'text', label: 'Text', icon: 'bi-fonts', drag: false }),
    Object.freeze({ key: 'number', label: 'Number', icon: 'bi-1-circle', drag: false })
  ]);

  // Colours drawn into the photo. `edge` is the outline that keeps text readable on any photo.
  const MARK_COLOURS = Object.freeze([
    Object.freeze({ key: 'red', label: 'Red', value: '#e02424', edge: '#ffffff' }),
    Object.freeze({ key: 'yellow', label: 'Yellow', value: '#facc15', edge: '#1f2937' }),
    Object.freeze({ key: 'green', label: 'Green', value: '#16a34a', edge: '#ffffff' }),
    Object.freeze({ key: 'blue', label: 'Blue', value: '#2563eb', edge: '#ffffff' }),
    Object.freeze({ key: 'black', label: 'Black', value: '#111827', edge: '#ffffff' }),
    Object.freeze({ key: 'white', label: 'White', value: '#ffffff', edge: '#1f2937' })
  ]);

  // Line and text sizes as a share of the photo's longer side, so a mark looks the same on a
  // small photo and a large one.
  const MARK_SIZES = Object.freeze([
    Object.freeze({ key: 'thin', label: 'Thin', line: 0.004, text: 0.03 }),
    Object.freeze({ key: 'medium', label: 'Medium', line: 0.008, text: 0.05 }),
    Object.freeze({ key: 'thick', label: 'Thick', line: 0.014, text: 0.08 })
  ]);

  /** The line width and text height, in pixels, of a mark of the given size on a photo. */
  function markMeasures(bounds, sizeKey) {
    const size = MARK_SIZES.find(item => item.key === sizeKey) || MARK_SIZES[1];
    const longest = Math.max(bounds.width, bounds.height);
    return { line: Math.max(2, Math.round(longest * size.line)), text: Math.max(10, Math.round(longest * size.text)) };
  }

  /** The upright rectangle between two dragged points, kept on the photo. */
  function dragRect(from, to, bounds) {
    const clampX = (value) => Math.min(bounds.width, Math.max(0, value));
    const clampY = (value) => Math.min(bounds.height, Math.max(0, value));
    const left = Math.round(Math.min(clampX(from.x), clampX(to.x)));
    const top = Math.round(Math.min(clampY(from.y), clampY(to.y)));
    const right = Math.round(Math.max(clampX(from.x), clampX(to.x)));
    const bottom = Math.round(Math.max(clampY(from.y), clampY(to.y)));
    return { x: left, y: top, width: right - left, height: bottom - top };
  }

  /**
   * The three corners of an arrow's head: the tip at the end point, and two corners behind it
   * on either side of the line. Null when the arrow has no length.
   */
  function arrowHead(from, to, line) {
    const length = Math.hypot(to.x - from.x, to.y - from.y);
    if (!length) return null;
    const ux = (to.x - from.x) / length;
    const uy = (to.y - from.y) / length;
    const head = Math.min(length, line * 4.5);
    const half = head * 0.55;
    const backX = to.x - ux * head;
    const backY = to.y - uy * head;
    return {
      tip: { x: to.x, y: to.y },
      left: { x: backX - uy * half, y: backY + ux * half },
      right: { x: backX + uy * half, y: backY - ux * half },
      // The line stops inside the head, so its square end does not poke out past the tip.
      neck: { x: to.x - ux * head * 0.8, y: to.y - uy * head * 0.8 }
    };
  }

  /** Drops pen points closer than `gap` to the one before, keeping the first and the last. */
  function thinPoints(points, gap) {
    if (points.length < 3) return points.slice();
    const kept = [points[0]];
    for (let i = 1; i < points.length - 1; i += 1) {
      const last = kept[kept.length - 1];
      if (Math.hypot(points[i].x - last.x, points[i].y - last.y) >= gap) kept.push(points[i]);
    }
    kept.push(points[points.length - 1]);
    return kept;
  }

  /**
   * The points of a pen line, thinned, or null when the pen never moved as far as the line is
   * wide from where it started: a tap is not a line (and would draw a dot or nothing,
   * depending on the browser).
   */
  function penStroke(points, line) {
    if (!points.length) return null;
    const first = points[0];
    const reach = points.reduce((most, point) => Math.max(most, Math.hypot(point.x - first.x, point.y - first.y)), 0);
    return reach >= line ? thinPoints(points, line / 2) : null;
  }

  /** The side of the squares an area is broken into when it is hidden: about a tenth of it. */
  function hideBlock(width, height) {
    return Math.max(6, Math.round(Math.max(width, height) / 10));
  }

  /**
   * Hides what is in a patch of RGBA pixels, in place. The patch is cut into squares and each
   * square is replaced by its average colour ("pixelate"); "blur" then blends smoothly between
   * the squares. Either way the detail is gone from the pixels: it cannot be brought back from
   * the saved photo. How see-through each pixel is gets averaged the same way, so the outline of
   * writing on a see-through PNG or WEBP is removed as well, not only its colour.
   *
   * Blur and pixelate keep one average per square, and a short text or number can sometimes be
   * guessed back from those averages by trying every possible text. "cover" makes the patch solid
   * black whatever was there (not its average, which would still tell how much ink it held), so
   * nothing of what was there is left to guess from.
   */
  function hidePixels(pixels, width, height, mode) {
    if (mode === 'cover') {
      for (let i = 0; i < width * height * 4; i += 4) {
        pixels[i] = 0;
        pixels[i + 1] = 0;
        pixels[i + 2] = 0;
        pixels[i + 3] = 255;
      }
      return pixels;
    }
    const block = hideBlock(width, height);
    const columns = Math.ceil(width / block);
    const rows = Math.ceil(height / block);
    const sums = new Float64Array(columns * rows * 4);
    const counts = new Float64Array(columns * rows);
    for (let y = 0; y < height; y += 1) {
      const row = Math.floor(y / block) * columns;
      for (let x = 0; x < width; x += 1) {
        const i = (y * width + x) * 4;
        const at = (row + Math.floor(x / block)) * 4;
        const alpha = pixels[i + 3];
        sums[at] += pixels[i] * alpha;
        sums[at + 1] += pixels[i + 1] * alpha;
        sums[at + 2] += pixels[i + 2] * alpha;
        sums[at + 3] += alpha;
        counts[at / 4] += 1;
      }
    }
    // For each square: its average colour times how solid it is, then how solid it is. Blending
    // colour in this form keeps a see-through square from darkening the solid one beside it.
    const grid = new Float64Array(columns * rows * 4);
    for (let at = 0; at < columns * rows * 4; at += 1) {
      grid[at] = sums[at] / counts[Math.floor(at / 4)];
    }
    const blend = [0, 0, 0, 0];
    for (let y = 0; y < height; y += 1) {
      const gy = Math.min(rows - 1, Math.max(0, (y + 0.5) / block - 0.5));
      const r0 = mode === 'blur' ? Math.floor(gy) : Math.floor(y / block);
      const r1 = mode === 'blur' ? Math.min(rows - 1, r0 + 1) : r0;
      const fy = mode === 'blur' ? gy - r0 : 0;
      for (let x = 0; x < width; x += 1) {
        const gx = Math.min(columns - 1, Math.max(0, (x + 0.5) / block - 0.5));
        const c0 = mode === 'blur' ? Math.floor(gx) : Math.floor(x / block);
        const c1 = mode === 'blur' ? Math.min(columns - 1, c0 + 1) : c0;
        const fx = mode === 'blur' ? gx - c0 : 0;
        const i = (y * width + x) * 4;
        for (let channel = 0; channel < 4; channel += 1) {
          const top = grid[(r0 * columns + c0) * 4 + channel] * (1 - fx) + grid[(r0 * columns + c1) * 4 + channel] * fx;
          const bottom = grid[(r1 * columns + c0) * 4 + channel] * (1 - fx) + grid[(r1 * columns + c1) * 4 + channel] * fx;
          blend[channel] = top * (1 - fy) + bottom * fy;
        }
        const solid = blend[3];
        pixels[i] = solid ? blend[0] / solid : 0;
        pixels[i + 1] = solid ? blend[1] / solid : 0;
        pixels[i + 2] = solid ? blend[2] / solid : 0;
        pixels[i + 3] = solid;
      }
    }
    return pixels;
  }

  // ---------------------------------------------------------------------------
  // Target file size
  // ---------------------------------------------------------------------------

  /**
   * A reduced photo that came out no smaller than the file it was made from is no reduction.
   * When it was saved in the type it already had, and that type has a quality setting, a lower
   * quality will get under the original: this gives the size to try again under. Null when the
   * photo is already smaller, or when quality cannot help (a PNG, or a change of type).
   */
  function retryUnderOriginal({ resultBytes, originalBytes, lossy, sameType }) {
    if (!(originalBytes > 1) || resultBytes < originalBytes) return null;
    return lossy && sameType ? originalBytes - 1 : null;
  }

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
    pixelsToMillimetres,
    COVER_MAX_ZOOM,
    dragCover,
    BACKGROUND_STATUS,
    measureBackground,
    changeBackground,
    findBackground,
    PASSPORT_GUIDE,
    headOutline,
    passportChecks,
    BACKGROUND_BRUSHES,
    wallColourAt,
    featherPlane,
    applyBackgroundFixes,
    sheetPdfPlan,
    coverCrop,
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
    FILTERS,
    CLEAN_UPS,
    adjustPixels,
    filterPixels,
    sharpenPixels,
    softenPixels,
    evenLighting,
    cleanDocument,
    isPixelStep,
    applyPixelStep,
    MARK_KINDS,
    MARK_COLOURS,
    MARK_SIZES,
    markMeasures,
    dragRect,
    arrowHead,
    thinPoints,
    penStroke,
    HIDE_MODES,
    hideBlock,
    hidePixels,
    retryUnderOriginal,
    encodeToTarget
  });
});
