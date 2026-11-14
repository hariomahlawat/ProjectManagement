const test = require('node:test');
const assert = require('node:assert/strict');
const tools = require('./photo-tools-core.js');

// A stand-in encoder: file size follows the pixel count and the quality, as a real JPG does.
function fakeEncoder(bytesPerPixel) {
  const calls = [];
  const encode = async (width, height, quality) => {
    calls.push({ width, height, quality });
    return new Uint8Array(Math.round(width * height * bytesPerPixel * quality));
  };
  return { encode, calls };
}

test('formatBytes uses B, KB and MB of 1,000 and rounds up', () => {
  assert.equal(tools.formatBytes(512), '512 B');
  assert.equal(tools.formatBytes(98000), '98 KB');
  assert.equal(tools.formatBytes(3200000), '3.2 MB');
  // Rounded up, so a file just over a limit never reads as fitting it.
  assert.equal(tools.formatBytes(50000), '50 KB');
  assert.equal(tools.formatBytes(50001), '51 KB');
  assert.equal(tools.formatBytes(51300), '52 KB');
  // MB to the nearest 0.1 MB.
  assert.equal(tools.formatBytes(999999), '1.0 MB');
  assert.equal(tools.formatBytes(1000001), '1.0 MB');
  assert.equal(tools.formatBytes(1012857), '1.0 MB');
  assert.equal(tools.formatBytes(1050000), '1.1 MB');
  assert.equal(tools.formatBytes(1049999), '1.0 MB');
});

test('parseTargetBytes reads KB and MB of 1,000 and rejects nonsense', () => {
  assert.equal(tools.parseTargetBytes('100', 'KB'), 100000);
  assert.equal(tools.parseTargetBytes('1.5', 'MB'), 1500000);
  assert.equal(tools.parseTargetBytes('1,5', 'MB'), 1500000);
  assert.equal(tools.parseTargetBytes('', 'KB'), null);
  assert.equal(tools.parseTargetBytes('0', 'KB'), null);
  assert.equal(tools.parseTargetBytes('abc', 'KB'), null);
  // A KB number written with thousands is not read as a decimal: "1,000 KB" is not 1 KB.
  assert.equal(tools.parseTargetBytes('1,000', 'KB'), 1000000);
  assert.equal(tools.parseTargetBytes('1,500,000', 'KB'), 1500000000);
  assert.equal(tools.parseTargetBytes('1,00,000', 'KB'), 100000000, 'Indian grouping');
  assert.equal(tools.parseTargetBytes('1,50', 'MB'), 1500000);
  // An MB number with a comma is a decimal: nobody means 1,500 MB.
  assert.equal(tools.parseTargetBytes('1,500', 'MB'), 1500000);
  assert.equal(tools.parseTargetBytes('1,0,0', 'KB'), null);
});

test('a target of 50 KB passes upload forms that count 1,000 or 1,024 bytes to a KB', () => {
  const target = tools.parseTargetBytes('50', 'KB');
  assert.ok(target <= 50 * 1000 && target <= 50 * 1024);
});

test('resizeDimensions keeps the shape and never enlarges', () => {
  assert.deepEqual(tools.resizeDimensions(4000, 3000, { type: 'keep' }), { width: 4000, height: 3000 });
  assert.deepEqual(tools.resizeDimensions(4000, 3000, { type: 'longest', value: 1600 }), { width: 1600, height: 1200 });
  assert.deepEqual(tools.resizeDimensions(3000, 4000, { type: 'longest', value: 1600 }), { width: 1200, height: 1600 });
  assert.deepEqual(tools.resizeDimensions(800, 600, { type: 'longest', value: 1600 }), { width: 800, height: 600 });
  assert.deepEqual(tools.resizeDimensions(4000, 3000, { type: 'percent', value: 50 }), { width: 2000, height: 1500 });
  assert.deepEqual(tools.resizeDimensions(4000, 3000, { type: 'percent', value: 250 }), { width: 4000, height: 3000 });
});

test('parseExactSide takes whole numbers from 16 to 10000 only', () => {
  assert.equal(tools.parseExactSide('600'), 600);
  assert.equal(tools.parseExactSide(' 16 '), 16);
  assert.equal(tools.parseExactSide('10000'), 10000);
  for (const bad of ['', '15', '10001', '12.5', '-40', 'abc', null, undefined]) {
    assert.equal(tools.parseExactSide(bad), null, String(bad));
  }
});

test('exactFit crop to fill cuts the edges evenly and fills the size', () => {
  // A wide photo into a square: the sides are cut.
  assert.deepEqual(tools.exactFit(4000, 3000, { width: 600, height: 600 }, 'fill'), {
    width: 600, height: 600,
    area: { x: 500, y: 0, width: 3000, height: 3000 },
    place: { x: 0, y: 0, width: 600, height: 600 }
  });
  // A tall photo into a wide size: the top and bottom are cut.
  const tall = tools.exactFit(3000, 4000, { width: 600, height: 400 }, 'fill');
  assert.deepEqual(tall.area, { x: 0, y: 1000, width: 3000, height: 2000 });
  // A small photo is made larger.
  const small = tools.exactFit(300, 200, { width: 600, height: 400 }, 'fill');
  assert.deepEqual(small.area, { x: 0, y: 0, width: 300, height: 200 });
  assert.deepEqual(small.place, { x: 0, y: 0, width: 600, height: 400 });
  // A small photo into a long thin size keeps the size's shape: cropped, never stretched.
  for (const [w, h, target] of [[100, 100, { width: 10000, height: 16 }], [100, 100, { width: 16, height: 10000 }], [333, 77, { width: 7001, height: 19 }]]) {
    const plan = tools.exactFit(w, h, target, 'fill');
    const { area } = plan;
    assert.ok(Math.abs(area.width / area.height - target.width / target.height) < 1e-9, `${w}x${h} → ${target.width}x${target.height}`);
    assert.ok(area.x >= 0 && area.y >= 0 && area.x + area.width <= w + 1e-9 && area.y + area.height <= h + 1e-9);
  }
  const thin = tools.exactFit(100, 100, { width: 10000, height: 16 }, 'fill').area;
  assert.deepEqual([thin.x, thin.width], [0, 100]);
  assert.ok(Math.abs(thin.height - 0.16) < 1e-9 && Math.abs(thin.y - 49.92) < 1e-9);
});

test('exactFit fit inside keeps the whole photo with an even border', () => {
  const wide = tools.exactFit(4000, 3000, { width: 600, height: 600 }, 'fit');
  assert.deepEqual(wide.area, { x: 0, y: 0, width: 4000, height: 3000 });
  assert.deepEqual(wide.place, { x: 0, y: 75, width: 600, height: 450 });
  const tall = tools.exactFit(3000, 4000, { width: 600, height: 400 }, 'fit');
  assert.deepEqual(tall.place, { x: 150, y: 0, width: 300, height: 400 });
  // Same shape: no border.
  assert.deepEqual(tools.exactFit(1200, 800, { width: 600, height: 400 }, 'fit').place, { x: 0, y: 0, width: 600, height: 400 });
  // Never wider or taller than the size, whatever the rounding.
  for (const [w, h] of [[1001, 999], [7, 3], [3, 7], [10000, 17]]) {
    const plan = tools.exactFit(w, h, { width: 333, height: 211 }, 'fit');
    assert.ok(plan.place.x + plan.place.width <= 333 && plan.place.y + plan.place.height <= 211, `${w}x${h}`);
  }
});

test('watermarkPlan sizes the mark from the shorter side and places it in a corner', () => {
  const plan = tools.watermarkPlan({ width: 4000, height: 3000, place: 'bottom-right', textWidth100: 500 });
  assert.equal(plan.fontSize, 135);
  assert.deepEqual([plan.x, plan.y, plan.align, plan.baseline], [3910, 2910, 'right', 'bottom']);
  const left = tools.watermarkPlan({ width: 4000, height: 3000, place: 'bottom-left', textWidth100: 500 });
  assert.deepEqual([left.x, left.align], [90, 'left']);
  // The same share on a small photo.
  assert.equal(tools.watermarkPlan({ width: 400, height: 300, place: 'centre', textWidth100: 100 }).fontSize, 14);
  // Never below 12 px for a tiny photo with short text.
  assert.equal(tools.watermarkPlan({ width: 100, height: 60, place: 'centre', textWidth100: 50 }).fontSize, 12);
});

test('watermarkPlan makes long text smaller to fit the photo', () => {
  // 60 characters: about 4000 px wide at 100 px.
  const plan = tools.watermarkPlan({ width: 1200, height: 800, place: 'bottom-right', textWidth100: 4000 });
  const textWidth = plan.fontSize * 40;
  assert.ok(textWidth <= 1200 - 24 * 2, `text ${textWidth}px`);
  assert.ok(plan.fontSize < 36);
  const across = tools.watermarkPlan({ width: 1200, height: 800, place: 'across', textWidth100: 4000 });
  assert.ok(across.fontSize * 40 <= 800 * 0.8);
});

test('watermarkPlan across the photo repeats round the centre at a slant', () => {
  const plan = tools.watermarkPlan({ width: 1200, height: 800, place: 'across', textWidth100: 400 });
  assert.equal(plan.fontSize, 48);
  assert.deepEqual([plan.x, plan.y, plan.align, plan.baseline], [600, 400, 'center', 'middle']);
  assert.ok(plan.angle < 0);
  assert.equal(plan.stepX, Math.round(48 * 4 + 48 * 3));
  assert.equal(plan.stepY, 192);
  assert.equal(plan.reach, Math.ceil(Math.hypot(1200, 800) / 2));
  assert.deepEqual(tools.WATERMARK_PLACES, ['bottom-right', 'bottom-left', 'centre', 'across']);
});

test('outputType follows the choice, or the original where it can be written', () => {
  assert.equal(tools.outputType('image/png', 'jpeg').extension, 'jpg');
  assert.equal(tools.outputType('image/png', 'same').extension, 'png');
  assert.equal(tools.outputType('image/webp', 'same').extension, 'webp');
  assert.equal(tools.outputType('image/bmp', 'same').extension, 'jpg');
  assert.equal(tools.outputType('', undefined).extension, 'jpg');
});

test('outputNames adds a suffix and keeps names unique', () => {
  const names = tools.outputNames([
    { name: 'Site.JPG', extension: 'jpg' },
    { name: 'site.png', extension: 'jpg' },
    { name: 'plan.final.png', extension: 'png' },
    { name: '', extension: 'jpg' }
  ], '-small');
  assert.deepEqual(names, ['Site-small.jpg', 'site-small (2).jpg', 'plan.final-small.png', 'photo-small.jpg']);
});

test('outputNames never repeats a name, even one that already looks numbered', () => {
  const names = tools.outputNames([
    { name: 'a.jpg', extension: 'jpg' },
    { name: 'a.jpg', extension: 'jpg' },
    { name: 'a (2).jpg', extension: 'jpg' }
  ]);
  assert.deepEqual(names, ['a.jpg', 'a (2).jpg', 'a (2) (2).jpg']);
  assert.equal(new Set(names.map(name => name.toLowerCase())).size, names.length);
});

test('toGreyscale makes every pixel grey and leaves transparency alone', () => {
  const pixels = new Uint8ClampedArray([255, 0, 0, 255, 0, 255, 0, 128, 10, 10, 10, 0]);
  tools.toGreyscale(pixels);
  assert.deepEqual(Array.from(pixels), [76, 76, 76, 255, 150, 150, 150, 128, 10, 10, 10, 0]);
});

test('retryUnderOriginal asks for a smaller file only when quality can give one', () => {
  const jpg = { lossy: true, sameType: true };
  assert.equal(tools.retryUnderOriginal({ ...jpg, resultBytes: 90_000, originalBytes: 60_000 }), 59_999);
  assert.equal(tools.retryUnderOriginal({ ...jpg, resultBytes: 60_000, originalBytes: 60_000 }), 59_999, 'the same size is no reduction');
  assert.equal(tools.retryUnderOriginal({ ...jpg, resultBytes: 40_000, originalBytes: 60_000 }), null, 'already smaller');
  // A PNG has no quality to lower, and a PNG saved as JPG is a different kind of file.
  assert.equal(tools.retryUnderOriginal({ lossy: false, sameType: true, resultBytes: 90_000, originalBytes: 60_000 }), null);
  assert.equal(tools.retryUnderOriginal({ lossy: true, sameType: false, resultBytes: 80_000, originalBytes: 25_000 }), null);
  assert.equal(tools.retryUnderOriginal({ ...jpg, resultBytes: 5, originalBytes: 1 }), null);
});

test('encodeToTarget without a target encodes once', async () => {
  const { encode, calls } = fakeEncoder(0.5);
  const result = await tools.encodeToTarget({ encode, width: 100, height: 100, lossy: true });
  assert.equal(calls.length, 1);
  assert.equal(result.reached, true);
  assert.equal(result.width, 100);
});

test('encodeToTarget keeps top quality when the photo already fits', async () => {
  const { encode, calls } = fakeEncoder(0.5);
  const result = await tools.encodeToTarget({ encode, width: 100, height: 100, lossy: true, targetBytes: 100000 });
  assert.equal(calls.length, 1);
  assert.equal(result.quality, tools.BEST_QUALITY);
  assert.equal(result.reached, true);
});

test('encodeToTarget lowers quality before it shrinks the photo', async () => {
  const { encode } = fakeEncoder(1);
  // 1000 x 1000 at quality q is q MB; 600 KB needs quality about 0.6 at full size.
  const result = await tools.encodeToTarget({ encode, width: 1000, height: 1000, lossy: true, targetBytes: 600000 });
  assert.equal(result.reached, true);
  assert.equal(result.width, 1000);
  assert.ok(result.bytes.length <= 600000);
  assert.ok(result.quality > 0.55 && result.quality <= 0.6, `quality ${result.quality}`);
});

test('encodeToTarget shrinks the photo when the lowest quality is still too big', async () => {
  const { encode } = fakeEncoder(1);
  const result = await tools.encodeToTarget({ encode, width: 4000, height: 3000, lossy: true, targetBytes: 100 * 1024 });
  assert.equal(result.reached, true);
  assert.ok(result.bytes.length <= 100 * 1024);
  assert.ok(result.width < 4000);
  assert.ok(Math.abs(result.width / result.height - 4 / 3) < 0.01);
  assert.ok(result.quality >= tools.LOWEST_QUALITY);
});

test('encodeToTarget shrinks a PNG, which has no quality setting', async () => {
  const { encode, calls } = fakeEncoder(2);
  const result = await tools.encodeToTarget({ encode, width: 1000, height: 500, lossy: false, targetBytes: 200000 });
  assert.equal(result.reached, true);
  assert.ok(result.bytes.length <= 200000);
  assert.ok(result.width < 1000);
  assert.ok(calls.every(call => call.quality === 1));
});

test('encodeToTarget says so when the target cannot be reached', async () => {
  // Every file is at least 5000 bytes, whatever the size: a 1 KB target is impossible.
  const encode = async () => new Uint8Array(5000);
  const result = await tools.encodeToTarget({ encode, width: 400, height: 300, lossy: true, targetBytes: 1024 });
  assert.equal(result.reached, false);
  assert.equal(result.bytes.length, 5000);
});

test('isSupportedPhoto accepts JPG, PNG and WEBP by type or by name', () => {
  assert.equal(tools.isSupportedPhoto('image/jpeg', 'a'), true);
  assert.equal(tools.isSupportedPhoto('', 'scan.PNG'), true);
  assert.equal(tools.isSupportedPhoto('image/webp', ''), true);
  assert.equal(tools.isSupportedPhoto('image/heic', 'IMG_1.HEIC'), false);
  assert.equal(tools.isSupportedPhoto('application/pdf', 'a.pdf'), false);
});

test('encodeToTarget with fixedSize never changes the dimensions', async () => {
  const { encode, calls } = fakeEncoder(1);
  const fits = await tools.encodeToTarget({
    encode, width: 413, height: 531, lossy: true, targetBytes: 50 * 1024, fixedSize: true, lowestQuality: 0.2
  });
  assert.equal(fits.reached, true);
  assert.equal(fits.width, 413);
  assert.ok(fits.bytes.length <= 50 * 1024);
  assert.ok(fits.quality < tools.LOWEST_QUALITY, 'may go below the usual floor to keep the exact size');

  const tooSmall = await tools.encodeToTarget({
    encode, width: 413, height: 531, lossy: true, targetBytes: 1024, fixedSize: true, lowestQuality: 0.2
  });
  assert.equal(tooSmall.reached, false);
  assert.equal(tooSmall.width, 413);
  assert.equal(tooSmall.height, 531);
  assert.ok(calls.every(call => call.width === 413 && call.height === 531));
});

test('passport sizes are the print sizes at 300 dpi', () => {
  const byKey = Object.fromEntries(tools.PASSPORT_SIZES.map(size => [size.key, size]));
  assert.deepEqual([byKey['35x45'].width, byKey['35x45'].height], [413, 531]);
  assert.deepEqual([byKey['2x2'].width, byKey['2x2'].height], [600, 600]);
  assert.deepEqual([byKey['20x25'].width, byKey['20x25'].height], [236, 295]);
  // The online size has the shape of 35 × 45 mm, so the face guide fits it the same way.
  assert.deepEqual([byKey['online-630x810'].width, byKey['online-630x810'].height], [630, 810]);
  assert.ok(Math.abs(630 / 810 - 413 / 531) < 0.002);
  assert.equal(tools.lengthToPixels('35', 'mm'), 413);
  assert.equal(tools.lengthToPixels('45', 'mm'), 531);
  assert.equal(tools.lengthToPixels('2', 'inch'), 600);
  assert.equal(tools.lengthToPixels('3,5', 'mm'), 41);
  assert.equal(tools.lengthToPixels('', 'mm'), null);
  assert.equal(tools.lengthToPixels('-4', 'mm'), null);
});

test('coverScale is the zoom at which the photo just fills the frame', () => {
  assert.equal(tools.coverScale(4000, 3000, 400, 500, 0), 500 / 3000);
  assert.equal(tools.coverScale(3000, 4000, 400, 500, 0), 400 / 3000);
  // A tilted photo must be larger to leave no empty corner.
  const tilted = tools.coverScale(4000, 3000, 400, 500, 10 * Math.PI / 180);
  assert.ok(tilted > 500 / 3000);
  // A quarter turn swaps the photo's sides: it is now 3000 wide and 4000 tall.
  assert.ok(Math.abs(tools.coverScale(4000, 3000, 400, 500, Math.PI / 2) - 400 / 3000) < 1e-9);
});

test('clampOffset keeps the frame inside the photo', () => {
  const place = { imageWidth: 1000, imageHeight: 1000, frameWidth: 400, frameHeight: 500, angle: 0, scale: 1 };
  assert.deepEqual(tools.clampOffset({ ...place, x: 100, y: -50 }), { x: 100, y: -50 });
  assert.deepEqual(tools.clampOffset({ ...place, x: 900, y: -900 }), { x: 300, y: -250 });
  // At the smallest zoom there is no room to move along the tight side.
  const tight = tools.clampOffset({ ...place, scale: 0.5, x: 80, y: 80 });
  assert.deepEqual(tight, { x: 50, y: 0 });

  // Tilted: every corner of the frame must still land on the photo.
  const angle = 12 * Math.PI / 180;
  const scale = tools.coverScale(1000, 800, 400, 500, angle) * 1.2;
  const moved = tools.clampOffset({ imageWidth: 1000, imageHeight: 800, frameWidth: 400, frameHeight: 500, angle, scale, x: 500, y: 400 });
  for (const [cx, cy] of [[-200, -250], [200, -250], [200, 250], [-200, 250]]) {
    const dx = cx - moved.x;
    const dy = cy - moved.y;
    const alongX = (dx * Math.cos(angle) + dy * Math.sin(angle)) / scale;
    const alongY = (-dx * Math.sin(angle) + dy * Math.cos(angle)) / scale;
    assert.ok(Math.abs(alongX) <= 500 + 1e-6 && Math.abs(alongY) <= 400 + 1e-6, `corner ${cx},${cy} is off the photo`);
  }
});

test('measureLighting finds the real range and the colour cast', () => {
  // A dull, yellowish photo: values 60-180, with blue lower than red and green.
  const pixels = new Uint8ClampedArray(4 * 1000);
  for (let i = 0; i < 1000; i += 1) {
    const value = 60 + Math.round((i / 999) * 120);
    pixels.set([value, value, Math.round(value * 0.8), 255], i * 4);
  }
  const lighting = tools.measureLighting(pixels);
  assert.ok(lighting.low > 40 && lighting.low < 70, `low ${lighting.low}`);
  assert.ok(lighting.high > 150 && lighting.high < 185, `high ${lighting.high}`);
  assert.ok(lighting.gains[2] > 1 && lighting.gains[0] < 1, 'blue is lifted and red eased');

  const flat = new Uint8ClampedArray(4 * 100).fill(120);
  assert.deepEqual(tools.measureLighting(flat), { low: 0, high: 255, gains: [1, 1, 1] });
  assert.deepEqual(tools.measureLighting(new Uint8ClampedArray(0)), { low: 0, high: 255, gains: [1, 1, 1] });
});

test('applyLighting stretches the range, and brightness alone only lifts', () => {
  const pixels = new Uint8ClampedArray([50, 50, 50, 200, 200, 200, 200, 255]);
  tools.applyLighting(pixels, { low: 50, high: 200, gains: [1, 1, 1] }, 0);
  assert.deepEqual(Array.from(pixels), [0, 0, 0, 200, 255, 255, 255, 255]);

  const plain = new Uint8ClampedArray([100, 110, 120, 255]);
  tools.applyLighting(plain, null, 10);
  assert.deepEqual(Array.from(plain), [113, 123, 133, 255]);

  const same = new Uint8ClampedArray([1, 2, 3, 4]);
  tools.applyLighting(same, null, 0);
  assert.deepEqual(Array.from(same), [1, 2, 3, 4]);
});

test('setJpegDpi writes the print resolution into a JFIF header only', () => {
  const jpeg = new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 0, 16, 0x4a, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0xff, 0xdb]);
  const stamped = tools.setJpegDpi(jpeg, 300);
  assert.equal(stamped[13], 1);
  assert.equal((stamped[14] << 8) | stamped[15], 300);
  assert.equal((stamped[16] << 8) | stamped[17], 300);
  assert.equal(stamped.length, jpeg.length);
  assert.equal(jpeg[13], 0, 'the original bytes are not changed');

  const png = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
  assert.equal(tools.setJpegDpi(png, 300), png);
});

test('sheetLayout fills the paper and turns it when that fits more', () => {
  const photoPaper = tools.SHEET_SIZES.find(sheet => sheet.key === '4x6');
  const a4 = tools.SHEET_SIZES.find(sheet => sheet.key === 'a4');

  const passport = tools.sheetLayout(photoPaper, 413, 531);
  assert.equal(passport.count, 8, '35 x 45 mm: eight on 4 x 6 inch paper');
  assert.deepEqual([passport.width, passport.height, passport.columns, passport.rows], [1800, 1200, 4, 2]);
  assert.equal(passport.positions.length, 8);
  for (const { x, y } of passport.positions) {
    assert.ok(x >= photoPaper.margin && x + 413 <= passport.width - photoPaper.margin);
    assert.ok(y >= photoPaper.margin && y + 531 <= passport.height - photoPaper.margin);
  }
  const [first, second] = passport.positions;
  assert.equal(second.x - first.x, 413 + 24, 'copies are a cutting gap apart');

  const onA4 = tools.sheetLayout(a4, 413, 531);
  assert.deepEqual([onA4.width, onA4.columns, onA4.rows, onA4.count], [2480, 5, 6, 30]);

  const tooBig = tools.sheetLayout(photoPaper, 2000, 2000);
  assert.equal(tooBig.count, 0);
  assert.deepEqual(tooBig.positions, []);
});

test('sheetLayout knows the four papers and how many passport photos each takes', () => {
  assert.deepEqual(tools.SHEET_SIZES.map(sheet => sheet.key), ['4x6', '5x7', 'a5', 'a4']);
  const counts = tools.SHEET_SIZES.map(sheet => tools.sheetLayout(sheet, 413, 531).count);
  assert.deepEqual(counts, [8, 9, 12, 30]);
  for (const sheet of tools.SHEET_SIZES) {
    const plan = tools.sheetLayout(sheet, 413, 531);
    for (const { x, y } of plan.positions) {
      assert.ok(x >= sheet.margin && x + 413 <= plan.width - sheet.margin, `${sheet.key} keeps its side edge clear`);
      assert.ok(y >= sheet.margin && y + 531 <= plan.height - sheet.margin, `${sheet.key} keeps its top edge clear`);
    }
  }
});

test('sheetLayout places only the copies asked for, from the top left', () => {
  const photoPaper = tools.SHEET_SIZES.find(sheet => sheet.key === '4x6');
  const full = tools.sheetLayout(photoPaper, 413, 531);

  const five = tools.sheetLayout(photoPaper, 413, 531, 5);
  assert.equal(five.count, 8, 'count is still how many fit');
  assert.deepEqual(five.positions, full.positions.slice(0, 5), 'the same places as on a full sheet');
  assert.equal(five.positions[4].y, full.positions[4].y, 'the fifth starts the second row');
  assert.equal(five.positions[4].x, full.positions[0].x);

  assert.equal(tools.sheetLayout(photoPaper, 413, 531, 99).positions.length, 8, 'never more than fit');
  for (const odd of [0, -3, 2.5, NaN, null, undefined, '4']) {
    assert.equal(tools.sheetLayout(photoPaper, 413, 531, odd).positions.length, 8, `${odd} fills the sheet`);
  }
});

test('sheetPdfPlan gives the paper and each photo their true size in points', () => {
  const near = (actual, expected, what) => assert.ok(Math.abs(actual - expected) < 0.01, `${what}: ${actual} is not ${expected}`);
  const a4 = tools.SHEET_SIZES.find(sheet => sheet.key === 'a4');
  const layout = tools.sheetLayout(a4, 413, 531, 7);
  const plan = tools.sheetPdfPlan(layout, 413, 531);

  // A4 is 210 x 297 mm: 595.28 x 841.89 points. 300 dpi pixels land within a tenth of a point.
  assert.ok(Math.abs(plan.pageWidth - 595.28) < 0.1 && Math.abs(plan.pageHeight - 841.89) < 0.1);
  assert.equal(plan.photos.length, 7);
  assert.equal(plan.cuts.length, 7);

  const [first] = plan.photos;
  near(first.width, 413 * 0.24, 'photo width in points');
  near(first.height, 531 * 0.24, 'photo height in points');
  // 413 x 531 pixels is 35 x 45 mm to the nearest pixel, so the print is within 0.05 mm.
  assert.ok(Math.abs(first.width - 35 / 25.4 * 72) < 0.15 && Math.abs(first.height - 45 / 25.4 * 72) < 0.15);
  // PDF counts up from the bottom: the first photo, at the top of the sheet, is the highest.
  near(first.x, layout.positions[0].x * 0.24, 'left edge');
  near(first.y + first.height, plan.pageHeight - layout.positions[0].y * 0.24, 'top edge');
  const sixth = plan.photos[5];
  assert.ok(sixth.y < first.y, 'the second row is lower on the page');
  for (const box of plan.photos) {
    assert.ok(box.x >= 0 && box.y >= 0 && box.x + box.width <= plan.pageWidth && box.y + box.height <= plan.pageHeight);
  }

  // The cut line is drawn around the photo, half its width outside each edge.
  const [cut] = plan.cuts;
  near(cut.x, first.x - plan.lineWidth / 2, 'cut line left');
  near(cut.width, first.width + plan.lineWidth, 'cut line width');
  near(plan.lineWidth, 0.48, 'a 2 pixel line');

  // Paper on its side keeps its turned shape.
  const photoPaper = tools.SHEET_SIZES.find(sheet => sheet.key === '4x6');
  const turned = tools.sheetPdfPlan(tools.sheetLayout(photoPaper, 413, 531), 413, 531);
  near(turned.pageWidth, 6 * 72, '6 inches wide');
  near(turned.pageHeight, 4 * 72, '4 inches high');
});

test('coverCrop takes the middle of a photo of another shape', () => {
  assert.deepEqual(tools.coverCrop(413, 531, 413, 531), { x: 0, y: 0, width: 413, height: 531, whole: true });
  assert.deepEqual(tools.coverCrop(826, 1062, 413, 531), { x: 0, y: 0, width: 826, height: 1062, whole: true });

  // A wide photo for a tall frame loses its sides.
  const wide = tools.coverCrop(4000, 3000, 413, 531);
  assert.equal(wide.height, 3000);
  assert.equal(wide.width, Math.round(3000 * 413 / 531));
  assert.equal(wide.x, Math.floor((4000 - wide.width) / 2));
  assert.equal(wide.y, 0);
  assert.equal(wide.whole, false);

  // A tall photo for a square frame loses its top and bottom.
  const tall = tools.coverCrop(1000, 3000, 600, 600);
  assert.deepEqual(tall, { x: 0, y: 1000, width: 1000, height: 1000, whole: false });

  // Rounding alone is not a different shape.
  assert.equal(tools.coverCrop(414, 531, 413, 531).whole, true);
  const tiny = tools.coverCrop(1, 1, 413, 531);
  assert.ok(tiny.width >= 1 && tiny.height >= 1);
});

test('coverCrop moves and zooms the piece inside the photo', () => {
  // A wide photo for a tall frame: there is room to move sideways only.
  const middle = tools.coverCrop(4000, 3000, 413, 531);
  const left = tools.coverCrop(4000, 3000, 413, 531, { zoom: 1, x: 0, y: 0.5 });
  const right = tools.coverCrop(4000, 3000, 413, 531, { zoom: 1, x: 1, y: 0.5 });
  assert.deepEqual([left.x, left.y, left.width, left.height], [0, 0, middle.width, 3000]);
  assert.equal(right.x + right.width, 4000);
  assert.deepEqual(tools.coverCrop(4000, 3000, 413, 531, { zoom: 1, x: 0.5, y: 0.5 }), middle, 'the middle is where it starts');

  // Zoom takes a smaller piece of the same shape, and then there is room both ways.
  const zoomed = tools.coverCrop(4000, 3000, 413, 531, { zoom: 2, x: 0.5, y: 0 });
  assert.equal(zoomed.height, 1500);
  assert.equal(zoomed.y, 0);
  assert.ok(Math.abs(zoomed.width / zoomed.height - 413 / 531) < 0.002);
  const low = tools.coverCrop(4000, 3000, 413, 531, { zoom: 2, x: 0.5, y: 1 });
  assert.equal(low.y + low.height, 3000);

  // Out-of-range values are held to what is possible; whole is about shape, not zoom.
  const odd = tools.coverCrop(4000, 3000, 413, 531, { zoom: 99, x: -4, y: NaN });
  assert.equal(odd.height, Math.round(3000 / tools.COVER_MAX_ZOOM));
  assert.equal(odd.x, 0);
  assert.equal(odd.y, Math.floor((3000 - odd.height) / 2));
  assert.equal(tools.coverCrop(826, 1062, 413, 531, { zoom: 2, x: 0.5, y: 0.5 }).whole, true);
  for (const place of [{ zoom: 1, x: 0, y: 0 }, { zoom: 3, x: 1, y: 1 }, { zoom: 1.7, x: 0.3, y: 0.9 }]) {
    const piece = tools.coverCrop(1234, 987, 600, 600, place);
    assert.ok(piece.x >= 0 && piece.y >= 0 && piece.x + piece.width <= 1234 && piece.y + piece.height <= 987);
  }
});

test('dragCover follows the pointer and stops at the edge of the photo', () => {
  const start = { zoom: 1, x: 0.5, y: 0.5 };
  // Dragging the photo right by a tenth of the frame shows more of its left.
  const moved = tools.dragCover(4000, 3000, 413, 531, start, 0.1, 0);
  const before = tools.coverCrop(4000, 3000, 413, 531, start);
  const after = tools.coverCrop(4000, 3000, 413, 531, moved);
  assert.ok(Math.abs((before.x - after.x) - before.width * 0.1) <= 1);
  assert.equal(moved.y, 0.5, 'no room up or down at this zoom');
  assert.equal(moved.zoom, 1);

  assert.equal(tools.dragCover(4000, 3000, 413, 531, start, 50, 0).x, 0, 'stops at the left edge');
  assert.equal(tools.dragCover(4000, 3000, 413, 531, start, -50, 0).x, 1, 'stops at the right edge');

  const zoomed = tools.dragCover(4000, 3000, 413, 531, { zoom: 2, x: 0.5, y: 0.5 }, 0, -0.2);
  assert.ok(zoomed.y > 0.5, 'dragging up shows more of the bottom');
  // A photo of the same shape at no zoom has nowhere to go.
  assert.deepEqual(tools.dragCover(413, 531, 413, 531, start, 0.3, 0.3), start);
});

test('pixelsToMillimetres names a print size', () => {
  assert.equal(tools.pixelsToMillimetres(413), 35);
  assert.equal(tools.pixelsToMillimetres(531), 45);
  assert.equal(tools.pixelsToMillimetres(600), 51);
  assert.equal(tools.pixelsToMillimetres(96, 96), 25);
});

test('measurePaper finds paper and ink, and cleanPaper makes black on white', () => {
  // 97 paper pixels, some a little shadowed, and 3 ink pixels.
  const values = [...Array(60).fill(190), ...Array(37).fill(170), 40, 40, 115];
  const pixels = new Uint8ClampedArray(values.flatMap(value => [value, value, value, 255]));
  const levels = tools.measurePaper(pixels);
  assert.deepEqual(levels, { ink: 40, paper: 190 });

  tools.cleanPaper(pixels, levels);
  assert.equal(pixels[0], 255, 'paper is white');
  assert.equal(pixels[60 * 4], 255, 'lightly shadowed paper is white too');
  assert.equal(pixels[97 * 4], 0, 'ink is black');
  const edge = pixels[99 * 4];
  assert.ok(edge > 0 && edge < 255, 'the soft edge of a stroke stays soft');
  assert.equal(pixels[3], 255, 'alpha is untouched');

  const blank = new Uint8ClampedArray(Array(40).fill(200));
  assert.equal(tools.measurePaper(blank), null, 'a blank sheet has no ink to find');
  assert.equal(tools.measurePaper(new Uint8ClampedArray(0)), null);
  const same = new Uint8ClampedArray([10, 20, 30, 255]);
  assert.deepEqual(Array.from(tools.cleanPaper(same, null)), [10, 20, 30, 255]);
});

test('straightenedSize trims a turned photo to the same shape, and editedSize follows the steps', () => {
  assert.deepEqual(tools.straightenedSize(4000, 3000, 0), { width: 4000, height: 3000 });
  const turned = tools.straightenedSize(4000, 3000, 5);
  assert.ok(turned.width < 4000 && turned.height < 3000);
  assert.ok(Math.abs(turned.width / turned.height - 4 / 3) < 0.002, 'the shape is kept');
  assert.deepEqual(tools.straightenedSize(4000, 3000, -5), turned, 'left and right trim the same');

  const steps = [
    { type: 'rotate', turns: 1 },
    { type: 'flip', axis: 'h' },
    { type: 'crop', x: 10, y: 10, width: 2000, height: 1000 },
    { type: 'rotate', turns: -1 },
    { type: 'resize', width: 500, height: 1000 },
    { type: 'rotate', turns: 2 }
  ];
  assert.deepEqual(tools.editedSize(4000, 3000, []), { width: 4000, height: 3000 });
  assert.deepEqual(tools.editedSize(4000, 3000, steps.slice(0, 1)), { width: 3000, height: 4000 });
  assert.deepEqual(tools.editedSize(4000, 3000, steps.slice(0, 4)), { width: 1000, height: 2000 });
  assert.deepEqual(tools.editedSize(4000, 3000, steps), { width: 500, height: 1000 });
});

test('defaultCrop and moveCrop keep the box on the photo', () => {
  const bounds = { width: 4000, height: 3000 };
  assert.deepEqual(tools.defaultCrop(bounds, null), { x: 0, y: 0, width: 4000, height: 3000 });
  assert.deepEqual(tools.defaultCrop(bounds, 1), { x: 500, y: 0, width: 3000, height: 3000 });
  assert.deepEqual(tools.defaultCrop(bounds, 16 / 9), { x: 0, y: 375, width: 4000, height: 2250 });

  const box = { x: 100, y: 100, width: 1000, height: 500 };
  assert.deepEqual(tools.moveCrop(box, 50, -20, bounds), { x: 150, y: 80, width: 1000, height: 500 });
  assert.deepEqual(tools.moveCrop(box, -900, 9000, bounds), { x: 0, y: 2500, width: 1000, height: 500 });
});

test('resizeCrop moves one side, keeps a fixed shape, and stops at the edges', () => {
  const bounds = { width: 4000, height: 3000 };
  const box = { x: 1000, y: 1000, width: 1000, height: 1000 };

  assert.deepEqual(tools.resizeCrop(box, 'e', { x: 2500, y: 0 }, null, bounds), { x: 1000, y: 1000, width: 1500, height: 1000 });
  assert.deepEqual(tools.resizeCrop(box, 'nw', { x: 800, y: 900 }, null, bounds), { x: 800, y: 900, width: 1200, height: 1100 });
  assert.deepEqual(tools.resizeCrop(box, 'se', { x: 9000, y: 9000 }, null, bounds), { x: 1000, y: 1000, width: 3000, height: 2000 });
  // Dragged past the opposite side: the box stops at its smallest size instead of flipping.
  assert.deepEqual(tools.resizeCrop(box, 'w', { x: 3000, y: 0 }, null, bounds), { x: 1984, y: 1000, width: 16, height: 1000 });

  // Square: the corner follows the pointer, the opposite corner stays.
  assert.deepEqual(tools.resizeCrop(box, 'se', { x: 2600, y: 2200 }, 1, bounds), { x: 1000, y: 1000, width: 1600, height: 1600 });
  assert.deepEqual(tools.resizeCrop(box, 'nw', { x: 500, y: 900 }, 1, bounds), { x: 500, y: 500, width: 1500, height: 1500 });
  // The photo's edge limits a fixed shape in both directions.
  assert.deepEqual(tools.resizeCrop(box, 'se', { x: 4000, y: 3000 }, 1, bounds), { x: 1000, y: 1000, width: 2000, height: 2000 });
  // A corner point that moves in along both sides shrinks a fixed shape (the keyboard does this).
  const whole = { x: 0, y: 0, width: 3000, height: 3000 };
  assert.deepEqual(tools.resizeCrop(whole, 'se', { x: 2970, y: 2970 }, 1, bounds), { x: 0, y: 0, width: 2970, height: 2970 });
  // A side handle grows the box evenly about its middle.
  assert.deepEqual(tools.resizeCrop(box, 's', { x: 0, y: 2400 }, 2, bounds), { x: 100, y: 1000, width: 2800, height: 1400 });
});

// A small grey picture: `fill(x, y)` gives the brightness of each pixel.
function picture(width, height, fill) {
  const pixels = new Uint8ClampedArray(width * height * 4);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const value = fill(x, y);
      const colour = Array.isArray(value) ? value : [value, value, value];
      pixels.set([...colour, 255], (y * width + x) * 4);
    }
  }
  return pixels;
}

const at = (pixels, width, x, y) => Array.from(pixels.slice((y * width + x) * 4, (y * width + x) * 4 + 3));

test('adjustPixels: zero changes nothing, and each control moves the right way', () => {
  const same = new Uint8ClampedArray([10, 120, 240, 200]);
  assert.deepEqual(Array.from(tools.adjustPixels(same, {})), [10, 120, 240, 200]);

  assert.deepEqual(Array.from(tools.adjustPixels(new Uint8ClampedArray([100, 100, 100, 255]), { brightness: 25 })), [132, 132, 132, 255]);
  const contrast = tools.adjustPixels(new Uint8ClampedArray([78, 78, 78, 255, 178, 178, 178, 255]), { contrast: 50 });
  assert.deepEqual(Array.from(contrast), [48, 48, 48, 255, 208, 208, 208, 255]);
  const warm = tools.adjustPixels(new Uint8ClampedArray([100, 100, 100, 255]), { warmth: 50 });
  assert.deepEqual(Array.from(warm), [130, 100, 70, 255]);
  const grey = tools.adjustPixels(new Uint8ClampedArray([200, 100, 50, 255]), { saturation: -50 });
  assert.ok(grey[0] === grey[1] && grey[1] === grey[2], 'no saturation is grey');
  const vivid = tools.adjustPixels(new Uint8ClampedArray([150, 100, 100, 255]), { saturation: 50 });
  assert.ok(vivid[0] > 150 && vivid[1] < 100, 'more saturation pushes colours apart');
});

test('filterPixels: six fixed recipes', () => {
  assert.deepEqual(tools.FILTERS.map(item => item.key), ['bw', 'sepia', 'warm', 'cool', 'vivid', 'faded']);
  const start = () => new Uint8ClampedArray([180, 120, 60, 255]);
  const bw = tools.filterPixels(start(), 'bw');
  assert.ok(bw[0] === bw[1] && bw[1] === bw[2]);
  const sepia = tools.filterPixels(start(), 'sepia');
  assert.ok(sepia[0] > sepia[1] && sepia[1] > sepia[2], 'sepia is brown: red over green over blue');
  assert.ok(tools.filterPixels(start(), 'warm')[2] < 60);
  assert.ok(tools.filterPixels(start(), 'cool')[2] > 60);
  assert.deepEqual(Array.from(tools.filterPixels(start(), 'nothing')), [180, 120, 60, 255]);
});

test('sharpenPixels and softenPixels leave flat areas alone and work on edges', () => {
  const flat = picture(6, 6, () => 120);
  assert.deepEqual(Array.from(tools.sharpenPixels(flat.slice(), 6, 6)), Array.from(flat));
  assert.deepEqual(Array.from(tools.softenPixels(flat.slice(), 6, 6)), Array.from(flat));

  // A soft step from dark to light: sharpening makes the dark side darker and the light side lighter.
  const edge = picture(6, 6, x => (x < 3 ? 100 : 160));
  const sharp = tools.sharpenPixels(edge.slice(), 6, 6);
  assert.ok(at(sharp, 6, 2, 3)[0] < 100 && at(sharp, 6, 3, 3)[0] > 160);
  assert.equal(at(sharp, 6, 0, 3)[0], 100, 'away from the edge nothing changes');

  // One bright speck is pulled towards its surroundings.
  const speck = picture(5, 5, (x, y) => (x === 2 && y === 2 ? 250 : 50));
  const calm = tools.softenPixels(speck.slice(), 5, 5);
  assert.ok(at(calm, 5, 2, 2)[0] < 200 && at(calm, 5, 2, 2)[0] > 50);
  assert.equal(calm[3], 255, 'alpha is untouched');

  // A white shape on a see-through background keeps a clean edge: invisible pixels do not count.
  const cutOut = new Uint8ClampedArray(5 * 5 * 4);
  cutOut.set([255, 255, 255, 255], (2 * 5 + 2) * 4);
  for (const change of [tools.softenPixels, tools.sharpenPixels]) {
    const result = change(cutOut.slice(), 5, 5);
    assert.deepEqual(at(result, 5, 2, 2), [255, 255, 255], `${change.name} leaves the lone white pixel white`);
    assert.equal(result[(2 * 5 + 2) * 4 + 3], 255);
    assert.equal(result[3], 0, 'the see-through part stays see-through');
  }
});

test('evenLighting lifts the dark side and leaves an evenly lit photo alone', () => {
  const even = picture(48, 48, () => 130);
  assert.deepEqual(Array.from(tools.evenLighting(even.slice(), 48, 48)), Array.from(even));

  // Light falls off from left to right.
  const shaded = picture(96, 48, x => 200 - x);
  const before = at(shaded, 96, 4, 24)[0] - at(shaded, 96, 91, 24)[0];
  tools.evenLighting(shaded, 96, 48);
  const after = at(shaded, 96, 4, 24)[0] - at(shaded, 96, 91, 24)[0];
  assert.ok(after < before * 0.6, `the difference across the photo shrinks (${before} to ${after})`);

  // An evenly lit shape beside a see-through area is left alone: nothing invisible is measured.
  const half = picture(96, 48, () => 200);
  for (let y = 0; y < 48; y += 1) {
    for (let x = 48; x < 96; x += 1) half.set([0, 0, 0, 0], (y * 96 + x) * 4);
  }
  const lit = tools.evenLighting(half.slice(), 96, 48);
  assert.deepEqual(at(lit, 96, 10, 24), [200, 200, 200]);
  assert.deepEqual(at(lit, 96, 47, 24), [200, 200, 200], 'right up to the edge of the shape');
  const empty = new Uint8ClampedArray(8 * 8 * 4);
  assert.deepEqual(Array.from(tools.evenLighting(empty.slice(), 8, 8)), Array.from(empty));
  assert.equal(tools.measurePaper(empty), null, 'a fully see-through picture has no paper');
});

test('cleanDocument gives white paper and dark ink under uneven light, and keeps colour', () => {
  // Paper lit unevenly (210 on the left down to 150 on the right) with a dark line and a red mark.
  const page = picture(96, 48, (x, y) => {
    const paper = 210 - Math.round(x * 60 / 95);
    if (y === 10 && x % 3 === 0) return 30;
    if (y === 30 && x === 48) return [Math.round(paper * 0.9), 40, 40];
    return paper;
  });
  tools.cleanDocument(page, 96, 48);
  assert.ok(at(page, 96, 5, 40)[0] >= 250, 'paper on the bright side is white');
  assert.ok(at(page, 96, 90, 40)[0] >= 250, 'paper on the dark side is white too');
  assert.ok(at(page, 96, 48, 10)[0] < 60, 'ink stays dark');
  const mark = at(page, 96, 48, 30);
  assert.ok(mark[0] > mark[1] + 80, 'a red mark stays red');

  // The same page under a yellow lamp: the paper still comes out white, not yellow.
  const yellowed = picture(96, 48, (x, y) => {
    const paper = 210 - Math.round(x * 60 / 95);
    if (y === 10 && x % 3 === 0) return [30, 28, 20];
    return [paper, Math.round(paper * 0.94), Math.round(paper * 0.7)];
  });
  tools.cleanDocument(yellowed, 96, 48);
  for (const x of [5, 90]) {
    const paper = at(yellowed, 96, x, 40);
    assert.ok(Math.min(...paper) >= 245, `yellowish paper is white at x=${x} (${paper})`);
  }
  assert.ok(at(yellowed, 96, 48, 10)[0] < 60, 'ink stays dark under the lamp');
});

test('applyPixelStep runs each clean-up and reports which steps are pixel steps', () => {
  assert.deepEqual(tools.CLEAN_UPS.map(item => item.key), ['enhance', 'cast', 'even', 'document', 'denoise', 'sharpen']);
  assert.equal(tools.isPixelStep({ type: 'cleanup', key: 'enhance' }), true);
  assert.equal(tools.isPixelStep({ type: 'adjust' }), true);
  assert.equal(tools.isPixelStep({ type: 'filter', key: 'bw' }), true);
  assert.equal(tools.isPixelStep({ type: 'crop' }), false);

  // A dull, yellowish picture: enhance stretches it and removes most of the tint.
  const dull = () => picture(20, 20, x => [90 + x * 4, 85 + x * 4, 60 + x * 4]);
  const enhanced = tools.applyPixelStep(dull(), 20, 20, { type: 'cleanup', key: 'enhance' });
  assert.ok(at(enhanced, 20, 0, 0)[0] < 60 && at(enhanced, 20, 19, 0)[0] > 220, 'the range is stretched');
  const cast = tools.applyPixelStep(dull(), 20, 20, { type: 'cleanup', key: 'cast' });
  const tint = (pixels) => at(pixels, 20, 10, 10)[0] - at(pixels, 20, 10, 10)[2];
  assert.ok(tint(cast) < tint(dull()), 'the yellow tint is smaller');
  assert.ok(Math.abs(at(cast, 20, 10, 10)[1] - at(dull(), 20, 10, 10)[1]) < 12, 'the light is left about the same');

  for (const { key } of tools.CLEAN_UPS) {
    const result = tools.applyPixelStep(dull(), 20, 20, { type: 'cleanup', key });
    assert.equal(result.length, 1600);
  }
  const untouched = dull();
  assert.deepEqual(Array.from(tools.applyPixelStep(untouched.slice(), 20, 20, { type: 'crop' })), Array.from(untouched));
  assert.deepEqual(tools.editedSize(400, 300, [{ type: 'cleanup', key: 'sharpen' }, { type: 'adjust', brightness: 5 }]), { width: 400, height: 300 });
});

test('mark measures scale with the photo, and dragRect stays on it', () => {
  assert.deepEqual(tools.markMeasures({ width: 4000, height: 3000 }, 'medium'), { line: 32, text: 200 });
  assert.deepEqual(tools.markMeasures({ width: 400, height: 300 }, 'thick'), { line: 6, text: 32 });
  assert.deepEqual(tools.markMeasures({ width: 100, height: 80 }, 'thin'), { line: 2, text: 10 }, 'never too small to see');
  assert.deepEqual(tools.markMeasures({ width: 4000, height: 3000 }, 'nothing'), { line: 32, text: 200 });

  const bounds = { width: 400, height: 300 };
  assert.deepEqual(tools.dragRect({ x: 300, y: 200 }, { x: 100, y: 50 }, bounds), { x: 100, y: 50, width: 200, height: 150 });
  assert.deepEqual(tools.dragRect({ x: 350, y: 250 }, { x: 900, y: -40 }, bounds), { x: 350, y: 0, width: 50, height: 250 });
  assert.deepEqual(tools.MARK_KINDS.map(item => item.key), ['box', 'circle', 'arrow', 'pen', 'text', 'number']);
});

test('arrowHead points along the line, and thinPoints keeps the ends', () => {
  const head = tools.arrowHead({ x: 0, y: 0 }, { x: 100, y: 0 }, 4);
  assert.deepEqual(head.tip, { x: 100, y: 0 });
  assert.equal(head.left.x, 82);
  assert.equal(head.right.x, 82);
  assert.ok(Math.abs(head.left.y + head.right.y) < 1e-9 && Math.abs(head.left.y) > 5, 'the corners sit either side of the line');
  assert.ok(head.neck.x > 82 && head.neck.x < 100);
  assert.equal(tools.arrowHead({ x: 5, y: 5 }, { x: 5, y: 5 }, 4), null);
  const short = tools.arrowHead({ x: 0, y: 0 }, { x: 0, y: 6 }, 4);
  assert.ok(Math.abs(short.left.y) < 1e-9, 'a very short arrow is all head');

  const line = [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 2, y: 0 }, { x: 10, y: 0 }, { x: 11, y: 0 }, { x: 12, y: 0 }];
  assert.deepEqual(tools.thinPoints(line, 5), [{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 12, y: 0 }]);
  assert.deepEqual(tools.thinPoints(line.slice(0, 2), 5), line.slice(0, 2));
});

test('penStroke ignores a tap, and a wiggle that never leaves the line width', () => {
  assert.equal(tools.penStroke([], 4), null);
  assert.equal(tools.penStroke([{ x: 5, y: 5 }], 4), null, 'a tap');
  assert.equal(tools.penStroke([{ x: 5, y: 5 }, { x: 5, y: 5 }], 4), null, 'a tap that sent a move');
  assert.equal(tools.penStroke([{ x: 5, y: 5 }, { x: 7, y: 6 }, { x: 5, y: 6 }], 4), null, 'a wiggle under the line width');
  // A loop that comes back to its start still counts: what matters is how far it went.
  const loop = [{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 10 }, { x: 0, y: 0 }];
  assert.deepEqual(tools.penStroke(loop, 4), tools.thinPoints(loop, 2));
});

test('hidePixels removes the detail: pixelate gives flat squares, blur blends them', () => {
  assert.equal(tools.hideBlock(200, 100), 20);
  assert.equal(tools.hideBlock(30, 20), 6);

  // Fine black and white stripes one pixel wide: nothing of them may remain.
  const stripes = () => picture(60, 60, x => (x % 2 ? 255 : 0));
  const squares = tools.hidePixels(stripes(), 60, 60, 'pixelate');
  for (let x = 0; x < 60; x += 1) {
    const value = at(squares, 60, x, 30)[0];
    assert.ok(value > 110 && value < 145, `stripe at x=${x} is gone (${value})`);
  }
  assert.deepEqual(at(squares, 60, 0, 0), at(squares, 60, 5, 5), 'one square is one colour');
  const blurred = tools.hidePixels(stripes(), 60, 60, 'blur');
  for (let x = 0; x < 60; x += 1) {
    const value = at(blurred, 60, x, 30)[0];
    assert.ok(value > 110 && value < 145, `stripe at x=${x} is gone under blur (${value})`);
  }

  // A dark half and a light half: pixelate keeps a hard step, blur a gradual one.
  const halves = () => picture(60, 60, x => (x < 30 ? 20 : 220));
  const hard = tools.hidePixels(halves(), 60, 60, 'pixelate');
  assert.equal(at(hard, 60, 29, 30)[0], 20);
  assert.equal(at(hard, 60, 30, 30)[0], 220);
  const soft = tools.hidePixels(halves(), 60, 60, 'blur');
  assert.ok(at(soft, 60, 29, 30)[0] > 60 && at(soft, 60, 30, 30)[0] < 180, 'the step is spread out');
  assert.equal(soft[3], 255, 'a solid photo stays solid');

  // See-through pixels do not darken what is hidden.
  const cutOut = picture(12, 12, () => 200);
  for (let i = 0; i < 6 * 12 * 4; i += 4) cutOut.set([0, 0, 0, 0], i);
  const hidden = tools.hidePixels(cutOut, 12, 12, 'pixelate');
  assert.deepEqual(at(hidden, 12, 3, 9), [200, 200, 200]);

  // Black writing on a see-through page is all in the alpha: its outline must go too.
  for (const mode of ['pixelate', 'blur']) {
    const writing = new Uint8ClampedArray(60 * 60 * 4);
    for (let y = 0; y < 60; y += 1) {
      for (let x = 0; x < 60; x += 2) writing[(y * 60 + x) * 4 + 3] = 255;
    }
    tools.hidePixels(writing, 60, 60, mode);
    for (let x = 0; x < 60; x += 1) {
      const alpha = writing[(30 * 60 + x) * 4 + 3];
      assert.ok(alpha > 110 && alpha < 145, `${mode}: the outline at x=${x} is gone (${alpha})`);
    }
  }
});

// The neighbour average as it was first written: a full-size copy of the picture. The new code
// keeps only three rows of sums; it must give the same picture.
function referenceSoftCopy(pixels, width, height) {
  const across = new Float32Array(pixels.length);
  const output = new Float64Array(pixels.length);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const i = (y * width + x) * 4;
      for (const j of [(y * width + Math.max(0, x - 1)) * 4, i, (y * width + Math.min(width - 1, x + 1)) * 4]) {
        const alpha = pixels[j + 3];
        for (let c = 0; c < 3; c += 1) across[i + c] += pixels[j + c] * alpha;
        across[i + 3] += alpha;
      }
    }
  }
  for (let y = 0; y < height; y += 1) {
    const up = Math.max(0, y - 1) * width;
    const down = Math.min(height - 1, y + 1) * width;
    for (let x = 0; x < width; x += 1) {
      const i = (y * width + x) * 4;
      const above = (up + x) * 4;
      const below = (down + x) * 4;
      const weight = across[above + 3] + across[i + 3] + across[below + 3];
      for (let c = 0; c < 3; c += 1) {
        output[i + c] = weight > 0 ? (across[above + c] + across[i + c] + across[below + c]) / weight : pixels[i + c];
      }
    }
  }
  return output;
}

test('sharpenPixels and softenPixels give the same picture as a full-size copy would', () => {
  let seed = 7;
  const random = () => {
    seed = (seed * 1103515245 + 12345) % 2147483648;
    return seed % 256;
  };
  for (const [width, height] of [[1, 1], [1, 6], [6, 1], [2, 2], [7, 5], [13, 9]]) {
    const start = new Uint8ClampedArray(width * height * 4);
    for (let i = 0; i < start.length; i += 1) start[i] = random();
    // Some fully see-through pixels, as a cut-out PNG has.
    for (let i = 3; i < start.length; i += 20) start[i] = 0;
    const soft = referenceSoftCopy(start, width, height);
    const expectedSoft = Uint8ClampedArray.from(start, (value, i) => (i % 4 === 3 ? value : (value + soft[i]) / 2));
    const expectedSharp = Uint8ClampedArray.from(start, (value, i) => (i % 4 === 3 ? value : value + (value - soft[i]) * 0.7));
    const gotSoft = tools.softenPixels(start.slice(), width, height);
    const gotSharp = tools.sharpenPixels(start.slice(), width, height);
    for (let i = 0; i < start.length; i += 1) {
      assert.ok(Math.abs(gotSoft[i] - expectedSoft[i]) <= 1, `soften ${width}x${height} at ${i}: ${gotSoft[i]} vs ${expectedSoft[i]}`);
      assert.ok(Math.abs(gotSharp[i] - expectedSharp[i]) <= 1, `sharpen ${width}x${height} at ${i}: ${gotSharp[i]} vs ${expectedSharp[i]}`);
    }
  }
});

test('hidePixels cover makes the area solid black whatever was there', () => {
  assert.deepEqual(Array.from(tools.HIDE_MODES), ['cover', 'blur', 'pixelate']);

  // Dark writing on light paper: every pixel becomes solid black, so no stroke is left.
  const writing = picture(80, 20, (x, y) => ((x % 7 < 2 && y > 4 && y < 16) ? 20 : 230));
  tools.hidePixels(writing, 80, 20, 'cover');
  for (let i = 0; i < writing.length; i += 4) {
    assert.deepEqual(Array.from(writing.slice(i, i + 4)), [0, 0, 0, 255], `pixel ${i / 4} is solid black`);
  }

  // The fill does not depend on what was covered: a little ink and a lot of ink (a "1" and an
  // "8"), or a see-through patch, all give exactly the same pixels.
  const little = picture(40, 10, (x) => (x < 4 ? 0 : 255));
  const lots = picture(40, 10, (x) => (x < 30 ? 0 : 255));
  const clear = new Uint8ClampedArray(40 * 10 * 4);
  for (const patch of [little, lots, clear]) tools.hidePixels(patch, 40, 10, 'cover');
  assert.deepEqual(Array.from(little), Array.from(lots));
  assert.deepEqual(Array.from(little), Array.from(clear));
});

// ----- Background colour -----

const WALL = [200, 196, 184];
const SKIN = [219, 162, 124];
const HAIR = [42, 35, 32];
const COAT = [38, 54, 77];
const BLUE = [207, 227, 247];

/**
 * A small passport photo: a wall, a head with hair in the middle and shoulders across the
 * bottom. `wall(x, y)` gives the wall colour, so it can be plain, shaded or patterned.
 */
function portrait(width, height, wall, shirt) {
  const pixels = new Uint8ClampedArray(width * height * 4);
  const cx = width / 2;
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      let colour = wall(x, y);
      const head = ((x - cx) / (width * 0.22)) ** 2 + ((y - height * 0.42) / (height * 0.24)) ** 2;
      if (y > height * 0.74) {
        colour = shirt && Math.abs(x - cx) < width * 0.12 ? shirt : COAT;
      } else if (head <= 1) {
        colour = y < height * 0.3 ? HAIR : SKIN;
      } else if (y > height * 0.6 && Math.abs(x - cx) < width * 0.1) {
        colour = SKIN;
      }
      pixels.set([colour[0], colour[1], colour[2], 255], (y * width + x) * 4);
    }
  }
  return pixels;
}

const rgbAt = (pixels, width, x, y) => Array.from(pixels.slice((y * width + x) * 4, (y * width + x) * 4 + 3));

test('changeBackground gives a plain wall the new colour and leaves the person alone', () => {
  const [w, h] = [80, 100];
  const pixels = portrait(w, h, () => WALL);
  const before = pixels.slice();
  const result = tools.changeBackground(pixels, w, h, BLUE, 50);

  assert.equal(result.status, 'changed');
  assert.ok(result.share > 0.4 && result.share < 0.7, `about half the photo is wall, got ${result.share}`);
  for (const [x, y] of [[0, 0], [w - 1, 0], [2, 50], [w - 3, 60], [40, 3]]) {
    assert.deepEqual(rgbAt(pixels, w, x, y), BLUE, `wall at ${x},${y}`);
  }
  // Well inside the hair, the face, the neck and the coat nothing has changed.
  for (const [x, y] of [[40, 24], [40, 45], [40, 68], [40, 90], [5, 95], [w - 5, 95]]) {
    assert.deepEqual(rgbAt(pixels, w, x, y), rgbAt(before, w, x, y), `person at ${x},${y}`);
  }
  for (let i = 3; i < pixels.length; i += 4) assert.equal(pixels[i], 255);
});

test('changeBackground follows a shadow across the wall', () => {
  const [w, h] = [80, 100];
  // Darker on the left, lighter on the right: a brightness change of 60 across the photo.
  const shaded = (x) => WALL.map(value => value - 30 + Math.round(60 * x / (w - 1)));
  const pixels = portrait(w, h, shaded);
  const result = tools.changeBackground(pixels, w, h, [255, 255, 255], 50);

  assert.equal(result.status, 'changed');
  assert.deepEqual(rgbAt(pixels, w, 0, 10), [255, 255, 255], 'the dark side');
  assert.deepEqual(rgbAt(pixels, w, w - 1, 10), [255, 255, 255], 'the light side');
  assert.deepEqual(rgbAt(pixels, w, 40, 45), SKIN, 'the face is not touched');
});

test('changeBackground refuses a patterned background and changes nothing', () => {
  const [w, h] = [80, 100];
  const curtain = (x) => [[123, 90, 60], [92, 111, 82], [199, 181, 146], [60, 80, 140]][Math.floor(x / 6) % 4];
  const pixels = portrait(w, h, curtain);
  const before = pixels.slice();

  assert.equal(tools.measureBackground(pixels, w, h).plain, false);
  assert.equal(tools.changeBackground(pixels, w, h, BLUE, 50).status, 'busy');
  assert.deepEqual(pixels, before);
  assert.equal(tools.measureBackground(portrait(w, h, () => WALL), w, h).plain, true);
});

test('changeBackground stops when everything or the face would be taken', () => {
  const [w, h] = [60, 80];
  // A photo of a wall and nothing else.
  const empty = new Uint8ClampedArray(w * h * 4);
  for (let i = 0; i < empty.length; i += 4) empty.set([WALL[0], WALL[1], WALL[2], 255], i);
  const emptyBefore = empty.slice();
  assert.equal(tools.changeBackground(empty, w, h, BLUE, 50).status, 'too-much');
  assert.deepEqual(empty, emptyBefore);

  // A face the colour of the wall: the middle of the photo would become background.
  const ghost = portrait(w, h, () => SKIN);
  const ghostBefore = ghost.slice();
  assert.equal(tools.changeBackground(ghost, w, h, BLUE, 50).status, 'too-much');
  assert.deepEqual(ghost, ghostBefore);
});

test('changeBackground finds nothing when the person fills the photo', () => {
  const [w, h] = [60, 80];
  const pixels = new Uint8ClampedArray(w * h * 4);
  for (let y = 0; y < h; y += 1) {
    for (let x = 0; x < w; x += 1) {
      // One pixel of wall along the top and the upper sides; the rest is hair.
      const wall = y < 1 || (y < 40 && (x === 0 || x === w - 1));
      pixels.set([...(wall ? WALL : HAIR), 255], (y * w + x) * 4);
    }
  }
  const before = pixels.slice();
  const result = tools.changeBackground(pixels, w, h, BLUE, 50);
  assert.equal(result.status, 'none');
  assert.deepEqual(pixels, before);
});

test('changeBackground takes more with a higher amount, and a light shirt survives at Less', () => {
  const [w, h] = [80, 100];
  const shirt = [246, 246, 244];
  const little = portrait(w, h, () => WALL, shirt);
  const less = tools.changeBackground(little, w, h, BLUE, 0);
  assert.equal(less.status, 'changed');
  assert.deepEqual(rgbAt(little, w, 40, 90), shirt, 'the white shirt is kept');

  // A wall with uneven patches: Less leaves the far patches, More takes them.
  const patchy = (x, y) => (x < 20 && y > 20 ? WALL.map(value => value - 45) : WALL);
  const lessPatchy = tools.changeBackground(portrait(w, h, patchy), w, h, BLUE, 0);
  const morePatchy = tools.changeBackground(portrait(w, h, patchy), w, h, BLUE, 100);
  assert.equal(lessPatchy.status, 'changed');
  assert.equal(morePatchy.status, 'changed');
  assert.ok(morePatchy.share > lessPatchy.share, 'More takes the darker patch as well');
});

test('changeBackground softens the edge and clears specks on the wall', () => {
  const [w, h] = [80, 100];
  const pixels = portrait(w, h, () => WALL);
  // A column that is a quarter hair and the rest wall, as a soft edge is in a real photo.
  const mixed = HAIR.map((value, channel) => Math.round((value + WALL[channel] * 3) / 4));
  const edgeX = 22;
  for (let y = 30; y < 50; y += 1) pixels.set([...mixed, 255], (y * w + edgeX) * 4);
  // A mark on the wall.
  pixels.set([20, 20, 20, 255], (8 * w + 8) * 4);

  const white = [255, 255, 255];
  assert.equal(tools.changeBackground(pixels, w, h, white, 50).status, 'changed');
  assert.deepEqual(rgbAt(pixels, w, 8, 8), white, 'the mark is gone');

  // The mixed pixel moves towards the new colour by the wall's share, and no further.
  const soft = rgbAt(pixels, w, edgeX, 40);
  soft.forEach((value, channel) => {
    assert.ok(value > mixed[channel], `channel ${channel} got lighter`);
    assert.ok(value < white[channel], `channel ${channel} is not plain background`);
    assert.ok(value <= mixed[channel] + (white[channel] - WALL[channel]), `channel ${channel} moved by no more than the wall changed`);
  });
});

test('changeBackground does not touch alpha', () => {
  const [w, h] = [80, 100];
  const pixels = portrait(w, h, () => WALL);
  for (let i = 3; i < pixels.length; i += 4) pixels[i] = (i >> 2) % 251;
  const alpha = Array.from(pixels.filter((_, index) => index % 4 === 3));
  assert.equal(tools.changeBackground(pixels, w, h, BLUE, 50).status, 'changed');
  assert.deepEqual(Array.from(pixels.filter((_, index) => index % 4 === 3)), alpha);
});

test('changeBackground follows a mottled backdrop', () => {
  const [w, h] = [120, 150];
  // Soft lighter and darker patches, as on a studio backdrop: 40 apart from darkest to lightest.
  const mottled = (x, y) => WALL.map(value => Math.round(value - 20 + 20 * Math.sin(x / 9) * Math.cos(y / 11)));
  const pixels = portrait(w, h, mottled);
  const result = tools.changeBackground(pixels, w, h, BLUE, 50);

  assert.equal(result.status, 'changed');
  let left = 0;
  for (let y = 0; y < Math.floor(h * 0.15); y += 1) {
    for (let x = 0; x < w; x += 1) {
      if (rgbAt(pixels, w, x, y).join() !== BLUE.join()) left += 1;
    }
  }
  assert.equal(left, 0, 'no patch of the old backdrop is left above the head');
  assert.deepEqual(rgbAt(pixels, w, 60, 68), SKIN, 'the face is not touched');
});

test('changeBackground keeps cloth of the wall colour, because cloth is not smooth', () => {
  const [w, h] = [80, 100];
  const pixels = portrait(w, h, () => WALL);
  // A knitted jacket in the colour of the wall: the same colour, but with a weave.
  for (let y = Math.ceil(h * 0.74); y < h; y += 1) {
    for (let x = 0; x < w; x += 1) {
      const thread = (x + y) % 4 < 2 ? 20 : -20;
      pixels.set(WALL.map(value => value + thread), (y * w + x) * 4);
    }
  }
  const before = pixels.slice();
  const result = tools.changeBackground(pixels, w, h, BLUE, 50);

  assert.equal(result.status, 'changed');
  assert.deepEqual(rgbAt(pixels, w, 2, 20), BLUE, 'the wall is changed');
  for (const [x, y] of [[10, 85], [40, 90], [70, 95], [40, 80]]) {
    assert.deepEqual(rgbAt(pixels, w, x, y), rgbAt(before, w, x, y), `jacket at ${x},${y}`);
  }
});

/** A wall model of one colour everywhere, as changeBackground returns it. */
function flatWall(colour, limit) {
  return { field: Float32Array.from(colour), cell: 1000, columns: 1, rows: 1, limit: limit ?? 14 };
}

/** A mask of one brush at full strength over every pixel. */
function brushMask(count, brush, strength) {
  const mask = new Uint8ClampedArray(count * 4);
  for (let i = 0; i < count; i += 1) {
    mask[i * 4 + tools.BACKGROUND_BRUSHES[brush].channel] = 255;
    mask[i * 4 + 3] = strength ?? 255;
  }
  return mask;
}

test('Restore brings the photo before the change back, and its soft rim blends', () => {
  const changed = new Uint8ClampedArray([...BLUE, 255, ...BLUE, 255, ...BLUE, 255]);
  const before = new Uint8ClampedArray([...COAT, 255, ...COAT, 255, ...WALL, 255]);
  const mask = brushMask(3, 'restore');
  mask[7] = 128; // the soft rim of a stroke
  mask[11] = 0; // not painted
  tools.applyBackgroundFixes(changed, before, mask, BLUE, { width: 3, wall: flatWall(WALL) });

  assert.deepEqual(rgbAt(changed, 3, 0, 0), COAT, 'the coat is back');
  rgbAt(changed, 3, 1, 0).forEach((value, channel) => {
    assert.ok(value > Math.min(COAT[channel], BLUE[channel]) && value < Math.max(COAT[channel], BLUE[channel]), `rim channel ${channel} is between the two`);
  });
  assert.deepEqual(rgbAt(changed, 3, 2, 0), BLUE, 'an unpainted pixel is left alone');
});

test('Refine Edge gives old background the new colour and keeps hair and skin under the brush', () => {
  // One row as the brush meets it at a hair edge: wall left over, wall seen through thin hair,
  // a strand of hair, skin. All four were left alone by the automatic change.
  const halfHair = WALL.map((value, channel) => Math.round((value + HAIR[channel]) / 2));
  const before = new Uint8ClampedArray([...WALL, 255, ...halfHair, 255, ...HAIR, 255, ...SKIN, 255]);
  const changed = before.slice();
  tools.applyBackgroundFixes(changed, before, brushMask(4, 'refine'), BLUE, { width: 4, wall: flatWall(WALL) });

  assert.deepEqual(rgbAt(changed, 4, 0, 0), BLUE, 'the old wall takes the new colour');
  assert.deepEqual(rgbAt(changed, 4, 2, 0), HAIR, 'the hair stays');
  assert.deepEqual(rgbAt(changed, 4, 3, 0), SKIN, 'the skin stays');
  // Thin hair over the wall: the hair stays darker than the new colour, and the old wall in it
  // is shifted towards the new colour (the blue channel rises), not painted over.
  const mixed = rgbAt(changed, 4, 1, 0);
  assert.ok(mixed[2] > halfHair[2], 'the wall seen through the hair moves towards the new colour');
  assert.ok(mixed.every((value, channel) => value < BLUE[channel]), 'the hair in it is not painted over');
});

test('Refine Edge keeps skin beside dark hair, even when both are in one small square', () => {
  // Hair, hair, skin, skin, then wall: the first square of the person-colour grid holds hair and
  // skin, so the person colour there is their average.
  const before = new Uint8ClampedArray([...HAIR, 255, ...HAIR, 255, ...SKIN, 255, ...SKIN, 255, ...WALL, 255, ...WALL, 255, ...WALL, 255, ...WALL, 255]);
  const changed = before.slice();
  tools.applyBackgroundFixes(changed, before, brushMask(8, 'refine'), BLUE, { width: 8, wall: flatWall(WALL) });
  assert.deepEqual(rgbAt(changed, 8, 2, 0), SKIN, 'skin stays');
  assert.deepEqual(rgbAt(changed, 8, 3, 0), SKIN, 'skin stays');
  assert.deepEqual(rgbAt(changed, 8, 0, 0), HAIR, 'hair stays');
  assert.deepEqual(rgbAt(changed, 8, 6, 0), BLUE, 'the wall changes');
});

test('Refine Edge keeps the soft edge where skin meets hair: it is all person', () => {
  const edge = SKIN.map((value, channel) => Math.round((value + HAIR[channel]) / 2));
  const row = [HAIR, HAIR, HAIR, edge, SKIN, SKIN, SKIN, SKIN, WALL, WALL, WALL, WALL];
  const before = new Uint8ClampedArray(row.flatMap(colour => [...colour, 255]));
  const changed = before.slice();
  tools.applyBackgroundFixes(changed, before, brushMask(row.length, 'refine'), BLUE, { width: row.length, wall: flatWall(WALL) });
  assert.deepEqual(rgbAt(changed, row.length, 3, 0), edge, 'the skin-hair edge stays');
  assert.deepEqual(rgbAt(changed, row.length, 10, 0), BLUE);
});

test('Refine Edge keeps grey hair against a grey wall, where every colour lies on one line', () => {
  // Two shades of neutral hair and a neutral wall: wall-and-hair and hair-and-hair fit equally.
  const wallGrey = [220, 220, 220];
  const row = [[20, 20, 20], [20, 20, 20], [60, 60, 60], [60, 60, 60], wallGrey, wallGrey, wallGrey, wallGrey];
  const before = new Uint8ClampedArray(row.flatMap(colour => [...colour, 255]));
  const changed = before.slice();
  const newColour = [100, 150, 255];
  tools.applyBackgroundFixes(changed, before, brushMask(row.length, 'refine'), newColour, { width: row.length, wall: flatWall(wallGrey) });
  assert.deepEqual(rgbAt(changed, row.length, 0, 0), [20, 20, 20], 'the darker hair stays');
  assert.deepEqual(rgbAt(changed, row.length, 2, 0), [60, 60, 60], 'the lighter hair stays');
  assert.deepEqual(rgbAt(changed, row.length, 6, 0), newColour, 'the wall changes');
});

test('a Refine Edge stroke is not changed by another stroke elsewhere', () => {
  // A wall, a band of hair, and edge pixels beside it that the brush refines. A distant Erase dab
  // must not change the refined pixels: the person's colours are read from the photo, not from
  // the area the strokes happen to cover.
  const wallGrey = [220, 220, 220];
  const [width, height] = [40, 12];
  const before = new Uint8ClampedArray(width * height * 4);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const colour = x >= 10 && x < 14 ? [30, 25, 20] : x >= 14 && x < 17 ? [182, 181, 180] : wallGrey;
      before.set([...colour, 255], (y * width + x) * 4);
    }
  }
  const newColour = [100, 150, 255];
  const refine = new Uint8ClampedArray(width * height * 4);
  for (let y = 0; y < height; y += 1) for (let x = 14; x < 17; x += 1) refine.set([0, 255, 0, 255], (y * width + x) * 4);
  const alone = before.slice();
  tools.applyBackgroundFixes(alone, before, refine, newColour, { width, wall: flatWall(wallGrey), cell: 4 });

  const withDab = refine.slice();
  withDab.set([0, 0, 255, 255], (0 * width + 1) * 4);
  const together = before.slice();
  tools.applyBackgroundFixes(together, before, withDab, newColour, { width, wall: flatWall(wallGrey), cell: 4 });

  for (let y = 0; y < height; y += 1) {
    for (let x = 14; x < 17; x += 1) {
      assert.deepEqual(rgbAt(together, width, x, y), rgbAt(alone, width, x, y), `refined pixel ${x},${y}`);
    }
  }
});

test('Refine Edge follows the colour the wall has at that spot', () => {
  // The wall is darker on the right. A pixel of that darker wall is still wall.
  const wall = { field: Float32Array.from([...WALL, ...WALL.map(value => value - 40)]), cell: 1, columns: 2, rows: 1, limit: 14 };
  const darker = WALL.map(value => value - 40);
  const before = new Uint8ClampedArray([...WALL, 255, ...darker, 255]);
  const changed = before.slice();
  tools.applyBackgroundFixes(changed, before, brushMask(2, 'refine'), BLUE, { width: 2, wall });
  assert.deepEqual(rgbAt(changed, 2, 0, 0), BLUE);
  assert.deepEqual(rgbAt(changed, 2, 1, 0), BLUE, 'the darker part of the wall is wall too');
});

test('Erase gives everything under the brush the new colour', () => {
  const before = new Uint8ClampedArray([...HAIR, 255, ...WALL, 200]);
  const changed = before.slice();
  tools.applyBackgroundFixes(changed, before, brushMask(2, 'erase'), BLUE, { width: 2, wall: flatWall(WALL) });
  assert.deepEqual(rgbAt(changed, 2, 0, 0), BLUE);
  assert.deepEqual(rgbAt(changed, 2, 1, 0), BLUE);
  assert.deepEqual([changed[3], changed[7]], [255, 200], 'alpha is not touched');
});

test('applyBackgroundFixes feathers the edge of a stroke', () => {
  // A stroke over the left half of a row of 21 wall pixels that the change left alone.
  const width = 21;
  const before = new Uint8ClampedArray(width * 4);
  for (let x = 0; x < width; x += 1) before.set([...WALL, 255], x * 4);
  const mask = new Uint8ClampedArray(width * 4);
  for (let x = 0; x < 10; x += 1) mask.set([0, 0, 255, 255], x * 4);

  const hard = before.slice();
  tools.applyBackgroundFixes(hard, before, mask, BLUE, { width, wall: flatWall(WALL) });
  const soft = before.slice();
  tools.applyBackgroundFixes(soft, before, mask, BLUE, { width, wall: flatWall(WALL), feather: 3 });

  assert.deepEqual(rgbAt(hard, width, 10, 0), WALL, 'without feather the edge is hard');
  assert.deepEqual(rgbAt(soft, width, 0, 0), BLUE, 'inside the stroke is the new colour');
  assert.deepEqual(rgbAt(soft, width, 20, 0), WALL, 'far outside is untouched');
  const blues = Array.from({ length: width }, (_, x) => soft[x * 4 + 2]);
  for (let x = 1; x < width; x += 1) assert.ok(blues[x] <= blues[x - 1], 'the stroke fades out steadily');
  assert.ok(blues[9] < BLUE[2] && blues[11] > WALL[2], 'the edge is a gradual blend');
});

test('a later stroke lies over an earlier one, and strokes never add up past full strength', () => {
  const before = new Uint8ClampedArray([...COAT, 255]);
  const changed = new Uint8ClampedArray([...BLUE, 255]);
  // Restore and Erase both at full strength on one pixel, as the mask can hold after blending.
  tools.applyBackgroundFixes(changed, before, new Uint8ClampedArray([255, 0, 255, 255]), BLUE, { width: 1 });
  rgbAt(changed, 1, 0, 0).forEach((value, channel) => {
    assert.ok(value >= Math.min(COAT[channel], BLUE[channel]) && value <= Math.max(COAT[channel], BLUE[channel]), `channel ${channel} stays between the two`);
  });
});

test('changeBackground returns the wall it found, for the touch-up brushes', () => {
  const [w, h] = [80, 100];
  const pixels = portrait(w, h, () => WALL);
  const result = tools.changeBackground(pixels, w, h, BLUE, 50);
  assert.equal(result.status, 'changed');
  const colour = tools.wallColourAt(result.wall, 5, 5, [0, 0, 0]);
  colour.forEach((value, channel) => assert.ok(Math.abs(value - WALL[channel]) < 2, `channel ${channel} is the wall`));
  assert.ok(result.wall.limit > 0);
});

// ----- Passport photo checks -----

/** A background mask (1 is background) with a head: an ellipse from `top`, `wide` across, and shoulders. */
function outlineMask(width, height, top, wide, extra) {
  const mask = new Uint8Array(width * height).fill(1);
  const cy = top + 0.34;
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const head = ((x / width - 0.5) / (wide / 2)) ** 2 + ((y / height - cy) / 0.34) ** 2 <= 1;
      if (head || y / height > 0.85) mask[y * width + x] = 0;
    }
  }
  if (extra) extra(mask);
  return mask;
}

function checksFor(options) {
  const [w, h] = [100, 130];
  const pixels = new Uint8ClampedArray(w * h * 4).fill(options.light ?? 150);
  return tools.passportChecks({
    pixels,
    width: w,
    height: h,
    found: options.found === undefined ? { status: 'changed', mask: outlineMask(w, h, options.top ?? 0.1, options.wide ?? 0.55, options.extra) } : options.found,
    plain: options.plain ?? true,
    colour: options.colour ?? [240, 240, 240],
    replaced: options.replaced ?? false,
    enlarged: options.enlarged ?? 0.6
  }).map(problem => problem.key);
}

test('headOutline finds where the head starts and how wide it is', () => {
  const [w, h] = [100, 130];
  const outline = tools.headOutline(outlineMask(w, h, 0.1, 0.55), w, h);
  assert.ok(Math.abs(outline.top - 0.1) < 0.02, `top ${outline.top}`);
  assert.ok(Math.abs(outline.width - 0.55) < 0.05, `width ${outline.width}`);

  // A line along the top edge of a scan, and a plant at the side, are not the head.
  const noisy = outlineMask(w, h, 0.2, 0.5, (mask) => {
    for (let x = 0; x < w; x += 1) mask[x] = 0;
    for (let y = 20; y < 100; y += 1) for (let x = 90; x < 100; x += 1) mask[y * w + x] = 0;
  });
  const second = tools.headOutline(noisy, w, h);
  assert.ok(Math.abs(second.top - 0.2) < 0.02, `top ${second.top}`);
  assert.ok(Math.abs(second.width - 0.5) < 0.05, `width ${second.width}`);

  assert.equal(tools.headOutline(new Uint8Array(w * h).fill(1), w, h), null, 'no person at all');
});

test('passportChecks finds nothing wrong with a well placed photo', () => {
  assert.deepEqual(checksFor({}), []);
  assert.equal(tools.PASSPORT_GUIDE.top, 0.08);
  assert.equal(tools.PASSPORT_GUIDE.chin, 0.83);
});

test('passportChecks reports head position and size', () => {
  assert.deepEqual(checksFor({ top: 0 }), ['head-cut']);
  assert.deepEqual(checksFor({ top: 0.01 }), ['head-high']);
  assert.deepEqual(checksFor({ top: 0.25 }), ['head-low']);
  assert.deepEqual(checksFor({ wide: 0.95 }), ['head-large']);
  assert.deepEqual(checksFor({ wide: 0.25 }), ['head-small']);
  // Without a background to tell the head from, the head is not judged at all.
  assert.deepEqual(checksFor({ top: 0.25, found: { status: 'busy', share: 0 }, plain: false }), ['background-busy']);
  assert.deepEqual(checksFor({ top: 0.25, found: null }), []);
});

test('passportChecks reports the background, unless it was replaced', () => {
  assert.deepEqual(checksFor({ plain: false }), ['background-busy']);
  assert.deepEqual(checksFor({ colour: [84, 127, 192] }), ['background-dark']);
  assert.deepEqual(checksFor({ colour: [207, 227, 247] }), [], 'light blue is light');
  assert.deepEqual(checksFor({ colour: [20, 50, 50], replaced: true }), []);
  assert.deepEqual(checksFor({ plain: false, replaced: true }), []);
});

test('passportChecks looks at the finished edges when the background was replaced', () => {
  // The new colour went round the person, but a busy scene was kept at the sides.
  const [w, h] = [100, 130];
  const pixels = new Uint8ClampedArray(w * h * 4).fill(150);
  for (let y = 0; y < h; y += 1) {
    for (let x = 0; x < w; x += 1) {
      const at = (y * w + x) * 4;
      const stripe = (x + y) % 6 < 3 ? 30 : 230;
      pixels[at] = pixels[at + 1] = pixels[at + 2] = stripe;
    }
  }
  const keys = tools.passportChecks({
    pixels, width: w, height: h, found: null, plain: true, colour: [240, 240, 240],
    replaced: true, enlarged: 0.6
  }).map(problem => problem.key);
  assert.ok(keys.includes('background-left'), keys.join());
});

test('passportChecks does not say "zoom in" when the photo is zoomed in as far as it goes', () => {
  const face = { crown: 0.08, chin: 0.5, centre: 0.5 };
  const base = {
    pixels: new Uint8ClampedArray(100 * 130 * 4).fill(150), width: 100, height: 130, found: null,
    plain: true, colour: [240, 240, 240], replaced: false, enlarged: 0.6, face
  };
  const [canZoom] = tools.passportChecks(base);
  assert.equal(canZoom.key, 'head-small');
  assert.match(canZoom.text, /Zoom in/);
  const [atLimit] = tools.passportChecks({ ...base, atZoomLimit: true });
  assert.equal(atLimit.key, 'head-small');
  assert.doesNotMatch(atLimit.text, /Zoom in/);
  assert.match(atLimit.text, /too far away/);
});

test('passportChecks reports an enlarged photo and a dark or washed-out face', () => {
  assert.deepEqual(checksFor({ enlarged: 1.1 }), [], 'a little is not worth a warning');
  assert.deepEqual(checksFor({ enlarged: 1.64 }), ['enlarged']);
  const [problem] = tools.passportChecks({
    pixels: new Uint8ClampedArray(100 * 130 * 4).fill(150), width: 100, height: 130, found: null,
    plain: true, colour: [240, 240, 240], replaced: false, enlarged: 1.64
  });
  assert.match(problem.text, /about 1\.6 times/);
  assert.deepEqual(checksFor({ light: 50 }), ['face-dark']);
  assert.deepEqual(checksFor({ light: 240 }), ['face-bright']);
});

test('findBackground finds the background and leaves the photo alone', () => {
  const [w, h] = [80, 100];
  const pixels = portrait(w, h, () => WALL);
  const before = pixels.slice();
  const found = tools.findBackground(pixels, w, h, 50);
  assert.equal(found.status, 'changed');
  assert.deepEqual(pixels, before);
  assert.equal(found.mask[0], 1, 'the corner is background');
  assert.equal(found.mask[45 * w + 40], 0, 'the face is not');
  const outline = tools.headOutline(found.mask, w, h);
  assert.ok(Math.abs(outline.top - 0.18) < 0.03, `the head of the test portrait starts at ${outline.top}`);
});

// ----- Remove background: the cut-out -----

/** A row of RGBA pixels from a list of [r, g, b] colours. */
const rowOf = colours => new Uint8ClampedArray(colours.flatMap(colour => [...colour, 255]));

test('cutoutBackground reads the background where the mask is sure, and fills it in behind the subject', () => {
  const [width, height] = [24, 24];
  const pixels = new Uint8ClampedArray(width * height * 4);
  const alpha = new Uint8ClampedArray(width * height);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const subject = x >= 12;
      pixels.set([...(subject ? SKIN : WALL), 255], (y * width + x) * 4);
      alpha[y * width + x] = subject ? 255 : 0;
    }
  }
  const background = tools.cutoutBackground(pixels, alpha, width, height, 4);
  for (const x of [2, 20]) {
    const colour = tools.wallColourAt(background, x, 10, [0, 0, 0]);
    colour.forEach((value, channel) => assert.ok(Math.abs(value - WALL[channel]) < 1, `x ${x} channel ${channel} is the wall`));
  }
  assert.equal(tools.cutoutBackground(pixels, new Uint8ClampedArray(width * height).fill(255), width, height, 4), null, 'no sure background');
});

test('cutoutColours takes the old background tint out of a soft edge', () => {
  // Hair seen half over the wall: the pixel is the average of the two, and the mask says half.
  const half = HAIR.map((value, channel) => Math.round((value + WALL[channel]) / 2));
  const pixels = rowOf([WALL, half, HAIR]);
  const alpha = new Uint8ClampedArray([0, 128, 255]);
  const background = flatWall(WALL);
  tools.cutoutColours(pixels, alpha, 3, background);
  rgbAt(pixels, 3, 1, 0).forEach((value, channel) => assert.ok(Math.abs(value - HAIR[channel]) <= 3, `channel ${channel} is hair again`));
  assert.deepEqual(rgbAt(pixels, 3, 2, 0), HAIR, 'solid hair is unchanged');
  assert.deepEqual([pixels[3], pixels[7], pixels[11]], [0, 128, 255], 'alpha comes from the mask');
});

test('cutoutOnto lays the cut-out on a colour or on a picture', () => {
  const cutout = new Uint8ClampedArray([...HAIR, 255, ...HAIR, 0, ...HAIR, 128]);
  const onColour = cutout.slice();
  tools.cutoutOnto(onColour, BLUE);
  assert.deepEqual(rgbAt(onColour, 3, 0, 0), HAIR);
  assert.deepEqual(rgbAt(onColour, 3, 1, 0), BLUE);
  rgbAt(onColour, 3, 2, 0).forEach((value, channel) => assert.ok(Math.abs(value - (HAIR[channel] + BLUE[channel]) / 2) <= 1));
  assert.deepEqual([onColour[3], onColour[7], onColour[11]], [255, 255, 255]);

  const onPicture = cutout.slice();
  tools.cutoutOnto(onPicture, rowOf([WALL, SKIN, WALL]));
  assert.deepEqual(rgbAt(onPicture, 3, 1, 0), SKIN);
});

test('applyCutoutStrokes: Restore, Erase and Refine Edge change the mask, always from the model mask', () => {
  // Wall, a strand of hair over the wall, solid hair, skin. The model missed the strand and
  // wrongly took the skin.
  const half = HAIR.map((value, channel) => Math.round((value + WALL[channel]) / 2));
  const original = rowOf([WALL, WALL, half, HAIR, HAIR, SKIN, SKIN, WALL]);
  const modelAlpha = new Uint8ClampedArray([0, 0, 0, 255, 255, 0, 0, 255]);
  const background = flatWall(WALL);
  const alpha = new Uint8ClampedArray(8);
  const mask = new Uint8ClampedArray(8 * 4);
  mask.set([0, 255, 0, 255], 2 * 4); // Refine Edge over the strand
  mask.set([0, 255, 0, 255], 1 * 4); // ... and the wall beside it
  mask.set([255, 0, 0, 255], 5 * 4); // Restore the skin
  mask.set([255, 0, 0, 255], 6 * 4);
  mask.set([0, 0, 255, 255], 7 * 4); // Erase a stray bit of wall the model took
  tools.applyCutoutStrokes(alpha, modelAlpha, original, mask, background, { width: 8, cell: 4 });

  assert.equal(alpha[0], 0, 'unpainted wall stays background');
  assert.equal(alpha[1], 0, 'Refine Edge keeps wall as background');
  assert.ok(alpha[2] > 100 && alpha[2] < 160, `Refine Edge makes the strand about half subject (${alpha[2]})`);
  assert.equal(alpha[3], 255, 'unpainted hair stays subject');
  assert.equal(alpha[5], 255, 'Restore brings the skin back');
  assert.equal(alpha[7], 0, 'Erase removes the stray bit');

  // Without strokes the mask is the model's again: Reset and Undo are exact.
  tools.applyCutoutStrokes(alpha, modelAlpha, original, new Uint8ClampedArray(8 * 4), background, { width: 8 });
  assert.deepEqual(Array.from(alpha), Array.from(modelAlpha));
});

test('refineCutoutEdge clears background the model was unsure of, and keeps strands and solid subject', () => {
  // Wall, wall the model gave 40% to, a strand half over the wall, solid hair, skin at the edge.
  const half = HAIR.map((value, channel) => Math.round((value + WALL[channel]) / 2));
  const original = rowOf([WALL, WALL, half, HAIR, HAIR, HAIR, SKIN, SKIN]);
  const alpha = new Uint8ClampedArray([0, 102, 128, 255, 255, 255, 230, 255]);
  tools.refineCutoutEdge(alpha, original, 8, flatWall(WALL), 4);
  assert.equal(alpha[0], 0, 'sure background stays background');
  assert.equal(alpha[1], 0, 'wall the model was unsure of becomes background');
  assert.ok(alpha[2] > 100 && alpha[2] <= 128, `the strand stays about half (${alpha[2]})`);
  assert.equal(alpha[3], 255, 'solid hair stays');
  assert.equal(alpha[6], 230, 'skin at the edge keeps the model value');
});

test('a cut-out rebuilt only where a stroke changed matches one rebuilt in full', () => {
  const [width, height] = [40, 30];
  const original = new Uint8ClampedArray(width * height * 4);
  const modelAlpha = new Uint8ClampedArray(width * height);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const subject = x >= 20;
      const strand = x >= 16 && x < 20 && y % 3 === 0;
      const colour = subject ? HAIR : strand ? HAIR.map((value, channel) => Math.round((value + WALL[channel]) / 2)) : WALL;
      original.set([...colour, 255], (y * width + x) * 4);
      modelAlpha[y * width + x] = subject ? 255 : x >= 16 ? 140 : 0;
    }
  }
  const background = flatWall(WALL);
  // Two strokes: Refine Edge over the strands, Restore further down.
  const fullMask = new Uint8ClampedArray(width * height * 4);
  const paint = (mask, maskWidth, left, top, x0, x1, y0, y1, colour) => {
    for (let y = y0; y < y1; y += 1) for (let x = x0; x < x1; x += 1) mask.set(colour, ((y - top) * maskWidth + (x - left)) * 4);
  };
  paint(fullMask, width, 0, 0, 14, 22, 2, 12, [0, 255, 0, 255]);
  paint(fullMask, width, 0, 0, 10, 14, 20, 26, [255, 0, 0, 255]);
  const full = new Uint8ClampedArray(width * height);
  tools.applyCutoutStrokes(full, modelAlpha, original, fullMask, background, { width, cell: 4 });

  // The same, rebuilt first without the Restore stroke, then only in the Restore stroke's area.
  const partial = new Uint8ClampedArray(width * height);
  const firstMask = new Uint8ClampedArray(width * height * 4);
  paint(firstMask, width, 0, 0, 14, 22, 2, 12, [0, 255, 0, 255]);
  tools.applyCutoutStrokes(partial, modelAlpha, original, firstMask, background, { width, cell: 4 });
  const region = { left: 8, top: 18, right: 16, bottom: 28 };
  const regionMask = new Uint8ClampedArray((region.right - region.left) * (region.bottom - region.top) * 4);
  paint(regionMask, region.right - region.left, region.left, region.top, 10, 14, 20, 26, [255, 0, 0, 255]);
  tools.applyCutoutStrokes(partial, modelAlpha, original, regionMask, background, { width, cell: 4, region });
  assert.deepEqual(Array.from(partial), Array.from(full));

  // The colours too: made in full, and made only in the region on top of the earlier ones.
  const coloursFull = new Uint8ClampedArray(original);
  tools.cutoutColours(coloursFull, full, width, background);
  const coloursPartial = new Uint8ClampedArray(original);
  tools.cutoutColours(coloursPartial, full, width, background);
  coloursPartial.set(original.subarray(0, 4)); // a stale pixel outside the region stays as it was
  tools.cutoutColours(coloursPartial, full, width, background, region);
  assert.deepEqual(Array.from(coloursPartial.subarray(4)), Array.from(coloursFull.subarray(4)));
});

test('the subject colours read once for a photo give the same strokes as reading them each time', () => {
  const half = HAIR.map((value, channel) => Math.round((value + WALL[channel]) / 2));
  const original = rowOf([WALL, WALL, half, HAIR, HAIR, SKIN, SKIN, WALL]);
  const modelAlpha = new Uint8ClampedArray([0, 0, 0, 255, 255, 0, 0, 255]);
  const mask = new Uint8ClampedArray(8 * 4);
  for (const x of [1, 2, 3, 6]) mask.set([0, 255, 0, 255], x * 4);
  const background = flatWall(WALL);
  const each = new Uint8ClampedArray(8);
  tools.applyCutoutStrokes(each, modelAlpha, original, mask, background, { width: 8, cell: 4 });
  const once = new Uint8ClampedArray(8);
  const colours = tools.cutoutPersonColours(original, modelAlpha, 8, background, 4);
  tools.applyCutoutStrokes(once, modelAlpha, original, mask, background, { width: 8, cell: 4, colours });
  assert.deepEqual(Array.from(once), Array.from(each));
});

test('cutoutColours leaves alone a pixel that cannot be a mix with the background', () => {
  // A blue shirt the model was unsure of (60%): taking the wall out would need a colour darker
  // than black, so it is the shirt's own colour, not a mix, and keeps it.
  const shirt = [45, 79, 138];
  const pixels = rowOf([shirt]);
  tools.cutoutColours(pixels, new Uint8ClampedArray([153]), 1, flatWall(WALL));
  assert.deepEqual(rgbAt(pixels, 1, 0, 0), shirt);
});

test('cutoutBackground with too little sure background for any square averages what there is', () => {
  // One sure background pixel in a 60 x 60 photo, everything else unsure: no square of 20 px
  // holds enough to be read on its own.
  const [width, height] = [60, 60];
  const pixels = new Uint8ClampedArray(width * height * 4);
  for (let i = 0; i < width * height; i += 1) pixels.set([...SKIN, 255], i * 4);
  pixels.set([...WALL, 255], 0);
  const alpha = new Uint8ClampedArray(width * height).fill(128);
  alpha[0] = 0;
  const background = tools.cutoutBackground(pixels, alpha, width, height, 20);
  assert.ok(Array.from(background.field).every(Number.isFinite), 'no square is left without a colour');
  assert.deepEqual(Array.from(tools.wallColourAt(background, 50, 50, [0, 0, 0])).map(Math.round), WALL);
});

test('scaleBackground gives the same colours at the same spot of a larger copy', () => {
  const field = Float32Array.from([10, 10, 10, 200, 200, 200, 100, 100, 100, 50, 50, 50]);
  const small = { field, cell: 10, columns: 2, rows: 2, limit: 14 };
  const large = tools.scaleBackground(small, 3);
  for (const [x, y] of [[0, 0], [7, 4], [15, 15], [19, 19]]) {
    const a = tools.wallColourAt(small, x, y, [0, 0, 0]).map(value => value.toFixed(3));
    const b = tools.wallColourAt(large, x * 3 + 1, y * 3 + 1, [0, 0, 0]).map(value => value.toFixed(3));
    assert.deepEqual(b, a, `spot ${x},${y}`);
  }
  assert.equal(tools.scaleBackground(null, 2), null);
});

test('a soft Erase makes the subject see-through without changing its colour', () => {
  // A solid grey subject pixel and a half-strength Erase over it.
  const grey = [150, 150, 150];
  const original = rowOf([WALL, grey]);
  const modelAlpha = new Uint8ClampedArray([0, 255]);
  const mask = new Uint8ClampedArray([0, 0, 0, 0, 0, 0, 255, 128]);
  const alpha = new Uint8ClampedArray(2);
  const colourAlpha = new Uint8ClampedArray(2);
  tools.applyCutoutStrokes(alpha, modelAlpha, original, mask, flatWall([255, 255, 255]), { width: 2, colourAlpha });
  assert.ok(Math.abs(alpha[1] - 127) <= 1, `half see-through (${alpha[1]})`);
  assert.equal(colourAlpha[1], 255, 'its colour is still all subject');

  const colours = new Uint8ClampedArray(original);
  tools.cutoutColours(colours, colourAlpha, 2, flatWall([255, 255, 255]), null, alpha);
  assert.deepEqual(rgbAt(colours, 2, 1, 0), grey, 'the colour is kept');
  assert.ok(Math.abs(colours[7] - 127) <= 1, 'and the pixel is half see-through');
});

// ----- Review fixes (PR 4 of docs/ux/photo-tools-review-fixes.md) -----

test('measuring weighs a pixel by how visible it is', () => {
  // A nearly invisible yellow fringe beside solid grey: the grey decides, not the fringe.
  const fringe = new Uint8ClampedArray([255, 255, 0, 1, 100, 100, 100, 255]);
  const lighting = tools.measureLighting(fringe);
  for (const gain of lighting.gains) assert.ok(Math.abs(gain - 1) < 0.01, `gain ${gain} should be about 1`);
  assert.deepEqual([lighting.low, lighting.high], [0, 255], 'no stretch from one grey level');

  // Paper and ink: dark ink drawn only in see-through pixels is not ink.
  const page = [];
  for (let i = 0; i < 400; i += 1) page.push(230, 230, 230, 255);
  for (let i = 0; i < 400; i += 1) page.push(0, 0, 0, 1);
  assert.equal(tools.measurePaper(new Uint8ClampedArray(page)), null);
});

test('encodeToTarget never makes the short side smaller than 16 px', async () => {
  const { encode, calls } = fakeEncoder(4);
  const result = await tools.encodeToTarget({ encode, lossy: true, width: 20, height: 3000, targetBytes: 10 });
  assert.equal(result.reached, false);
  for (const call of calls) assert.ok(Math.min(call.width, call.height) >= 16, `${call.width} × ${call.height}`);
});

test('a fixed-shape crop works out its height from the whole-pixel width', () => {
  const bounds = { width: 100, height: 120 };
  const ratio = 9 / 16;
  // 120 high would need 67.5 wide: 67 × 119 is closer to 9 : 16 than 68 × 120.
  const crop = tools.defaultCrop(bounds, ratio);
  assert.deepEqual(crop, { x: 16, y: 0, width: 67, height: 119 });
  const moved = tools.resizeCrop({ x: 0, y: 0, width: 45, height: 80 }, 'se', { x: 54, y: 96 }, ratio, bounds);
  assert.equal(moved.height, Math.round(moved.width / ratio));
  assert.ok(moved.y + moved.height <= bounds.height);
});

test('Edit photo caps the picture area at what every browser can draw', () => {
  assert.equal(tools.EDIT_MAX_AREA, 4096 * 4096);
});

// ----- Reduce size keeps the pixels (PR 8 of docs/ux/photo-tools-review-fixes.md) -----

test('Reduce size keeps the pixels by default and drops the old saved 1600 px default', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const page = fs.readFileSync(path.join(__dirname, '..', '..', '..', 'Pages', 'Tools', 'Photo', 'Index.cshtml'), 'utf8');
  const app = fs.readFileSync(path.join(__dirname, 'photo-tools-app.js'), 'utf8');
  assert.match(page, /<option value="keep" selected>/);
  assert.doesNotMatch(page, /<option value="longest:1600" selected>/);
  assert.match(app, /version: SETTINGS_VERSION/);
  assert.match(app, /saved\.version !== SETTINGS_VERSION && saved\.dimensions === OLD_DEFAULT_DIMENSIONS/);
});

test('with the pixels kept, a size is reached by quality first and pixels only when needed', async () => {
  // 4000 × 3000 at about 1 byte a pixel: 0.92 quality is too big for 6 MB, a lower one fits.
  const { encode, calls } = fakeEncoder(1);
  const fits = await tools.encodeToTarget({ encode, lossy: true, width: 4000, height: 3000, targetBytes: 6000000 });
  assert.equal(fits.reached, true);
  assert.deepEqual([fits.width, fits.height], [4000, 3000], 'the pixels are kept');
  assert.ok(calls.every(call => call.width === 4000));

  // 100 KB cannot be reached at 4000 × 3000 even at the lowest quality: the pixels are made fewer.
  const small = await tools.encodeToTarget({ encode, lossy: true, width: 4000, height: 3000, targetBytes: 100000 });
  assert.equal(small.reached, true);
  assert.ok(small.width < 4000);
});

// ----- The face placed (PR 4 of docs/ux/photo-tools-passport-done-for-you.md) -----

// A face as the detector answers: eyes 40 px apart at y 300, the box from y 250 to 430.
function detectedFace(tiltDegrees = 0) {
  const t = tiltDegrees * Math.PI / 180;
  const eye = (dx) => [400 + dx * Math.cos(t), 300 + dx * Math.sin(t)];
  return { box: { left: 340, top: 250, width: 120, height: 180 }, points: [eye(-20), eye(20), [400, 340], [385, 380], [415, 380]] };
}

test('placeFace puts the head between the guide lines, the eyes level and the face in the middle', () => {
  const frame = { frameWidth: 350, frameHeight: 450 };
  const face = detectedFace(5);
  const placement = tools.placeFace({ imageWidth: 800, imageHeight: 1000, ...frame, face });
  assert.ok(Math.abs(placement.angle + 5 * Math.PI / 180) < 1e-9, 'turned to make the eyes level');
  assert.ok(placement.zoom >= 1 && placement.zoom <= 4);
  const scale = tools.coverScale(800, 1000, 350, 450, placement.angle) * placement.zoom;
  const shares = tools.faceShares({ imageWidth: 800, imageHeight: 1000, ...frame, face, placement: { angle: placement.angle, scale, x: placement.x, y: placement.y } });
  assert.ok(Math.abs(shares.crown - tools.PASSPORT_GUIDE.top) < 0.01, `crown ${shares.crown}`);
  assert.ok(Math.abs(shares.chin - tools.PASSPORT_GUIDE.chin) < 0.01, `chin ${shares.chin}`);
  assert.ok(Math.abs(shares.centre - 0.5) < 0.01, `centre ${shares.centre}`);
});

test('placeFace puts the top of a head that stays too small on the top line', () => {
  const frame = { frameWidth: 350, frameHeight: 450 };
  // A small face in a large photo: 4 times is not enough to fill the guide.
  const face = { box: { left: 1990, top: 500, width: 20, height: 30 }, points: [[1997, 510], [2003, 510], [2000, 515], [1998, 520], [2002, 520]] };
  const placement = tools.placeFace({ imageWidth: 4000, imageHeight: 5000, ...frame, face, maxAngle: 0 });
  assert.equal(placement.tooFar, true);
  assert.equal(placement.zoom, 4);
  const scale = tools.coverScale(4000, 5000, 350, 450, placement.angle) * placement.zoom;
  const shares = tools.faceShares({ imageWidth: 4000, imageHeight: 5000, ...frame, face, placement: { angle: placement.angle, scale, x: placement.x, y: placement.y } });
  assert.ok(Math.abs(shares.crown - tools.PASSPORT_GUIDE.top) < 0.01, `crown ${shares.crown}`);
  assert.ok(shares.chin < tools.PASSPORT_GUIDE.chin, 'the head is still small');
  // A face that fits is not "too far".
  assert.equal(tools.placeFace({ imageWidth: 800, imageHeight: 1000, ...frame, face: detectedFace(0) }).tooFar, false);
});

test('placeFace leaves a photo on its side for Rotate', () => {
  const frame = { imageWidth: 800, imageHeight: 1000, frameWidth: 350, frameHeight: 450 };
  // Eyes one above the other: the photo is on its side, or upside down.
  assert.equal(tools.placeFace({ ...frame, face: detectedFace(90) }), null);
  assert.equal(tools.placeFace({ ...frame, face: detectedFace(-90) }), null);
  assert.equal(tools.placeFace({ ...frame, face: detectedFace(180) }), null);
  // Up to 30 degrees is still placed, with the turn held at the straighten range.
  assert.ok(tools.placeFace({ ...frame, face: detectedFace(28) }));
});

test('placeFace keeps the turn within the straighten range and the zoom at 1 or more', () => {
  const tilted = tools.placeFace({ imageWidth: 800, imageHeight: 1000, frameWidth: 350, frameHeight: 450, face: detectedFace(25) });
  assert.ok(Math.abs(tilted.angle) <= Math.PI / 12 + 1e-9);
  // A face filling the whole photo cannot be made smaller than "just fills the frame".
  const huge = { box: { left: 0, top: 0, width: 800, height: 1000 }, points: [[300, 400], [500, 400], [400, 550], [340, 700], [460, 700]] };
  assert.equal(tools.placeFace({ imageWidth: 800, imageHeight: 1000, frameWidth: 350, frameHeight: 450, face: huge }).zoom, 1);
});

test('passportChecks judges the head from the real face when there is one', () => {
  const run = (face) => tools.passportChecks({
    pixels: new Uint8ClampedArray(100 * 130 * 4).fill(150), width: 100, height: 130,
    found: null, plain: true, colour: [240, 240, 240], replaced: false, enlarged: 0.6, face
  }).map(problem => problem.key);
  assert.deepEqual(run({ crown: 0.08, chin: 0.83, centre: 0.5 }), []);
  assert.deepEqual(run({ crown: 0.0, chin: 0.75, centre: 0.5 }), ['head-cut']);
  assert.deepEqual(run({ crown: 0.02, chin: 0.80, centre: 0.5 }), ['head-high']);
  assert.deepEqual(run({ crown: 0.16, chin: 0.90, centre: 0.5 }), ['head-low']);
  assert.deepEqual(run({ crown: 0.08, chin: 0.98, centre: 0.5 }), ['head-large']);
  assert.deepEqual(run({ crown: 0.09, chin: 0.72, centre: 0.5 }), ['head-small']);
  assert.deepEqual(run({ crown: 0.08, chin: 0.83, centre: 0.62 }), ['face-side']);
  assert.deepEqual(run(false), ['no-face']);
});

// ----- ID card copy (PR 4 of docs/ux/photo-tools-more.md) -----

test('idCardLayout puts the front above the back on A4, true size, centred, with room below', () => {
  const wide = tools.idCardLayout();
  assert.equal(wide.width, 2480);
  assert.equal(wide.height, 3508);
  // 85.6 × 54 mm at 300 dpi.
  assert.deepEqual(wide.card, { width: 1011, height: 638 });
  const [front, back] = wide.positions;
  assert.equal(front.x, back.x);
  assert.ok(Math.abs(front.x * 2 + wide.card.width - wide.width) <= 1, 'centred across');
  assert.equal(front.y, 295, '25 mm from the top');
  assert.equal(back.y - (front.y + wide.card.height), 142, '12 mm between the cards');
  assert.ok(back.y + wide.card.height < wide.height / 2, 'both cards in the top half, room to sign below');

  const tall = tools.idCardLayout(true);
  assert.deepEqual(tall.card, { width: 638, height: 1011 });
  assert.ok(tall.positions[1].y + tall.card.height + 59 <= tall.height, 'an upright pair still fits inside the margin');

  // The PDF plan Print sheet uses takes it as it is: the card is 85.6 mm wide in points.
  const pdf = tools.sheetPdfPlan(wide, wide.card.width, wide.card.height);
  assert.ok(Math.abs(pdf.photos[0].width - 85.6 / 25.4 * 72) < 0.2);
  assert.ok(Math.abs(pdf.pageWidth - 595.3) < 0.2);
  assert.equal(pdf.photos.length, 2);
});
