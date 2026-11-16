// PRISM Collage Studio — the collage model and its rules.
//
// Everything in this file runs in the user's browser (and in Node for tests). Collage Studio
// never uploads photos to PRISM: collages are made in the browser and saved by the user. The
// model is plain JSON; Konva only draws it (docs/ux/collage-studio.md). Nothing here touches the
// page, a canvas or the network, so every rule can be tested without a browser.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) {
    module.exports = api;
  } else {
    root.PrismCollage = api;
  }
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  /** The model's version, kept in every collage so a later version can read an older draft. */
  const MODEL_VERSION = 1;

  /** Every page is 1,000 units wide; its height follows the size. Saving scales it up. */
  const PAGE_WIDTH = 1000;

  /**
   * Page sizes. `width` and `height` are the pixels of a print-quality picture: 300 dots per inch
   * for paper, a full-resolution picture for screens. `mm` is the paper size, for a PDF.
   */
  const SIZES = Object.freeze({
    a4l: Object.freeze({ label: 'A4 landscape', note: '297 × 210 mm', width: 3508, height: 2480, mm: [297, 210] }),
    a4p: Object.freeze({ label: 'A4 portrait', note: '210 × 297 mm', width: 2480, height: 3508, mm: [210, 297] }),
    a3p: Object.freeze({ label: 'A3 poster', note: '297 × 420 mm', width: 3508, height: 4961, mm: [297, 420] }),
    slide: Object.freeze({ label: 'Slide 16:9', note: 'Briefing slides', width: 3840, height: 2160, mm: null }),
    square: Object.freeze({ label: 'Square', note: 'WhatsApp, posts', width: 3000, height: 3000, mm: null }),
    story: Object.freeze({ label: 'Phone status', note: '9:16, full screen', width: 2160, height: 3840, mm: null }),
    banner: Object.freeze({ label: 'Banner 3:1', note: 'Notice board strip', width: 6000, height: 2000, mm: null })
  });
  const DEFAULT_SIZE = 'a4l';

  /** Photos a collage takes. A photo larger than MAX_BYTES is refused before it is read. */
  const PHOTO_LIMITS = Object.freeze({
    maxBytes: 50 * 1000 * 1000,
    maxPhotos: 60,
    // Larger than any phone or camera photo; beyond it a picture is refused, not decoded.
    maxPixels: 250 * 1000 * 1000,
    // The editor draws a copy at most this long on its long side; Save uses the original.
    workingSide: 2000,
    // The tray shows a small copy made from the working copy, never from the original.
    thumbnailSide: 240
  });

  /** Item sizes in page units: never too small to grab, never far larger than the page. */
  const ITEM_LIMITS = Object.freeze({ minWidth: 24, maxWidth: 4000 });

  /** Turning: the angle clicks to straight and every 45° within this many degrees. */
  const ANGLE_SNAP = Object.freeze({ every: 45, within: 3, stepWithShift: 15 });

  /** Moving: the item snaps to a guide within this many screen pixels. */
  const SNAP_PIXELS = 7;

  const sizeOf = (key) => SIZES[key] || SIZES[DEFAULT_SIZE];

  /** The page's height in page units for a size. */
  function pageHeight(key) {
    const size = sizeOf(key);
    return PAGE_WIDTH * size.height / size.width;
  }

  /** A new, empty collage. */
  function newCollage(sizeKey) {
    return {
      version: MODEL_VERSION,
      size: SIZES[sizeKey] ? sizeKey : DEFAULT_SIZE,
      background: { type: 'color', value: '#ffffff' },
      // The layout last chosen ({ key, gap, radius }), or null for a free arrangement.
      layout: null,
      items: []
    };
  }

  // ---------------------------------------------------------------------------
  // Photos
  // ---------------------------------------------------------------------------

  /**
   * The picture type from a file's first bytes, never from its name or the type the browser
   * guesses. Returns 'jpeg', 'png', 'gif', 'webp', 'bmp' or null.
   */
  function sniffImageType(bytes) {
    if (!bytes || bytes.length < 12) return null;
    const at = (i, values) => values.every((v, k) => bytes[i + k] === v);
    if (at(0, [0xFF, 0xD8, 0xFF])) return 'jpeg';
    if (at(0, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return 'png';
    if (at(0, [0x47, 0x49, 0x46, 0x38])) return 'gif';
    if (at(0, [0x52, 0x49, 0x46, 0x46]) && at(8, [0x57, 0x45, 0x42, 0x50])) return 'webp';
    if (at(0, [0x42, 0x4D])) return 'bmp';
    return null;
  }

  /**
   * Which of the chosen files can be added, and why the others cannot. `files` are
   * { name, size, type } with `type` already sniffed (null when not a picture). `already` is the
   * number of photos the collage has.
   */
  function checkPhotos(files, already) {
    const accepted = [];
    const refused = [];
    let room = Math.max(0, PHOTO_LIMITS.maxPhotos - (already || 0));
    for (const file of files) {
      // A file named as a photo whose contents are not one is most likely damaged.
      if (file.unreadable || (!file.type && PHOTO_NAME.test(String(file.name || '')))) refused.push({ name: file.name, reason: 'unreadable' });
      else if (!file.type) refused.push({ name: file.name, reason: 'not-photo' });
      else if (file.size > PHOTO_LIMITS.maxBytes) refused.push({ name: file.name, reason: 'too-large' });
      else if (room <= 0) refused.push({ name: file.name, reason: 'too-many' });
      else { accepted.push(file); room -= 1; }
    }
    return { accepted, refused };
  }

  /**
   * The largest canvas, in pixels, the editor gives the browser for each layer of the page. iPhones
   * draw nothing on a canvas over 16,777,216 pixels; the page keeps a margin below that.
   */
  const CANVAS_LIMITS = { maxPixels: 16000000, minRatio: 0.5 };

  /**
   * The pixel ratio for a page canvas of this size: the screen's own, made smaller when the canvas
   * would pass the limit (zoomed far in on a phone). The page then looks a little softer instead
   * of going blank.
   */
  function canvasPixelRatio(width, height, deviceRatio) {
    const device = Number.isFinite(deviceRatio) && deviceRatio > 0 ? deviceRatio : 1;
    const area = Math.max(1, width) * Math.max(1, height);
    const fits = Math.sqrt(CANVAS_LIMITS.maxPixels / area);
    return Math.max(Math.min(CANVAS_LIMITS.minRatio, device), Math.min(device, fits));
  }

  /** A file name that says it is a photo. */
  const PHOTO_NAME = /\.(jpe?g|png|webp|gif|bmp)$/i;

  /** One sentence for photos that were not added, or '' when every photo was. */
  function refusedMessage(refused) {
    if (!refused.length) return '';
    const by = (reason) => refused.filter((r) => r.reason === reason);
    const names = (list) => list.length === 1 ? `“${list[0].name}”` : `${list.length} files`;
    const parts = [];
    const notPhoto = by('not-photo');
    const tooLarge = by('too-large');
    const tooMany = by('too-many');
    const unreadable = by('unreadable');
    const tooManyPixels = by('too-many-pixels');
    if (unreadable.length) parts.push(`${names(unreadable)} could not be opened; ${unreadable.length === 1 ? 'it may be' : 'they may be'} damaged`);
    if (notPhoto.length) parts.push(`${names(notPhoto)} ${notPhoto.length === 1 ? 'is' : 'are'} not a JPG, PNG, WebP, GIF or BMP photo`);
    if (tooLarge.length) parts.push(`${names(tooLarge)} ${tooLarge.length === 1 ? 'is' : 'are'} larger than 50 MB`);
    if (tooManyPixels.length) parts.push(`${names(tooManyPixels)} ${tooManyPixels.length === 1 ? 'has' : 'have'} more than 250 million pixels`);
    if (tooMany.length) parts.push(`${names(tooMany)} did not fit: a collage takes up to ${PHOTO_LIMITS.maxPhotos} photos`);
    const text = parts.join('; ');
    return text.charAt(0).toUpperCase() + text.slice(1) + '.';
  }

  /** The working copy's size: the photo made smaller to `side` on its long side, never larger. */
  function workingSize(width, height, side = PHOTO_LIMITS.workingSide) {
    const scale = Math.min(1, side / Math.max(width, height));
    return { width: Math.max(1, Math.round(width * scale)), height: Math.max(1, Math.round(height * scale)) };
  }

  /**
   * Where a newly added photo goes: a little off the centre, so photos added one after another do
   * not hide each other, and small enough to leave room for more. `count` is the number of photos
   * already on the page.
   */
  function placeNewPhoto(sizeKey, aspect, count) {
    const height = pageHeight(sizeKey);
    const fit = Math.min(PAGE_WIDTH * 0.42, height * 0.42 * aspect);
    const width = Math.max(ITEM_LIMITS.minWidth, Math.round(fit));
    const step = (count || 0) % 7;
    const dx = [0, -60, 60, -30, 30, -90, 90][step];
    const dy = [0, -40, 40, 50, -50, 20, -20][step];
    return { x: PAGE_WIDTH / 2 + dx, y: height / 2 + dy, width };
  }

  /** How a photo fills its frame: no zoom, centred. */
  const FULL_CROP = Object.freeze({ zoom: 1, x: 0.5, y: 0.5 });
  const MAX_CROP_ZOOM = 4;

  /**
   * A photo item. `photo` is the id of the photo in the collage's photo store. `aspect` is the
   * frame's shape (width / height); the photo fills it, cut evenly at the edges, and `crop`
   * says how far it is zoomed and which part shows.
   */
  function photoItem(id, photo, aspect, place) {
    return {
      id,
      kind: 'photo',
      photo,
      x: place.x,
      y: place.y,
      width: place.width,
      aspect,
      angle: 0,
      opacity: 100,
      radius: 0,
      crop: { ...FULL_CROP },
      ...PLAIN_FINISH
    };
  }

  /** An empty frame from a layout, waiting for a photo. It is never saved into the picture. */
  function slotItem(id, place) {
    return { id, kind: 'slot', x: place.x, y: place.y, width: place.width, aspect: place.aspect, angle: 0, opacity: 100, radius: 0 };
  }

  /** A slot filled with a photo: the frame keeps its place, size, angle and corners. */
  function fillSlot(slot, photo) {
    return { ...slot, kind: 'photo', photo, crop: { ...FULL_CROP }, ...PLAIN_FINISH };
  }

  /**
   * The empty frame a photo leaves when it is taken out of a layout: same id, place, size, turn
   * and corners, and its cell in the layout, so the layout keeps its shape (a change of space or
   * page size moves it with the others) and another photo can go in.
   */
  function emptyFrame(item) {
    const slot = { id: item.id, kind: 'slot', x: item.x, y: item.y, width: item.width, aspect: item.aspect, angle: item.angle || 0, opacity: 100, radius: item.radius || 0 };
    if (Number.isInteger(item.cell)) slot.cell = item.cell;
    return slot;
  }

  /**
   * Where a new item of this half size goes: the first of a few places on the page (centre first,
   * then down and to the sides) where it covers nothing, or else the one where it covers least.
   */
  function freeSpot(items, sizeKey, halfWidth, halfHeight) {
    const height = pageHeight(sizeKey);
    const rows = [0.5, 0.3, 0.7, 0.15, 0.85];
    const columns = [0.5, 0.27, 0.73];
    const boxes = (items || []).filter((item) => !item.hidden).map((item) => {
      const box = turnedHalfBox(item.width, itemHeight(item), item.angle || 0);
      return { x: item.x, y: item.y, hw: box.halfWidth, hh: box.halfHeight };
    });
    let best = null;
    for (const row of rows) {
      for (const column of columns) {
        const x = Math.min(PAGE_WIDTH - halfWidth, Math.max(halfWidth, PAGE_WIDTH * column));
        const y = Math.min(height - halfHeight, Math.max(halfHeight, height * row));
        let covered = 0;
        for (const box of boxes) {
          const w = Math.min(x + halfWidth, box.x + box.hw) - Math.max(x - halfWidth, box.x - box.hw);
          const h = Math.min(y + halfHeight, box.y + box.hh) - Math.max(y - halfHeight, box.y - box.hh);
          if (w > 0 && h > 0) covered += w * h;
        }
        if (covered === 0) return { x, y };
        if (!best || covered < best.covered) best = { x, y, covered };
      }
    }
    return { x: best.x, y: best.y };
  }

  // ---------------------------------------------------------------------------
  // The part of a photo a frame shows
  // ---------------------------------------------------------------------------

  /**
   * The part of the photo the frame shows, as fractions of the photo (0 to 1): the largest
   * part with the frame's shape (so the frame is filled and nothing is stretched), made smaller
   * by the zoom, centred on crop.x and crop.y but never past the photo's edges.
   */
  function cropRegion(photoAspect, frameAspect, crop) {
    const zoom = Math.min(MAX_CROP_ZOOM, Math.max(1, Number(crop?.zoom) || 1));
    // Width and height of the frame's shape inside the photo, as fractions of the photo.
    let w = 1;
    let h = 1;
    if (frameAspect > photoAspect) h = photoAspect / frameAspect;
    else w = frameAspect / photoAspect;
    w /= zoom;
    h /= zoom;
    const cx = Math.min(1 - w / 2, Math.max(w / 2, Number.isFinite(crop?.x) ? crop.x : 0.5));
    const cy = Math.min(1 - h / 2, Math.max(h / 2, Number.isFinite(crop?.y) ? crop.y : 0.5));
    return { x: cx - w / 2, y: cy - h / 2, width: w, height: h };
  }

  /** The crop once kept inside the photo, so what is stored always matches what is shown. */
  function clampCrop(photoAspect, frameAspect, crop) {
    const zoom = Math.min(MAX_CROP_ZOOM, Math.max(1, Number(crop?.zoom) || 1));
    const r = cropRegion(photoAspect, frameAspect, { ...crop, zoom });
    return { zoom, x: r.x + r.width / 2, y: r.y + r.height / 2 };
  }

  /**
   * The crop after the photo is dragged inside its frame by (dx, dy) page units, measured along
   * the frame's own sides. Dragging right shows more of the photo's left.
   */
  function panCrop(photoAspect, item, dx, dy) {
    const inner = innerBox(item);
    const aspect = inner.width / inner.height;
    const r = cropRegion(photoAspect, aspect, item.crop);
    // A mirrored photo is drawn the other way round, so a drag across it moves the view back.
    const across = item.flip ? -dx : dx;
    return clampCrop(photoAspect, aspect, {
      zoom: item.crop?.zoom || 1,
      x: r.x + r.width / 2 - across / inner.width * r.width,
      y: r.y + r.height / 2 - dy / inner.height * r.height
    });
  }

  // ---------------------------------------------------------------------------
  // Frames, shapes and looks
  // ---------------------------------------------------------------------------

  /**
   * Frames around a photo, as fractions of the frame's width: the white edge on each side, at
   * the top and at the bottom (a polaroid's deep bottom holds a caption). Frames go with the
   * square shape only; a shaped photo has none.
   */
  const FRAMES = Object.freeze({
    none: Object.freeze({ label: 'None', side: 0, top: 0, bottom: 0 }),
    border: Object.freeze({ label: 'White border', side: 0.035, top: 0.035, bottom: 0.035 }),
    polaroid: Object.freeze({ label: 'Polaroid', side: 0.05, top: 0.05, bottom: 0.2 }),
    tape: Object.freeze({ label: 'Taped', side: 0.035, top: 0.035, bottom: 0.035 }),
    torn: Object.freeze({ label: 'Torn edge', side: 0.035, top: 0.035, bottom: 0.035 })
  });
  const SHAPES = Object.freeze({
    rect: 'Square',
    oval: 'Oval',
    heart: 'Heart',
    star: 'Star',
    hex: 'Hexagon',
    arch: 'Arch'
  });
  /** Looks as CSS filter lists; the same text styles the screen, the save and the previews. */
  const LOOKS = Object.freeze({
    none: Object.freeze({ label: 'Original', filter: '' }),
    bw: Object.freeze({ label: 'Black & white', filter: 'grayscale(1) contrast(1.08)' }),
    sepia: Object.freeze({ label: 'Sepia', filter: 'sepia(0.85)' }),
    vintage: Object.freeze({ label: 'Vintage', filter: 'sepia(0.35) contrast(0.9) brightness(1.06) saturate(0.8)' }),
    vivid: Object.freeze({ label: 'Vivid', filter: 'saturate(1.55) contrast(1.1)' }),
    soft: Object.freeze({ label: 'Soft', filter: 'brightness(1.08) contrast(0.86) saturate(0.9)' }),
    cool: Object.freeze({ label: 'Cool', filter: 'hue-rotate(-12deg) saturate(1.1) brightness(1.03)' }),
    warm: Object.freeze({ label: 'Warm', filter: 'sepia(0.25) saturate(1.3) brightness(1.04)' })
  });
  const CAPTION_MAX = 40;
  const ADJUST_LIMITS = Object.freeze({ bright: [50, 150], contrast: [50, 150], warmth: [-50, 50] });

  /** A photo as it comes: no frame, square, no shadow, its own colours, not mirrored. */
  const PLAIN_FINISH = Object.freeze({
    frame: 'none', caption: '', shadow: false, shape: 'rect', look: 'none', bright: 100, contrast: 100, warmth: 0, flip: false
  });

  /** Whether the item has a frame drawn around its photo (a frame needs the square shape). */
  function isFramed(item) {
    return item.kind === 'photo' && (item.shape || 'rect') === 'rect' && FRAMES[item.frame] && item.frame !== 'none';
  }

  /** A caption strip's height, as a fraction of the photo's width. */
  const CAPTION_STRIP = 0.11;

  /**
   * Whether a photo shows its caption on a white strip under the picture: any photo with a
   * caption, except a polaroid, which writes it by hand in its own deep bottom edge.
   */
  function hasCaptionStrip(item) {
    if (item?.kind !== 'photo' || !String(item.caption || '').trim()) return false;
    return !(isFramed(item) && item.frame === 'polaroid');
  }

  /** The caption strip's height in page units (0 when there is none). */
  /** The most of an item's height a caption strip takes, so the picture always keeps room. */
  const CAPTION_STRIP_MAX_SHARE = 0.4;

  function captionStripHeight(item) {
    if (!hasCaptionStrip(item)) return 0;
    // A very wide or flattened photo keeps most of its height for the picture.
    return Math.min(item.width * CAPTION_STRIP, (item.width / item.aspect) * CAPTION_STRIP_MAX_SHARE);
  }

  /**
   * The frame's edges in page units: left, right, top and bottom. A caption strip is under the
   * picture, inside the frame's bottom edge.
   */
  function framePads(item) {
    const strip = captionStripHeight(item);
    if (!isFramed(item)) return { left: 0, right: 0, top: 0, bottom: strip };
    const frame = FRAMES[item.frame];
    return { left: item.width * frame.side, right: item.width * frame.side, top: item.width * frame.top, bottom: item.width * frame.bottom + strip };
  }

  /** The picture area inside the frame, in the item's own units (0, 0 is its top-left corner). */
  function innerBox(item) {
    const pads = framePads(item);
    const height = item.width / item.aspect;
    return {
      x: pads.left,
      y: pads.top,
      width: Math.max(1, item.width - pads.left - pads.right),
      height: Math.max(1, height - pads.top - pads.bottom)
    };
  }

  /** The smallest picture area a frame keeps, in page units, however flat it is squeezed. */
  const MIN_PICTURE = 24;

  /**
   * The item's aspect kept so a framed photo still has a picture area at least MIN_PICTURE high:
   * a polaroid's edges follow its width, so squeezing it flat could otherwise leave no photo.
   */
  function limitFrameAspect(item) {
    if (!isFramed(item) && !hasCaptionStrip(item)) return item.aspect;
    const pads = framePads(item);
    const minHeight = pads.top + pads.bottom + Math.max(MIN_PICTURE, item.width * 0.1);
    return Math.min(item.aspect, item.width / minHeight);
  }

  /** The shape of the picture area, width / height. */
  function innerAspect(item) {
    const inner = innerBox(item);
    return inner.width / inner.height;
  }

  /**
   * The whole item's aspect for a picture area of `pictureAspect` with the item's frame: used to
   * keep a photo's own shape when a frame is put on or taken off, and to fit a frame to a photo.
   */
  function outerAspectFor(item, pictureAspect) {
    const frame = isFramed(item) ? FRAMES[item.frame] : FRAMES.none;
    const width = item.width;
    const innerWidth = width * (1 - 2 * frame.side);
    const withoutStrip = innerWidth / pictureAspect + width * (frame.top + frame.bottom);
    if (!hasCaptionStrip(item)) return width / withoutStrip;
    // The inverse of captionStripHeight: the strip is its full height unless that is more than
    // its share of the whole item, in which case it is that share.
    const full = withoutStrip + width * CAPTION_STRIP;
    const height = width * CAPTION_STRIP <= full * CAPTION_STRIP_MAX_SHARE ? full : withoutStrip / (1 - CAPTION_STRIP_MAX_SHARE);
    return width / height;
  }

  /**
   * The CSS filter for a photo's look and its brightness, contrast and warmth; '' for none.
   * Warmth above 0 adds a little sepia and colour, below 0 turns the hue toward blue.
   */
  function lookFilter(item) {
    const parts = [];
    const look = LOOKS[item.look] || LOOKS.none;
    if (look.filter) parts.push(look.filter);
    const bright = Number.isFinite(item.bright) ? item.bright : 100;
    const contrast = Number.isFinite(item.contrast) ? item.contrast : 100;
    const warmth = Number.isFinite(item.warmth) ? item.warmth : 0;
    if (bright !== 100) parts.push(`brightness(${bright / 100})`);
    if (contrast !== 100) parts.push(`contrast(${contrast / 100})`);
    if (warmth > 0) parts.push(`sepia(${(warmth / 160).toFixed(3)}) saturate(${(1 + warmth / 200).toFixed(3)})`);
    if (warmth < 0) parts.push(`hue-rotate(${(warmth * 0.35).toFixed(1)}deg) saturate(${(1 - warmth / 300).toFixed(3)})`);
    return parts.join(' ');
  }

  /**
   * A path for a shape filling the box (x, y, w, h), as steps any 2D canvas can follow:
   * ['M', x, y], ['L', x, y], ['Q', cx, cy, x, y], ['C', c1x, c1y, c2x, c2y, x, y],
   * ['E', cx, cy, rx, ry] (a whole ellipse), ['R', x, y, w, h, r] (a rounded rectangle) and ['Z'].
   */
  function shapePath(shape, x, y, w, h, radius = 0) {
    if (shape === 'oval') return [['E', x + w / 2, y + h / 2, w / 2, h / 2]];
    if (shape === 'heart') {
      return [
        ['M', x + w / 2, y + h * 0.28],
        ['C', x + w / 2, y + h * 0.05, x + w * 0.05, y, x + w * 0.02, y + h * 0.3],
        ['C', x, y + h * 0.58, x + w * 0.3, y + h * 0.72, x + w / 2, y + h],
        ['C', x + w * 0.7, y + h * 0.72, x + w, y + h * 0.58, x + w * 0.98, y + h * 0.3],
        ['C', x + w * 0.95, y, x + w / 2, y + h * 0.05, x + w / 2, y + h * 0.28],
        ['Z']
      ];
    }
    if (shape === 'star') {
      const steps = [];
      for (let i = 0; i < 10; i += 1) {
        const r = i % 2 ? 0.42 : 1;
        const a = -Math.PI / 2 + i * Math.PI / 5;
        steps.push([i ? 'L' : 'M', x + w / 2 + Math.cos(a) * r * w / 2, y + h * 0.53 + Math.sin(a) * r * h * 0.53]);
      }
      steps.push(['Z']);
      return steps;
    }
    if (shape === 'hex') {
      return [['M', x + w * 0.25, y], ['L', x + w * 0.75, y], ['L', x + w, y + h / 2], ['L', x + w * 0.75, y + h], ['L', x + w * 0.25, y + h], ['L', x, y + h / 2], ['Z']];
    }
    if (shape === 'arch') {
      const r = Math.min(w / 2, h);
      return [['M', x, y + h], ['L', x, y + r], ['Q', x, y, x + r, y], ['L', x + w - r, y], ['Q', x + w, y, x + w, y + r], ['L', x + w, y + h], ['Z']];
    }
    return [['R', x, y, w, h, Math.max(0, Math.min(radius, w / 2, h / 2))]];
  }

  /**
   * A torn paper edge round the box: points a little inside each side, jagged in a pattern that
   * is always the same, so the screen and the saved picture match.
   */
  function tornEdge(w, h) {
    const n = 22;
    const depth = Math.min(w, h) * 0.022;
    const rand = (i) => ((Math.sin(i * 12.9898) * 43758.5453) % 1 + 1) % 1;
    const points = [];
    for (let i = 0; i <= n; i += 1) points.push([i / n * w, rand(i) * depth]);
    for (let i = 0; i <= n; i += 1) points.push([w - rand(i + 50) * depth, i / n * h]);
    for (let i = n; i >= 0; i -= 1) points.push([i / n * w, h - rand(i + 100) * depth]);
    for (let i = n; i >= 0; i -= 1) points.push([rand(i + 150) * depth, i / n * h]);
    return points;
  }


  // ---------------------------------------------------------------------------
  // Text
  // ---------------------------------------------------------------------------

  /**
   * Fonts for text, all shipped with PRISM (wwwroot/fonts/collage, SIL Open Font License). Each
   * English font falls back to a Devanagari one, so Hindi typed in any font still shows.
   */
  const FONTS = Object.freeze({
    clean: Object.freeze({ label: 'Clean', css: 'Mulish, "Noto Sans Devanagari", sans-serif' }),
    elegant: Object.freeze({ label: 'Elegant', css: '"Playfair Display", "Tiro Devanagari Hindi", serif' }),
    script: Object.freeze({ label: 'Script', css: '"Dancing Script", Kalam, cursive' }),
    hand: Object.freeze({ label: 'Handwritten', css: 'Caveat, Kalam, cursive' }),
    poster: Object.freeze({ label: 'Poster', css: '"Bebas Neue", "Baloo 2", sans-serif' }),
    rounded: Object.freeze({ label: 'Rounded', css: '"Baloo 2", sans-serif' }),
    typewriter: Object.freeze({ label: 'Typewriter', css: '"Courier Prime", "Noto Sans Devanagari", monospace' }),
    hindiHand: Object.freeze({ label: 'हिंदी हस्तलिपि', css: 'Kalam, cursive' }),
    hindiFormal: Object.freeze({ label: 'हिंदी औपचारिक', css: '"Tiro Devanagari Hindi", "Noto Sans Devanagari", serif' })
  });
  const TEXT_LIMITS = Object.freeze({
    size: [8, 300], spacing: [-5, 40], outline: [0, 12], curve: [-100, 100], maxLength: 300
  });
  const HEX_COLOUR = /^#[0-9a-f]{6}$/i;

  /** A text item: the words and how they look. Its box is measured by layoutText(). */
  function textItem(id, text, place, look = {}) {
    return {
      id,
      kind: 'text',
      text,
      x: place.x,
      y: place.y,
      width: 100,
      aspect: 2,
      angle: 0,
      opacity: 100,
      font: 'clean',
      size: 40,
      color: '#172033',
      bold: true,
      italic: false,
      align: 'center',
      spacing: 0,
      outline: 0,
      outlineColor: '#ffffff',
      shadow: false,
      glow: false,
      label: '',
      curve: 0,
      ...look
    };
  }

  /**
   * The letters of a text as a reader sees them: a Hindi syllable with its vowel signs is one
   * cluster, so it is never split apart. Falls back to code points where the browser cannot tell.
   */
  function graphemes(text) {
    if (typeof Intl !== 'undefined' && typeof Intl.Segmenter === 'function') {
      return [...new Intl.Segmenter('hi', { granularity: 'grapheme' }).segment(text)].map((part) => part.segment);
    }
    return [...text];
  }

  /** Sticker sizes in page units: never too small to grab, never far larger than the page. */
  const STICKER_LIMITS = Object.freeze({ width: [20, 700] });
  // "unit:<id>" is a crest or logo an administrator uploaded to PRISM (Crests and logos).
  const STICKER_ID = /^(tw:[0-9a-f]+(-[0-9a-f]+)*|draw:[a-z]+|unit:[1-9][0-9]{0,8})$/;

  /**
   * A sticker: `sticker` names it in the catalogue (collage-stickers.js), "tw:" for an emoji
   * drawing, "draw:" for a drawn sticker, which takes a `colour`, and "unit:" for a crest or
   * logo from PRISM.
   */
  function stickerItem(id, sticker, place, look = {}) {
    return {
      id,
      kind: 'sticker',
      sticker,
      colour: '',
      x: place.x,
      y: place.y,
      width: place.width,
      aspect: place.aspect || 1,
      angle: 0,
      opacity: 100,
      flip: false,
      shadow: false,
      ...look
    };
  }

  /** The CSS font for a text item, as a canvas and the browser's font loader take it. */
  function fontOf(item, size = item.size) {
    const family = (FONTS[item.font] || FONTS.clean).css;
    return `${item.italic ? 'italic ' : ''}${item.bold ? 700 : 400} ${size}px ${family}`;
  }

  /**
   * Where every letter of a text item goes, in the item's own units, and the size of its box.
   * `measure(text, font)` gives the width of a run of text (a canvas's measureText). Straight
   * text keeps its lines and alignment; curved text is one line set along an arc, bent up for a
   * curve above 0 and down below it. Letter spacing is in hundredths of the font size.
   */
  function layoutText(item, measure) {
    const size = item.size;
    const font = fontOf(item);
    const gap = (item.spacing || 0) / 100 * size;
    const pad = (item.outline || 0) + size * 0.08;
    const labelX = item.label && !item.curve ? size * 0.5 : 0;
    const labelY = item.label && !item.curve ? size * 0.14 : 0;
    const theta = (item.curve || 0) / 100 * Math.PI;

    if (Math.abs(theta) < 1e-3) {
      // Whole lines, measured and drawn as one run each, so joined letters (Hindi) stay joined.
      const lines = String(item.text || ' ').split('\n').map((line) => line || ' ');
      const lineHeight = size * 1.25;
      const widths = lines.map((line) => measure(line, font) + gap * Math.max(0, graphemes(line).length - 1));
      const textWidth = Math.max(...widths);
      const width = textWidth + 2 * (pad + labelX);
      const height = lines.length * lineHeight + 2 * (pad + labelY);
      const glyphs = lines.map((line, row) => {
        const left = pad + labelX + (item.align === 'left' ? 0 : item.align === 'right' ? textWidth - widths[row] : (textWidth - widths[row]) / 2);
        return { ch: line, left, x: left + widths[row] / 2, y: pad + labelY + row * lineHeight + lineHeight / 2, angle: 0, width: widths[row] };
      });
      return { width, height, glyphs, label: item.label ? { x: pad, y: pad, width: width - 2 * pad, height: height - 2 * pad } : null };
    }

    // Curved: one line, each letter cluster (a Hindi syllable stays whole) set along the arc.
    const chars = graphemes(String(item.text || ' ').replace(/\n/g, ' '));
    const length = chars.reduce((sum, ch) => sum + measure(ch, font), 0) + gap * Math.max(0, chars.length - 1);
    const radius = length / Math.abs(theta);
    const half = Math.abs(theta) / 2;
    const chord = 2 * radius * Math.sin(Math.min(half, Math.PI / 2));
    const sagitta = radius * (1 - Math.cos(half));
    const width = Math.max(chord, half > Math.PI / 2 ? 2 * radius : 0) + size + 2 * pad;
    const height = size * 1.25 + sagitta + 2 * pad;
    const up = theta > 0;
    const cx = width / 2;
    // Letters stand on the arc, their middle a little above it.
    const cy = up ? pad + size * 0.6 + radius : pad + size * 0.6 + sagitta - radius;
    const glyphs = [];
    let along = 0;
    for (const ch of chars) {
      const w = measure(ch, font);
      const phi = (along + w / 2) / radius - half;
      glyphs.push(up
        ? { ch, x: cx + radius * Math.sin(phi), y: cy - radius * Math.cos(phi), angle: phi }
        : { ch, x: cx + radius * Math.sin(phi), y: cy + radius * Math.cos(phi), angle: -phi });
      along += w + gap;
    }
    return { width, height, glyphs, label: null };
  }

  // ---------------------------------------------------------------------------
  // Backgrounds
  // ---------------------------------------------------------------------------

  /** Plain colours offered first; any other colour can be picked too. */
  const BACKGROUND_COLOURS = Object.freeze(['#ffffff', '#f6f1e7', '#fde2e4', '#e3f2fd', '#e8f5e9', '#fff8e1', '#ede7f6', '#172033', '#0f1f3d', '#4a0d1c', '#1b5e20', '#000000']);

  /**
   * Gradients: `angle` in degrees for a linear one (0 runs left to right, 90 top to bottom), or
   * `radial` from the middle; `stops` are [place 0 to 1, colour].
   */
  const GRADIENTS = Object.freeze({
    sunset: Object.freeze({ label: 'Sunset', angle: 70, stops: [[0, '#ff9a76'], [0.55, '#ffcf8a'], [1, '#fff1c1']] }),
    ocean: Object.freeze({ label: 'Ocean', angle: 70, stops: [[0, '#1e3c72'], [0.6, '#2a7ab0'], [1, '#8fd3f4']] }),
    mint: Object.freeze({ label: 'Mint', angle: 70, stops: [[0, '#d4fc79'], [1, '#96e6a1']] }),
    navy: Object.freeze({ label: 'Navy', angle: 70, stops: [[0, '#0f1f3d'], [1, '#1d3461']] }),
    festive: Object.freeze({ label: 'Festive', radial: true, stops: [[0, '#8a2433'], [0.7, '#4a0d1c'], [1, '#2a0612']] }),
    tricolour: Object.freeze({ label: 'Tricolour', angle: 90, stops: [[0, '#ffcf99'], [0.38, '#ffffff'], [0.62, '#ffffff'], [1, '#bfe6b8']] }),
    peach: Object.freeze({ label: 'Peach', angle: 70, stops: [[0, '#ffecd2'], [1, '#fcb69f']] }),
    lavender: Object.freeze({ label: 'Lavender', angle: 70, stops: [[0, '#e0c3fc'], [1, '#8ec5fc']] })
  });

  /** Patterns, drawn by collage-draw.js in page units, so they look the same at any size. */
  const PATTERNS = Object.freeze({
    gingham: Object.freeze({ label: 'Gingham' }),
    dots: Object.freeze({ label: 'Polka dots' }),
    confetti: Object.freeze({ label: 'Confetti' }),
    stripes: Object.freeze({ label: 'Stripes' }),
    gridpaper: Object.freeze({ label: 'Grid paper' }),
    stars: Object.freeze({ label: 'Night stars' })
  });

  /** Textures, drawn too: no picture files to ship, and sharp at print quality. */
  const TEXTURES = Object.freeze({
    paper: Object.freeze({ label: 'Warm paper' }),
    kraft: Object.freeze({ label: 'Kraft' }),
    linen: Object.freeze({ label: 'Linen' }),
    wood: Object.freeze({ label: 'Wood' }),
    chalk: Object.freeze({ label: 'Chalkboard' }),
    cork: Object.freeze({ label: 'Cork board' })
  });

  const WHITE_BACKGROUND = Object.freeze({ type: 'color', value: '#ffffff' });

  /**
   * A background read back from a draft: a known kind with a known name, a #rrggbb colour, or
   * one of the draft's own photos; anything else is plain white.
   */
  function readBackground(raw, photoIds) {
    if (!raw || typeof raw !== 'object') return { ...WHITE_BACKGROUND };
    if (raw.type === 'color' && /^#[0-9a-f]{6}$/i.test(raw.value)) return { type: 'color', value: raw.value };
    if (raw.type === 'gradient' && GRADIENTS[raw.key]) return { type: 'gradient', key: raw.key };
    if (raw.type === 'pattern' && PATTERNS[raw.key]) return { type: 'pattern', key: raw.key };
    if (raw.type === 'texture' && TEXTURES[raw.key]) return { type: 'texture', key: raw.key };
    if (raw.type === 'photo' && (photoIds || []).includes(raw.photo)) return { type: 'photo', photo: raw.photo };
    return { ...WHITE_BACKGROUND };
  }

  /** Whether two backgrounds are the same choice. */
  function sameBackground(a, b) {
    return Boolean(a && b) && a.type === b.type && (a.value || a.key || a.photo) === (b.value || b.key || b.photo);
  }

  /**
   * A small number from 0 to 1 that is always the same for the same `n`, for speckles, grain
   * and confetti, so a pattern never changes between the page and the saved picture.
   */
  function steadyRandom(n) {
    const x = Math.sin(n * 12.9898 + 78.233) * 43758.5453;
    return x - Math.floor(x);
  }

  // ---------------------------------------------------------------------------
  // Layouts
  // ---------------------------------------------------------------------------

  /** Grids on 12 × 12 units: [x, y, width, height] for each frame. */
  const LAYOUTS = Object.freeze({
    t2: Object.freeze({ label: '2 side by side', cells: [[0, 0, 6, 12], [6, 0, 6, 12]] }),
    t3: Object.freeze({ label: '1 big, 2 small', cells: [[0, 0, 7, 12], [7, 0, 5, 6], [7, 6, 5, 6]] }),
    t3c: Object.freeze({ label: '3 columns', cells: [[0, 0, 4, 12], [4, 0, 4, 12], [8, 0, 4, 12]] }),
    t4: Object.freeze({ label: '4 grid', cells: [[0, 0, 6, 6], [6, 0, 6, 6], [0, 6, 6, 6], [6, 6, 6, 6]] }),
    t4s: Object.freeze({ label: '4 in a strip', cells: [[0, 0, 3, 12], [3, 0, 3, 12], [6, 0, 3, 12], [9, 0, 3, 12]] }),
    t5: Object.freeze({ label: '2 over 3', cells: [[0, 0, 6, 7], [6, 0, 6, 7], [0, 7, 4, 5], [4, 7, 4, 5], [8, 7, 4, 5]] }),
    t6: Object.freeze({ label: '6 grid', cells: [[0, 0, 4, 6], [4, 0, 4, 6], [8, 0, 4, 6], [0, 6, 4, 6], [4, 6, 4, 6], [8, 6, 4, 6]] }),
    t7: Object.freeze({ label: 'Mosaic of 7', cells: [[0, 0, 5, 7], [5, 0, 4, 4], [9, 0, 3, 4], [5, 4, 7, 3], [0, 7, 4, 5], [4, 7, 4, 5], [8, 7, 4, 5]] }),
    t8: Object.freeze({ label: '8 grid', cells: [[0, 0, 3, 6], [3, 0, 3, 6], [6, 0, 3, 6], [9, 0, 3, 6], [0, 6, 3, 6], [3, 6, 3, 6], [6, 6, 3, 6], [9, 6, 3, 6]] }),
    t9: Object.freeze({ label: '9 grid', cells: [0, 1, 2].flatMap((r) => [0, 1, 2].map((c) => [c * 4, r * 4, 4, 4])) }),
    t12: Object.freeze({ label: '12 grid', cells: [0, 1, 2].flatMap((r) => [0, 1, 2, 3].map((c) => [c * 3, r * 4, 3, 4])) })
  });
  const LAYOUT_LIMITS = Object.freeze({ gapMax: 60, radiusMax: 60, defaultGap: 12 });

  /**
   * The frames of a layout on a page: centre, width and shape of each. `gap` is the space
   * between frames and around the page's edge, in page units.
   */
  function layoutFrames(sizeKey, key, gap) {
    const layout = LAYOUTS[key];
    if (!layout) return [];
    const g = Math.min(LAYOUT_LIMITS.gapMax, Math.max(0, Number(gap) || 0));
    const height = pageHeight(sizeKey);
    const areaW = PAGE_WIDTH - g;
    const areaH = height - g;
    return layout.cells.map(([cx, cy, cw, ch]) => {
      const x = g + cx / 12 * areaW;
      const y = g + cy / 12 * areaH;
      const w = cw / 12 * areaW - g;
      const h = ch / 12 * areaH - g;
      return { x: x + w / 2, y: y + h / 2, width: w, aspect: w / h };
    });
  }

  // ---------------------------------------------------------------------------
  // Lock
  // ---------------------------------------------------------------------------

  /**
   * What a lock stops, by action name: anything that moves, turns, reshapes, refills or removes
   * the item. Order, Duplicate (the copy is never locked), Hide and Unlock stay open.
   */
  const LOCK_BLOCKS = Object.freeze(['delete', 'adjust', 'replace', 'fill-slot', 'rotate-left', 'rotate-right', 'straighten', 'fit-photo']);

  /** True when the item is locked and the action is one a lock stops. */
  function lockBlocks(item, action) {
    return Boolean(item?.locked) && LOCK_BLOCKS.includes(action);
  }

  /** Locked photos and empty frames: the items a change of layout has to ask about. */
  function lockedFrames(collage) {
    return collage.items.filter((item) => (item.kind === 'photo' || item.kind === 'slot') && item.locked);
  }

  /** How far past the page edge an item may stay (a tilted frame's corner, say), in page units. */
  const PAGE_EDGE_SLACK = 30;

  /**
   * The item, moved the least it takes for its turned box to sit inside the page when more than a
   * little of it is off the page; otherwise as it is.
   */
  function keptOnPage(item, sizeKey) {
    const height = pageHeight(sizeKey);
    const box = turnedHalfBox(item.width, itemHeight(item), item.angle || 0);
    const inside = (value, half, limit) => {
      if (half * 2 >= limit) return limit / 2;
      const over = Math.max(half - value, value + half - limit);
      return over > PAGE_EDGE_SLACK ? Math.min(limit - half, Math.max(half, value)) : value;
    };
    const x = inside(item.x, box.halfWidth, PAGE_WIDTH);
    const y = inside(item.y, box.halfHeight, height);
    return x === item.x && y === item.y ? item : { ...item, x, y };
  }

  /** How much of a layout frame an item covers, as a share of the frame (0 to 1). */
  function coveredShare(frame, item) {
    const box = turnedHalfBox(item.width, itemHeight(item), item.angle || 0);
    const fw = frame.width / 2;
    const fh = frame.width / frame.aspect / 2;
    const w = Math.min(frame.x + fw, item.x + box.halfWidth) - Math.max(frame.x - fw, item.x - box.halfWidth);
    const h = Math.min(frame.y + fh, item.y + box.halfHeight) - Math.max(frame.y - fh, item.y - box.halfHeight);
    return w > 0 && h > 0 ? (w * h) / (4 * fw * fh) : 0;
  }

  /** A frame a locked item covers more of than this is left out of the layout. */
  const LOCK_COVER_LIMIT = 0.05;

  /**
   * The collage arranged in a layout. Its photos fill the frames in their current order, then
   * `spare` photos (ids from the photo store, with their shape) fill what is left, then empty
   * frames wait for photos. Photos beyond the frames leave the page (they stay in Photos) and are
   * returned as `removed`. Every frame is straight, uncut and given the layout's rounded corners.
   *
   * Locked photos and frames: with `locked: 'join'` they go into the layout like the others and
   * stay locked. Otherwise (`'keep'`, the default) each stays where it is, moved only as far as
   * it takes to be inside the page, and the layout leaves out every frame it covers, so nothing
   * overlaps without the user choosing it.
   */
  function applyLayout(collage, key, gap, radius, spare, newId, { locked = 'keep' } = {}) {
    const all = layoutFrames(collage.size, key, gap);
    if (!all.length) return { collage, removed: [] };
    const r = Math.min(LAYOUT_LIMITS.radiusMax, Math.max(0, Number(radius) || 0));
    const isFrame = (item) => item.kind === 'photo' || item.kind === 'slot';
    const join = locked === 'join';
    const stays = (item) => !isFrame(item) || (!join && item.locked);
    // Text, stickers and kept frames stay in their order, so nothing above a kept photo goes under it.
    const others = collage.items.filter(stays).map((item) => (isFrame(item) ? keptOnPage(item, collage.size) : item));
    const kept = others.filter(isFrame);
    // Each frame keeps its own cell, so a later change of space puts it back in its own place.
    const frames = all.map((frame, cell) => ({ ...frame, cell }))
      .filter((frame) => !kept.some((item) => coveredShare(frame, item) > LOCK_COVER_LIMIT));
    const photos = collage.items.filter((item) => item.kind === 'photo' && (join || !item.locked));
    // Locked empty frames put into the layout keep their id and lock, after the photos.
    const lockedSlots = join ? collage.items.filter((item) => item.kind === 'slot' && item.locked) : [];
    const sources = photos.map((item) => item.photo);
    for (const id of spare || []) if (sources.length < frames.length && !sources.includes(id)) sources.push(id);
    const placed = frames.map(({ cell, ...place }, index) => {
      const existing = photos[index];
      if (existing) return { ...existing, ...place, cell, angle: 0, radius: r, crop: { ...FULL_CROP } };
      if (index < sources.length) return { ...photoItem(newId(), sources[index], place.aspect, place), cell, radius: r };
      const slot = lockedSlots.shift();
      if (slot) return { ...slot, ...place, cell, angle: 0, radius: r };
      return { ...slotItem(newId(), place), cell, radius: r };
    });
    // Locked empty frames with no frame left for them stay where they are.
    others.push(...lockedSlots.map((item) => keptOnPage(item, collage.size)));
    const removed = photos.slice(frames.length).map((item) => item.id);
    return {
      collage: { ...collage, layout: { key, gap: Math.min(LAYOUT_LIMITS.gapMax, Math.max(0, Number(gap) || 0)), radius: r }, items: [...placed, ...others] },
      removed
    };
  }

  /**
   * The current layout laid out again, after a change of space or page size: every frame goes
   * back to its own place in the grid (`cell`), so a photo stays in the frame it was put in and
   * an empty frame stays empty, whatever order the frames are drawn in. Frames are made straight;
   * a photo's zoom and the part it shows are kept. Items that are not frames, and locked
   * frames, are left alone.
   */
  function reflowLayout(collage, gap) {
    if (!collage.layout) return collage;
    const g = Math.min(LAYOUT_LIMITS.gapMax, Math.max(0, Number(gap) || 0));
    const frames = layoutFrames(collage.size, collage.layout.key, g);
    return {
      ...collage,
      layout: { ...collage.layout, gap: g },
      items: collage.items.map((item) => {
        const frame = Number.isInteger(item.cell) ? frames[item.cell] : null;
        return frame && (item.kind === 'photo' || item.kind === 'slot') && !item.locked ? { ...item, ...frame, angle: 0 } : item;
      })
    };
  }

  /**
   * "Make it casual": every photo and empty frame tilted a little, left or right in turn,
   * nudged off the grid and made a little smaller so the tilted corners show. The same collage
   * always gives the same result. Locked items stay as they are.
   */
  function makeCasual(collage) {
    let n = 0;
    return {
      ...collage,
      layout: null,
      items: collage.items.map((item) => {
        if ((item.kind !== 'photo' && item.kind !== 'slot') || item.locked) return item;
        const i = n++;
        const angle = (i % 2 ? 1 : -1) * (3 + (i * 5) % 6);
        const moved = { ...item, angle, width: item.width * 0.9, x: item.x + ((i * 29) % 21) - 10, y: item.y + ((i * 17) % 17) - 8 };
        if (item.kind !== 'photo') return moved;
        // A frame or shape the user chose is kept; the photo is only tilted and moved.
        if ((item.frame && item.frame !== 'none') || (item.shape && item.shape !== 'rect')) return { ...moved, shadow: true };
        // Polaroids, with every third one taped, a shadow and the photo's own shape kept.
        const framed = { ...moved, shape: 'rect', frame: i % 3 === 2 ? 'tape' : 'polaroid', shadow: true };
        return { ...framed, aspect: outerAspectFor(framed, innerAspect(moved)) };
      })
    };
  }

  /** Every photo and empty frame turned straight, except locked ones. */
  function straightenAll(collage) {
    return { ...collage, items: collage.items.map((item) => ((item.kind === 'photo' || item.kind === 'slot') && !item.locked ? { ...item, angle: 0 } : item)) };
  }

  /**
   * The photos moved one frame along: each frame shows the next photo, the last the first.
   * A locked photo keeps its place and is passed over.
   */
  function shufflePhotos(collage) {
    const moves = (item) => item.kind === 'photo' && !item.locked;
    const photos = collage.items.filter(moves);
    if (photos.length < 2) return collage;
    const sources = photos.map((item) => item.photo);
    sources.push(sources.shift());
    let i = 0;
    return {
      ...collage,
      items: collage.items.map((item) => (moves(item) ? { ...item, photo: sources[i++], crop: { ...FULL_CROP } } : item))
    };
  }

  // ---------------------------------------------------------------------------
  // Geometry
  // ---------------------------------------------------------------------------

  /** An item's height in page units. */
  function itemHeight(item) {
    return item.width / item.aspect;
  }

  /** Half the width and height of the box around an item once it is turned. */
  function turnedHalfBox(width, height, angle) {
    const rad = angle * Math.PI / 180;
    const c = Math.abs(Math.cos(rad));
    const s = Math.abs(Math.sin(rad));
    return { halfWidth: (width * c + height * s) / 2, halfHeight: (width * s + height * c) / 2 };
  }

  /** An angle as a number of degrees from -180 (not included) to 180. */
  function normaliseAngle(angle) {
    let a = ((Number(angle) || 0) % 360 + 360) % 360;
    if (a > 180) a -= 360;
    return Math.abs(a) < 1e-9 ? 0 : a;
  }

  /**
   * The angle once snapped: to a multiple of 15° while Shift is held, otherwise to straight or
   * a multiple of 45° when it is within 3° of one.
   */
  function snapAngle(angle, withShift) {
    const a = normaliseAngle(angle);
    if (withShift) return normaliseAngle(Math.round(a / ANGLE_SNAP.stepWithShift) * ANGLE_SNAP.stepWithShift);
    const nearest = Math.round(a / ANGLE_SNAP.every) * ANGLE_SNAP.every;
    return Math.abs(nearest - a) <= ANGLE_SNAP.within ? normaliseAngle(nearest) : a;
  }

  /** A width kept between the smallest and the largest an item may be. */
  function clampWidth(width) {
    return Math.min(ITEM_LIMITS.maxWidth, Math.max(ITEM_LIMITS.minWidth, Number(width) || ITEM_LIMITS.minWidth));
  }

  /**
   * Where a moved item snaps. Its centre snaps to the page's centre lines and to the centres of
   * the other items; its turned box snaps to the page's edges. `threshold` is in page units.
   * Returns the new centre and the guides to draw: { axis: 'x' | 'y', at }.
   */
  function snapMove({ x, y, halfWidth, halfHeight }, sizeKey, others, threshold) {
    const height = pageHeight(sizeKey);
    const xs = [
      { value: PAGE_WIDTH / 2, guide: PAGE_WIDTH / 2 },
      { value: halfWidth, guide: 0 },
      { value: PAGE_WIDTH - halfWidth, guide: PAGE_WIDTH }
    ];
    const ys = [
      { value: height / 2, guide: height / 2 },
      { value: halfHeight, guide: 0 },
      { value: height - halfHeight, guide: height }
    ];
    for (const other of others || []) {
      xs.push({ value: other.x, guide: other.x });
      ys.push({ value: other.y, guide: other.y });
    }
    const pick = (list, v) => {
      let best = null;
      for (const c of list) {
        const d = Math.abs(c.value - v);
        if (d <= threshold && (!best || d < best.d)) best = { ...c, d };
      }
      return best;
    };
    const sx = pick(xs, x);
    const sy = pick(ys, y);
    const guides = [];
    if (sx) guides.push({ axis: 'x', at: sx.guide });
    if (sy) guides.push({ axis: 'y', at: sy.guide });
    return { x: sx ? sx.value : x, y: sy ? sy.value : y, guides };
  }

  /**
   * The collage on a new page size. Items keep their place across the page and their size, so
   * a landscape collage turned upright stays in order; nothing is cut off the page. A locked
   * item keeps its exact place.
   */
  function changeSize(collage, sizeKey) {
    if (!SIZES[sizeKey] || sizeKey === collage.size) return collage;
    const from = pageHeight(collage.size);
    const to = pageHeight(sizeKey);
    return {
      ...collage,
      size: sizeKey,
      items: collage.items.map((item) => (item.locked ? item : { ...item, y: item.y * to / from }))
    };
  }

  // ---------------------------------------------------------------------------
  // Arranging
  // ---------------------------------------------------------------------------

  /**
   * The items with one moved in the drawing order: 'forward' and 'backward' one step,
   * 'front' and 'back' all the way. The last item is drawn in front.
   */
  function reorder(items, id, how) {
    const index = items.findIndex((item) => item.id === id);
    if (index < 0) return items;
    const list = items.slice();
    const [item] = list.splice(index, 1);
    let to = index;
    if (how === 'forward') to = Math.min(list.length, index + 1);
    else if (how === 'backward') to = Math.max(0, index - 1);
    else if (how === 'front') to = list.length;
    else if (how === 'back') to = 0;
    list.splice(to, 0, item);
    return list;
  }

  /** A copy of an item, a little down and to the right, with a new id; never hidden or locked. */
  function duplicateItem(item, id) {
    return { ...item, id, x: item.x + 24, y: item.y + 24, hidden: false, locked: false };
  }

  /**
   * The empty frame whose shape is closest to a photo's (by how much one is wider than the
   * other), so a wide photo goes into a wide frame; the first such frame on a tie, or null.
   */
  function bestSlotFor(items, aspect) {
    let best = null;
    let bestGap = Infinity;
    for (const item of items || []) {
      if (item.kind !== 'slot' || item.hidden || item.locked || !(item.aspect > 0) || !(aspect > 0)) continue;
      const gap = Math.abs(Math.log(item.aspect / aspect));
      if (gap < bestGap - 1e-9) { best = item; bestGap = gap; }
    }
    return best;
  }

  const MONTHS = Object.freeze(['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']);

  /**
   * A name for a collage nobody named: the first line of its first text, or the day and time
   * it was started ("Collage, 15 Nov 14:05"), so drafts can be told apart.
   */
  function autoTitle(items, startedAt) {
    const placeholders = new Set(Object.values(TEXT_PLACEHOLDERS));
    const text = (items || []).find((item) => item.kind === 'text' && String(item.text || '').trim() && !placeholders.has(String(item.text).trim()));
    if (text) {
      const line = String(text.text).split('\n').map((part) => part.trim()).find(Boolean) || '';
      if (line) return line.length > 40 ? `${line.slice(0, 39)}…` : line;
    }
    const when = new Date(Number.isFinite(startedAt) ? startedAt : Date.now());
    const time = `${String(when.getHours()).padStart(2, '0')}:${String(when.getMinutes()).padStart(2, '0')}`;
    return `Collage, ${when.getDate()} ${MONTHS[when.getMonth()]} ${time}`;
  }

  /**
   * What a slider is at, with its unit: sizes, space and corners as a share of the page's width
   * (the page is PAGE_WIDTH units across whatever its paper), turns in degrees, and the rest in
   * percent or times.
   */
  function sliderText(prop, value) {
    const number = Number(value) || 0;
    if (prop === 'angle') return `${Math.round(number)}°`;
    if (prop === 'opacity' || prop === 'bright' || prop === 'contrast') return `${Math.round(number)}%`;
    if (prop === 'zoom') return `${number.toFixed(1)}×`;
    if (prop === 'warmth') return number > 0 ? `+${Math.round(number)}` : String(Math.round(number));
    if (prop === 'width') return `${Math.round(number / PAGE_WIDTH * 100)}% of the page`;
    if (prop === 'radius' || prop === 'gap') {
      const share = number / PAGE_WIDTH * 100;
      return number ? `${share < 10 ? share.toFixed(1) : Math.round(share)}% of the page` : 'None';
    }
    return String(Math.round(number));
  }

  /** Ready-made words that only show where to type; they never name a collage. */
  const TEXT_PLACEHOLDERS = Object.freeze({ heading: 'Your heading', subheading: 'A subheading', line: 'A line of text', hindi: 'यहाँ लिखें' });

  /** Where "Line up on the page" can put an item. */
  const ALIGN_PLACES = Object.freeze(['left', 'centre', 'right', 'top', 'middle', 'bottom']);

  /**
   * The item's centre lined up with the page: its turned box against an edge (left, right, top,
   * bottom) or on the page's centre line (centre across, middle down). Only one direction
   * changes.
   */
  function alignItem(item, sizeKey, place) {
    const box = turnedHalfBox(item.width, itemHeight(item), item.angle);
    const height = pageHeight(sizeKey);
    if (place === 'left') return { x: box.halfWidth, y: item.y };
    if (place === 'centre') return { x: PAGE_WIDTH / 2, y: item.y };
    if (place === 'right') return { x: PAGE_WIDTH - box.halfWidth, y: item.y };
    if (place === 'top') return { x: item.x, y: box.halfHeight };
    if (place === 'middle') return { x: item.x, y: height / 2 };
    if (place === 'bottom') return { x: item.x, y: height - box.halfHeight };
    return { x: item.x, y: item.y };
  }

  /**
   * A short name for an item in the Layers list: the words of a text, the kind of anything
   * else. Photos and stickers are named by the caller, which knows their files.
   */
  function layerName(item) {
    if (item.kind === 'text') {
      const first = String(item.text || '').split('\n')[0].trim();
      return first ? (first.length > 28 ? `${first.slice(0, 27)}…` : first) : 'Text';
    }
    if (item.kind === 'slot') return 'Empty frame';
    if (item.kind === 'sticker') return 'Sticker';
    return 'Photo';
  }

  // ---------------------------------------------------------------------------
  // Saving
  // ---------------------------------------------------------------------------

  /** The long side of a picture saved for screens. */
  const SCREEN_SIDE = 1920;
  /** Points per inch in a PDF, and the dots per inch a print-quality picture has. */
  const PDF_POINTS_PER_INCH = 72;
  const PRINT_DPI = 300;

  /**
   * The picture's pixels: the size's print pixels (300 dots per inch on paper) or, for
   * 'screen', 1,920 pixels on the long side. `dpi` is the dots per inch on paper, or null.
   */
  function savePixels(sizeKey, quality) {
    const size = sizeOf(sizeKey);
    let width = size.width;
    let height = size.height;
    if (quality === 'screen') {
      const s = SCREEN_SIDE / Math.max(width, height);
      width = Math.round(width * s);
      height = Math.round(height * s);
    }
    return { width, height, dpi: paperDpi(sizeKey, width) };
  }

  /** Dots per inch of a picture `width` pixels wide printed on the size's paper; null if not paper. */
  function paperDpi(sizeKey, width) {
    const size = sizeOf(sizeKey);
    return size.mm ? Math.round(width / (size.mm[0] / 25.4)) : null;
  }

  /**
   * A picture made smaller, keeping its shape, so it has at most `maxArea` pixels. Some browsers
   * cannot draw a canvas past a size (Safari stops near 16.7 million pixels): the picture is then
   * saved a little smaller rather than not at all.
   */
  function fitArea(width, height, maxArea) {
    if (!(maxArea > 0) || width * height <= maxArea) return { width, height, scaled: false };
    const s = Math.sqrt(maxArea / (width * height));
    return { width: Math.max(1, Math.floor(width * s)), height: Math.max(1, Math.floor(height * s)), scaled: true };
  }

  /**
   * The PDF page in points. Paper sizes are their true size; the other sizes are printed at
   * 300 dots per inch, so a slide is 12.8 × 7.2 inches.
   */
  function pdfPage(sizeKey) {
    const size = sizeOf(sizeKey);
    if (size.mm) {
      return { width: size.mm[0] / 25.4 * PDF_POINTS_PER_INCH, height: size.mm[1] / 25.4 * PDF_POINTS_PER_INCH };
    }
    return { width: size.width / PRINT_DPI * PDF_POINTS_PER_INCH, height: size.height / PRINT_DPI * PDF_POINTS_PER_INCH };
  }

  /**
   * How large to read a photo's original for a frame `framePixels` wide in the saved picture:
   * enough for the part the frame shows to have that many pixels, never more than the original
   * has, so a small frame never makes the browser hold a huge photo.
   */
  function decodeSize(photo, frameAspect, crop, framePixels) {
    const region = cropRegion(photo.width / photo.height, frameAspect, crop);
    const width = Math.min(photo.width, Math.max(1, Math.ceil(framePixels / region.width)));
    return { width, height: Math.max(1, Math.round(width * photo.height / photo.width)) };
  }

  /** The items a saved picture shows: everything except empty frames and hidden items, in order. */
  function itemsToSave(collage) {
    return collage.items.filter((item) => item.kind !== 'slot' && !item.hidden);
  }

  /**
   * A file name from the collage's name: no characters that Windows or a phone refuses, no
   * leading or trailing dots or spaces, at most 80 characters; "Collage" when nothing is left.
   */
  function fileStem(name) {
    const clean = String(name ?? '')
      // eslint-disable-next-line no-control-regex
      .replace(/[\u0000-\u001f<>:"/\\|?*]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim()
      .replace(/^[.\s]+|[.\s]+$/g, '')
      .slice(0, 80)
      .trim();
    return /^(con|prn|aux|nul|com\d|lpt\d)$/i.test(clean) || !clean ? 'Collage' : clean;
  }

  // ---------------------------------------------------------------------------
  // Drafts (kept in this browser; docs/ux/collage-studio.md, owner decision 15 Nov 2026)
  // ---------------------------------------------------------------------------

  /** At most this many drafts; a new one past it asks before the oldest is deleted. */
  const MAX_DRAFTS = 30;
  /** A deleted draft can be brought back for this long before it is removed for good. */
  const DRAFT_UNDO_MS = 10000;

  const isNumber = (value) => typeof value === 'number' && Number.isFinite(value);

  /**
   * A draft's collage read back from storage, checked item by item so a damaged or hand-edited
   * draft can never break the editor: unknown kinds, items without a usable size or place, and
   * photos the draft does not hold are dropped. An older version is brought up to this one.
   * Returns null when nothing usable is left or the draft was made by a newer PRISM.
   */
  // Ids the editor makes: a letter, then letters and digits.
  const ITEM_ID = /^[A-Za-z][A-Za-z0-9_-]{0,40}$/;
  // How much wider than tall (or the reverse) an item may be: well past any real frame or text.
  const ASPECT_LIMITS = Object.freeze([0.02, 50]);

  function readDraftModel(raw, photoIds) {
    if (!raw || typeof raw !== 'object' || !Array.isArray(raw.items)) return null;
    const version = isNumber(raw.version) ? raw.version : 1;
    if (version > MODEL_VERSION) return null;
    const known = new Set(photoIds || []);
    const items = [];
    const seen = new Set();
    for (const item of raw.items) {
      // An id is written into the page, so only the plain ids the editor makes are kept, once.
      if (!item || typeof item !== 'object' || typeof item.id !== 'string' || !ITEM_ID.test(item.id) || seen.has(item.id)) continue;
      if (!['photo', 'slot', 'text', 'sticker'].includes(item.kind)) continue;
      if (item.kind === 'sticker' && !STICKER_ID.test(item.sticker)) continue;
      if (!isNumber(item.x) || !isNumber(item.y) || !isNumber(item.width) || !isNumber(item.aspect) || !(item.aspect > 0)) continue;
      if (item.kind === 'photo' && !known.has(item.photo)) continue;
      seen.add(item.id);
      const clean = {
        ...item,
        width: clampWidth(item.width),
        aspect: Math.min(ASPECT_LIMITS[1], Math.max(ASPECT_LIMITS[0], item.aspect)),
        angle: normaliseAngle(isNumber(item.angle) ? item.angle : 0),
        opacity: Math.min(100, Math.max(10, isNumber(item.opacity) ? item.opacity : 100)),
        radius: Math.min(LAYOUT_LIMITS.radiusMax, Math.max(0, isNumber(item.radius) ? item.radius : 0)),
        hidden: item.hidden === true,
        locked: item.locked === true
      };
      // A layout cell is a small whole number, or nothing.
      if (!(Number.isInteger(item.cell) && item.cell >= 0 && item.cell < 64)) delete clean.cell;
      if (item.kind === 'sticker') {
        // Stickers have their own limits (down to 20), so the saved width is clamped, not the
        // item width already raised to the general minimum.
        clean.width = Math.min(STICKER_LIMITS.width[1], Math.max(STICKER_LIMITS.width[0], item.width));
        clean.colour = HEX_COLOUR.test(item.colour) ? item.colour : '';
        clean.flip = item.flip === true;
        clean.shadow = item.shadow === true;
        delete clean.crop;
        delete clean.radius;
      }
      if (item.kind === 'text') {
        const range = (value, [low, high], fallback) => Math.min(high, Math.max(low, isNumber(value) ? value : fallback));
        clean.text = typeof item.text === 'string' ? item.text.slice(0, TEXT_LIMITS.maxLength) : '';
        clean.font = FONTS[item.font] ? item.font : 'clean';
        clean.size = range(item.size, TEXT_LIMITS.size, 40);
        clean.spacing = range(item.spacing, TEXT_LIMITS.spacing, 0);
        clean.outline = range(item.outline, TEXT_LIMITS.outline, 0);
        clean.curve = range(item.curve, TEXT_LIMITS.curve, 0);
        clean.color = HEX_COLOUR.test(item.color) ? item.color : '#172033';
        clean.outlineColor = HEX_COLOUR.test(item.outlineColor) ? item.outlineColor : '#ffffff';
        clean.label = HEX_COLOUR.test(item.label) ? item.label : '';
        clean.align = ['left', 'center', 'right'].includes(item.align) ? item.align : 'center';
        for (const flag of ['bold', 'italic', 'shadow', 'glow']) clean[flag] = item[flag] === true;
        delete clean.crop;
        delete clean.radius;
      }
      if (item.kind === 'photo') {
        const within = (value, [low, high], fallback) => Math.min(high, Math.max(low, isNumber(value) ? value : fallback));
        clean.frame = FRAMES[item.frame] ? item.frame : 'none';
        clean.shape = SHAPES[item.shape] ? item.shape : 'rect';
        clean.look = LOOKS[item.look] ? item.look : 'none';
        clean.caption = typeof item.caption === 'string' ? item.caption.slice(0, CAPTION_MAX) : '';
        clean.shadow = item.shadow === true;
        clean.flip = item.flip === true;
        clean.bright = within(item.bright, ADJUST_LIMITS.bright, 100);
        clean.contrast = within(item.contrast, ADJUST_LIMITS.contrast, 100);
        clean.warmth = within(item.warmth, ADJUST_LIMITS.warmth, 0);
        const crop = item.crop || FULL_CROP;
        clean.crop = {
          zoom: Math.min(MAX_CROP_ZOOM, Math.max(1, isNumber(crop.zoom) ? crop.zoom : 1)),
          x: isNumber(crop.x) ? Math.min(1, Math.max(0, crop.x)) : 0.5,
          y: isNumber(crop.y) ? Math.min(1, Math.max(0, crop.y)) : 0.5
        };
      }
      items.push(clean);
    }
    const layout = raw.layout && LAYOUTS[raw.layout.key]
      ? {
        key: raw.layout.key,
        gap: Math.min(LAYOUT_LIMITS.gapMax, Math.max(0, isNumber(raw.layout.gap) ? raw.layout.gap : LAYOUT_LIMITS.defaultGap)),
        radius: Math.min(LAYOUT_LIMITS.radiusMax, Math.max(0, isNumber(raw.layout.radius) ? raw.layout.radius : 0))
      }
      : null;
    const background = readBackground(raw.background, photoIds);
    return { version: MODEL_VERSION, size: SIZES[raw.size] ? raw.size : DEFAULT_SIZE, background, layout, items };
  }

  /**
   * Which drafts are shown on the start screen and which are removed for good: a draft deleted
   * more than DRAFT_UNDO_MS ago is purged; one deleted since then is hidden (its Undo may still
   * be on screen). Shown drafts come newest first.
   */
  function sortDrafts(drafts, now) {
    const shown = [];
    const purge = [];
    for (const draft of drafts || []) {
      if (draft.deletedAt) {
        if (now - draft.deletedAt > DRAFT_UNDO_MS) purge.push(draft.id);
      } else {
        shown.push(draft);
      }
    }
    shown.sort((a, b) => (b.saved || 0) - (a.saved || 0));
    return { shown, purge };
  }

  /** The draft to delete to make room for a new one, or null when there is room. */
  function draftToMakeRoom(shown) {
    if ((shown || []).length < MAX_DRAFTS) return null;
    return shown.reduce((oldest, draft) => ((draft.saved || 0) < (oldest.saved || 0) ? draft : oldest));
  }

  /** "2 min ago", "3 h ago", "yesterday" or a date, for a draft's last save. */
  function savedAgo(saved, now) {
    const seconds = Math.max(0, (now - saved) / 1000);
    if (seconds < 50) return 'just now';
    if (seconds < 3600) return `${Math.round(seconds / 60)} min ago`;
    if (seconds < 86400) return `${Math.round(seconds / 3600)} h ago`;
    if (seconds < 172800) return 'yesterday';
    return new Date(saved).toLocaleDateString('en-IN', { day: 'numeric', month: 'short', year: 'numeric' });
  }

  // ---------------------------------------------------------------------------
  // Undo
  // ---------------------------------------------------------------------------

  /**
   * Undo and redo over snapshots of the collage. `record` keeps a snapshot unless it is the same
   * as the current one; a new snapshot after an undo drops what could have been redone.
   */
  function createHistory(first, limit = 100) {
    let list = [JSON.stringify(first)];
    let at = 0;
    return {
      record(collage) {
        const snapshot = JSON.stringify(collage);
        if (snapshot === list[at]) return false;
        list = list.slice(0, at + 1);
        list.push(snapshot);
        if (list.length > limit) list.shift();
        at = list.length - 1;
        return true;
      },
      undo() { if (at > 0) at -= 1; return JSON.parse(list[at]); },
      redo() { if (at < list.length - 1) at += 1; return JSON.parse(list[at]); },
      canUndo: () => at > 0,
      canRedo: () => at < list.length - 1,
      current: () => JSON.parse(list[at]),
      /** True when any step Undo or Redo can reach shows this photo (in a frame or as the background). */
      mentions: (photoId) => {
        const mark = `"photo":${JSON.stringify(String(photoId))}`;
        return list.some((snapshot) => snapshot.includes(mark));
      }
    };
  }

  /** The ids of the photos a collage shows: in its items, and as its background. */
  function photosInUse(collage) {
    const used = new Set();
    for (const item of collage?.items || []) if (item.kind === 'photo' && item.photo) used.add(item.photo);
    if (collage?.background?.type === 'photo' && collage.background.photo) used.add(collage.background.photo);
    return used;
  }

  return Object.freeze({
    MODEL_VERSION,
    PAGE_WIDTH,
    SIZES,
    DEFAULT_SIZE,
    PHOTO_LIMITS,
    ITEM_LIMITS,
    ANGLE_SNAP,
    SNAP_PIXELS,
    pageHeight,
    newCollage,
    sniffImageType,
    checkPhotos,
    refusedMessage,
    workingSize,
    placeNewPhoto,
    FULL_CROP,
    MAX_CROP_ZOOM,
    FONTS,
    TEXT_LIMITS,
    textItem,
    fontOf,
    graphemes,
    STICKER_LIMITS,
    stickerItem,
    layoutText,
    BACKGROUND_COLOURS,
    GRADIENTS,
    PATTERNS,
    TEXTURES,
    readBackground,
    sameBackground,
    steadyRandom,
    FRAMES,
    SHAPES,
    LOOKS,
    CAPTION_MAX,
    ADJUST_LIMITS,
    PLAIN_FINISH,
    isFramed,
    framePads,
    innerBox,
    innerAspect,
    outerAspectFor,
    limitFrameAspect,
    lookFilter,
    shapePath,
    tornEdge,
    photoItem,
    slotItem,
    fillSlot,
    cropRegion,
    clampCrop,
    panCrop,
    LAYOUTS,
    LAYOUT_LIMITS,
    layoutFrames,
    applyLayout,
    reflowLayout,
    LOCK_BLOCKS,
    lockBlocks,
    lockedFrames,
    keptOnPage,
    makeCasual,
    straightenAll,
    shufflePhotos,
    itemHeight,
    turnedHalfBox,
    normaliseAngle,
    snapAngle,
    clampWidth,
    snapMove,
    changeSize,
    reorder,
    duplicateItem,
    bestSlotFor,
    autoTitle,
    ALIGN_PLACES,
    alignItem,
    layerName,
    SCREEN_SIDE,
    PRINT_DPI,
    savePixels,
    paperDpi,
    fitArea,
    pdfPage,
    decodeSize,
    itemsToSave,
    fileStem,
    MAX_DRAFTS,
    DRAFT_UNDO_MS,
    readDraftModel,
    sortDrafts,
    photosInUse,
    sliderText,
    CAPTION_STRIP,
    hasCaptionStrip,
    captionStripHeight,
    emptyFrame,
    freeSpot,
    TEXT_PLACEHOLDERS,
    CANVAS_LIMITS,
    canvasPixelRatio,
    draftToMakeRoom,
    savedAgo,
    createHistory
  });
});
