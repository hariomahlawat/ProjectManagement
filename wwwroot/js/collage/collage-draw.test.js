const test = require('node:test');
const assert = require('node:assert/strict');
const collage = require('./collage-core.js');
const draw = require('./collage-draw.js');

// A 2D context that records what is drawn, enough to check the order and the settings.
function recorder({ filters = true } = {}) {
  const calls = [];
  const ctx = {
    calls,
    set fillStyle(v) { calls.push(['fillStyle', v]); },
    set font(v) { calls.push(['font', v]); },
    set textAlign(v) {}, set textBaseline(v) {},
    set shadowColor(v) { calls.push(['shadowColor', v]); },
    set shadowBlur(v) { calls.push(['shadowBlur', v]); },
    set shadowOffsetX(v) {}, set shadowOffsetY(v) {}
  };
  for (const name of ['strokeText', 'save', 'restore', 'beginPath', 'closePath', 'moveTo', 'lineTo', 'quadraticCurveTo', 'bezierCurveTo', 'ellipse', 'rect', 'roundRect', 'clip', 'fill', 'translate', 'scale', 'rotate', 'fillRect', 'fillText', 'drawImage']) {
    ctx[name] = (...args) => calls.push([name, ...args]);
  }
  if (filters) {
    Object.defineProperty(ctx, 'filter', { set(v) { calls.push(['filter', v]); }, get() { return 'none'; }, enumerable: true });
  }
  return ctx;
}

const names = (ctx) => ctx.calls.map((c) => c[0]);

test('a plain photo is clipped to its box and drawn from the part its frame shows', () => {
  const ctx = recorder();
  const item = collage.photoItem('a', 'p', 1, { x: 0, y: 0, width: 100 });
  draw.drawPhoto(ctx, item, 'SRC', 400, 300);
  const order = names(ctx);
  assert.ok(order.indexOf('clip') < order.indexOf('drawImage'));
  assert.ok(!order.includes('filter'), 'no look, no filter');
  assert.ok(!order.includes('fill'), 'no frame and no shadow, nothing filled behind');
  const image = ctx.calls.find((c) => c[0] === 'drawImage');
  // A square frame on a 4:3 photo shows its middle 300 × 300 pixels.
  assert.deepEqual(image, ['drawImage', 'SRC', 50, 0, 300, 300, 0, 0, 100, 100]);
});

test('a polaroid is filled white, its photo set inside and its caption written below', () => {
  const ctx = recorder();
  const item = { ...collage.photoItem('a', 'p', 1, { x: 0, y: 0, width: 200 }), frame: 'polaroid', caption: 'Our team', shadow: true };
  item.aspect = collage.outerAspectFor(item, 1);
  draw.drawPhoto(ctx, item, 'SRC', 100, 100, 2);
  const order = names(ctx);
  assert.ok(order.indexOf('fill') < order.indexOf('drawImage'), 'the frame first, then the photo');
  assert.ok(ctx.calls.some((c) => c[0] === 'shadowBlur' && c[1] === 28), 'the shadow is in device pixels');
  const image = ctx.calls.find((c) => c[0] === 'drawImage');
  assert.deepEqual(image.slice(6), [10, 10, 180, 180]);
  const text = ctx.calls.find((c) => c[0] === 'fillText');
  assert.equal(text[1], 'Our team');
});

test('a look sets the filter, and a mirrored photo is flipped inside its frame', () => {
  const ctx = recorder();
  const item = { ...collage.photoItem('a', 'p', 1, { x: 0, y: 0, width: 100 }), look: 'bw', flip: true };
  draw.drawPhoto(ctx, item, 'SRC', 100, 100);
  assert.ok(ctx.calls.some((c) => c[0] === 'filter' && c[1] === 'grayscale(1) contrast(1.08)'));
  assert.ok(ctx.calls.some((c) => c[0] === 'scale' && c[1] === -1 && c[2] === 1));
  // A canvas without filters still draws the photo, only without the look.
  const old = recorder({ filters: false });
  draw.drawPhoto(old, item, 'SRC', 100, 100);
  assert.ok(names(old).includes('drawImage'));
});

test('a shaped photo is cut to its shape, and taped and torn frames draw their extras', () => {
  const heart = recorder();
  draw.drawPhoto(heart, { ...collage.photoItem('a', 'p', 1, { x: 0, y: 0, width: 100 }), shape: 'heart' }, 'SRC', 100, 100);
  assert.ok(names(heart).includes('bezierCurveTo'));
  const taped = recorder();
  draw.drawPhoto(taped, { ...collage.photoItem('a', 'p', 1, { x: 0, y: 0, width: 100 }), frame: 'tape' }, 'SRC', 100, 100);
  assert.equal(taped.calls.filter((c) => c[0] === 'fillRect').length, 2, 'two strips of tape');
  const torn = recorder();
  draw.drawPhoto(torn, { ...collage.photoItem('a', 'p', 1, { x: 0, y: 0, width: 100 }), frame: 'torn' }, 'SRC', 100, 100);
  assert.ok(torn.calls.filter((c) => c[0] === 'lineTo').length > 80, 'a jagged edge');
});

test('text is drawn a whole line at a time, with its label, outline and shadow', () => {
  const ctx = recorder();
  const item = collage.textItem('t', 'शुभ दीपावली\nHappy Diwali', { x: 0, y: 0 }, { size: 40, outline: 3, shadow: true, label: '#ffd23f' });
  const layout = collage.layoutText(item, (text) => text.length * 20);
  draw.drawText(ctx, item, layout, 2);
  const texts = ctx.calls.filter((c) => c[0] === 'fillText').map((c) => c[1]);
  assert.deepEqual(texts, ['शुभ दीपावली', 'Happy Diwali'], 'each line once, whole');
  assert.equal(ctx.calls.filter((c) => c[0] === 'strokeText').length, 2, 'outlined');
  const order = names(ctx);
  assert.ok(order.indexOf('fill') < order.indexOf('fillText'), 'the label is behind the words');
  assert.ok(ctx.calls.some((c) => c[0] === 'shadowBlur' && c[1] > 0));
});

test('curved text is drawn letter cluster by cluster, each turned along the arc', () => {
  const ctx = recorder();
  const item = collage.textItem('t', 'HAPPY', { x: 0, y: 0 }, { size: 40, curve: 40 });
  draw.drawText(ctx, item, collage.layoutText(item, (text) => text.length * 20));
  assert.equal(ctx.calls.filter((c) => c[0] === 'fillText').length, 5);
  assert.equal(ctx.calls.filter((c) => c[0] === 'rotate').length, 4, 'all but the middle letter lean');
});
