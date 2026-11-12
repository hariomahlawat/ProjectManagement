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

test('formatBytes uses B, KB and MB', () => {
  assert.equal(tools.formatBytes(512), '512 B');
  assert.equal(tools.formatBytes(98 * 1024), '98 KB');
  assert.equal(tools.formatBytes(3.2 * 1024 * 1024), '3.2 MB');
});

test('parseTargetBytes reads KB and MB and rejects nonsense', () => {
  assert.equal(tools.parseTargetBytes('100', 'KB'), 102400);
  assert.equal(tools.parseTargetBytes('1.5', 'MB'), 1572864);
  assert.equal(tools.parseTargetBytes('1,5', 'MB'), 1572864);
  assert.equal(tools.parseTargetBytes('', 'KB'), null);
  assert.equal(tools.parseTargetBytes('0', 'KB'), null);
  assert.equal(tools.parseTargetBytes('abc', 'KB'), null);
  // A number written with thousands is not read as a decimal: "1,000 KB" is not 1 KB.
  assert.equal(tools.parseTargetBytes('1,000', 'KB'), 1024000);
  assert.equal(tools.parseTargetBytes('1,500,000', 'KB'), 1536000000);
  assert.equal(tools.parseTargetBytes('1,50', 'MB'), 1572864);
  assert.equal(tools.parseTargetBytes('1,0,0', 'KB'), null);
});

test('resizeDimensions keeps the shape and never enlarges', () => {
  assert.deepEqual(tools.resizeDimensions(4000, 3000, { type: 'keep' }), { width: 4000, height: 3000 });
  assert.deepEqual(tools.resizeDimensions(4000, 3000, { type: 'longest', value: 1600 }), { width: 1600, height: 1200 });
  assert.deepEqual(tools.resizeDimensions(3000, 4000, { type: 'longest', value: 1600 }), { width: 1200, height: 1600 });
  assert.deepEqual(tools.resizeDimensions(800, 600, { type: 'longest', value: 1600 }), { width: 800, height: 600 });
  assert.deepEqual(tools.resizeDimensions(4000, 3000, { type: 'percent', value: 50 }), { width: 2000, height: 1500 });
  assert.deepEqual(tools.resizeDimensions(4000, 3000, { type: 'percent', value: 250 }), { width: 4000, height: 3000 });
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

test('toGreyscale makes every pixel grey and leaves transparency alone', () => {
  const pixels = new Uint8ClampedArray([255, 0, 0, 255, 0, 255, 0, 128, 10, 10, 10, 0]);
  tools.toGreyscale(pixels);
  assert.deepEqual(Array.from(pixels), [76, 76, 76, 255, 150, 150, 150, 128, 10, 10, 10, 0]);
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
  const [photoPaper, a4] = tools.SHEET_SIZES;

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

test('hidePixels cover leaves one plain colour and nothing of what was there', () => {
  assert.deepEqual(Array.from(tools.HIDE_MODES), ['cover', 'blur', 'pixelate']);

  // Dark writing on light paper: every pixel becomes the same colour, so no stroke is left.
  const writing = picture(80, 20, (x, y) => ((x % 7 < 2 && y > 4 && y < 16) ? 20 : 230));
  tools.hidePixels(writing, 80, 20, 'cover');
  const first = Array.from(writing.slice(0, 4));
  for (let i = 0; i < writing.length; i += 4) {
    assert.deepEqual(Array.from(writing.slice(i, i + 4)), first, `pixel ${i / 4} is the plain colour`);
  }
  assert.ok(first[0] > 20 && first[0] < 230, 'the colour is the average of the area');
  assert.equal(first[3], 255, 'a solid photo stays solid');

  // Two different texts of the same amount of ink give exactly the same cover.
  const one = picture(40, 10, (x) => (x < 10 ? 0 : 255));
  const two = picture(40, 10, (x) => (x >= 30 ? 0 : 255));
  tools.hidePixels(one, 40, 10, 'cover');
  tools.hidePixels(two, 40, 10, 'cover');
  assert.deepEqual(Array.from(one), Array.from(two));
});
