// Reduce size (docs/ux/pdf-tools-reduce-size.md): the parts that run without a browser. Pixels are
// re-encoded by the page with a canvas; here a stand-in encoder records what it was asked for.
const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const zlib = require('node:zlib');

const PDFLib = require(path.join(__dirname, '..', '..', 'lib', 'pdf-lib', '1.17.1', 'dist', 'pdf-lib.min.js'));
const tools = require('./pdf-tools-core.js');

const { PDFName, PDFRawStream, PDFDict } = PDFLib;
const bytesOf = (text) => Uint8Array.from(Buffer.from(text, 'latin1'));
const PNG_1X1 = Uint8Array.from(Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64'));

/** Bytes that do not compress, so an image stream is as large as its pixels. */
function noise(length, seed = 7) {
  const out = new Uint8Array(length);
  let state = seed;
  for (let i = 0; i < length; i += 1) {
    state = (Math.imul(state, 1103515245) + 12345) >>> 0;
    out[i] = state >>> 24;
  }
  return out;
}

/** An image XObject added to a document, compressed with Flate unless `filter` says otherwise. */
function addImage(document, { width, height, space = 'DeviceRGB', bits = 8, filter = 'FlateDecode', data, extra = {} }) {
  const channels = space === 'DeviceGray' ? 1 : 3;
  const pixels = data || noise(width * height * channels);
  const contents = filter === 'FlateDecode' ? zlib.deflateSync(pixels) : pixels;
  const dict = { Type: 'XObject', Subtype: 'Image', Width: width, Height: height, ColorSpace: space, BitsPerComponent: bits, ...extra };
  if (filter) dict.Filter = filter;
  return document.context.register(document.context.stream(contents, dict));
}

/** A page that draws the given images: [{ ref, name, matrix: [a b c d e f] }]. */
function pageDrawing(document, draws, size = [612, 792]) {
  const page = document.addPage(size);
  const xobjects = {};
  draws.forEach(({ ref, name }) => { xobjects[name] = ref; });
  page.node.set(PDFName.of('Resources'), document.context.obj({ XObject: xobjects }));
  const ops = draws.map(({ name, matrix }) => `q ${matrix.join(' ')} cm /${name} Do Q`).join('\n');
  page.node.set(PDFName.of('Contents'), document.context.register(document.context.stream(bytesOf(ops))));
  return page;
}

/** A stand-in for the browser's encoder: answers with bytes of a chosen size and records each call. */
function fakeEncoder(jpegBytes = 400, { grey } = {}) {
  const calls = [];
  const encodeImage = async (input) => {
    calls.push(input);
    const result = { jpeg: new Uint8Array(jpegBytes).fill(0xab) };
    if (input.grey && grey) result.grey = grey(input.targetWidth * input.targetHeight);
    return result;
  };
  return { encodeImage, calls };
}

const imageStreams = async (bytes) => {
  const document = await PDFLib.PDFDocument.load(bytes);
  return document.context.enumerateIndirectObjects()
    .filter(([, object]) => object instanceof PDFRawStream && String(object.dict.get(PDFName.of('Subtype'))) === '/Image')
    .map(([, object]) => object);
};

// -----------------------------------------------------------------------------
// Reading content streams
// -----------------------------------------------------------------------------

test('contentOperators reads operators and operands, skipping strings, arrays, comments and inline images', () => {
  const content = bytesOf([
    '% a comment with q Q cm Do inside',
    'q 2 0 0 3 10 20 cm',
    '(a string with (nested) parens and an escaped \\) Do) Tj',
    '[(TJ) 120 (array)] TJ',
    '<</MCID 3>> BDC /Im1 Do EMC',
    'BI /W 4 /H 1 /CS /G /BPC 8 ID QQQQ EI',
    '/Im#32 Do Q'
  ].join('\n'));
  const seen = [];
  tools.contentOperators(content, (operator, operands) => seen.push([operator, operands]));
  assert.deepEqual(seen.map(([operator]) => operator), ['q', 'cm', 'Tj', 'TJ', 'BDC', 'Do', 'EMC', 'Do', 'Q']);
  assert.deepEqual(seen[1][1], [2, 0, 0, 3, 10, 20]);
  assert.deepEqual(seen[5][1], ['/Im1']);
  assert.deepEqual(seen[7][1], ['/Im2'], 'a #xx escape in a name is decoded');
});

// -----------------------------------------------------------------------------
// Measuring how large each image is drawn
// -----------------------------------------------------------------------------

test('measureImageUses keeps the largest use of each image, through cm, q/Q and form XObjects', async () => {
  const document = await PDFLib.PDFDocument.create();
  const image = addImage(document, { width: 10, height: 10 });
  const other = addImage(document, { width: 10, height: 10 });
  // A form that draws `other` at 100 x 50, placed with its own Matrix scaled by 2.
  const form = document.context.register(document.context.stream(bytesOf('q 100 0 0 50 0 0 cm /Pic Do Q'), {
    Type: 'XObject', Subtype: 'Form', BBox: [0, 0, 200, 100], Matrix: [2, 0, 0, 2, 0, 0],
    Resources: { XObject: { Pic: other } }
  }));
  const page = document.addPage([612, 792]);
  page.node.set(PDFName.of('Resources'), document.context.obj({ XObject: { A: image, F: form } }));
  page.node.set(PDFName.of('Contents'), document.context.register(document.context.stream(bytesOf([
    'q 144 0 0 72 0 0 cm /A Do Q',
    // Turned a quarter: the image's width runs up the page, 216 points long.
    'q 0 216 -36 0 300 300 cm /A Do Q',
    'q 0.5 0 0 0.5 0 0 cm /F Do Q'
  ].join('\n')))));

  const { uses, unmeasured } = tools.measureImageUses(PDFLib, document);
  assert.deepEqual(uses.get(String(image)), { width: 216, height: 72 }, 'largest along each side of the image');
  assert.deepEqual(uses.get(String(other)), { width: 100, height: 50 }, '0.5 x 2 x 100 by 0.5 x 2 x 50');
  assert.equal(unmeasured.size, 0);
});

test('measureImageUses counts a page UserUnit, and leaves images in annotation appearances unmeasured', async () => {
  const document = await PDFLib.PDFDocument.create();
  const image = addImage(document, { width: 10, height: 10 });
  const stamp = addImage(document, { width: 10, height: 10 });
  const page = pageDrawing(document, [{ ref: image, name: 'A', matrix: [72, 0, 0, 72, 0, 0] }]);
  page.node.set(PDFName.of('UserUnit'), document.context.obj(2));
  const appearance = document.context.register(document.context.stream(bytesOf('/S Do'), {
    Type: 'XObject', Subtype: 'Form', BBox: [0, 0, 10, 10], Resources: { XObject: { S: stamp } }
  }));
  const annotation = document.context.register(document.context.obj({
    Type: 'Annot', Subtype: 'Stamp', Rect: [0, 0, 100, 100], AP: { N: appearance }
  }));
  page.node.set(PDFName.of('Annots'), document.context.obj([annotation]));

  const { uses, unmeasured } = tools.measureImageUses(PDFLib, document);
  assert.deepEqual(uses.get(String(image)), { width: 144, height: 144 }, 'one inch at UserUnit 2 is two inches');
  assert.ok(unmeasured.has(String(stamp)));
});

test('measureImageUses leaves images drawn by patterns, Type 3 glyphs and soft-mask groups unmeasured', async () => {
  const document = await PDFLib.PDFDocument.create();
  const shared = addImage(document, { width: 600, height: 600 });
  const glyph = addImage(document, { width: 600, height: 600 });
  const group = addImage(document, { width: 600, height: 600 });
  // A tiling pattern that draws `shared` six inches wide: its size depends on where it is painted.
  const pattern = document.context.register(document.context.stream(bytesOf('q 432 0 0 432 0 0 cm /Img Do Q'), {
    Type: 'Pattern', PatternType: 1, PaintType: 1, TilingType: 1, BBox: [0, 0, 432, 432], XStep: 432, YStep: 432,
    Resources: { XObject: { Img: shared } }
  }));
  const glyphProc = document.context.register(document.context.stream(bytesOf('0 0 0 0 0 0 d1 /G Do')));
  const type3 = document.context.register(document.context.obj({
    Type: 'Font', Subtype: 'Type3', FontBBox: [0, 0, 1, 1], FontMatrix: [1, 0, 0, 1, 0, 0],
    CharProcs: { g: glyphProc },
    Resources: { XObject: { G: glyph } }
  }));
  const maskGroup = document.context.register(document.context.stream(bytesOf('/M Do'), {
    Type: 'XObject', Subtype: 'Form', BBox: [0, 0, 1, 1], Resources: { XObject: { M: group } }
  }));
  const page = document.addPage([612, 792]);
  page.node.set(PDFName.of('Resources'), document.context.obj({
    XObject: { Img: shared },
    Pattern: { P1: pattern },
    Font: { T3: type3 },
    ExtGState: { GS: { SMask: { S: 'Luminosity', G: maskGroup } } }
  }));
  // Drawn directly at half an inch: on its own, that would allow 75 x 75 px at 150 dpi.
  page.node.set(PDFName.of('Contents'), document.context.register(document.context.stream(bytesOf(
    'q 36 0 0 36 0 0 cm /Img Do Q /Pattern cs /P1 scn 0 0 612 792 re f'))));

  const measured = tools.measureImageUses(PDFLib, document);
  for (const ref of [shared, glyph, group]) assert.ok(measured.unmeasured.has(String(ref)), String(ref));
  const plan = tools.reduceImagePlan(PDFLib, document, measured, tools.PDF_SIZE_PRESETS[1]);
  const sharedPlan = plan.find((item) => String(item.ref) === String(shared));
  assert.deepEqual(sharedPlan.target, { width: 600, height: 600, scaled: false }, 'kept at full size for its pattern use');
});

test('a Type 3 font without its own resources draws from the enclosing ones; only images its glyphs draw are unmeasured', async () => {
  const document = await PDFLib.PDFDocument.create();
  const shared = addImage(document, { width: 600, height: 600 });
  const untouched = addImage(document, { width: 600, height: 600 });
  // Glyph "a" draws /Img from the page's resources, six inches wide; glyph "b" draws nothing.
  const glyphA = document.context.register(document.context.stream(bytesOf('432 0 0 0 0 0 d1 q 432 0 0 432 0 0 cm /Img Do Q')));
  const glyphB = document.context.register(document.context.stream(bytesOf('500 0 0 0 0 0 d1 0 0 10 10 re f')));
  const type3 = document.context.register(document.context.obj({
    Type: 'Font', Subtype: 'Type3', FontBBox: [0, 0, 1, 1], FontMatrix: [1, 0, 0, 1, 0, 0],
    CharProcs: { a: glyphA, b: glyphB }
  }));
  const page = document.addPage([612, 792]);
  page.node.set(PDFName.of('Resources'), document.context.obj({ XObject: { Img: shared, Other: untouched }, Font: { T3: type3 } }));
  page.node.set(PDFName.of('Contents'), document.context.register(document.context.stream(bytesOf(
    'q 36 0 0 36 0 0 cm /Img Do Q q 36 0 0 36 100 0 cm /Other Do Q BT /T3 1 Tf (ab) Tj ET'))));

  const measured = tools.measureImageUses(PDFLib, document);
  assert.ok(measured.unmeasured.has(String(shared)), 'drawn by a glyph from the enclosing resources');
  assert.ok(!measured.unmeasured.has(String(untouched)), 'an image no glyph draws is still measured');
  const plan = tools.reduceImagePlan(PDFLib, document, measured, tools.PDF_SIZE_PRESETS[1]);
  assert.deepEqual(plan.find((item) => String(item.ref) === String(shared)).target, { width: 600, height: 600, scaled: false });
  assert.deepEqual(plan.find((item) => String(item.ref) === String(untouched)).target, { width: 75, height: 75, scaled: true });
});

test('one glyph shared by two Type 3 fonts is read in each font\'s resources', async () => {
  const document = await PDFLib.PDFDocument.create();
  const first = addImage(document, { width: 600, height: 600 });
  const second = addImage(document, { width: 600, height: 600, data: noise(600 * 600 * 3, 99) });
  // The same glyph stream (as after identical glyphs are stored once); /Img differs by font.
  const glyph = document.context.register(document.context.stream(bytesOf('0 0 0 0 0 0 d1 q 432 0 0 432 0 0 cm /Img Do Q')));
  const font = (image) => document.context.register(document.context.obj({
    Type: 'Font', Subtype: 'Type3', FontBBox: [0, 0, 1, 1], FontMatrix: [1, 0, 0, 1, 0, 0],
    CharProcs: { g: glyph }, Resources: { XObject: { Img: image } }
  }));
  // A form without its own resources, used from two places with different /Img.
  const form = document.context.register(document.context.stream(bytesOf('/Img Do'), { Type: 'XObject', Subtype: 'Form', BBox: [0, 0, 1, 1] }));
  const third = addImage(document, { width: 600, height: 600, data: noise(600 * 600 * 3, 5) });
  const fourth = addImage(document, { width: 600, height: 600, data: noise(600 * 600 * 3, 6) });
  const maskState = (image) => document.context.obj({
    SMask: { S: 'Luminosity', G: document.context.register(document.context.stream(bytesOf('/F Do'), {
      Type: 'XObject', Subtype: 'Form', BBox: [0, 0, 1, 1], Resources: { XObject: { F: form, Img: image } }
    })) }
  });
  const page = document.addPage([612, 792]);
  page.node.set(PDFName.of('Resources'), document.context.obj({
    XObject: { A: first, B: second, C: third, D: fourth },
    Font: { F1: font(first), F2: font(second) },
    ExtGState: { G1: maskState(third), G2: maskState(fourth) }
  }));
  page.node.set(PDFName.of('Contents'), document.context.register(document.context.stream(bytesOf(
    ['A', 'B', 'C', 'D'].map((name, i) => `q 36 0 0 36 ${i * 40} 0 cm /${name} Do Q`).join(' ')))));
  const measured = tools.measureImageUses(PDFLib, document);
  for (const ref of [first, second]) assert.ok(measured.unmeasured.has(String(ref)), `glyph image ${ref}`);
  for (const ref of [third, fourth]) assert.ok(measured.unmeasured.has(String(ref)), `form image ${ref}`);
});

test('measureImageUses ends on resources that lead back to themselves', async () => {
  const document = await PDFLib.PDFDocument.create();
  const image = addImage(document, { width: 600, height: 600 });
  const resources = document.context.obj({ XObject: { Img: image } });
  // A form and a Type 3 font that both draw from these same resources, and draw each other.
  const glyph = document.context.register(document.context.stream(bytesOf('0 0 0 0 0 0 d1 /Self Do /Img Do')));
  const font = document.context.register(document.context.obj({ Type: 'Font', Subtype: 'Type3', CharProcs: { g: glyph } }));
  const form = document.context.register(document.context.stream(bytesOf('BT /T3 1 Tf (g) Tj ET /Self Do'), {
    Type: 'XObject', Subtype: 'Form', BBox: [0, 0, 1, 1]
  }));
  resources.set(PDFName.of('Font'), document.context.obj({ T3: font }));
  resources.get(PDFName.of('XObject')).set(PDFName.of('Self'), form);
  const page = document.addPage([612, 792]);
  page.node.set(PDFName.of('Resources'), resources);
  page.node.set(PDFName.of('Contents'), document.context.register(document.context.stream(bytesOf('/Self Do'))));
  const measured = tools.measureImageUses(PDFLib, document);
  assert.ok(measured.unmeasured.has(String(image)));
});

test('reduceTarget keeps enough pixels for the preset at the largest use, and never enlarges', () => {
  // 3000 x 2000 px drawn 6 x 4 inches: 150 dpi needs 900 x 600.
  assert.deepEqual(tools.reduceTarget(3000, 2000, { width: 432, height: 288 }, 150), { width: 900, height: 600, scaled: true });
  // Already near 150 dpi: kept as it is.
  assert.deepEqual(tools.reduceTarget(950, 630, { width: 432, height: 288 }, 150), { width: 950, height: 630, scaled: false });
  // Small on the page, so smaller in pixels; the shape is kept.
  assert.deepEqual(tools.reduceTarget(1000, 500, { width: 72, height: 36 }, 100), { width: 100, height: 50, scaled: true });
  // Not measured: the pixel size is kept.
  assert.deepEqual(tools.reduceTarget(3000, 2000, null, 150), { width: 3000, height: 2000, scaled: false });
});

// -----------------------------------------------------------------------------
// Decoding pixels
// -----------------------------------------------------------------------------

test('undoPngPredictor reverses each PNG filter type', () => {
  // Two grey pixels per row, four rows, one per filter type 1 to 4, after a type 0 row.
  const rows = [[10, 20], [30, 50], [35, 60], [40, 40], [100, 7]];
  const encode = (type, row, previous) => row.map((value, i) => {
    const left = i > 0 ? row[i - 1] : 0;
    const up = previous ? previous[i] : 0;
    const upLeft = i > 0 && previous ? previous[i - 1] : 0;
    let predicted = 0;
    if (type === 1) predicted = left;
    else if (type === 2) predicted = up;
    else if (type === 3) predicted = (left + up) >> 1;
    else if (type === 4) {
      const p = left + up - upLeft;
      const pa = Math.abs(p - left); const pb = Math.abs(p - up); const pc = Math.abs(p - upLeft);
      predicted = pa <= pb && pa <= pc ? left : pb <= pc ? up : upLeft;
    }
    return (value - predicted) & 0xff;
  });
  const encoded = [];
  rows.forEach((row, index) => encoded.push(index, ...encode(index, row, rows[index - 1])));
  assert.deepEqual([...tools.undoPngPredictor(Uint8Array.from(encoded), 1, 8, 2)], rows.flat());
  assert.equal(tools.undoPngPredictor(Uint8Array.from([9, 1, 2]), 1, 8, 2), null, 'unknown filter type');
});

test('jpegClearOrientation makes a copy that says "as stored", so the browser does not turn it', () => {
  const jpeg = (orientation, little) => {
    const u16 = (value) => (little ? [value & 0xff, value >> 8] : [value >> 8, value & 0xff]);
    const u32 = (value) => (little ? [value, 0, 0, 0] : [0, 0, 0, value]);
    const tiff = [
      ...(little ? [0x49, 0x49] : [0x4d, 0x4d]), ...u16(42), ...u32(8),
      ...u16(1), ...u16(0x0112), ...u16(3), ...u32(1), ...u16(orientation), 0, 0,
      ...u32(0)
    ];
    const app1 = [0x45, 0x78, 0x69, 0x66, 0, 0, ...tiff];
    const length = app1.length + 2;
    return Uint8Array.from([0xff, 0xd8, 0xff, 0xe1, length >> 8, length & 0xff, ...app1, 0xff, 0xd9]);
  };
  for (const little of [true, false]) {
    for (const orientation of [2, 3, 6, 8]) {
      const original = jpeg(orientation, little);
      const cleared = tools.jpegClearOrientation(original);
      assert.equal(tools.jpegRotation(cleared), 0);
      assert.equal(tools.jpegMirrored(cleared), false);
      assert.notEqual(cleared, original, 'a copy: the PDF\'s own bytes are not changed');
      assert.equal(tools.jpegRotation(original) !== 0 || tools.jpegMirrored(original), true);
      assert.equal(cleared.length, original.length);
      assert.equal([...cleared].filter((byte, at) => byte !== original[at]).length, 1, 'only the orientation value changes');
    }
  }
  const plain = jpeg(1, true);
  assert.equal(tools.jpegClearOrientation(plain), plain, 'already "as stored": the same bytes');
  const noExif = Uint8Array.from([0xff, 0xd8, 0xff, 0xd9]);
  assert.equal(tools.jpegClearOrientation(noExif), noExif);
});

// -----------------------------------------------------------------------------
// Which images are changed
// -----------------------------------------------------------------------------

test('reduceImagePlan leaves masks, black-and-white scans, print colour, odd formats and small images alone', async () => {
  const document = await PDFLib.PDFDocument.create();
  const big = { width: 120, height: 120 };
  const mask = addImage(document, { ...big, space: 'DeviceGray' });
  const plan = {
    photo: addImage(document, { ...big, extra: { SMask: mask } }),
    grey: addImage(document, { ...big, space: 'DeviceGray', data: noise(120 * 120) }),
    jpeg: addImage(document, { ...big, filter: 'DCTDecode', data: noise(40000) }),
    cmyk: addImage(document, { ...big, space: 'DeviceCMYK', data: noise(120 * 120 * 4) }),
    jbig2: addImage(document, { ...big, space: 'DeviceGray', bits: 1, filter: 'JBIG2Decode', data: noise(20000) }),
    decode: addImage(document, { ...big, extra: { Decode: [1, 0, 1, 0, 1, 0] } }),
    small: addImage(document, { width: 20, height: 20 }),
    stencil: addImage(document, { ...big, space: 'DeviceGray', bits: 1, extra: { ImageMask: true }, data: noise(2000) })
  };
  pageDrawing(document, Object.entries(plan).map(([name, ref]) => ({ ref, name, matrix: [36, 0, 0, 36, 0, 0] })));
  const measured = tools.measureImageUses(PDFLib, document);
  const result = tools.reduceImagePlan(PDFLib, document, measured, tools.PDF_SIZE_PRESETS[1]);
  const byRef = new Map(result.map((item) => [String(item.ref), item]));
  const outcome = (ref) => (byRef.get(String(ref)).action === 'skip' ? byRef.get(String(ref)).reason : byRef.get(String(ref)).source);

  assert.equal(outcome(mask), tools.REDUCE_SKIP.mask, 'a soft mask stays a lossless mask');
  assert.equal(outcome(plan.photo), 'pixels', 'the image with the mask is re-saved; its mask is not');
  assert.equal(outcome(plan.grey), 'pixels');
  assert.equal(byRef.get(String(plan.grey)).grey, true);
  assert.equal(outcome(plan.jpeg), 'jpeg');
  assert.equal(outcome(plan.cmyk), tools.REDUCE_SKIP.printColour);
  assert.equal(outcome(plan.jbig2), tools.REDUCE_SKIP.blackWhite);
  assert.equal(outcome(plan.decode), tools.REDUCE_SKIP.other);
  assert.equal(outcome(plan.small), tools.REDUCE_SKIP.small);
  assert.equal(outcome(plan.stencil), tools.REDUCE_SKIP.mask);
  // Drawn half an inch square at 150 dpi: 75 x 75 px.
  assert.deepEqual(byRef.get(String(plan.photo)).target, { width: 75, height: 75, scaled: true });
});

test('isSignedPdf finds a signed signature field, and a document with permissions', async () => {
  const plain = await PDFLib.PDFDocument.create();
  plain.addPage();
  assert.equal(tools.isSignedPdf(PDFLib, plain), false);

  const signed = await PDFLib.PDFDocument.create();
  signed.addPage();
  const value = signed.context.register(signed.context.obj({ Type: 'Sig', Filter: 'Adobe.PPKLite', ByteRange: [0, 10, 20, 30] }));
  const parent = signed.context.register(signed.context.obj({ FT: 'Sig', T: PDFLib.PDFString.of('Signature1') }));
  const widget = signed.context.register(signed.context.obj({ Type: 'Annot', Subtype: 'Widget', Parent: parent, V: value, Rect: [0, 0, 0, 0] }));
  signed.context.lookup(parent).set(PDFName.of('Kids'), signed.context.obj([widget]));
  signed.catalog.set(PDFName.of('AcroForm'), signed.context.obj({ Fields: [parent] }));
  assert.equal(tools.isSignedPdf(PDFLib, signed), true, 'the value on a kid of a /Sig field counts');

  const empty = await PDFLib.PDFDocument.create();
  empty.addPage();
  const unsigned = empty.context.register(empty.context.obj({ FT: 'Sig', T: PDFLib.PDFString.of('Sign here') }));
  empty.catalog.set(PDFName.of('AcroForm'), empty.context.obj({ Fields: [unsigned] }));
  assert.equal(tools.isSignedPdf(PDFLib, empty), false, 'an empty signature field is not a signature');

  const certified = await PDFLib.PDFDocument.create();
  certified.addPage();
  certified.catalog.set(PDFName.of('Perms'), certified.context.obj({ DocMDP: value }));
  assert.equal(tools.isSignedPdf(PDFLib, certified), true);
});

// -----------------------------------------------------------------------------
// Lossless clean-up
// -----------------------------------------------------------------------------

test('deduplicateObjects stores repeated fonts and images once and keeps every page drawing them', async () => {
  const one = await PDFLib.PDFDocument.create();
  const font = await one.embedFont(PDFLib.StandardFonts.Helvetica);
  const page = one.addPage([300, 300]);
  page.drawText('REPEATED', { x: 20, y: 150, size: 12, font });
  const picture = await one.embedPng(PNG_1X1);
  page.drawImage(picture, { x: 10, y: 10, width: 50, height: 50 });
  const single = await one.save();
  // The same file merged three times: each copy brings its own font and image objects.
  const merged = await tools.assemblePages(PDFLib, [single, single, single], [0, 1, 2].map((source) => ({ source, index: 0 })));

  const document = await PDFLib.PDFDocument.load(merged);
  const before = document.context.enumerateIndirectObjects().length;
  const count = tools.deduplicateObjects(PDFLib, document);
  const after = document.context.enumerateIndirectObjects().length;
  assert.ok(count >= 4, `merged ${count}`);
  assert.equal(before - after, count);
  const saved = await PDFLib.PDFDocument.load(await document.save());
  assert.equal(saved.getPageCount(), 3);
  // Every page still names its font and image, now the same objects.
  const fonts = new Set(saved.getPages().map((item) => String(item.node.Resources().lookup(PDFName.of('Font'), PDFDict).values()[0])));
  const images = new Set(saved.getPages().map((item) => String(item.node.Resources().lookup(PDFName.of('XObject'), PDFDict).values()[0])));
  assert.equal(fonts.size, 1, 'one font for all three pages');
  assert.equal(images.size, 1, 'one image for all three pages');
});

test('deduplicateObjects never merges pages, even identical ones', async () => {
  const document = await PDFLib.PDFDocument.create();
  document.addPage([200, 200]);
  document.addPage([200, 200]);
  tools.deduplicateObjects(PDFLib, document);
  const saved = await PDFLib.PDFDocument.load(await document.save());
  assert.equal(saved.getPageCount(), 2);
});

test('compressPlainStreams compresses streams stored without compression, but not metadata', async () => {
  const document = await PDFLib.PDFDocument.create();
  const page = document.addPage([200, 200]);
  const content = bytesOf('BT /F1 12 Tf 10 10 Td (hello) Tj ET\n'.repeat(40));
  page.node.set(PDFName.of('Contents'), document.context.register(document.context.stream(content)));
  const metadata = document.context.register(document.context.stream(bytesOf('<x:xmpmeta>'.padEnd(2000, ' ')), { Type: 'Metadata', Subtype: 'XML' }));
  document.catalog.set(PDFName.of('Metadata'), metadata);
  assert.equal(tools.compressPlainStreams(PDFLib, document), 1);
  const stream = document.context.lookup(page.node.get(PDFName.of('Contents')));
  assert.equal(String(stream.dict.get(PDFName.of('Filter'))), '/FlateDecode');
  assert.deepEqual([...PDFLib.decodePDFRawStream(stream).decode()], [...content]);
  assert.equal(document.context.lookup(metadata).dict.get(PDFName.of('Filter')), undefined);
});

// -----------------------------------------------------------------------------
// The whole run
// -----------------------------------------------------------------------------

test('reducePdf re-saves a large image at the preset resolution and keeps text, mask and size honest', async () => {
  const document = await PDFLib.PDFDocument.create();
  const font = await document.embedFont(PDFLib.StandardFonts.Helvetica);
  const mask = addImage(document, { width: 600, height: 600, space: 'DeviceGray', data: noise(600 * 600, 3) });
  const photo = addImage(document, { width: 600, height: 600, extra: { SMask: mask, Interpolate: true } });
  const page = pageDrawing(document, [{ ref: photo, name: 'P', matrix: [144, 0, 0, 144, 36, 36] }]);
  page.drawText('KEEPTHISTEXT', { x: 20, y: 700, size: 12, font });
  const original = await document.save();

  const encoder = fakeEncoder(3000);
  const progress = [];
  const result = await tools.reducePdf(PDFLib, original, {
    preset: 'standard',
    encodeImage: encoder.encodeImage,
    onProgress: (done, total) => progress.push(`${done}/${total}`)
  });

  // Two inches at 150 dpi: 300 x 300.
  assert.equal(encoder.calls.length, 1);
  assert.deepEqual(
    { w: encoder.calls[0].targetWidth, h: encoder.calls[0].targetHeight, q: encoder.calls[0].quality, source: encoder.calls[0].source, channels: encoder.calls[0].channels },
    { w: 300, h: 300, q: 0.75, source: 'pixels', channels: 3 });
  assert.equal(encoder.calls[0].data.length, 600 * 600 * 3, 'decoded pixels were handed over');
  assert.deepEqual(progress, ['1/1']);
  assert.equal(result.unchanged, false);
  assert.ok(result.bytes.length < original.length / 2, `${original.length} -> ${result.bytes.length}`);
  assert.equal(result.report.resaved, 1);
  assert.equal(result.report.resized, 1);
  assert.equal(result.report.skipped[tools.REDUCE_SKIP.mask], 1);

  const saved = await PDFLib.PDFDocument.load(result.bytes);
  const images = await imageStreams(result.bytes);
  const resaved = images.find((stream) => String(stream.dict.get(PDFName.of('Filter'))) === '/DCTDecode');
  assert.ok(resaved, 'the photo is a JPG now');
  assert.equal(resaved.dict.get(PDFName.of('Width')).asNumber(), 300);
  assert.equal(String(resaved.dict.get(PDFName.of('ColorSpace'))), '/DeviceRGB');
  assert.ok(resaved.dict.get(PDFName.of('SMask')), 'the soft mask is still attached');
  assert.equal(String(resaved.dict.get(PDFName.of('Interpolate'))), 'true', 'other keys are kept');
  assert.equal(saved.getPageCount(), 1);
  const contents = saved.getPage(0).node.Contents();
  const text = (contents instanceof PDFLib.PDFArray ? contents.asArray() : [saved.getPage(0).node.get(PDFName.of('Contents'))])
    .map((ref) => Buffer.from(PDFLib.decodePDFRawStream(saved.context.lookup(ref)).decode()).toString('latin1')).join('');
  assert.match(text, new RegExp(Buffer.from('KEEPTHISTEXT').toString('hex'), 'i'), 'text is untouched');
});

test('reducePdf keeps a grey image grey when grey pixels are smaller than a colour JPG', async () => {
  const document = await PDFLib.PDFDocument.create();
  const scan = addImage(document, { width: 400, height: 400, space: 'DeviceGray', data: noise(400 * 400, 11) });
  pageDrawing(document, [{ ref: scan, name: 'S', matrix: [144, 0, 0, 144, 0, 0] }]);
  const original = await document.save();
  // A flat grey image compresses to almost nothing, far below the 20 KB "JPG".
  const encoder = fakeEncoder(20000, { grey: (length) => new Uint8Array(length).fill(200) });
  const result = await tools.reducePdf(PDFLib, original, { preset: 'standard', encodeImage: encoder.encodeImage });
  assert.equal(encoder.calls[0].grey, true);
  const [image] = await imageStreams(result.bytes);
  assert.equal(String(image.dict.get(PDFName.of('ColorSpace'))), '/DeviceGray');
  assert.equal(String(image.dict.get(PDFName.of('Filter'))), '/FlateDecode');
  assert.equal(image.dict.get(PDFName.of('Width')).asNumber(), 300);
});

test('reducePdf keeps an image when re-saving it is not clearly smaller, and offers the original when nothing shrinks', async () => {
  const document = await PDFLib.PDFDocument.create();
  const font = await document.embedFont(PDFLib.StandardFonts.Helvetica);
  document.addPage([300, 300]).drawText('Only text here', { x: 20, y: 150, size: 12, font });
  const textOnly = await document.save();
  const result = await tools.reducePdf(PDFLib, textOnly, { preset: 'screen', encodeImage: async () => null });
  assert.equal(result.unchanged, true);
  assert.equal(result.bytes, textOnly, 'the very same bytes: a bigger file is never offered');

  const withImage = await PDFLib.PDFDocument.create();
  const image = addImage(withImage, { width: 100, height: 100 });
  pageDrawing(withImage, [{ ref: image, name: 'I', matrix: [72, 0, 0, 72, 0, 0] }]);
  const bytes = await withImage.save();
  // The "JPG" is as large as the original stream, so the original image is kept.
  const big = await tools.reducePdf(PDFLib, bytes, { preset: 'print', encodeImage: fakeEncoder(40000).encodeImage });
  assert.equal(big.report.resaved, 0);
  assert.equal(big.report.skipped[tools.REDUCE_SKIP.notSmaller], 1);
});

test('reducePdf refuses a signed PDF, and an encrypted one, without changing anything', async () => {
  const signed = await PDFLib.PDFDocument.create();
  signed.addPage();
  signed.catalog.set(PDFName.of('Perms'), signed.context.obj({}));
  await assert.rejects(
    tools.reducePdf(PDFLib, await signed.save(), { encodeImage: fakeEncoder().encodeImage }),
    (error) => error.code === 'signed' && /digitally signed/.test(error.message));
  await assert.rejects(
    tools.reducePdf(PDFLib, bytesOf('not a pdf'), { encodeImage: fakeEncoder().encodeImage }),
    (error) => error.code === 'unreadable');
});

test('describePdf reports pages, size and how much of the file is images', async () => {
  const document = await PDFLib.PDFDocument.create();
  const image = addImage(document, { width: 100, height: 100 });
  pageDrawing(document, [{ ref: image, name: 'I', matrix: [72, 0, 0, 72, 0, 0] }]);
  document.addPage();
  const bytes = await document.save();
  const info = await tools.describePdf(PDFLib, bytes);
  assert.equal(info.pages, 2);
  assert.equal(info.bytes, bytes.length);
  assert.equal(info.imageCount, 1);
  assert.ok(info.imageBytes > 30000 && info.imageBytes < bytes.length);
  assert.equal(info.signed, false);
});
