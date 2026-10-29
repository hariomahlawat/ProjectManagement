const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');

const PDFLib = require(path.join(__dirname, '..', '..', 'lib', 'pdf-lib', '1.17.1', 'dist', 'pdf-lib.min.js'));
const tools = require('./pdf-tools-core.js');

// -----------------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------------

/** Creates a PDF whose pages have distinct sizes so their order can be asserted. */
async function makePdf(sizes, rotations = []) {
  const document = await PDFLib.PDFDocument.create();
  sizes.forEach((size, index) => {
    const page = document.addPage(size);
    if (rotations[index]) {
      page.setRotation(PDFLib.degrees(rotations[index]));
    }
  });
  return document.save();
}

async function describePages(bytes) {
  const document = await PDFLib.PDFDocument.load(bytes);
  return document.getPages().map((page) => ({
    width: Math.round(page.getWidth()),
    height: Math.round(page.getHeight()),
    rotation: page.getRotation().angle
  }));
}

async function contentOf(bytes, pageIndex) {
  const document = await PDFLib.PDFDocument.load(bytes);
  const contents = document.getPage(pageIndex).node.Contents();
  if (!contents) {
    return '';
  }
  const streams = contents instanceof PDFLib.PDFArray
    ? contents.asArray().map((ref) => document.context.lookup(ref))
    : [contents];
  return streams
    .map((stream) => (stream instanceof PDFLib.PDFRawStream
      ? PDFLib.decodePDFRawStream(stream).decode()
      : stream.getContents()))
    .map((data) => Buffer.from(data).toString('latin1'))
    .join('\n');
}

/** Returns [a, b, c, d, e, f] for every text matrix (Tm) in a content stream. */
function textMatrices(content) {
  return Array.from(content.matchAll(/([-\d.e]+) ([-\d.e]+) ([-\d.e]+) ([-\d.e]+) ([-\d.e]+) ([-\d.e]+) Tm/g))
    .map((match) => match.slice(1).map(Number));
}

const near = (actual, expected, message) =>
  assert.ok(Math.abs(actual - expected) < 0.01, `${message ?? ''} expected ${expected}, got ${actual}`);

// A tiny valid 1x1 PNG.
const PNG_1X1 = Uint8Array.from(Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64'));

// -----------------------------------------------------------------------------
// Page ranges
// -----------------------------------------------------------------------------

test('parsePageRanges converts one-based ranges into zero-based groups', () => {
  assert.deepEqual(tools.parsePageRanges('1-3, 5, 8-', 9), [[0, 1, 2], [4], [7, 8]]);
  assert.deepEqual(tools.parsePageRanges(' 2 ; 2 ', 3), [[1], [1]]);
});

test('parsePageRanges rejects invalid, empty, backwards and out-of-range input', () => {
  assert.throws(() => tools.parsePageRanges('', 3), { code: 'range-empty' });
  assert.throws(() => tools.parsePageRanges('a-b', 3), { code: 'range-invalid' });
  assert.throws(() => tools.parsePageRanges('0', 3), { code: 'range-invalid' });
  assert.throws(() => tools.parsePageRanges('3-1', 3), { code: 'range-invalid' });
  assert.throws(() => tools.parsePageRanges('2-4', 3), { code: 'range-out-of-bounds' });
});

test('pageSetFromRanges de-duplicates and sorts', () => {
  assert.deepEqual(tools.pageSetFromRanges('4, 1-2, 2', 5), [0, 1, 3]);
});

test('chunkPages splits into fixed-size groups with a shorter final group', () => {
  assert.deepEqual(tools.chunkPages(5, 2), [[0, 1], [2, 3], [4]]);
  assert.throws(() => tools.chunkPages(5, 0), { code: 'range-invalid' });
});

// -----------------------------------------------------------------------------
// Geometry
// -----------------------------------------------------------------------------

test('normalizeRotation snaps to quarter turns in [0, 360)', () => {
  assert.equal(tools.normalizeRotation(-90), 270);
  assert.equal(tools.normalizeRotation(450), 90);
  assert.equal(tools.normalizeRotation(undefined), 0);
});

test('displayToPdfPoint maps display corners for every page rotation', () => {
  const box = { x: 10, y: 20, width: 200, height: 300 };
  // Display top-left corner of the rendered page, for each /Rotate value.
  assert.deepEqual(tools.displayToPdfPoint(box, 0, 0, 0), { x: 10, y: 320 });
  assert.deepEqual(tools.displayToPdfPoint(box, 90, 0, 0), { x: 10, y: 20 });
  assert.deepEqual(tools.displayToPdfPoint(box, 180, 0, 0), { x: 210, y: 20 });
  assert.deepEqual(tools.displayToPdfPoint(box, 270, 0, 0), { x: 210, y: 320 });
  // Display bottom-right corner is always the opposite PDF corner.
  assert.deepEqual(tools.displayToPdfPoint(box, 90, 300, 200), { x: 210, y: 320 });
  assert.deepEqual(tools.displaySize(box, 90), { width: 300, height: 200 });
});

test('effectiveBox clips the CropBox to the MediaBox like pdf.js', () => {
  const media = { x: 0, y: 0, width: 300, height: 400 };
  assert.deepEqual(tools.effectiveBox(media, { x: -100, y: -100, width: 500, height: 600 }), media);
  assert.deepEqual(tools.effectiveBox(media, { x: 50, y: 60, width: 100, height: 100 }), { x: 50, y: 60, width: 100, height: 100 });
  assert.deepEqual(tools.effectiveBox(media, { x: 500, y: 500, width: 10, height: 10 }), media, 'no overlap falls back to the MediaBox');
});

test('applyAnnotations maps against the visible box when the CropBox overhangs the MediaBox', async () => {
  const document = await PDFLib.PDFDocument.create();
  const page = document.addPage([300, 400]);
  page.setCropBox(-100, -100, 500, 600);
  const output = await tools.applyAnnotations(PDFLib, await document.save(), [
    { page: 0, type: 'text', left: 150, top: 200, text: 'X', fontSize: 12, baseline: 10 }
  ]);
  const [matrix] = textMatrices(await contentOf(output, 0));
  near(matrix[4], 150, 'x');
  near(matrix[5], 190, 'y');
});

test('positionBlock places blocks at corners and centres inside the margin', () => {
  const page = { width: 100, height: 200 };
  const block = { width: 20, height: 10 };
  assert.deepEqual(tools.positionBlock('top-left', page, block, 5), { left: 5, top: 5 });
  assert.deepEqual(tools.positionBlock('bottom-right', page, block, 5), { left: 75, top: 185 });
  assert.deepEqual(tools.positionBlock('middle-center', page, block, 5), { left: 40, top: 95 });
});

test('fitImage contains and centres', () => {
  assert.deepEqual(tools.fitImage(200, 100, 100, 100), { x: 0, y: 25, width: 100, height: 50 });
  assert.throws(() => tools.fitImage(0, 1, 1, 1), { code: 'image-size' });
});

test('orientedImageDraw fills the target rectangle for each EXIF rotation', () => {
  const rect = { x: 10, y: 20, width: 30, height: 40 };
  assert.deepEqual(tools.orientedImageDraw(rect, 0), { x: 10, y: 20, width: 30, height: 40, rotate: 0 });
  assert.deepEqual(tools.orientedImageDraw(rect, 90), { x: 10, y: 60, width: 40, height: 30, rotate: -90 });
  assert.deepEqual(tools.orientedImageDraw(rect, 180), { x: 40, y: 60, width: 30, height: 40, rotate: 180 });
  assert.deepEqual(tools.orientedImageDraw(rect, 270), { x: 40, y: 20, width: 40, height: 30, rotate: 90 });
});

test('jpegRotation reads the EXIF orientation tag', () => {
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
  assert.equal(tools.jpegRotation(jpeg(6, true)), 90);
  assert.equal(tools.jpegRotation(jpeg(3, false)), 180);
  assert.equal(tools.jpegRotation(jpeg(8, true)), 270);
  assert.equal(tools.jpegRotation(jpeg(1, true)), 0);
  assert.equal(tools.jpegRotation(PNG_1X1), 0);
  assert.equal(tools.jpegRotation(Uint8Array.from([0xff, 0xd8, 0xff])), 0);
});

// -----------------------------------------------------------------------------
// Small helpers
// -----------------------------------------------------------------------------

test('formatStamp, hexToRgb and baseName', () => {
  assert.equal(tools.formatStamp('Page {n} of {total} · {date}', { n: 2, total: 9, date: 'today' }), 'Page 2 of 9 · today');
  assert.deepEqual(tools.hexToRgb('#ff0000'), { r: 1, g: 0, b: 0 });
  assert.deepEqual(tools.hexToRgb('nope'), { r: 0, g: 0, b: 0 });
  assert.equal(tools.baseName('C:\\fakepath\\Report: final.v2.pdf'), 'Report_ final.v2');
  assert.equal(tools.baseName('', 'merged'), 'merged');
});

test('crc32 matches the standard check value', () => {
  assert.equal(tools.crc32(new TextEncoder().encode('123456789')), 0xcbf43926);
});

test('createZip writes a readable stored archive', () => {
  const entries = [
    { name: 'a.txt', data: new TextEncoder().encode('hello') },
    { name: 'pages/b.pdf', data: Uint8Array.from([1, 2, 3]) }
  ];
  const zip = tools.createZip(entries, new Date(2024, 0, 2, 3, 4, 6));
  const view = new DataView(zip.buffer);

  const end = zip.length - 22;
  assert.equal(view.getUint32(end, true), 0x06054b50);
  assert.equal(view.getUint16(end + 10, true), 2);

  let central = view.getUint32(end + 16, true);
  const names = [];
  for (let i = 0; i < 2; i += 1) {
    assert.equal(view.getUint32(central, true), 0x02014b50);
    const nameLength = view.getUint16(central + 28, true);
    const localOffset = view.getUint32(central + 42, true);
    const size = view.getUint32(central + 20, true);
    const name = new TextDecoder().decode(zip.subarray(central + 46, central + 46 + nameLength));
    const dataStart = localOffset + 30 + view.getUint16(localOffset + 26, true);
    const data = zip.subarray(dataStart, dataStart + size);
    assert.equal(view.getUint32(central + 16, true), tools.crc32(data));
    names.push([name, Array.from(data)]);
    central += 46 + nameLength;
  }
  assert.deepEqual(names, [['a.txt', [104, 101, 108, 108, 111]], ['pages/b.pdf', [1, 2, 3]]]);
});

// -----------------------------------------------------------------------------
// PDF operations
// -----------------------------------------------------------------------------

test('assemblePages merges, reorders, repeats, drops and rotates pages', async () => {
  const first = await makePdf([[100, 100], [101, 100]], [0, 90]);
  const second = await makePdf([[200, 100]]);

  const output = await tools.assemblePages(PDFLib, [first, second], [
    { source: 1, index: 0 },
    { source: 0, index: 1, rotate: 90 },
    { source: 0, index: 0, rotate: -90 },
    { source: 1, index: 0, rotate: 180 }
  ]);

  assert.deepEqual(await describePages(output), [
    { width: 200, height: 100, rotation: 0 },
    { width: 101, height: 100, rotation: 180 },
    { width: 100, height: 100, rotation: 270 },
    { width: 200, height: 100, rotation: 180 }
  ]);
});

test('assemblePages keeps form fields and their values', async () => {
  const document = await PDFLib.PDFDocument.create();
  const first = document.addPage([300, 300]);
  const second = document.addPage([300, 300]);
  const form = document.getForm();
  const name = form.createTextField('name');
  name.setText('Ada');
  name.addToPage(first, { x: 10, y: 10 });
  form.createCheckBox('agree').addToPage(second, { x: 10, y: 10 });
  const pdf = await document.save();

  const output = await tools.assemblePages(PDFLib, [pdf], [{ source: 0, index: 1 }, { source: 0, index: 0 }]);
  const fields = (await PDFLib.PDFDocument.load(output)).getForm();
  assert.deepEqual(fields.getFields().map((field) => field.getName()).sort(), ['agree', 'name']);
  assert.equal(fields.getTextField('name').getText(), 'Ada');

  const [onlySecond] = await tools.splitPdf(PDFLib, pdf, [[1]]);
  assert.deepEqual((await PDFLib.PDFDocument.load(onlySecond)).getForm().getFields().map((field) => field.getName()), ['agree']);
});

/** Every object in the saved file, as text (streams decoded), to prove something is absent. */
async function allObjectText(bytes) {
  const document = await PDFLib.PDFDocument.load(bytes);
  return document.context.enumerateIndirectObjects().map(([, object]) => {
    if (object instanceof PDFLib.PDFRawStream) {
      return `${object.dict.toString()}\n${Buffer.from(PDFLib.decodePDFRawStream(object).decode()).toString('latin1')}`;
    }
    return object.toString();
  }).join('\n');
}

test('dropping pages also drops their form fields and content from the saved file', async () => {
  const document = await PDFLib.PDFDocument.create();
  const font = await document.embedFont(PDFLib.StandardFonts.Helvetica);
  const first = document.addPage([300, 300]);
  const second = document.addPage([300, 300]);
  second.drawText('SECRETPAGETEXT', { x: 20, y: 150, size: 12, font });
  const form = document.getForm();
  const visible = form.createTextField('person.public');
  visible.setText('PublicValue');
  visible.addToPage(first, { x: 10, y: 10 });
  const hidden = form.createTextField('person.secret');
  hidden.setText('SecretValue');
  hidden.addToPage(second, { x: 10, y: 10 });
  const pdf = await document.save();

  const [kept] = await tools.splitPdf(PDFLib, pdf, [[0]]);
  const output = await PDFLib.PDFDocument.load(kept);
  assert.equal(output.getPageCount(), 1);
  assert.deepEqual(output.getForm().getFields().map((field) => field.getName()), ['person.public']);
  assert.equal(output.getForm().getTextField('person.public').getText(), 'PublicValue');

  const text = await allObjectText(kept);
  assert.doesNotMatch(text, /SecretValue/, 'dropped field value is not in the file');
  assert.doesNotMatch(text, /5345435245545041474554455854|SECRETPAGETEXT/i, 'dropped page content is not in the file');
  assert.doesNotMatch(text, /\(secret\)|<736563726574>/i, 'dropped field is not in the file');
});

test('a link to a dropped page does not carry that page into the saved file', async () => {
  const document = await PDFLib.PDFDocument.create();
  const font = await document.embedFont(PDFLib.StandardFonts.Helvetica);
  const first = document.addPage([300, 300]);
  const second = document.addPage([300, 300]);
  second.drawText('SECRETPAGETEXT', { x: 20, y: 150, size: 12, font });
  const link = document.context.register(document.context.obj({
    Type: 'Annot',
    Subtype: 'Link',
    Rect: [10, 10, 100, 30],
    Dest: [second.ref, 'Fit']
  }));
  first.node.set(PDFLib.PDFName.of('Annots'), document.context.obj([link]));

  const pdf = await document.save();
  const [kept] = await tools.splitPdf(PDFLib, pdf, [[0]]);
  assert.doesNotMatch(await allObjectText(kept), /5345435245545041474554455854|SECRETPAGETEXT/i);

  // A link between two kept pages still works and points at the output page.
  const both = await PDFLib.PDFDocument.load(await tools.assemblePages(PDFLib, [pdf], [{ source: 0, index: 0 }, { source: 0, index: 1 }]));
  const annots = both.getPage(0).node.Annots();
  assert.equal(annots.size(), 1);
  const destination = both.context.lookup(annots.get(0)).lookup(PDFLib.PDFName.of('Dest'));
  assert.equal(String(destination.get(0)), String(both.getPage(1).ref));
});

/** Number of page objects anywhere in the saved file (not just in the page tree). */
async function pageObjectCount(bytes) {
  const document = await PDFLib.PDFDocument.load(bytes);
  return document.context.enumerateIndirectObjects()
    .filter(([, object]) => object instanceof PDFLib.PDFDict && String(object.get(PDFLib.PDFName.of('Type'))) === '/Page')
    .length;
}

test('a form button that jumps to a dropped page does not carry that page into the saved file', async () => {
  const document = await PDFLib.PDFDocument.create();
  const font = await document.embedFont(PDFLib.StandardFonts.Helvetica);
  const first = document.addPage([300, 300]);
  const second = document.addPage([300, 300]);
  second.drawText('SECRETPAGETEXT', { x: 20, y: 150, size: 12, font });
  const context = document.context;
  const button = context.register(context.obj({
    Type: 'Annot',
    Subtype: 'Widget',
    FT: 'Btn',
    Ff: 65536,
    T: PDFLib.PDFHexString.fromText('jump'),
    Rect: [10, 10, 100, 30],
    P: first.ref,
    A: { S: 'GoTo', D: [second.ref, 'Fit'] },
    AA: { U: { S: 'GoTo', D: [second.ref, 'Fit'] } }
  }));
  first.node.set(PDFLib.PDFName.of('Annots'), context.obj([button]));
  document.catalog.set(PDFLib.PDFName.of('AcroForm'), context.obj({ Fields: [button] }));
  const pdf = await document.save();

  const [kept] = await tools.splitPdf(PDFLib, pdf, [[0]]);
  assert.equal(await pageObjectCount(kept), 1, 'only the kept page is in the file');
  assert.doesNotMatch(await allObjectText(kept), /5345435245545041474554455854|SECRETPAGETEXT/i);
  const output = await PDFLib.PDFDocument.load(kept);
  assert.deepEqual(output.getForm().getFields().map((field) => field.getName()), ['jump'], 'the button itself is kept');

  // Keeping both pages keeps the jump, pointed at the output page.
  const both = await PDFLib.PDFDocument.load(await tools.assemblePages(PDFLib, [pdf], [{ source: 0, index: 1 }, { source: 0, index: 0 }]));
  const widget = both.context.lookup(both.getPage(1).node.Annots().get(0));
  const action = widget.lookup(PDFLib.PDFName.of('A'));
  assert.equal(String(action.lookup(PDFLib.PDFName.of('D')).get(0)), String(both.getPage(0).ref));
});

test('named link destinations survive assembly and are re-pointed or removed', async () => {
  const document = await PDFLib.PDFDocument.create();
  const font = await document.embedFont(PDFLib.StandardFonts.Helvetica);
  const first = document.addPage([300, 300]);
  const second = document.addPage([300, 300]);
  second.drawText('SECRETPAGETEXT', { x: 20, y: 150, size: 12, font });
  const context = document.context;
  // One name from the old-style /Dests dictionary, one from the /Names /Dests name tree.
  document.catalog.set(PDFLib.PDFName.of('Dests'), context.obj({ chapter2: [second.ref, 'Fit'] }));
  document.catalog.set(PDFLib.PDFName.of('Names'), context.obj({
    Dests: context.obj({ Names: [PDFLib.PDFString.of('appendix'), context.obj({ D: [second.ref, 'Fit'] })] })
  }));
  const byName = context.register(context.obj({ Type: 'Annot', Subtype: 'Link', Rect: [10, 10, 100, 30], Dest: 'chapter2' }));
  const byTree = context.register(context.obj({
    Type: 'Annot', Subtype: 'Link', Rect: [10, 40, 100, 60], A: { S: 'GoTo', D: PDFLib.PDFString.of('appendix') }
  }));
  first.node.set(PDFLib.PDFName.of('Annots'), context.obj([byName, byTree]));
  const pdf = await document.save();

  const both = await PDFLib.PDFDocument.load(await tools.assemblePages(PDFLib, [pdf], [{ source: 0, index: 0 }, { source: 0, index: 1 }]));
  const annots = both.getPage(0).node.Annots();
  assert.equal(annots.size(), 2);
  const target = String(both.getPage(1).ref);
  assert.equal(String(both.context.lookup(annots.get(0)).lookup(PDFLib.PDFName.of('Dest')).get(0)), target);
  const action = both.context.lookup(annots.get(1)).lookup(PDFLib.PDFName.of('A'));
  assert.equal(String(action.lookup(PDFLib.PDFName.of('D')).get(0)), target);

  const [kept] = await tools.splitPdf(PDFLib, pdf, [[0]]);
  assert.equal(await pageObjectCount(kept), 1);
  assert.equal((await PDFLib.PDFDocument.load(kept)).getPage(0).node.Annots()?.size() ?? 0, 0, 'links to the dropped page are removed');
});

test('an annotation that refers to an annotation on a dropped page does not carry that page in', async () => {
  const document = await PDFLib.PDFDocument.create();
  const font = await document.embedFont(PDFLib.StandardFonts.Helvetica);
  const first = document.addPage([300, 300]);
  const second = document.addPage([300, 300]);
  second.drawText('SECRETPAGETEXT', { x: 20, y: 150, size: 12, font });
  const context = document.context;
  const original = context.register(context.obj({ Type: 'Annot', Subtype: 'Text', Rect: [10, 10, 30, 30], P: second.ref }));
  second.node.set(PDFLib.PDFName.of('Annots'), context.obj([original]));
  const reply = context.register(context.obj({ Type: 'Annot', Subtype: 'Text', Rect: [10, 10, 30, 30], P: first.ref, IRT: original }));
  first.node.set(PDFLib.PDFName.of('Annots'), context.obj([reply]));

  const [kept] = await tools.splitPdf(PDFLib, await document.save(), [[0]]);
  assert.equal(await pageObjectCount(kept), 1, 'neither the dropped page nor a duplicate of the kept page is in the file');
  assert.doesNotMatch(await allObjectText(kept), /5345435245545041474554455854|SECRETPAGETEXT/i);
  const output = await PDFLib.PDFDocument.load(kept);
  const kept0 = output.context.lookup(output.getPage(0).node.Annots().get(0));
  assert.equal(String(kept0.get(PDFLib.PDFName.of('P'))), String(output.getPage(0).ref), 'the reply still points at its own page');
});

test('an unforeseen reference to a dropped page refuses to save instead of leaking it', async () => {
  const document = await PDFLib.PDFDocument.create();
  const first = document.addPage([300, 300]);
  const second = document.addPage([300, 300]);
  first.node.set(PDFLib.PDFName.of('PieceInfo'), document.context.obj({ Custom: { Target: second.ref } }));

  await assert.rejects(tools.splitPdf(PDFLib, await document.save(), [[0]]), { code: 'unsupported-structure' });
});

test('assemblePages rejects empty selections and missing pages', async () => {
  const pdf = await makePdf([[100, 100]]);
  await assert.rejects(tools.assemblePages(PDFLib, [pdf], []), { code: 'no-pages' });
  await assert.rejects(tools.assemblePages(PDFLib, [pdf], [{ source: 0, index: 3 }]), { code: 'range-out-of-bounds' });
});

test('loadPdf reports unreadable files without leaking parser errors', async () => {
  await assert.rejects(tools.loadPdf(PDFLib, new TextEncoder().encode('not a pdf')), { code: 'unreadable' });
});

test('splitPdf produces one document per group', async () => {
  const pdf = await makePdf([[100, 100], [101, 100], [102, 100]]);
  const parts = await tools.splitPdf(PDFLib, pdf, tools.parsePageRanges('1-2, 3', 3));
  assert.equal(parts.length, 2);
  assert.deepEqual((await describePages(parts[0])).map((page) => page.width), [100, 101]);
  assert.deepEqual((await describePages(parts[1])).map((page) => page.width), [102]);
});

test('imagesToPdf creates one page per image with the requested page size', async () => {
  const output = await tools.imagesToPdf(PDFLib, [
    { bytes: PNG_1X1, type: 'png' },
    { bytes: PNG_1X1, type: 'png' }
  ], { pageSize: 'a4', orientation: 'landscape', margin: 20 });
  assert.deepEqual(await describePages(output), [
    { width: 842, height: 595, rotation: 0 },
    { width: 842, height: 595, rotation: 0 }
  ]);

  const fitted = await tools.imagesToPdf(PDFLib, [{ bytes: PNG_1X1, type: 'png' }], { pageSize: 'fit', margin: 0 });
  const [page] = await describePages(fitted);
  assert.equal(page.width, 1);
  await assert.rejects(tools.imagesToPdf(PDFLib, [], {}), { code: 'no-images' });
});

test('addWatermark draws centred, tilted text on the selected pages only', async () => {
  const pdf = await makePdf([[400, 400], [400, 400]]);
  const output = await tools.addWatermark(PDFLib, pdf, {
    text: 'DRAFT', fontSize: 40, angle: 0, position: 'middle-center', pages: '2'
  });

  assert.equal(textMatrices(await contentOf(output, 0)).length, 0);
  const [matrix] = textMatrices(await contentOf(output, 1));
  const font = await (await PDFLib.PDFDocument.create()).embedFont(PDFLib.StandardFonts.HelveticaBold);
  near(matrix[0], 1, 'no rotation');
  near(matrix[4], 200 - font.widthOfTextAtSize('DRAFT', 40) / 2, 'centred horizontally');

  await assert.rejects(tools.addWatermark(PDFLib, pdf, { text: '  ' }), { code: 'text-empty' });
  await assert.rejects(tools.addWatermark(PDFLib, pdf, { text: 'मसौदा' }), { code: 'text-unsupported' });
});

test('addTextStamps numbers stamped pages and keeps text upright on rotated pages', async () => {
  const pdf = await makePdf([[200, 300], [200, 300], [200, 300]], [0, 90, 0]);
  const output = await tools.addTextStamps(PDFLib, pdf, {
    stamps: [{ template: 'Page {n} of {total}', position: 'bottom-right' }],
    fontSize: 10,
    margin: 28,
    skipFirst: true
  });

  assert.equal(textMatrices(await contentOf(output, 0)).length, 0, 'first page skipped');

  const font = await (await PDFLib.PDFDocument.create()).embedFont(PDFLib.StandardFonts.Helvetica);
  const width = font.widthOfTextAtSize('Page 1 of 2', 10);
  const descent = font.heightAtSize(10) - font.heightAtSize(10, { descender: false });
  const [rotated] = textMatrices(await contentOf(output, 1));
  // A 90° page displays 300 wide by 200 high; display (u, v) maps to PDF (x = v, y = u).
  near(rotated[1], 1, 'text rotated 90° counter-clockwise');
  near(rotated[4], 200 - 28 - descent, 'baseline near the displayed bottom edge');
  near(rotated[5], 300 - 28 - width, 'right-aligned on the displayed page');

  const content = await contentOf(output, 2);
  assert.match(content, /<506167652032206F662032>/, 'third page is "Page 2 of 2"');
});

test('applyAnnotations flattens text, shapes and images', async () => {
  const pdf = await makePdf([[300, 300]]);
  const output = await tools.applyAnnotations(PDFLib, pdf, [
    { page: 0, type: 'text', left: 10, top: 20, width: 100, height: 40, text: 'Line one\nLine two', fontSize: 12 },
    { page: 0, type: 'highlight', left: 10, top: 100, width: 50, height: 10, color: '#ffff00' },
    { page: 0, type: 'whiteout', left: 10, top: 120, width: 50, height: 10 },
    { page: 0, type: 'rect', left: 10, top: 140, width: 50, height: 10, color: '#ff0000' },
    { page: 0, type: 'image', left: 100, top: 100, width: 80, height: 40, imageBytes: PNG_1X1, imageType: 'png' }
  ]);

  const content = await contentOf(output, 0);
  const matrices = textMatrices(content);
  assert.equal(matrices.length, 2);
  near(matrices[1][5] - matrices[0][5], -14.4, 'second line one line-height lower');
  assert.match(content, /Do/, 'image drawn');
  const document = await PDFLib.PDFDocument.load(output);
  const states = document.getPage(0).node.Resources().lookup(PDFLib.PDFName.of('ExtGState'), PDFLib.PDFDict);
  const blendModes = states.values().map((ref) => document.context.lookup(ref).get(PDFLib.PDFName.of('BM'))?.toString());
  assert.ok(blendModes.includes('/Multiply'), 'highlight uses multiply blending');
  assert.equal((content.match(/^h$/gm) || []).length, 3, 'three rectangles drawn');
  // Display top 140 + height 10 on a 300pt page puts the rectangle's bottom edge at y = 150.
  assert.match(content, /^1 0 0 1 10 150 cm$/m, 'outline rectangle anchored at its bottom-left corner');

  const measured = await tools.applyAnnotations(PDFLib, pdf, [
    { page: 0, type: 'text', left: 10, top: 20, text: 'Measured', fontSize: 12, baseline: 11 }
  ]);
  near(textMatrices(await contentOf(measured, 0))[0][5], 300 - 20 - 11, 'uses the measured baseline');

  await assert.rejects(tools.applyAnnotations(PDFLib, pdf, []), { code: 'no-annotations' });
  await assert.rejects(tools.applyAnnotations(PDFLib, pdf, [{ page: 4, type: 'rect' }]), { code: 'range-out-of-bounds' });
});
