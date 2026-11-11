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
