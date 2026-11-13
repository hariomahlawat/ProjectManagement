// PRISM PDF Tools — core operations.
//
// Everything in this file runs in the user's browser (and in Node for tests). The PDF Tools
// module never uploads files to PRISM: users edit their own PDFs locally and download the
// result. pdf-lib is passed in explicitly so the operations stay testable without a DOM.
(function (root, factory) {
  'use strict';
  const zip = typeof module === 'object' && module.exports
    ? require('../tools-shared/zip-writer.js')
    : root.PrismZipWriter;
  const api = factory(zip);
  if (typeof module === 'object' && module.exports) {
    module.exports = api;
  } else {
    root.PrismPdfTools = api;
  }
})(typeof self !== 'undefined' ? self : this, function (zip) {
  'use strict';

  // ---------------------------------------------------------------------------
  // Errors
  // ---------------------------------------------------------------------------
  class PdfToolError extends Error {
    constructor(code, message) {
      super(message);
      this.name = 'PdfToolError';
      this.code = code;
    }
  }

  // ---------------------------------------------------------------------------
  // Page ranges
  // ---------------------------------------------------------------------------

  /**
   * Parses "1-3, 5, 8-" into zero-based page groups: [[0,1,2],[4],[7,...last]].
   * Page numbers are one-based for the user. "8-" runs to the last page.
   */
  function parsePageRanges(input, pageCount) {
    const text = String(input ?? '').trim();
    if (!text) {
      throw new PdfToolError('range-empty', 'Enter at least one page or range, for example 1-3, 5.');
    }
    if (!Number.isInteger(pageCount) || pageCount < 1) {
      throw new PdfToolError('range-no-pages', 'The document has no pages.');
    }

    const groups = [];
    // A range typed in Word or pasted from elsewhere often has an en dash ("1–3"), a figure
    // dash or a minus sign; read them all as a hyphen.
    for (const rawToken of text.replace(/[\u2010-\u2015\u2212]/g, '-').split(/[,;]/)) {
      const token = rawToken.trim();
      if (!token) {
        continue;
      }
      const match = /^(\d+)\s*(?:-\s*(\d*))?$/.exec(token);
      if (!match) {
        throw new PdfToolError('range-invalid', `"${token}" is not a page or range. Use forms like 4, 2-6 or 8-.`);
      }
      const start = Number.parseInt(match[1], 10);
      const isRange = token.includes('-');
      const end = isRange ? (match[2] ? Number.parseInt(match[2], 10) : pageCount) : start;

      if (start < 1 || end < 1) {
        throw new PdfToolError('range-invalid', 'Page numbers start at 1.');
      }
      if (start > pageCount || end > pageCount) {
        throw new PdfToolError('range-out-of-bounds', `"${token}" is outside the document, which has ${pageCount} page${pageCount === 1 ? '' : 's'}.`);
      }
      if (end < start) {
        throw new PdfToolError('range-invalid', `"${token}" runs backwards. Write the lower page first.`);
      }

      const group = [];
      for (let page = start; page <= end; page += 1) {
        group.push(page - 1);
      }
      groups.push(group);
    }

    if (groups.length === 0) {
      throw new PdfToolError('range-empty', 'Enter at least one page or range, for example 1-3, 5.');
    }
    return groups;
  }

  /** Flattens parsed groups into a sorted, de-duplicated set of zero-based page indexes. */
  function pageSetFromRanges(input, pageCount) {
    const indexes = new Set();
    for (const group of parsePageRanges(input, pageCount)) {
      group.forEach((index) => indexes.add(index));
    }
    return Array.from(indexes).sort((a, b) => a - b);
  }

  /** Splits [0..pageCount) into consecutive groups of the given size. */
  function chunkPages(pageCount, size) {
    const chunk = Number.parseInt(size, 10);
    if (!Number.isInteger(chunk) || chunk < 1) {
      throw new PdfToolError('range-invalid', 'Pages per file must be a whole number of at least 1.');
    }
    const groups = [];
    for (let start = 0; start < pageCount; start += chunk) {
      const group = [];
      for (let index = start; index < Math.min(start + chunk, pageCount); index += 1) {
        group.push(index);
      }
      groups.push(group);
    }
    return groups;
  }

  // ---------------------------------------------------------------------------
  // Geometry
  //
  // "Display" coordinates are what the user sees: origin at the top-left of the page as
  // rendered (after the page's /Rotate is applied), x to the right, y downwards, in PDF
  // points. pdf-lib draws in unrotated PDF user space (origin bottom-left, y up), so
  // everything placed by the user is converted here.
  // ---------------------------------------------------------------------------

  /**
   * A page's /Rotate as viewers apply it: a multiple of 90 in [0, 360). Any other value is not
   * valid PDF; pdf.js (and so the previews here) shows such a page as not turned, so it is read
   * as 0 rather than rounded to the nearest quarter turn.
   */
  function normalizeRotation(degrees) {
    const value = Number(degrees) || 0;
    if (!Number.isInteger(value) || value % 90 !== 0) {
      return 0;
    }
    return ((value % 360) + 360) % 360;
  }

  /**
   * The page area viewers show: the CropBox clipped to the MediaBox, falling back to the
   * MediaBox when they do not overlap. pdf.js renders exactly this box, so the editor preview
   * and the saved PDF must both map coordinates against it.
   */
  function effectiveBox(rawMediaBox, rawCropBox) {
    const mediaBox = normalizeBox(rawMediaBox);
    const cropBox = normalizeBox(rawCropBox);
    const x0 = Math.max(mediaBox.x, cropBox.x);
    const y0 = Math.max(mediaBox.y, cropBox.y);
    const x1 = Math.min(mediaBox.x + mediaBox.width, cropBox.x + cropBox.width);
    const y1 = Math.min(mediaBox.y + mediaBox.height, cropBox.y + cropBox.height);
    if (x1 <= x0 || y1 <= y0) {
      return { x: mediaBox.x, y: mediaBox.y, width: mediaBox.width, height: mediaBox.height };
    }
    return { x: x0, y: y0, width: x1 - x0, height: y1 - y0 };
  }

  /**
   * A PDF rectangle may name any two opposite corners ([612 792 0 0] is valid); pdf-lib keeps
   * them as written, which gives a negative width or height. Puts the lower-left corner first.
   */
  function normalizeBox(box) {
    const width = Number(box.width) || 0;
    const height = Number(box.height) || 0;
    return {
      x: width < 0 ? box.x + width : box.x,
      y: height < 0 ? box.y + height : box.y,
      width: Math.abs(width),
      height: Math.abs(height)
    };
  }

  /** The page's /UserUnit (the size of one unit in 1/72 inch); 1 when absent or invalid. */
  function pageUserUnit(PDFLib, page) {
    let value = null;
    try {
      value = page.node.lookupMaybe(PDFLib.PDFName.of('UserUnit'), PDFLib.PDFNumber);
    } catch {
      return 1; // not a number
    }
    const unit = value ? value.asNumber() : 1;
    return Number.isFinite(unit) && unit > 0 ? unit : 1;
  }

  function visibleBox(page) {
    return effectiveBox(page.getMediaBox(), page.getCropBox());
  }

  /** Width/height of the page as displayed. box is {x, y, width, height}. */
  function displaySize(box, rotation) {
    const r = normalizeRotation(rotation);
    return r === 90 || r === 270
      ? { width: box.height, height: box.width }
      : { width: box.width, height: box.height };
  }

  /** Converts a display point (u right, v down) into PDF user space. */
  function displayToPdfPoint(box, rotation, u, v) {
    const x0 = box.x;
    const y0 = box.y;
    const x1 = box.x + box.width;
    const y1 = box.y + box.height;
    switch (normalizeRotation(rotation)) {
      case 90:
        return { x: x0 + v, y: y0 + u };
      case 180:
        return { x: x1 - u, y: y0 + v };
      case 270:
        return { x: x1 - v, y: y1 - u };
      default:
        return { x: x0 + u, y: y1 - v };
    }
  }

  /**
   * Returns the pdf-lib anchor (bottom-left corner, counter-clockwise rotation in degrees)
   * that makes a display-space rectangle appear upright on a page with the given /Rotate.
   */
  function placeRect(box, rotation, rect) {
    const anchor = displayToPdfPoint(box, rotation, rect.left, rect.top + rect.height);
    return { x: anchor.x, y: anchor.y, rotate: normalizeRotation(rotation) };
  }

  /**
   * Anchor for a text line whose baseline starts at display point (u, v) and which is
   * additionally tilted by `angle` degrees counter-clockwise as seen by the user.
   */
  function placeText(box, rotation, u, v, angle) {
    const anchor = displayToPdfPoint(box, rotation, u, v);
    return { x: anchor.x, y: anchor.y, rotate: normalizeRotation(rotation) + (Number(angle) || 0) };
  }

  /** Display-space top-left for a block of the given size at one of nine positions. */
  function positionBlock(position, page, block, margin) {
    const [vertical, horizontal] = String(position || 'bottom-center').split('-');
    const m = Math.max(0, Number(margin) || 0);
    let left;
    if (horizontal === 'left') {
      left = m;
    } else if (horizontal === 'right') {
      left = page.width - m - block.width;
    } else {
      left = (page.width - block.width) / 2;
    }
    let top;
    if (vertical === 'top') {
      top = m;
    } else if (vertical === 'middle') {
      top = (page.height - block.height) / 2;
    } else {
      top = page.height - m - block.height;
    }
    return { left, top };
  }

  // ---------------------------------------------------------------------------
  // Images
  // ---------------------------------------------------------------------------

  const PAGE_SIZES = Object.freeze({
    a4: [595.28, 841.89],
    a3: [841.89, 1190.55],
    letter: [612, 792],
    legal: [612, 1008]
  });

  /** Scales an image (contain) into a content box and centres it. All values in points. */
  function fitImage(imageWidth, imageHeight, boxWidth, boxHeight) {
    if (!(imageWidth > 0) || !(imageHeight > 0) || !(boxWidth > 0) || !(boxHeight > 0)) {
      throw new PdfToolError('image-size', 'The image has no usable size.');
    }
    const scale = Math.min(boxWidth / imageWidth, boxHeight / imageHeight);
    const width = imageWidth * scale;
    const height = imageHeight * scale;
    return { x: (boxWidth - width) / 2, y: (boxHeight - height) / 2, width, height };
  }

  /**
   * Reads the EXIF orientation of a JPEG and returns the clockwise rotation, in degrees,
   * needed to show it upright (0, 90, 180 or 270). Mirrored orientations are treated as
   * their rotation component (see jpegMirrored); returns 0 when there is no EXIF orientation.
   */
  function jpegRotation(bytes) {
    const orientation = jpegOrientation(bytes);
    if (orientation === 3 || orientation === 4) return 180;
    if (orientation === 5 || orientation === 6) return 90;
    if (orientation === 7 || orientation === 8) return 270;
    return 0;
  }

  /** True when a JPEG's EXIF orientation also mirrors it (2, 4, 5 or 7; some front cameras). */
  function jpegMirrored(bytes) {
    return [2, 4, 5, 7].includes(jpegOrientation(bytes));
  }

  /** The EXIF orientation of a JPEG, 1 to 8, or 1 when there is none. */
  function jpegOrientation(bytes) {
    const entry = jpegOrientationEntry(bytes);
    return entry ? entry.orientation : 1;
  }

  /**
   * A copy of a JPEG whose EXIF orientation says "as stored" (1), or the same bytes when it
   * already does. Browsers turn a JPEG by its EXIF orientation when decoding it, and current
   * ones ignore a request not to; PDF viewers never do. Reduce size decodes this copy, so the
   * pixels are the ones the PDF shows.
   */
  function jpegClearOrientation(bytes) {
    const data = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
    const entry = jpegOrientationEntry(data);
    if (!entry || entry.orientation === 1) return data;
    const copy = new Uint8Array(data);
    copy[entry.at] = entry.little ? 1 : 0;
    copy[entry.at + 1] = entry.little ? 0 : 1;
    return copy;
  }

  /** Where a JPEG's EXIF orientation is: { orientation, at, little }, or null when it has none. */
  function jpegOrientationEntry(bytes) {
    const data = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
    if (data.length < 4 || data[0] !== 0xff || data[1] !== 0xd8) {
      return null;
    }
    let offset = 2;
    while (offset + 4 <= data.length) {
      if (data[offset] !== 0xff) {
        return null;
      }
      const marker = data[offset + 1];
      if (marker === 0xda || marker === 0xd9) {
        return null;
      }
      const length = (data[offset + 2] << 8) | data[offset + 3];
      if (length < 2) {
        return null;
      }
      if (marker === 0xe1 && offset + 10 <= data.length &&
          data[offset + 4] === 0x45 && data[offset + 5] === 0x78 && data[offset + 6] === 0x69 &&
          data[offset + 7] === 0x66 && data[offset + 8] === 0 && data[offset + 9] === 0) {
        return orientationFromTiff(data, offset + 10, Math.min(data.length, offset + 2 + length));
      }
      offset += 2 + length;
    }
    return null;
  }

  function orientationFromTiff(data, start, end) {
    if (start + 8 > end) {
      return null;
    }
    const little = data[start] === 0x49 && data[start + 1] === 0x49;
    const big = data[start] === 0x4d && data[start + 1] === 0x4d;
    if (!little && !big) {
      return null;
    }
    const u16 = (at) => (little ? data[at] | (data[at + 1] << 8) : (data[at] << 8) | data[at + 1]);
    const u32 = (at) => (little
      ? (data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24)) >>> 0
      : ((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]) >>> 0);
    const ifd = start + u32(start + 4);
    if (ifd + 2 > end) {
      return null;
    }
    const count = u16(ifd);
    for (let i = 0; i < count; i += 1) {
      const entry = ifd + 2 + i * 12;
      if (entry + 12 > end) {
        return null;
      }
      if (u16(entry) === 0x0112) {
        const orientation = u16(entry + 8);
        return { orientation: orientation >= 1 && orientation <= 8 ? orientation : 1, at: entry + 8, little };
      }
    }
    return null;
  }

  /**
   * pdf-lib parameters that draw a raw image so that, after rotating it clockwise by
   * `clockwise` degrees, it fills the target rectangle (PDF space, bottom-left origin).
   */
  function orientedImageDraw(rect, clockwise) {
    switch (normalizeRotation(clockwise)) {
      case 90:
        return { x: rect.x, y: rect.y + rect.height, width: rect.height, height: rect.width, rotate: -90 };
      case 180:
        return { x: rect.x + rect.width, y: rect.y + rect.height, width: rect.width, height: rect.height, rotate: 180 };
      case 270:
        return { x: rect.x + rect.width, y: rect.y, width: rect.height, height: rect.width, rotate: 90 };
      default:
        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height, rotate: 0 };
    }
  }

  // ---------------------------------------------------------------------------
  // Text helpers
  // ---------------------------------------------------------------------------

  function formatStamp(template, values) {
    return String(template ?? '').replace(/\{(n|total|date)\}/g, (_, key) => String(values[key] ?? ''));
  }

  function hexToRgb(hex) {
    const match = /^#?([0-9a-f]{6})$/i.exec(String(hex || '').trim());
    if (!match) {
      return { r: 0, g: 0, b: 0 };
    }
    const value = Number.parseInt(match[1], 16);
    return { r: ((value >> 16) & 0xff) / 255, g: ((value >> 8) & 0xff) / 255, b: (value & 0xff) / 255 };
  }

  function clamp(value, min, max, fallback) {
    // A field left blank means "use the default", not 0 (Number('') is 0).
    if (value === null || value === undefined || String(value).trim() === '') {
      return fallback;
    }
    const number = Number(value);
    if (!Number.isFinite(number)) {
      return fallback;
    }
    return Math.min(max, Math.max(min, number));
  }

  /** Produces "report" from "C:\\fakepath\\Report (final).pdf" style names, safe for downloads. */
  function baseName(fileName, fallback) {
    const name = String(fileName || '').split(/[\\/]/).pop().replace(/\.[^.]+$/, '');
    const safe = name.replace(/[<>:"/\\|?*\u0000-\u001f]+/g, '_').replace(/\s+/g, ' ').trim().slice(0, 120);
    return safe || fallback || 'document';
  }

  // Standard PDF fonts only cover WinAnsi (Latin) characters. Report unsupported text
  // clearly instead of letting pdf-lib fail with an encoding stack trace: which character,
  // in which text, and where, so the user knows what to change.
  function ensureEncodable(font, text, where) {
    try {
      font.encodeText(text);
      return;
    } catch {
      // Find the first character the font cannot show.
    }
    let bad = '';
    for (const character of String(text)) {
      try {
        font.encodeText(character);
      } catch {
        bad = character;
        break;
      }
    }
    const shown = String(text).length > 40 ? `${String(text).slice(0, 40)}…` : String(text);
    const place = where ? ` ${where}` : '';
    const rupee = bad === '\u20b9' ? ' For the rupee sign, write Rs. instead.' : '';
    throw new PdfToolError(
      'text-unsupported',
      `The text "${shown}"${place} has a character the built-in PDF font cannot show${bad ? ` (${bad})` : ''}. ` +
      `Use Latin letters, digits and common punctuation.${rupee}`);
  }

  // ---------------------------------------------------------------------------
  // ZIP: shared with Photo Tools (wwwroot/js/tools-shared/zip-writer.js)
  // ---------------------------------------------------------------------------

  const { crc32, createZip } = zip;

  // ---------------------------------------------------------------------------
  // PDF operations (pdf-lib)
  // ---------------------------------------------------------------------------

  async function loadPdf(PDFLib, bytes) {
    try {
      return await PDFLib.PDFDocument.load(bytes, { updateMetadata: false });
    } catch (error) {
      if (error && (error.name === 'EncryptedPDFError' || /encrypt/i.test(error.message || ''))) {
        throw new PdfToolError(
          'encrypted',
          'This PDF is encrypted (it has a password or editing restrictions), so it cannot be changed here.');
      }
      throw new PdfToolError('unreadable', 'This file could not be read as a PDF. It may be damaged or not a PDF.');
    }
  }

  function save(document) {
    return document.save({ useObjectStreams: true });
  }

  /**
   * Builds a new PDF from pages of one or more source PDFs.
   * sources: [Uint8Array | ArrayBuffer]; pages: [{ source, index, rotate }] where rotate is
   * the extra clockwise rotation, in degrees, applied on top of the page's own rotation.
   */
  async function assemblePages(PDFLib, sources, pages) {
    if (!Array.isArray(pages) || pages.length === 0) {
      throw new PdfToolError('no-pages', 'There are no pages to save.');
    }
    const loaded = [];
    for (const bytes of sources) {
      loaded.push(bytes ? await loadPdf(PDFLib, bytes) : null);
    }
    return assembleLoaded(PDFLib, loaded, pages);
  }

  /**
   * assemblePages for documents that are already loaded. A loaded source can be used for
   * several outputs (Split reads its file once): the only change made to a source is tagging
   * the page references in its annotations, and a tag names a page of the source, not of any
   * one output, so it is resolved afresh against each output.
   */
  async function assembleLoaded(PDFLib, loaded, pages) {
    if (!Array.isArray(pages) || pages.length === 0) {
      throw new PdfToolError('no-pages', 'There are no pages to save.');
    }
    const output = await PDFLib.PDFDocument.create();
    // copyPages per source keeps shared resources (fonts, images) de-duplicated.
    const copies = new Map();
    for (let sourceIndex = 0; sourceIndex < loaded.length; sourceIndex += 1) {
      const indexes = pages.filter((page) => page.source === sourceIndex).map((page) => page.index);
      if (indexes.length === 0) {
        continue;
      }
      const document = loaded[sourceIndex];
      if (!document) {
        throw new PdfToolError('no-source', 'A source file is missing.');
      }
      const unique = Array.from(new Set(indexes));
      unique.forEach((index) => {
        if (!Number.isInteger(index) || index < 0 || index >= document.getPageCount()) {
          throw new PdfToolError('range-out-of-bounds', 'A selected page does not exist in its file.');
        }
      });
      tagPageReferences(PDFLib, document, sourceIndex, unique);
      const copied = await output.copyPages(document, unique);
      unique.forEach((index, position) => copies.set(`${sourceIndex}:${index}`, copied[position]));
    }

    const used = new Set();
    const outputPages = new Map();
    const pageSources = new Map();
    for (const page of pages) {
      const key = `${page.source}:${page.index}`;
      let copy = copies.get(key);
      if (used.has(key)) {
        // The same page appears twice: copy it again so each occurrence is its own page.
        [copy] = await output.copyPages(loaded[page.source], [page.index]);
      }
      used.add(key);
      const current = copy.getRotation().angle;
      copy.setRotation(PDFLib.degrees(normalizeRotation(normalizeRotation(current) + (Number(page.rotate) || 0))));
      output.addPage(copy);
      pageSources.set(String(copy.ref), page.source);
      if (!outputPages.has(key)) {
        outputPages.set(key, copy.ref);
      }
    }
    carryFormFields(PDFLib, output, loaded, pageSources);
    resolvePageTags(PDFLib, output, outputPages);
    removeUnreachableObjects(PDFLib, output);
    assertOnlyOutputPages(PDFLib, output);
    return save(output);
  }

  /**
   * copyPages brings form widgets (and, through their /Parent links, their fields) across,
   * but not the document-level /AcroForm that lists the fields, so viewers stop treating them
   * as a form. Rebuild it from the widgets on the output pages only: shared parent fields keep
   * just the kids that lead to a kept widget, so fields on dropped pages are not carried over.
   *
   * A field keeps its own answer. PDF viewers treat fields with the same name as one field, so
   * two filled copies of the same form, merged, would show or overwrite each other's answers.
   * A top-level field whose name an earlier file already uses is renamed "name (2)", "name (3)".
   * Fields of the same file keep their names, so a page used twice still shares its fields.
   */
  function carryFormFields(PDFLib, output, sources, pageSources) {
    const { PDFName, PDFDict, PDFArray, PDFRef, PDFHexString } = PDFLib;
    const PARENT = PDFName.of('Parent');
    const KIDS = PDFName.of('Kids');
    const kept = new Set();
    const roots = new Map();
    const rootSources = new Map();

    for (const page of output.getPages()) {
      const annots = page.node.Annots();
      if (!annots) continue;
      for (let i = 0; i < annots.size(); i += 1) {
        const ref = annots.get(i);
        const annot = ref instanceof PDFRef ? output.context.lookup(ref) : null;
        if (!(annot instanceof PDFDict) || String(annot.get(PDFName.of('Subtype'))) !== '/Widget') {
          continue;
        }
        // The copied /P points at a copy of the source page; point it at the page it is on.
        annot.set(PDFName.of('P'), page.ref);
        let nodeRef = ref;
        let node = annot;
        while (!kept.has(String(nodeRef))) {
          kept.add(String(nodeRef));
          const parent = node.get(PARENT);
          if (!(parent instanceof PDFRef)) {
            roots.set(String(nodeRef), nodeRef);
            rootSources.set(String(nodeRef), pageSources ? pageSources.get(String(page.ref)) : 0);
            break;
          }
          nodeRef = parent;
          node = output.context.lookup(parent, PDFDict);
        }
      }
    }
    if (roots.size === 0) {
      return;
    }

    // Prune every kept field's /Kids to the kids that lead to a kept widget.
    for (const key of kept) {
      const [objectNumber, generation] = key.split(' ').map(Number);
      const node = output.context.lookup(PDFRef.of(objectNumber, generation));
      const kids = node instanceof PDFDict ? node.lookupMaybe(KIDS, PDFArray) : undefined;
      if (kids) {
        const remaining = kids.asArray().filter((kid) => kid instanceof PDFRef && kept.has(String(kid)));
        node.set(KIDS, output.context.obj(remaining));
      }
    }

    // Names are taken in page order, so the first file keeps its field names. Every name that is
    // already in use is reserved first, so a new name never repeats one that a later file has
    // (a file with both "name" and "name (2)").
    const entries = [];
    for (const [key, ref] of roots) {
      const node = output.context.lookup(ref);
      const title = node instanceof PDFDict ? node.lookup(PDFName.of('T')) : undefined;
      const name = title && typeof title.decodeText === 'function' ? title.decodeText() : undefined;
      if (name !== undefined) entries.push({ node, name, source: rootSources.get(key) });
    }
    const taken = new Set(entries.map((entry) => entry.name));
    const firstSource = new Map();
    // A field of one file seen again (its page used twice) gets the same new name, so it still
    // shares its answer, as it does in that file.
    const renamed = new Map();
    for (const { node, name, source } of entries) {
      if (!firstSource.has(name)) {
        firstSource.set(name, source);
        continue;
      }
      if (firstSource.get(name) === source) continue;
      const key = `${source}\u0000${name}`;
      let next = renamed.get(key);
      if (!next) {
        let number = 2;
        while (taken.has(`${name} (${number})`)) number += 1;
        next = `${name} (${number})`;
        taken.add(next);
        renamed.set(key, next);
      }
      node.set(PDFName.of('T'), PDFHexString.fromText(next));
    }

    const acroForm = output.context.obj({});
    acroForm.set(PDFName.of('Fields'), output.context.obj(Array.from(roots.values())));
    const source = sources.find((document) => document && document.catalog.lookupMaybe(PDFName.of('AcroForm'), PDFDict));
    if (source) {
      const sourceForm = source.catalog.lookupMaybe(PDFName.of('AcroForm'), PDFDict);
      const copier = PDFLib.PDFObjectCopier.for(source.context, output.context);
      for (const key of ['DA', 'DR', 'NeedAppearances', 'Q']) {
        const value = sourceForm.get(PDFName.of(key));
        if (value !== undefined) {
          acroForm.set(PDFName.of(key), copier.copy(value));
        }
      }
    }
    output.catalog.set(PDFName.of('AcroForm'), output.context.register(acroForm));
  }

  const PAGE_TAG_PREFIX = 'PrismPdfToolsPage:';
  // On an annotation itself, /P (its own page) is re-pointed by resolvePageTags and /Parent
  // (its form field) by carryFormFields. Deeper in the graph these keys are ordinary links.
  const STRUCTURAL_KEYS = new Set(['/P', '/Parent']);

  /** Named destinations of a document: /Dests dictionary and the /Names /Dests name tree. */
  function namedDestinations(PDFLib, document) {
    const { PDFName, PDFDict, PDFArray } = PDFLib;
    const destinations = new Map();
    const add = (name, value) => {
      const explicit = value instanceof PDFDict ? value.lookup(PDFName.of('D')) : value;
      if (name !== undefined && explicit instanceof PDFArray) {
        destinations.set(name, explicit);
      }
    };
    const dests = document.catalog.lookupMaybe(PDFName.of('Dests'), PDFDict);
    if (dests) {
      dests.entries().forEach(([key]) => add(key.decodeText(), dests.lookup(key)));
    }
    const names = document.catalog.lookupMaybe(PDFName.of('Names'), PDFDict);
    const visit = (node, depth) => {
      if (!(node instanceof PDFDict) || depth > 32) return;
      const leaves = node.lookupMaybe(PDFName.of('Names'), PDFArray);
      if (leaves) {
        for (let i = 0; i + 1 < leaves.size(); i += 2) {
          add(destinationName(PDFLib, leaves.lookup(i)), leaves.lookup(i + 1));
        }
      }
      const kids = node.lookupMaybe(PDFName.of('Kids'), PDFArray);
      if (kids) {
        for (let i = 0; i < kids.size(); i += 1) visit(kids.lookup(i), depth + 1);
      }
    };
    visit(names ? names.lookup(PDFName.of('Dests')) : undefined, 0);
    return destinations;
  }

  function destinationName(PDFLib, value) {
    const { PDFName, PDFString, PDFHexString } = PDFLib;
    return value instanceof PDFName || value instanceof PDFString || value instanceof PDFHexString
      ? value.decodeText()
      : undefined;
  }

  /**
   * copyPages copies any page an annotation refers to (a link, a button's GoTo action, an
   * additional action...) as a separate, unlisted page object, and drops the source's named
   * destinations. Before copying, rewrite each annotation on the selected pages so that:
   * named destinations become explicit ones, and every reference to a page of this source
   * becomes a name recording which page it was. resolvePageTags then points those at the
   * output pages, so no page is dragged into the output through an annotation.
   */
  function tagPageReferences(PDFLib, document, sourceIndex, indexes) {
    const { PDFName, PDFDict, PDFArray, PDFRef, PDFStream } = PDFLib;
    const pageIndexByRef = new Map(document.getPages().map((page, index) => [String(page.ref), index]));
    const named = namedDestinations(PDFLib, document);
    const tagFor = (ref) => PDFName.of(`${PAGE_TAG_PREFIX}${sourceIndex}:${pageIndexByRef.get(String(ref))}`);
    const seen = new Set();

    const visit = (value, isAnnotation = false) => {
      if (value instanceof PDFRef) {
        const key = String(value);
        if (seen.has(key) || pageIndexByRef.has(key)) return;
        seen.add(key);
        visit(document.context.lookup(value), isAnnotation);
      } else if (value instanceof PDFDict) {
        for (const [key, entry] of value.entries()) {
          const name = String(key);
          if (isAnnotation && STRUCTURAL_KEYS.has(name)) continue;
          if (name === '/Dest' || name === '/D') {
            const explicit = named.get(destinationName(PDFLib, value.lookup(key)));
            if (explicit) {
              value.set(key, document.context.obj(explicit.asArray()));
            }
          }
          const current = value.get(key);
          if (current instanceof PDFRef && pageIndexByRef.has(String(current))) {
            value.set(key, tagFor(current));
          } else {
            visit(current);
          }
        }
      } else if (value instanceof PDFArray) {
        value.asArray().forEach((entry, index) => {
          if (entry instanceof PDFRef && pageIndexByRef.has(String(entry))) {
            value.set(index, tagFor(entry));
          } else {
            visit(entry);
          }
        });
      } else if (value instanceof PDFStream) {
        visit(value.dict);
      }
    };

    for (const index of indexes) {
      const annots = document.getPage(index).node.Annots();
      if (annots) {
        annots.asArray().forEach((ref) => visit(ref, true));
      }
    }
  }

  /**
   * Points tagged page references in the output's annotations at the output pages. Anything
   * that still leads to a page that is not in the output (a dropped page, or a page object from
   * elsewhere) is removed: a link annotation as a whole, otherwise just the entry (such as a
   * button's /A or /AA action) that refers to it. It would be broken anyway, and keeping it
   * would keep that page's content in the file.
   */
  function resolvePageTags(PDFLib, output, outputPages) {
    const { PDFName, PDFDict, PDFArray, PDFRef, PDFStream } = PDFLib;
    const pages = new Set(output.getPages().map((page) => String(page.ref)));
    const isStrayPage = (ref) => {
      if (pages.has(String(ref))) return false;
      const object = output.context.lookup(ref);
      return object instanceof PDFDict && String(object.get(PDFName.of('Type'))) === '/Page';
    };
    const resolveTag = (value) => {
      if (!(value instanceof PDFName)) return undefined;
      const name = value.decodeText();
      if (!name.startsWith(PAGE_TAG_PREFIX)) return undefined;
      return outputPages.get(name.slice(PAGE_TAG_PREFIX.length)) || null;
    };

    // Returns false when the value still leads to a page that is not in the output.
    const resolve = (value, seen) => {
      if (value instanceof PDFRef) {
        const key = String(value);
        if (seen.has(key) || pages.has(key)) return true;
        if (isStrayPage(value)) return false;
        seen.add(key);
        return resolve(output.context.lookup(value), seen);
      }
      if (value instanceof PDFDict) {
        let ok = true;
        for (const [key, entry] of value.entries()) {
          const tag = resolveTag(entry);
          if (tag) value.set(key, tag);
          else if (tag === null || !resolve(entry, seen)) ok = false;
        }
        return ok;
      }
      if (value instanceof PDFArray) {
        let ok = true;
        value.asArray().forEach((entry, index) => {
          const tag = resolveTag(entry);
          if (tag) value.set(index, tag);
          else if (tag === null || !resolve(entry, seen)) ok = false;
        });
        return ok;
      }
      if (value instanceof PDFStream) {
        return resolve(value.dict, seen);
      }
      return true;
    };

    for (const page of output.getPages()) {
      // Article-thread beads chain pages together; the threads are not carried over.
      page.node.delete(PDFName.of('B'));
      const annots = page.node.Annots();
      if (!annots) continue;
      const keep = annots.asArray().filter((ref) => {
        const annot = ref instanceof PDFRef ? output.context.lookup(ref) : ref;
        if (!(annot instanceof PDFDict)) return true;
        const isLink = String(annot.get(PDFName.of('Subtype'))) === '/Link';
        if (annot.has(PDFName.of('P'))) {
          annot.set(PDFName.of('P'), page.ref);
        }
        for (const [key, entry] of annot.entries()) {
          if (STRUCTURAL_KEYS.has(String(key))) continue;
          const tag = resolveTag(entry);
          if (tag) {
            annot.set(key, tag);
            continue;
          }
          if (tag === null || !resolve(entry, new Set())) {
            if (isLink) return false;
            annot.delete(key);
          }
        }
        return true;
      });
      if (keep.length !== annots.size()) {
        page.node.set(PDFName.of('Annots'), output.context.obj(keep));
      }
    }
  }

  /**
   * Last line of defence for Split, extract and delete: after clean-up the file must contain
   * no page object other than the output pages. If some unforeseen structure still references
   * another page, refuse to save rather than silently keep content the user removed.
   */
  function assertOnlyOutputPages(PDFLib, output) {
    const { PDFDict, PDFName } = PDFLib;
    const pages = new Set(output.getPages().map((page) => String(page.ref)));
    const stray = output.context.enumerateIndirectObjects().some(([ref, object]) =>
      object instanceof PDFDict &&
      String(object.get(PDFName.of('Type'))) === '/Page' &&
      !pages.has(String(ref)));
    if (stray) {
      throw new PdfToolError(
        'unsupported-structure',
        'This PDF links its pages together in a way that cannot be separated safely, so it was not saved. No file was created.');
    }
  }

  /**
   * Deletes every object the document no longer references. copyPages follows links such as
   * widget /P entries, shared form fields and link destinations into pages the user dropped;
   * those copies are unreachable once the output is assembled, but pdf-lib would still write
   * them into the file. Dropped pages must not survive in the saved bytes.
   */
  function removeUnreachableObjects(PDFLib, document) {
    const { PDFRef, PDFDict, PDFArray, PDFStream } = PDFLib;
    const context = document.context;
    const reachable = new Set();
    const pending = [context.trailerInfo.Root, context.trailerInfo.Info].filter(Boolean);
    while (pending.length) {
      const value = pending.pop();
      if (value instanceof PDFRef) {
        const key = String(value);
        if (!reachable.has(key)) {
          reachable.add(key);
          pending.push(context.lookup(value));
        }
      } else if (value instanceof PDFDict) {
        value.entries().forEach(([, entry]) => pending.push(entry));
      } else if (value instanceof PDFArray) {
        value.asArray().forEach((entry) => pending.push(entry));
      } else if (value instanceof PDFStream) {
        pending.push(value.dict);
      }
    }
    for (const [ref] of context.enumerateIndirectObjects()) {
      if (!reachable.has(String(ref))) {
        context.delete(ref);
      }
    }
  }

  async function splitPdf(PDFLib, bytes, groups) {
    if (!Array.isArray(groups) || groups.length === 0) {
      throw new PdfToolError('no-pages', 'There are no pages to split.');
    }
    // Read once: a large file read again for every part made "every page" slow.
    const document = await loadPdf(PDFLib, bytes);
    const results = [];
    for (const group of groups) {
      results.push(await assembleLoaded(PDFLib, [document], group.map((index) => ({ source: 0, index }))));
    }
    return results;
  }

  /**
   * images: [{ bytes, type: 'jpg' | 'png', width, height }] (pixel size, used for 'fit').
   * options: { pageSize: 'a4' | 'a3' | 'letter' | 'legal' | 'fit', orientation: 'auto' | 'portrait' | 'landscape', margin }
   */
  async function imagesToPdf(PDFLib, images, options) {
    if (!Array.isArray(images) || images.length === 0) {
      throw new PdfToolError('no-images', 'Add at least one image.');
    }
    const settings = options || {};
    const margin = clamp(settings.margin, 0, 144, 0);
    const output = await PDFLib.PDFDocument.create();

    for (const image of images) {
      const embedded = image.type === 'png'
        ? await output.embedPng(image.bytes)
        : await output.embedJpg(image.bytes);
      const clockwise = image.type === 'jpg' ? jpegRotation(image.bytes) : 0;
      const quarter = clockwise === 90 || clockwise === 270;
      // Upright size of the picture, in points at 96 dpi.
      const uprightWidth = (quarter ? embedded.height : embedded.width) * 0.75;
      const uprightHeight = (quarter ? embedded.width : embedded.height) * 0.75;

      let pageWidth;
      let pageHeight;
      if (settings.pageSize === 'fit' || !PAGE_SIZES[settings.pageSize]) {
        pageWidth = uprightWidth + margin * 2;
        pageHeight = uprightHeight + margin * 2;
      } else {
        const [short, long] = PAGE_SIZES[settings.pageSize];
        const landscape = settings.orientation === 'landscape' ||
          (settings.orientation !== 'portrait' && uprightWidth > uprightHeight);
        pageWidth = landscape ? long : short;
        pageHeight = landscape ? short : long;
      }

      const contentWidth = Math.max(1, pageWidth - margin * 2);
      const contentHeight = Math.max(1, pageHeight - margin * 2);
      const fitted = fitImage(uprightWidth, uprightHeight, contentWidth, contentHeight);
      const target = { x: margin + fitted.x, y: margin + fitted.y, width: fitted.width, height: fitted.height };
      const draw = orientedImageDraw(target, clockwise);

      const page = output.addPage([pageWidth, pageHeight]);
      page.drawImage(embedded, {
        x: draw.x,
        y: draw.y,
        width: draw.width,
        height: draw.height,
        rotate: PDFLib.degrees(draw.rotate)
      });
    }
    return save(output);
  }

  function selectedIndexes(pageCount, pages) {
    if (pages === undefined || pages === null || String(pages).trim() === '') {
      return Array.from({ length: pageCount }, (_, index) => index);
    }
    return pageSetFromRanges(pages, pageCount);
  }

  /**
   * options: { text, fontSize, opacity, color, angle, position, pages }
   * position: 'middle-center' or any positionBlock value; angle in degrees counter-clockwise.
   */
  async function addWatermark(PDFLib, bytes, options) {
    const settings = options || {};
    const text = String(settings.text || '').trim();
    if (!text) {
      throw new PdfToolError('text-empty', 'Enter the watermark text.');
    }
    const document = await loadPdf(PDFLib, bytes);
    const font = await document.embedFont(PDFLib.StandardFonts.HelveticaBold);
    ensureEncodable(font, text, 'in the watermark');

    const physicalSize = clamp(settings.fontSize, 6, 300, 60);
    const opacity = clamp(settings.opacity, 0.02, 1, 0.25);
    const angle = clamp(settings.angle, -360, 360, 45);
    const rgb = hexToRgb(settings.color || '#c0392b');
    const position = settings.position || 'middle-center';
    const pages = document.getPages();

    for (const index of selectedIndexes(pages.length, settings.pages)) {
      const page = pages[index];
      // Sizes are chosen in points; a page with /UserUnit 2 has units twice as large.
      const unit = pageUserUnit(PDFLib, page);
      const fontSize = physicalSize / unit;
      const textWidth = font.widthOfTextAtSize(text, fontSize);
      const capHeight = font.heightAtSize(fontSize, { descender: false }) * 0.72;
      const rotation = page.getRotation().angle;
      const box = visibleBox(page);
      const view = displaySize(box, rotation);

      // Size of the rotated text's bounding box, used to keep corner positions on the page.
      const radians = (angle * Math.PI) / 180;
      const boundsWidth = Math.abs(textWidth * Math.cos(radians)) + Math.abs(capHeight * Math.sin(radians));
      const boundsHeight = Math.abs(textWidth * Math.sin(radians)) + Math.abs(capHeight * Math.cos(radians));
      const block = positionBlock(position, view, { width: boundsWidth, height: boundsHeight }, 36 / unit);
      const centreU = block.left + boundsWidth / 2;
      const centreV = block.top + boundsHeight / 2;

      // Baseline start relative to the text centre, rotated by the user's angle (y up).
      const offsetX = (-textWidth / 2) * Math.cos(radians) - (-capHeight / 2) * Math.sin(radians);
      const offsetY = (-textWidth / 2) * Math.sin(radians) + (-capHeight / 2) * Math.cos(radians);
      const anchor = placeText(box, rotation, centreU + offsetX, centreV - offsetY, angle);

      page.drawText(text, {
        x: anchor.x,
        y: anchor.y,
        size: fontSize,
        font,
        color: PDFLib.rgb(rgb.r, rgb.g, rgb.b),
        opacity,
        rotate: PDFLib.degrees(anchor.rotate)
      });
    }
    return save(document);
  }

  /**
   * Adds page numbers and header/footer text.
   * options: {
   *   stamps: [{ template, position }], // e.g. { template: 'Page {n} of {total}', position: 'bottom-center' }
   *   fontSize, margin, color, startAt, skipFirst, pages, date
   * }
   * {n} counts only the stamped pages, starting at startAt; {total} is the last number used, so
   * "Page {n} of {total}" reads "Page 5 of 7" for three pages starting at 5.
   */
  async function addTextStamps(PDFLib, bytes, options) {
    const settings = options || {};
    const stamps = (settings.stamps || []).filter((stamp) => String(stamp.template || '').trim());
    if (stamps.length === 0) {
      throw new PdfToolError('text-empty', 'Choose a page number format or enter header/footer text.');
    }
    const document = await loadPdf(PDFLib, bytes);
    const font = await document.embedFont(PDFLib.StandardFonts.Helvetica);
    const physicalSize = clamp(settings.fontSize, 6, 72, 10);
    const physicalMargin = clamp(settings.margin, 0, 144, 28);
    const startAt = Math.round(clamp(settings.startAt, 0, 100000, 1));
    const rgb = hexToRgb(settings.color || '#000000');
    const pages = document.getPages();

    let indexes = selectedIndexes(pages.length, settings.pages);
    if (settings.skipFirst) {
      indexes = indexes.filter((index) => index !== 0);
    }
    const total = indexes.length + startAt - 1;

    indexes.forEach((index, ordinal) => {
      const page = pages[index];
      // Sizes are chosen in points; a page with /UserUnit 2 has units twice as large.
      const unit = pageUserUnit(PDFLib, page);
      const fontSize = physicalSize / unit;
      const margin = physicalMargin / unit;
      const ascent = font.heightAtSize(fontSize, { descender: false });
      const descent = Math.max(0, font.heightAtSize(fontSize) - ascent);
      const rotation = page.getRotation().angle;
      const box = visibleBox(page);
      const view = displaySize(box, rotation);

      for (const stamp of stamps) {
        const text = formatStamp(stamp.template, { n: startAt + ordinal, total, date: settings.date || '' });
        ensureEncodable(font, text, `for page ${index + 1}`);
        const width = font.widthOfTextAtSize(text, fontSize);
        const block = positionBlock(stamp.position, view, { width, height: ascent + descent }, margin);
        const anchor = placeText(box, rotation, block.left, block.top + ascent, 0);
        page.drawText(text, {
          x: anchor.x,
          y: anchor.y,
          size: fontSize,
          font,
          color: PDFLib.rgb(rgb.r, rgb.g, rgb.b),
          rotate: PDFLib.degrees(anchor.rotate)
        });
      }
    });
    return save(document);
  }

  /**
   * Flattens user annotations onto the pages.
   * items: [{
   *   page, type: 'text' | 'image' | 'rect' | 'highlight' | 'whiteout',
   *   left, top, width, height,             // display points
   *   text, fontSize, baseline, color,      // text: baseline = top-to-first-baseline, points
 *   opacity,                              // shapes / images
   *   imageBytes, imageType: 'png' | 'jpg'  // image / signature
   * }]
   */
  async function applyAnnotations(PDFLib, bytes, items) {
    if (!Array.isArray(items) || items.length === 0) {
      throw new PdfToolError('no-annotations', 'Add text, a signature or a shape before saving.');
    }
    const document = await loadPdf(PDFLib, bytes);
    const pages = document.getPages();
    let font = null;
    const images = new Map();

    for (const item of items) {
      const page = pages[item.page];
      if (!page) {
        throw new PdfToolError('range-out-of-bounds', 'An annotation refers to a page that does not exist.');
      }
      const rotation = page.getRotation().angle;
      const box = visibleBox(page);
      const rect = {
        left: Number(item.left) || 0,
        top: Number(item.top) || 0,
        width: Math.max(1, Number(item.width) || 1),
        height: Math.max(1, Number(item.height) || 1)
      };
      const rgb = hexToRgb(item.color || '#000000');
      const colour = PDFLib.rgb(rgb.r, rgb.g, rgb.b);

      if (item.type === 'text') {
        font = font || await document.embedFont(PDFLib.StandardFonts.Helvetica);
        const fontSize = clamp(item.fontSize, 4, 200, 14);
        const lineHeight = fontSize * 1.2;
        // The editor passes the first baseline it measured on screen so the saved text lands
        // exactly where it was shown; otherwise use the font's ascent.
        const firstBaseline = Number.isFinite(item.baseline) && item.baseline > 0
          ? item.baseline
          : font.heightAtSize(fontSize, { descender: false });
        const lines = String(item.text || '').replace(/\r\n?/g, '\n').split('\n');
        lines.forEach((line, lineIndex) => {
          if (!line) {
            return;
          }
          ensureEncodable(font, line, `on page ${item.page + 1}`);
          const anchor = placeText(box, rotation, rect.left, rect.top + firstBaseline + lineIndex * lineHeight, 0);
          page.drawText(line, {
            x: anchor.x,
            y: anchor.y,
            size: fontSize,
            font,
            color: colour,
            rotate: PDFLib.degrees(anchor.rotate)
          });
        });
        continue;
      }

      const anchor = placeRect(box, rotation, rect);

      if (item.type === 'image') {
        if (!item.imageBytes) {
          continue;
        }
        let embedded = images.get(item.imageBytes);
        if (!embedded) {
          embedded = item.imageType === 'jpg'
            ? await document.embedJpg(item.imageBytes)
            : await document.embedPng(item.imageBytes);
          images.set(item.imageBytes, embedded);
        }
        page.drawImage(embedded, {
          x: anchor.x,
          y: anchor.y,
          width: rect.width,
          height: rect.height,
          rotate: PDFLib.degrees(anchor.rotate),
          opacity: clamp(item.opacity, 0, 1, 1)
        });
        continue;
      }

      const shape = {
        x: anchor.x,
        y: anchor.y,
        width: rect.width,
        height: rect.height,
        rotate: PDFLib.degrees(anchor.rotate)
      };
      if (item.type === 'whiteout') {
        page.drawRectangle({ ...shape, color: PDFLib.rgb(1, 1, 1), opacity: 1 });
      } else if (item.type === 'highlight') {
        // Multiply keeps the text under a highlight dark, as a real highlighter would.
        page.drawRectangle({
          ...shape,
          color: colour,
          opacity: clamp(item.opacity, 0.05, 1, 0.35),
          blendMode: PDFLib.BlendMode.Multiply
        });
      } else if (item.type === 'rect') {
        page.drawRectangle({ ...shape, borderColor: colour, borderWidth: 2, borderOpacity: clamp(item.opacity, 0, 1, 1) });
      }
    }
    return save(document);
  }

  // ---------------------------------------------------------------------------
  // Reduce size (docs/ux/pdf-tools-reduce-size.md)
  //
  // The file is changed in place: identical streams are stored once, images are re-saved at the
  // resolution they are shown at, plain streams are compressed, and page thumbnails and objects
  // nothing refers to are left out. Re-encoding pixels needs a canvas, so it is passed in
  // (`encodeImage`); everything else is here and runs in Node for tests.
  // ---------------------------------------------------------------------------

  // Starting values, tuned on real files before they are relied on (see the plan).
  const PDF_SIZE_PRESETS = Object.freeze([
    Object.freeze({ key: 'screen', label: 'Screen', note: 'For viewing and email', dpi: 100, quality: 0.6 }),
    Object.freeze({ key: 'standard', label: 'Standard', note: 'Good for most forms and portals', dpi: 150, quality: 0.75 }),
    Object.freeze({ key: 'print', label: 'Print', note: 'Keeps detail for printing', dpi: 220, quality: 0.85 })
  ]);

  // An image smaller than this is not worth the risk of re-saving.
  const REDUCE_MIN_IMAGE_BYTES = 10 * 1024;
  // A re-saved image is kept only when it is clearly smaller than the one it replaces.
  const REDUCE_KEEP_RATIO = 0.9;
  // An image already close to the preset's resolution keeps its pixel size.
  const REDUCE_MIN_SCALE = 0.9;

  // Why an image was left as it is; the screen counts these.
  const REDUCE_SKIP = Object.freeze({
    mask: 'mask',           // an image mask or soft mask (transparency)
    blackWhite: 'bw',       // JBIG2 or CCITT black-and-white scan formats
    printColour: 'colour',  // CMYK, ICC-based or separation colour (print files)
    small: 'small',         // under REDUCE_MIN_IMAGE_BYTES
    other: 'other',         // a format the browser cannot re-save safely
    notSmaller: 'kept'      // re-saving did not make it clearly smaller
  });

  function pdfMatrixMultiply(m1, m2) {
    return [
      m1[0] * m2[0] + m1[1] * m2[2],
      m1[0] * m2[1] + m1[1] * m2[3],
      m1[2] * m2[0] + m1[3] * m2[2],
      m1[2] * m2[1] + m1[3] * m2[3],
      m1[4] * m2[0] + m1[5] * m2[2] + m2[4],
      m1[4] * m2[1] + m1[5] * m2[3] + m2[5]
    ];
  }

  const isWhiteByte = (c) => c === 0 || c === 9 || c === 10 || c === 12 || c === 13 || c === 32;
  const isDelimiterByte = (c) => c === 40 || c === 41 || c === 60 || c === 62 || c === 91 || c === 93
    || c === 123 || c === 125 || c === 47 || c === 37;

  /**
   * True when the bytes after a possible inline-image EI look like content again (printable
   * text and white space), not more binary image data, as pdf.js also checks.
   */
  function followedByText(bytes, from) {
    const end = Math.min(bytes.length, from + 16);
    for (let at = from; at < end; at += 1) {
      const c = bytes[at];
      if (!isWhiteByte(c) && (c < 32 || c > 126)) return false;
    }
    return true;
  }

  /**
   * Reads the operators of a content stream and calls `onOperator(name, operands)` for each.
   * Operands are numbers, names ("/Im1") or null for anything else (strings, arrays, dictionaries).
   * Inline images (BI ... ID ... EI) are skipped whole. Only what measuring images needs is kept.
   */
  function contentOperators(bytes, onOperator) {
    const length = bytes.length;
    let operands = [];
    let nest = 0;
    let i = 0;
    const push = (value) => { if (nest === 0) operands.push(value); };
    const readWord = () => {
      const start = i;
      while (i < length && !isWhiteByte(bytes[i]) && !isDelimiterByte(bytes[i])) i += 1;
      return String.fromCharCode(...bytes.subarray(start, i));
    };
    while (i < length) {
      const c = bytes[i];
      if (isWhiteByte(c)) { i += 1; continue; }
      if (c === 37) { // % comment
        while (i < length && bytes[i] !== 10 && bytes[i] !== 13) i += 1;
        continue;
      }
      if (c === 40) { // ( string )
        let depth = 1;
        i += 1;
        while (i < length && depth > 0) {
          if (bytes[i] === 92) { i += 2; continue; }
          if (bytes[i] === 40) depth += 1;
          else if (bytes[i] === 41) depth -= 1;
          i += 1;
        }
        push(null);
        continue;
      }
      if (c === 60) { // << or <hex>
        if (bytes[i + 1] === 60) { nest += 1; i += 2; continue; }
        while (i < length && bytes[i] !== 62) i += 1;
        i += 1;
        push(null);
        continue;
      }
      if (c === 62) { // >>
        i += bytes[i + 1] === 62 ? 2 : 1;
        nest = Math.max(0, nest - 1);
        push(null);
        continue;
      }
      if (c === 91) { nest += 1; i += 1; continue; }
      if (c === 93) { nest = Math.max(0, nest - 1); i += 1; push(null); continue; }
      if (c === 123 || c === 125) { i += 1; continue; }
      if (c === 47) { // /Name
        i += 1;
        const raw = readWord();
        push(`/${raw.replace(/#([0-9A-Fa-f]{2})/g, (_, hex) => String.fromCharCode(parseInt(hex, 16)))}`);
        continue;
      }
      if ((c >= 48 && c <= 57) || c === 43 || c === 45 || c === 46) {
        const word = readWord();
        const number = Number(word);
        push(Number.isFinite(number) ? number : null);
        continue;
      }
      const word = readWord();
      if (!word) { i += 1; continue; }
      if (nest > 0) continue;
      if (word === 'BI') {
        // Skip the inline image: its dictionary, then the data from ID to the EI that ends it.
        const dictStart = i;
        while (i < length) {
          const at = i;
          if (bytes[at] === 73 && bytes[at + 1] === 68 && isWhiteByte(bytes[at - 1]) && (at + 2 >= length || isWhiteByte(bytes[at + 2]))) {
            i = at + 3;
            break;
          }
          i += 1;
        }
        // The data can hold the bytes "EI" itself. When the dictionary gives its length (/L or
        // /Length), start looking for EI after the data.
        const header = String.fromCharCode(...bytes.subarray(dictStart, Math.max(dictStart, i - 3)));
        const stated = /\/L(?:ength)?\s+(\d+)(?![\d.])/.exec(header);
        if (stated) i = Math.min(length, i + Number(stated[1]));
        while (i < length) {
          if (bytes[i] === 69 && bytes[i + 1] === 73 && isWhiteByte(bytes[i - 1])
              && (i + 2 >= length || isWhiteByte(bytes[i + 2]) || isDelimiterByte(bytes[i + 2]))
              && followedByText(bytes, i + 2)) {
            i += 2;
            break;
          }
          i += 1;
        }
        operands = [];
        continue;
      }
      if (word === 'true' || word === 'false' || word === 'null') { push(null); continue; }
      onOperator(word, operands);
      operands = [];
    }
  }

  /** The decoded bytes of a stream, or null when pdf-lib cannot decode it. */
  function streamBytes(PDFLib, stream) {
    try {
      if (stream instanceof PDFLib.PDFRawStream) return PDFLib.decodePDFRawStream(stream).decode();
      if (typeof stream.getUnencodedContents === 'function') return stream.getUnencodedContents();
    } catch {
      // Not decodable here; the caller treats its images as unmeasured.
    }
    return null;
  }

  /**
   * How large each image is drawn, in points, at its largest use: Map of "n g R" to
   * { width, height } along the image's own sides. Images drawn where this cannot be measured
   * (an annotation's appearance, a tiling pattern, a Type 3 font's glyphs, a soft-mask group, a
   * page that cannot be read) are in `unmeasured`, whatever their other uses.
   */
  function measureImageUses(PDFLib, document) {
    const { PDFName, PDFDict, PDFArray, PDFRef, PDFNumber, PDFStream } = PDFLib;
    const context = document.context;
    const uses = new Map();
    const unmeasured = new Set();
    const XOBJECT = PDFName.of('XObject');
    const SUBTYPE = PDFName.of('Subtype');
    const number = (value) => (value instanceof PDFNumber ? value.asNumber() : Number(value));

    const xobjects = (resources) => {
      const dict = resources instanceof PDFDict ? resources.lookupMaybe(XOBJECT, PDFDict) : undefined;
      return dict ? dict.entries() : [];
    };
    // contentOperators decodes #xx in names ("/Big#20Pic" is "/Big Pic"), while String(PDFName)
    // keeps them escaped; key the resources by the decoded name so the two meet.
    const xobjectNames = (resources) => new Map(xobjects(resources).map(([key, ref]) => [`/${key.decodeText()}`, ref]));
    // Past the nesting limit nothing more is followed, but the images named here are still
    // treated as unmeasured rather than forgotten.
    const markHere = (resources) => {
      for (const [, ref] of xobjects(resources)) {
        if (ref instanceof PDFRef && subtypeOf(ref) === '/Image') unmeasured.add(String(ref));
      }
    };
    const subtypeOf = (ref) => {
      const object = ref instanceof PDFRef ? context.lookup(ref) : ref;
      return object instanceof PDFStream ? String(object.dict.get(SUBTYPE)) : '';
    };

    const lookupDict = (dict, key) => (dict instanceof PDFDict ? dict.lookupMaybe(PDFName.of(key), PDFDict) : undefined);
    const resolve = (value) => (value instanceof PDFRef ? context.lookup(value) : value);

    /**
     * Images can also be drawn by a tiling pattern, a Type 3 font's glyphs or a soft-mask group;
     * how large depends on where those are painted, which is not followed here. Every image they
     * draw is unmeasured, so it keeps its pixel size even when a direct use is small. Their
     * content is read for the images it draws, resolved in their own resources or, when they have
     * none, in the enclosing ones (as viewers do). `seen` holds what was visited, so resources that
     * lead back to themselves end.
     */
    // What was read, keyed by the object and the resources it was read in: one glyph, pattern or
    // form can draw a different image in each place it is used (two Type 3 fonts sharing a
    // glyph, a form without resources of its own), so it is read again in each. Each pair once,
    // so resources that lead back to themselves end.
    const once = (seen, object, resources) => {
      let contexts = seen.get(object);
      if (!contexts) {
        contexts = new Set();
        seen.set(object, contexts);
      }
      if (contexts.has(resources)) return false;
      contexts.add(resources);
      return true;
    };

    const markIndirect = (resources, depth, seen) => {
      if (depth > 12 || !(resources instanceof PDFDict) || !once(seen, resources, null)) return;
      const own = (holder) => {
        const value = holder instanceof PDFStream ? holder.dict.lookup(PDFName.of('Resources')) : holder.lookup(PDFName.of('Resources'));
        return value instanceof PDFDict ? value : resources;
      };
      const patterns = lookupDict(resources, 'Pattern');
      for (const value of patterns ? patterns.values() : []) {
        const pattern = resolve(value);
        if (pattern instanceof PDFStream) markDrawnBy([pattern], own(pattern), depth + 1, seen);
      }
      const fonts = lookupDict(resources, 'Font');
      for (const value of fonts ? fonts.values() : []) {
        const font = resolve(value);
        if (!(font instanceof PDFDict) || String(font.get(PDFName.of('Subtype'))) !== '/Type3') continue;
        const procs = lookupDict(font, 'CharProcs');
        const glyphs = procs ? procs.values().map(resolve).filter((item) => item instanceof PDFStream) : [];
        markDrawnBy(glyphs, own(font), depth + 1, seen);
      }
      const states = lookupDict(resources, 'ExtGState');
      for (const value of states ? states.values() : []) {
        const state = resolve(value);
        const mask = state instanceof PDFDict ? resolve(state.get(PDFName.of('SMask'))) : undefined;
        const group = mask instanceof PDFDict ? resolve(mask.get(PDFName.of('G'))) : undefined;
        if (group instanceof PDFStream) markDrawnBy([group], own(group), depth + 1, seen);
      }
    };

    /** A form and everything it can draw is unmeasured. */
    const markForm = (ref, resources, depth, seen) => {
      const form = context.lookup(ref);
      if (!(form instanceof PDFStream)) return;
      const own = form.dict.lookup(PDFName.of('Resources'));
      const used = own instanceof PDFDict ? own : resources;
      if (!once(seen, form, used)) return;
      markAll(used, depth + 1, seen);
    };

    /** The images these content streams draw (by name, in `resources`) are unmeasured. */
    const markDrawnBy = (streams, resources, depth, seen) => {
      if (depth > 12) { markHere(resources); return; }
      markIndirect(resources, depth, seen);
      const names = xobjectNames(resources);
      for (const stream of streams) {
        if (!once(seen, stream, resources)) continue;
        const bytes = streamBytes(PDFLib, stream);
        if (!bytes) {
          // Cannot be read: anything it could reach is treated as drawn.
          markAll(resources, depth + 1, seen);
          continue;
        }
        contentOperators(bytes, (operator, operands) => {
          if (operator !== 'Do' || typeof operands[operands.length - 1] !== 'string') return;
          const ref = names.get(operands[operands.length - 1]);
          if (!(ref instanceof PDFRef)) return;
          const subtype = subtypeOf(ref);
          if (subtype === '/Image') unmeasured.add(String(ref));
          else if (subtype === '/Form') markForm(ref, resources, depth + 1, seen);
        });
      }
    };

    /** Every image under these resources (forms, patterns, Type 3 fonts and masks followed) is unmeasured. */
    const markAll = (resources, depth, seen) => {
      if (depth > 12) { markHere(resources); return; }
      markIndirect(resources, depth, seen);
      for (const [, ref] of xobjects(resources)) {
        if (!(ref instanceof PDFRef)) continue;
        const subtype = subtypeOf(ref);
        if (subtype === '/Image') unmeasured.add(String(ref));
        else if (subtype === '/Form') markForm(ref, resources, depth + 1, seen);
      }
    };

    const run = (contents, resources, ctm, scale, depth, stack) => {
      if (depth > 12) {
        // Too deep to measure: everything it can draw keeps its pixel size.
        markAll(resources, 0, new Map());
        return;
      }
      markIndirect(resources, depth, new Map());
      const bytes = contents.map((stream) => streamBytes(PDFLib, stream));
      if (bytes.some((part) => !part)) {
        markAll(resources, depth, new Map());
        return;
      }
      const joined = new Uint8Array(bytes.reduce((sum, part) => sum + part.length + 1, 0));
      let offset = 0;
      for (const part of bytes) {
        joined.set(part, offset);
        joined[offset + part.length] = 10;
        offset += part.length + 1;
      }
      const names = xobjectNames(resources);
      const states = [];
      let current = ctm;
      contentOperators(joined, (operator, operands) => {
        if (operator === 'q') {
          states.push(current);
        } else if (operator === 'Q') {
          current = states.pop() || ctm;
        } else if (operator === 'cm' && operands.length >= 6) {
          const m = operands.slice(-6);
          if (m.every((value) => typeof value === 'number')) current = pdfMatrixMultiply(m, current);
        } else if (operator === 'Do' && typeof operands[operands.length - 1] === 'string') {
          const ref = names.get(operands[operands.length - 1]);
          if (!(ref instanceof PDFRef)) return;
          const key = String(ref);
          const subtype = subtypeOf(ref);
          if (subtype === '/Image') {
            const width = Math.hypot(current[0], current[1]) * scale;
            const height = Math.hypot(current[2], current[3]) * scale;
            if (!(width > 0) || !(height > 0)) {
              unmeasured.add(key);
              return;
            }
            const before = uses.get(key);
            const larger = !before || width * height > before.width * before.height;
            uses.set(key, {
              width: Math.max(width, before ? before.width : 0),
              height: Math.max(height, before ? before.height : 0),
              // The page of the largest use, where a before-and-after preview shows it best.
              page: larger ? currentPage : before.page
            });
          } else if (subtype === '/Form' && !stack.has(key)) {
            const form = context.lookup(ref);
            const matrix = form.dict.lookupMaybe(PDFName.of('Matrix'), PDFArray);
            const values = matrix ? matrix.asArray().map(number) : [1, 0, 0, 1, 0, 0];
            const inner = values.length === 6 && values.every(Number.isFinite) ? pdfMatrixMultiply(values, current) : current;
            const innerResources = form.dict.lookup(PDFName.of('Resources')) || resources;
            stack.add(key);
            run([form], innerResources, inner, scale, depth + 1, stack);
            stack.delete(key);
          }
        }
      });
    };

    let currentPage = 0;
    for (const [pageIndex, page] of document.getPages().entries()) {
      currentPage = pageIndex;
      const node = page.node;
      const resources = node.Resources();
      const userUnit = node.lookupMaybe(PDFName.of('UserUnit'), PDFNumber);
      const scale = userUnit ? userUnit.asNumber() : 1;
      const contents = node.Contents();
      const streams = contents instanceof PDFArray
        ? contents.asArray().map((ref) => context.lookup(ref)).filter((item) => item instanceof PDFStream)
        : contents instanceof PDFStream ? [contents] : [];
      run(streams, resources, [1, 0, 0, 1, 0, 0], scale, 0, new Set());

      // Annotation appearances are drawn into the annotation's box at a size not measured here.
      const annots = node.Annots();
      for (const ref of annots ? annots.asArray() : []) {
        const annot = ref instanceof PDFRef ? context.lookup(ref) : ref;
        const appearance = annot instanceof PDFDict ? annot.lookupMaybe(PDFName.of('AP'), PDFDict) : undefined;
        if (!appearance) continue;
        for (const [, value] of appearance.entries()) {
          const target = value instanceof PDFRef ? context.lookup(value) : value;
          const forms = target instanceof PDFStream ? [target]
            : target instanceof PDFDict ? target.values().map((item) => (item instanceof PDFRef ? context.lookup(item) : item)).filter((item) => item instanceof PDFStream)
              : [];
          for (const form of forms) markAll(form.dict.lookup(PDFName.of('Resources')), 0, new Map());
        }
      }
    }
    return { uses, unmeasured };
  }

  /**
   * The pixel size an image is re-saved at: enough for `dpi` at its largest use, never larger
   * than it is. Unchanged when it cannot be measured or is already close to that resolution.
   */
  function reduceTarget(pixelWidth, pixelHeight, use, dpi) {
    if (!use || !(use.width > 0) || !(use.height > 0)) {
      return { width: pixelWidth, height: pixelHeight, scaled: false };
    }
    const wantWidth = (use.width / 72) * dpi;
    const wantHeight = (use.height / 72) * dpi;
    const scale = Math.min(1, Math.max(wantWidth / pixelWidth, wantHeight / pixelHeight));
    if (scale > REDUCE_MIN_SCALE) return { width: pixelWidth, height: pixelHeight, scaled: false };
    return {
      width: Math.max(1, Math.round(pixelWidth * scale)),
      height: Math.max(1, Math.round(pixelHeight * scale)),
      scaled: true
    };
  }

  /** Undoes the PNG predictors (Predictor 10 to 15) a FlateDecode image may use. Null if malformed. */
  function undoPngPredictor(data, colors, bitsPerComponent, columns) {
    const bytesPerPixel = Math.max(1, Math.ceil((colors * bitsPerComponent) / 8));
    const rowLength = Math.ceil((colors * bitsPerComponent * columns) / 8);
    const rows = Math.floor(data.length / (rowLength + 1));
    const output = new Uint8Array(rows * rowLength);
    let previous = new Uint8Array(rowLength);
    for (let row = 0; row < rows; row += 1) {
      const start = row * (rowLength + 1);
      const type = data[start];
      const input = data.subarray(start + 1, start + 1 + rowLength);
      const current = output.subarray(row * rowLength, (row + 1) * rowLength);
      for (let i = 0; i < rowLength; i += 1) {
        const left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
        const up = previous[i];
        const upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
        let value;
        if (type === 0) value = input[i];
        else if (type === 1) value = input[i] + left;
        else if (type === 2) value = input[i] + up;
        else if (type === 3) value = input[i] + ((left + up) >> 1);
        else if (type === 4) {
          const p = left + up - upLeft;
          const pa = Math.abs(p - left);
          const pb = Math.abs(p - up);
          const pc = Math.abs(p - upLeft);
          value = input[i] + (pa <= pb && pa <= pc ? left : pb <= pc ? up : upLeft);
        } else {
          return null;
        }
        current[i] = value & 0xff;
      }
      previous = current;
    }
    return output;
  }

  /** The filters of a stream, as names without the slash. */
  function streamFilters(PDFLib, dict) {
    const { PDFName, PDFArray } = PDFLib;
    const filter = dict.lookup(PDFName.of('Filter'));
    if (!filter) return [];
    if (filter instanceof PDFArray) return filter.asArray().map((item) => String(item).replace(/^\//, ''));
    return [String(filter).replace(/^\//, '')];
  }

  /**
   * Every image XObject of the document, with what can be done with it. `action` is 'resave'
   * (with a target size) or 'skip' (with one of REDUCE_SKIP).
   */
  function reduceImagePlan(PDFLib, document, measured, preset) {
    const { PDFName, PDFRawStream, PDFRef, PDFNumber, PDFArray, PDFDict, PDFBool } = PDFLib;
    const context = document.context;
    const get = (dict, key) => dict.lookup(PDFName.of(key));
    const masks = new Set();
    const images = [];
    for (const [ref, object] of context.enumerateIndirectObjects()) {
      if (!(object instanceof PDFRawStream) || String(object.dict.get(PDFName.of('Subtype'))) !== '/Image') continue;
      images.push({ ref, stream: object });
      for (const key of ['SMask', 'Mask']) {
        const value = object.dict.get(PDFName.of(key));
        if (value instanceof PDFRef) masks.add(String(value));
      }
    }

    return images.map(({ ref, stream }) => {
      const dict = stream.dict;
      const key = String(ref);
      const size = stream.contents.length;
      const width = get(dict, 'Width') instanceof PDFNumber ? get(dict, 'Width').asNumber() : 0;
      const height = get(dict, 'Height') instanceof PDFNumber ? get(dict, 'Height').asNumber() : 0;
      const plan = { ref, stream, width, height, size };
      const skip = (reason) => ({ ...plan, action: 'skip', reason });

      const imageMask = get(dict, 'ImageMask');
      if (masks.has(key) || (imageMask instanceof PDFBool && imageMask.asBoolean())) return skip(REDUCE_SKIP.mask);
      const filters = streamFilters(PDFLib, dict);
      if (filters.some((name) => name === 'JBIG2Decode' || name === 'CCITTFaxDecode')) return skip(REDUCE_SKIP.blackWhite);
      const colourSpace = get(dict, 'ColorSpace');
      const space = colourSpace instanceof PDFArray ? String(colourSpace.get(0)) : String(colourSpace || '');
      if (['/DeviceCMYK', '/ICCBased', '/Separation', '/DeviceN', '/CalRGB', '/CalGray', '/Lab'].includes(space)) {
        return skip(REDUCE_SKIP.printColour);
      }
      if (space !== '/DeviceRGB' && space !== '/DeviceGray') return skip(REDUCE_SKIP.other);
      const bits = get(dict, 'BitsPerComponent');
      if (!(bits instanceof PDFNumber) || bits.asNumber() !== 8) return skip(REDUCE_SKIP.other);
      // A Decode array or a colour-key mask changes what the pixels mean; a lossy copy would break it.
      if (get(dict, 'Decode') || get(dict, 'Mask')) return skip(REDUCE_SKIP.other);
      if (!(width > 0) || !(height > 0)) return skip(REDUCE_SKIP.other);
      if (size < REDUCE_MIN_IMAGE_BYTES) return skip(REDUCE_SKIP.small);

      let source;
      if (filters.length === 1 && filters[0] === 'DCTDecode') {
        // /ColorTransform says how a viewer turns the JPG's channels into colours; a browser
        // decides that from the JPG alone, so the copy could change colour.
        const parms = get(dict, 'DecodeParms');
        if (parms instanceof PDFDict && parms.lookup(PDFName.of('ColorTransform'))) return skip(REDUCE_SKIP.other);
        source = 'jpeg';
      } else if (filters.every((name) => ['FlateDecode', 'LZWDecode', 'ASCIIHexDecode', 'ASCII85Decode', 'RunLengthDecode'].includes(name))) {
        const parms = get(dict, 'DecodeParms');
        const last = parms instanceof PDFArray ? parms.lookup(parms.size() - 1) : parms;
        const predictor = last instanceof PDFDict && last.lookup(PDFName.of('Predictor')) instanceof PDFNumber
          ? last.lookup(PDFName.of('Predictor')).asNumber() : 1;
        if (predictor !== 1 && predictor < 10) return skip(REDUCE_SKIP.other);
        source = 'pixels';
      } else {
        return skip(REDUCE_SKIP.other);
      }

      // A soft mask with /Matte holds colours blended for this image's exact pixels; it must keep
      // the image's size, so the image keeps its size too (it may still be re-saved).
      const softMask = get(dict, 'SMask');
      const matte = softMask instanceof PDFRawStream && softMask.dict.lookup(PDFName.of('Matte'));
      const use = matte || measured.unmeasured.has(key) ? null : measured.uses.get(key) || null;
      const target = reduceTarget(width, height, use, preset.dpi);
      return { ...plan, action: 'resave', source, grey: space === '/DeviceGray', use, target };
    });
  }

  /** The pixels of a 'pixels' image: { data, channels } with 8 bits per channel, or null. */
  function decodeImagePixels(PDFLib, image) {
    const { PDFName, PDFArray, PDFDict, PDFNumber } = PDFLib;
    const dict = image.stream.dict;
    let data = streamFilters(PDFLib, dict).length ? streamBytes(PDFLib, image.stream) : image.stream.contents;
    if (!data) return null;
    const parms = dict.lookup(PDFName.of('DecodeParms'));
    const last = parms instanceof PDFArray ? parms.lookup(parms.size() - 1) : parms;
    if (last instanceof PDFDict) {
      const read = (key, fallback) => (last.lookup(PDFName.of(key)) instanceof PDFNumber ? last.lookup(PDFName.of(key)).asNumber() : fallback);
      if (read('Predictor', 1) >= 10) {
        data = undoPngPredictor(data, read('Colors', 1), read('BitsPerComponent', 8), read('Columns', 1));
        if (!data) return null;
      }
    }
    const channels = image.grey ? 1 : 3;
    const expected = image.width * image.height * channels;
    if (data.length < expected) return null;
    return { data: data.subarray(0, expected), channels };
  }

  /** True when the document carries a digital signature, which any change would break. */
  function isSignedPdf(PDFLib, document) {
    const { PDFName, PDFDict, PDFArray, PDFRef } = PDFLib;
    const catalog = document.catalog;
    if (catalog.lookupMaybe(PDFName.of('Perms'), PDFDict)) return true;
    const form = catalog.lookupMaybe(PDFName.of('AcroForm'), PDFDict);
    const fields = form ? form.lookupMaybe(PDFName.of('Fields'), PDFArray) : undefined;
    if (!fields) return false;
    const seen = new Set();
    const visit = (value, inheritedType) => {
      const key = value instanceof PDFRef ? String(value) : null;
      if (key) {
        if (seen.has(key)) return false;
        seen.add(key);
      }
      const node = value instanceof PDFRef ? document.context.lookup(value) : value;
      if (!(node instanceof PDFDict)) return false;
      const type = node.get(PDFName.of('FT')) ? String(node.get(PDFName.of('FT'))) : inheritedType;
      if (type === '/Sig' && node.get(PDFName.of('V'))) return true;
      const kids = node.lookupMaybe(PDFName.of('Kids'), PDFArray);
      return kids ? kids.asArray().some((kid) => visit(kid, type)) : false;
    };
    return fields.asArray().some((field) => visit(field, undefined));
  }

  /** True when the document says it is PDF/A in its metadata. */
  function claimsPdfA(PDFLib, document) {
    const { PDFName, PDFStream } = PDFLib;
    const metadata = document.catalog.lookup(PDFName.of('Metadata'));
    if (!(metadata instanceof PDFStream)) return false;
    const bytes = streamBytes(PDFLib, metadata);
    return Boolean(bytes) && /pdfaid:part/.test(String.fromCharCode(...bytes.subarray(0, 65536)));
  }

  /**
   * Stores identical streams, fonts, font descriptors and graphics states once, pointing every
   * reference at one copy. Pages and other objects are never merged. Repeated a few times, so
   * fonts become equal once their font files have been merged. Returns how many were merged.
   */
  function deduplicateObjects(PDFLib, document) {
    const { PDFName, PDFRawStream, PDFDict, PDFArray, PDFRef } = PDFLib;
    const context = document.context;
    const MERGEABLE_DICTS = new Set(['/Font', '/FontDescriptor', '/ExtGState']);
    const SKIP_STREAMS = new Set(['/XRef', '/ObjStm', '/Metadata']);
    let merged = 0;
    for (let round = 0; round < 4; round += 1) {
      const first = new Map();
      const replace = new Map();
      for (const [ref, object] of context.enumerateIndirectObjects()) {
        let key = null;
        if (object instanceof PDFRawStream) {
          if (SKIP_STREAMS.has(String(object.dict.get(PDFName.of('Type'))))) continue;
          key = `s${object.dict.toString()}|${object.contents.length}|${crc32(object.contents)}`;
        } else if (object instanceof PDFDict && MERGEABLE_DICTS.has(String(object.get(PDFName.of('Type'))))) {
          key = `d${object.toString()}`;
        }
        if (!key) continue;
        const earlier = first.get(key);
        if (!earlier) {
          first.set(key, ref);
          continue;
        }
        // The same bytes, not only the same checksum.
        if (object instanceof PDFRawStream) {
          const other = context.lookup(earlier).contents;
          if (other.length !== object.contents.length || other.some((byte, at) => byte !== object.contents[at])) continue;
        }
        replace.set(String(ref), earlier);
      }
      if (replace.size === 0) break;
      const rewrite = (value) => {
        if (value instanceof PDFDict) {
          for (const [key, entry] of value.entries()) {
            const to = entry instanceof PDFRef ? replace.get(String(entry)) : undefined;
            if (to) value.set(key, to);
            else if (entry instanceof PDFDict || entry instanceof PDFArray) rewrite(entry);
          }
        } else if (value instanceof PDFArray) {
          value.asArray().forEach((entry, index) => {
            const to = entry instanceof PDFRef ? replace.get(String(entry)) : undefined;
            if (to) value.set(index, to);
            else if (entry instanceof PDFDict || entry instanceof PDFArray) rewrite(entry);
          });
        } else if (value instanceof PDFRawStream) {
          rewrite(value.dict);
        }
      };
      for (const [, object] of context.enumerateIndirectObjects()) rewrite(object);
      for (const key of replace.keys()) {
        const [objectNumber, generation] = key.split(' ').map(Number);
        context.delete(PDFRef.of(objectNumber, generation));
      }
      merged += replace.size;
    }
    return merged;
  }

  /** Compresses streams stored without compression (lossless). Returns how many were compressed. */
  function compressPlainStreams(PDFLib, document) {
    const { PDFName, PDFRawStream } = PDFLib;
    const context = document.context;
    let compressed = 0;
    for (const [ref, object] of context.enumerateIndirectObjects()) {
      if (!(object instanceof PDFRawStream) || object.dict.get(PDFName.of('Filter'))) continue;
      // Without a filter, /DecodeParms means nothing today; with Flate it would apply.
      if (object.dict.get(PDFName.of('DecodeParms'))) continue;
      // Metadata stays readable as text, as PDF/A asks.
      if (['/Metadata', '/XRef', '/ObjStm'].includes(String(object.dict.get(PDFName.of('Type'))))) continue;
      if (object.contents.length < 256) continue;
      const smaller = context.flateStream(object.contents, {});
      if (smaller.getContentsSize() >= object.contents.length * 0.95) continue;
      for (const [key, value] of object.dict.entries()) {
        if (String(key) !== '/Length' && String(key) !== '/Filter') smaller.dict.set(key, value);
      }
      context.assign(ref, smaller);
      compressed += 1;
    }
    return compressed;
  }

  /** Leaves out page thumbnails stored in the file; viewers draw their own. */
  function dropPageThumbnails(PDFLib, document) {
    let dropped = 0;
    for (const page of document.getPages()) {
      if (page.node.get(PDFLib.PDFName.of('Thumb'))) {
        page.node.delete(PDFLib.PDFName.of('Thumb'));
        dropped += 1;
      }
    }
    return dropped;
  }

  /** What is in a PDF, for the screen before any work: pages, size and how much is images. */
  async function describePdf(PDFLib, bytes) {
    const document = await loadPdf(PDFLib, bytes);
    let imageBytes = 0;
    let imageCount = 0;
    for (const [, object] of document.context.enumerateIndirectObjects()) {
      if (object instanceof PDFLib.PDFRawStream && String(object.dict.get(PDFLib.PDFName.of('Subtype'))) === '/Image') {
        imageBytes += object.contents.length;
        imageCount += 1;
      }
    }
    return {
      pages: document.getPageCount(),
      bytes: bytes.length,
      imageBytes,
      imageCount,
      signed: isSignedPdf(PDFLib, document)
    };
  }

  const SIGNED_PDF_MESSAGE = 'This PDF is digitally signed. Making it smaller would break the signature, so it was not changed.';
  const CANCELLED_MESSAGE = 'Stopped. No file was made.';

  // A size target is reached by lowering the quality first and the resolution after, never below
  // this resolution or this quality.
  const REDUCE_MIN_DPI = 72;
  const REDUCE_LOWEST_QUALITY = 0.35;

  /**
   * "2" + "MB" → 2097152. Null when the value is not a positive number. A comma is a decimal point
   * ("1,5" MB), except in a number written with thousands ("1,000" KB), which is not 1 KB.
   */
  function parseSizeLimit(value, unit) {
    const text = String(value ?? '').trim();
    const plain = /^\d{1,3}(,\d{3})+$/.test(text) ? text.replace(/,/g, '') : text.replace(',', '.');
    const number = Number(plain);
    if (!plain || !Number.isFinite(number) || number <= 0) return null;
    const factor = String(unit).toUpperCase() === 'MB' ? 1024 * 1024 : 1024;
    return Math.round(number * factor);
  }

  /**
   * Finds the settings that keep the pictures within `budget` bytes, best-looking first.
   * `measure(dpi, quality)` re-saves every picture at those settings and resolves to an attempt
   * with its total `bytes`. The quality is searched first at the starting resolution; only when the
   * lowest quality is still too big is the resolution stepped down, never below `minDpi`.
   * Resolves to the chosen attempt with `reached`; when nothing fits, the smallest one tried.
   */
  async function searchReduceSettings({ dpi, quality, budget, measure, minDpi = REDUCE_MIN_DPI, lowestQuality = REDUCE_LOWEST_QUALITY }) {
    let smallest = null;
    const keep = (attempt) => {
      if (!smallest || attempt.bytes < smallest.bytes) smallest = attempt;
      return attempt;
    };
    let current = dpi;
    for (let round = 0; round < 8; round += 1) {
      const top = keep(await measure(current, quality));
      if (top.bytes <= budget) return { ...top, reached: true };
      const floor = keep(await measure(current, Math.min(quality, lowestQuality)));
      if (floor.bytes <= budget) {
        let found = floor;
        let low = Math.min(quality, lowestQuality);
        let high = quality;
        for (let step = 0; step < 4; step += 1) {
          const middle = (low + high) / 2;
          const attempt = keep(await measure(current, middle));
          if (attempt.bytes <= budget) {
            found = attempt;
            low = middle;
          } else {
            high = middle;
          }
        }
        return { ...found, reached: true };
      }
      if (current <= minDpi) break;
      // Picture size follows the pixel count, so aim a little under with the square root.
      const scale = budget > 0 ? Math.sqrt(budget / floor.bytes) * 0.95 : 0;
      current = Math.max(minDpi, Math.round(current * Math.min(0.9, Math.max(0.5, scale))));
    }
    return { ...smallest, reached: false };
  }

  /**
   * Makes a PDF smaller. options:
   *   preset       'screen' | 'standard' | 'print'
   *   targetBytes  optional size to keep the file under
   *   encodeImage  see below
   *   onProgress   ({ step, done, total }) for each picture re-saved; `step` counts the attempts
   *   signal       an AbortSignal; stops between pictures with a 'cancelled' error
   *
   * encodeImage({ source: 'jpeg', bytes } | { source: 'pixels', data, channels },
   *             width, height, targetWidth, targetHeight, quality, grey)
   *   → { jpeg?: Uint8Array (3-colour JPG at the target size), grey?: Uint8Array (8-bit grey
   *       pixels at the target size, for a grey image) } or null when it cannot be read.
   *
   * Returns { bytes, unchanged, report }. `unchanged` is true, and `bytes` the original, when the
   * result would not be smaller: a bigger file is never offered as a saving.
   */
  async function reducePdf(PDFLib, bytes, options) {
    const settings = options || {};
    const preset = PDF_SIZE_PRESETS.find((item) => item.key === settings.preset) || PDF_SIZE_PRESETS[1];
    const targetBytes = settings.targetBytes > 0 ? settings.targetBytes : null;
    const stopIfCancelled = () => {
      if (settings.signal && settings.signal.aborted) throw new PdfToolError('cancelled', CANCELLED_MESSAGE);
    };
    const document = await loadPdf(PDFLib, bytes);
    if (isSignedPdf(PDFLib, document)) throw new PdfToolError('signed', SIGNED_PDF_MESSAGE);
    const { PDFName, PDFRawStream } = PDFLib;
    const context = document.context;

    // 1. Lossless clean-up, before any picture is touched.
    const report = {
      preset: preset.key,
      merged: deduplicateObjects(PDFLib, document),
      thumbnails: dropPageThumbnails(PDFLib, document),
      compressed: compressPlainStreams(PDFLib, document),
      resaved: 0,
      resized: 0,
      skipped: {},
      pdfA: claimsPdfA(PDFLib, document),
      previewPage: null,
      target: null
    };
    removeUnreachableObjects(PDFLib, document);

    // 2. Which pictures can be re-saved. The same at every resolution; only their sizes change.
    const plan = reduceImagePlan(PDFLib, document, measureImageUses(PDFLib, document), preset);
    const fixedSkips = {};
    plan.filter((image) => image.action === 'skip').forEach((image) => {
      fixedSkips[image.reason] = (fixedSkips[image.reason] || 0) + 1;
    });
    const work = plan.filter((image) => image.action === 'resave');
    // Keys the old dictionary keeps; the new stream sets its own size, colour and filter.
    const REPLACED = new Set(['/Length', '/Filter', '/DecodeParms', '/Width', '/Height', '/ColorSpace', '/BitsPerComponent', '/DL']);
    let step = 0;

    // Each setting is re-saved once; the search may ask for it again.
    const attempts = new Map();

    /** Re-saves every picture at one resolution and quality. Nothing is put in the file yet. */
    const attempt = async (dpi, quality) => {
      const key = `${dpi}|${quality}`;
      if (attempts.has(key)) return attempts.get(key);
      const made = await resaveAll(dpi, quality);
      attempts.set(key, made);
      return made;
    };

    const resaveAll = async (dpi, quality) => {
      step += 1;
      const chosen = new Map();
      const skipped = {};
      let imageBytes = 0;
      let resized = 0;
      for (const [index, image] of work.entries()) {
        stopIfCancelled();
        settings.onProgress?.({ step, done: index + 1, total: work.length });
        const target = reduceTarget(image.width, image.height, image.use, dpi);
        let input = null;
        if (image.source === 'jpeg') {
          input = { source: 'jpeg', bytes: image.stream.contents };
        } else {
          const pixels = decodeImagePixels(PDFLib, image);
          if (pixels) input = { source: 'pixels', ...pixels };
        }
        let encoded = null;
        if (input) {
          try {
            encoded = await settings.encodeImage({
              ...input,
              width: image.width,
              height: image.height,
              targetWidth: target.width,
              targetHeight: target.height,
              quality,
              grey: image.grey
            });
          } catch {
            encoded = null;
          }
        }
        stopIfCancelled();
        if (!encoded) {
          skipped[REDUCE_SKIP.other] = (skipped[REDUCE_SKIP.other] || 0) + 1;
          imageBytes += image.size;
          continue;
        }
        const candidates = [];
        const base = (colourSpace) => {
          const dict = context.obj({});
          for (const [key, value] of image.stream.dict.entries()) {
            if (!REPLACED.has(String(key))) dict.set(key, value);
          }
          dict.set(PDFName.of('Width'), context.obj(target.width));
          dict.set(PDFName.of('Height'), context.obj(target.height));
          dict.set(PDFName.of('ColorSpace'), PDFName.of(colourSpace));
          dict.set(PDFName.of('BitsPerComponent'), context.obj(8));
          return dict;
        };
        if (encoded.jpeg && encoded.jpeg.length) {
          const dict = base('DeviceRGB');
          dict.set(PDFName.of('Filter'), PDFName.of('DCTDecode'));
          candidates.push(PDFRawStream.of(dict, encoded.jpeg));
        }
        if (image.grey && encoded.grey && encoded.grey.length === target.width * target.height) {
          const stream = context.flateStream(encoded.grey, {});
          for (const [key, value] of base('DeviceGray').entries()) stream.dict.set(key, value);
          candidates.push(stream);
        }
        candidates.sort((a, b) => a.getContentsSize() - b.getContentsSize());
        const best = candidates[0];
        if (!best || best.getContentsSize() >= image.size * REDUCE_KEEP_RATIO) {
          skipped[REDUCE_SKIP.notSmaller] = (skipped[REDUCE_SKIP.notSmaller] || 0) + 1;
          imageBytes += image.size;
          continue;
        }
        chosen.set(image, best);
        imageBytes += best.getContentsSize();
        if (target.scaled) resized += 1;
      }
      return { dpi, quality, bytes: imageBytes, chosen, skipped, resized };
    };

    /**
     * Puts one attempt's pictures in the file (and the others back as they were) and saves it.
     * Nothing is pruned here: an object only an original picture refers to (its /DecodeParms, say)
     * looks unused while a new picture stands in its place, but a later round decodes the original
     * again and needs it. Pruning comes once, after the search; it only makes the file smaller.
     */
    const saveWith = async (chosen) => {
      for (const image of work) context.assign(image.ref, chosen.get(image) || image.stream);
      return save(document);
    };

    // 3. Settings: the preset, or the best-looking ones that keep the file under the target.
    let result;
    let output;
    if (targetBytes && work.length) {
      // Everything but the pictures being re-saved stays as it is now, so the file's size is that
      // part plus the pictures; the pictures' share is the budget. That is an estimate: a new
      // picture's dictionary and the file's tables add a little, so a file the estimate says fits
      // can still come out over. Then the overshoot, and a little more, comes off the budget and
      // the search runs again (settings already tried are not re-saved again).
      const fixedBytes = (await save(document)).length - work.reduce((sum, image) => sum + image.size, 0);
      let budget = targetBytes - fixedBytes;
      for (let round = 0; round < 4; round += 1) {
        result = await searchReduceSettings({ dpi: preset.dpi, quality: preset.quality, budget, measure: attempt });
        stopIfCancelled();
        output = await saveWith(result.chosen);
        if (output.length <= targetBytes || !result.reached) break;
        budget -= output.length - targetBytes + Math.max(256, Math.round(targetBytes * 0.002));
      }
    } else {
      result = await attempt(preset.dpi, preset.quality);
      stopIfCancelled();
      output = await saveWith(result.chosen);
    }

    // The search is over: now objects nothing refers to can go, which only makes the file smaller.
    removeUnreachableObjects(PDFLib, document);
    output = await save(document);
    // Saving gives the page its turn, so Stop may have been pressed meanwhile: then there is no file.
    stopIfCancelled();

    // 4. What was done.
    report.resaved = result.chosen.size;
    report.resized = result.resized;
    report.dpi = result.dpi;
    report.quality = result.quality;
    report.skipped = { ...fixedSkips };
    for (const [reason, count] of Object.entries(result.skipped)) {
      report.skipped[reason] = (report.skipped[reason] || 0) + count;
    }
    // The page of the largest re-saved picture, for a before-and-after preview. A JPG with its own
    // orientation is left out: the browser's PDF preview turns it, so the two would not match.
    const largest = Array.from(result.chosen.keys())
      .filter((image) => image.use && Number.isInteger(image.use.page))
      .filter((image) => image.source !== 'jpeg' || jpegOrientation(image.stream.contents) === 1)
      .sort((a, b) => b.size - a.size)[0];
    report.previewPage = largest ? largest.use.page : null;

    if (targetBytes) {
      report.target = {
        bytes: targetBytes,
        reached: output.length <= targetBytes,
        dpi: result.dpi,
        quality: result.quality,
        // Missed with the lowest settings this tool uses, not for want of trying them.
        atLowest: result.dpi <= REDUCE_MIN_DPI
      };
    }
    if (output.length >= bytes.length) {
      return { bytes, unchanged: true, report };
    }
    return { bytes: output, unchanged: false, report };
  }

  async function pageCount(PDFLib, bytes) {
    const document = await loadPdf(PDFLib, bytes);
    return document.getPageCount();
  }

  return Object.freeze({
    PdfToolError,
    PAGE_SIZES,
    parsePageRanges,
    pageSetFromRanges,
    chunkPages,
    normalizeRotation,
    normalizeBox,
    effectiveBox,
    displaySize,
    displayToPdfPoint,
    placeRect,
    placeText,
    positionBlock,
    fitImage,
    jpegRotation,
    jpegOrientation,
    jpegMirrored,
    jpegClearOrientation,
    orientedImageDraw,
    formatStamp,
    hexToRgb,
    baseName,
    crc32,
    createZip,
    loadPdf,
    assemblePages,
    splitPdf,
    imagesToPdf,
    addWatermark,
    addTextStamps,
    applyAnnotations,
    pageCount,
    PDF_SIZE_PRESETS,
    REDUCE_SKIP,
    SIGNED_PDF_MESSAGE,
    REDUCE_MIN_DPI,
    REDUCE_LOWEST_QUALITY,
    parseSizeLimit,
    searchReduceSettings,
    contentOperators,
    measureImageUses,
    reduceTarget,
    undoPngPredictor,
    reduceImagePlan,
    isSignedPdf,
    deduplicateObjects,
    compressPlainStreams,
    describePdf,
    reducePdf
  });
});
