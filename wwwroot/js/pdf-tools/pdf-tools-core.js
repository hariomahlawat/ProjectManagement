// PRISM PDF Tools — core operations.
//
// Everything in this file runs in the user's browser (and in Node for tests). The PDF Tools
// module never uploads files to PRISM: users edit their own PDFs locally and download the
// result. pdf-lib is passed in explicitly so the operations stay testable without a DOM.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) {
    module.exports = api;
  } else {
    root.PrismPdfTools = api;
  }
})(typeof self !== 'undefined' ? self : this, function () {
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
    for (const rawToken of text.split(/[,;]/)) {
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

  function normalizeRotation(degrees) {
    const value = Math.round(Number(degrees) || 0);
    const quarter = Math.round(value / 90) * 90;
    return ((quarter % 360) + 360) % 360;
  }

  /**
   * The page area viewers show: the CropBox clipped to the MediaBox, falling back to the
   * MediaBox when they do not overlap. pdf.js renders exactly this box, so the editor preview
   * and the saved PDF must both map coordinates against it.
   */
  function effectiveBox(mediaBox, cropBox) {
    const x0 = Math.max(mediaBox.x, cropBox.x);
    const y0 = Math.max(mediaBox.y, cropBox.y);
    const x1 = Math.min(mediaBox.x + mediaBox.width, cropBox.x + cropBox.width);
    const y1 = Math.min(mediaBox.y + mediaBox.height, cropBox.y + cropBox.height);
    if (x1 <= x0 || y1 <= y0) {
      return { x: mediaBox.x, y: mediaBox.y, width: mediaBox.width, height: mediaBox.height };
    }
    return { x: x0, y: y0, width: x1 - x0, height: y1 - y0 };
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
   * their rotation component; returns 0 when there is no EXIF orientation.
   */
  function jpegRotation(bytes) {
    const data = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
    if (data.length < 4 || data[0] !== 0xff || data[1] !== 0xd8) {
      return 0;
    }
    let offset = 2;
    while (offset + 4 <= data.length) {
      if (data[offset] !== 0xff) {
        return 0;
      }
      const marker = data[offset + 1];
      if (marker === 0xda || marker === 0xd9) {
        return 0;
      }
      const length = (data[offset + 2] << 8) | data[offset + 3];
      if (length < 2) {
        return 0;
      }
      if (marker === 0xe1 && offset + 10 <= data.length &&
          data[offset + 4] === 0x45 && data[offset + 5] === 0x78 && data[offset + 6] === 0x69 &&
          data[offset + 7] === 0x66 && data[offset + 8] === 0 && data[offset + 9] === 0) {
        return orientationFromTiff(data, offset + 10, Math.min(data.length, offset + 2 + length));
      }
      offset += 2 + length;
    }
    return 0;
  }

  function orientationFromTiff(data, start, end) {
    if (start + 8 > end) {
      return 0;
    }
    const little = data[start] === 0x49 && data[start + 1] === 0x49;
    const big = data[start] === 0x4d && data[start + 1] === 0x4d;
    if (!little && !big) {
      return 0;
    }
    const u16 = (at) => (little ? data[at] | (data[at + 1] << 8) : (data[at] << 8) | data[at + 1]);
    const u32 = (at) => (little
      ? (data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24)) >>> 0
      : ((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]) >>> 0);
    const ifd = start + u32(start + 4);
    if (ifd + 2 > end) {
      return 0;
    }
    const count = u16(ifd);
    for (let i = 0; i < count; i += 1) {
      const entry = ifd + 2 + i * 12;
      if (entry + 12 > end) {
        return 0;
      }
      if (u16(entry) === 0x0112) {
        const orientation = u16(entry + 8);
        if (orientation === 3 || orientation === 4) return 180;
        if (orientation === 5 || orientation === 6) return 90;
        if (orientation === 7 || orientation === 8) return 270;
        return 0;
      }
    }
    return 0;
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
  // clearly instead of letting pdf-lib fail with an encoding stack trace.
  function ensureEncodable(font, text) {
    try {
      font.encodeText(text);
    } catch {
      throw new PdfToolError(
        'text-unsupported',
        'This text contains characters that the built-in PDF fonts cannot show. Use Latin letters, digits and common punctuation.');
    }
  }

  // ---------------------------------------------------------------------------
  // ZIP (store only — PDFs and images are already compressed)
  // ---------------------------------------------------------------------------

  let crcTable = null;

  function crc32(bytes) {
    if (!crcTable) {
      crcTable = new Uint32Array(256);
      for (let n = 0; n < 256; n += 1) {
        let c = n;
        for (let k = 0; k < 8; k += 1) {
          c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        }
        crcTable[n] = c >>> 0;
      }
    }
    let crc = 0xffffffff;
    for (let i = 0; i < bytes.length; i += 1) {
      crc = crcTable[(crc ^ bytes[i]) & 0xff] ^ (crc >>> 8);
    }
    return (crc ^ 0xffffffff) >>> 0;
  }

  /** Builds a ZIP archive from [{ name, data: Uint8Array }]. Names must be unique. */
  function createZip(entries, date) {
    const encoder = new TextEncoder();
    const when = date instanceof Date ? date : new Date();
    const dosTime = (when.getHours() << 11) | (when.getMinutes() << 5) | Math.floor(when.getSeconds() / 2);
    const dosDate = ((Math.max(1980, when.getFullYear()) - 1980) << 9) | ((when.getMonth() + 1) << 5) | when.getDate();

    const locals = [];
    const centrals = [];
    let offset = 0;

    for (const entry of entries) {
      const name = encoder.encode(entry.name);
      const data = entry.data instanceof Uint8Array ? entry.data : new Uint8Array(entry.data);
      const crc = crc32(data);

      const local = new Uint8Array(30 + name.length);
      const lv = new DataView(local.buffer);
      lv.setUint32(0, 0x04034b50, true);
      lv.setUint16(4, 20, true);
      lv.setUint16(6, 0x0800, true); // UTF-8 names
      lv.setUint16(8, 0, true); // stored
      lv.setUint16(10, dosTime, true);
      lv.setUint16(12, dosDate, true);
      lv.setUint32(14, crc, true);
      lv.setUint32(18, data.length, true);
      lv.setUint32(22, data.length, true);
      lv.setUint16(26, name.length, true);
      local.set(name, 30);

      const central = new Uint8Array(46 + name.length);
      const cv = new DataView(central.buffer);
      cv.setUint32(0, 0x02014b50, true);
      cv.setUint16(4, 20, true);
      cv.setUint16(6, 20, true);
      cv.setUint16(8, 0x0800, true);
      cv.setUint16(10, 0, true);
      cv.setUint16(12, dosTime, true);
      cv.setUint16(14, dosDate, true);
      cv.setUint32(16, crc, true);
      cv.setUint32(20, data.length, true);
      cv.setUint32(24, data.length, true);
      cv.setUint16(28, name.length, true);
      cv.setUint32(42, offset, true);
      central.set(name, 46);

      locals.push(local, data);
      centrals.push(central);
      offset += local.length + data.length;
    }

    const centralSize = centrals.reduce((sum, part) => sum + part.length, 0);
    const end = new Uint8Array(22);
    const ev = new DataView(end.buffer);
    ev.setUint32(0, 0x06054b50, true);
    ev.setUint16(8, entries.length, true);
    ev.setUint16(10, entries.length, true);
    ev.setUint32(12, centralSize, true);
    ev.setUint32(16, offset, true);

    const parts = [...locals, ...centrals, end];
    const total = parts.reduce((sum, part) => sum + part.length, 0);
    const output = new Uint8Array(total);
    let position = 0;
    for (const part of parts) {
      output.set(part, position);
      position += part.length;
    }
    return output;
  }

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
    for (const page of pages) {
      const key = `${page.source}:${page.index}`;
      let copy = copies.get(key);
      if (used.has(key)) {
        // The same page appears twice: copy it again so each occurrence is its own page.
        [copy] = await output.copyPages(loaded[page.source], [page.index]);
      }
      used.add(key);
      const current = copy.getRotation().angle;
      copy.setRotation(PDFLib.degrees(normalizeRotation(current + (Number(page.rotate) || 0))));
      output.addPage(copy);
      if (!outputPages.has(key)) {
        outputPages.set(key, copy.ref);
      }
    }
    carryFormFields(PDFLib, output, loaded);
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
   * Fields with the same name in different sources merge, as they would in any PDF form.
   */
  function carryFormFields(PDFLib, output, sources) {
    const { PDFName, PDFDict, PDFArray, PDFRef } = PDFLib;
    const PARENT = PDFName.of('Parent');
    const KIDS = PDFName.of('Kids');
    const kept = new Set();
    const roots = new Map();

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
    const results = [];
    for (const group of groups) {
      results.push(await assemblePages(PDFLib, [bytes], group.map((index) => ({ source: 0, index }))));
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
    ensureEncodable(font, text);

    const fontSize = clamp(settings.fontSize, 6, 300, 60);
    const opacity = clamp(settings.opacity, 0.02, 1, 0.25);
    const angle = clamp(settings.angle, -360, 360, 45);
    const rgb = hexToRgb(settings.color || '#c0392b');
    const textWidth = font.widthOfTextAtSize(text, fontSize);
    const capHeight = font.heightAtSize(fontSize, { descender: false }) * 0.72;
    const position = settings.position || 'middle-center';
    const pages = document.getPages();

    for (const index of selectedIndexes(pages.length, settings.pages)) {
      const page = pages[index];
      const rotation = page.getRotation().angle;
      const box = visibleBox(page);
      const view = displaySize(box, rotation);

      // Size of the rotated text's bounding box, used to keep corner positions on the page.
      const radians = (angle * Math.PI) / 180;
      const boundsWidth = Math.abs(textWidth * Math.cos(radians)) + Math.abs(capHeight * Math.sin(radians));
      const boundsHeight = Math.abs(textWidth * Math.sin(radians)) + Math.abs(capHeight * Math.cos(radians));
      const block = positionBlock(position, view, { width: boundsWidth, height: boundsHeight }, 36);
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
   * {n} counts only the stamped pages, starting at startAt; {total} is the number of stamped pages.
   */
  async function addTextStamps(PDFLib, bytes, options) {
    const settings = options || {};
    const stamps = (settings.stamps || []).filter((stamp) => String(stamp.template || '').trim());
    if (stamps.length === 0) {
      throw new PdfToolError('text-empty', 'Choose a page number format or enter header/footer text.');
    }
    const document = await loadPdf(PDFLib, bytes);
    const font = await document.embedFont(PDFLib.StandardFonts.Helvetica);
    const fontSize = clamp(settings.fontSize, 6, 72, 10);
    const margin = clamp(settings.margin, 0, 144, 28);
    const startAt = Math.round(clamp(settings.startAt, 0, 100000, 1));
    const rgb = hexToRgb(settings.color || '#000000');
    const pages = document.getPages();

    let indexes = selectedIndexes(pages.length, settings.pages);
    if (settings.skipFirst) {
      indexes = indexes.filter((index) => index !== 0);
    }
    const total = indexes.length + startAt - 1;
    const ascent = font.heightAtSize(fontSize, { descender: false });
    const descent = Math.max(0, font.heightAtSize(fontSize) - ascent);

    indexes.forEach((index, ordinal) => {
      const page = pages[index];
      const rotation = page.getRotation().angle;
      const box = visibleBox(page);
      const view = displaySize(box, rotation);

      for (const stamp of stamps) {
        const text = formatStamp(stamp.template, { n: startAt + ordinal, total, date: settings.date || '' });
        ensureEncodable(font, text);
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
          ensureEncodable(font, line);
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
    effectiveBox,
    displaySize,
    displayToPdfPoint,
    placeRect,
    placeText,
    positionBlock,
    fitImage,
    jpegRotation,
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
    pageCount
  });
});
