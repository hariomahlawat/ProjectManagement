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
