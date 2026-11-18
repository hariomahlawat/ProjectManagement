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
  for (const name of ['arc', 'strokeText', 'save', 'restore', 'beginPath', 'closePath', 'moveTo', 'lineTo', 'quadraticCurveTo', 'bezierCurveTo', 'ellipse', 'rect', 'roundRect', 'clip', 'fill', 'translate', 'scale', 'rotate', 'fillRect', 'fillText', 'drawImage']) {
    ctx[name] = (...args) => calls.push([name, ...args]);
  }
  const gradient = (kind) => (...args) => { calls.push([kind, ...args]); return { addColorStop: (...stop) => calls.push(['stop', ...stop]) }; };
  ctx.createLinearGradient = gradient('linear');
  ctx.createRadialGradient = gradient('radial');
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

test('a sticker fills its box, mirrored and shadowed when asked', () => {
  const ctx = recorder();
  const item = collage.stickerItem('s', 'draw:heart', { x: 0, y: 0, width: 110, aspect: 1.1 }, { flip: true, shadow: true });
  draw.drawSticker(ctx, item, 'IMG', 2);
  assert.ok(ctx.calls.some((c) => c[0] === 'scale' && c[1] === -1));
  assert.ok(ctx.calls.some((c) => c[0] === 'shadowBlur' && c[1] === 20));
  const image = ctx.calls.find((c) => c[0] === 'drawImage');
  assert.deepEqual(image.slice(0, 5), ['drawImage', 'IMG', 0, 0, 110]);
  assert.ok(Math.abs(image[5] - 100) < 1e-9);
});

test('a background fills the whole page: colour, gradient, pattern, texture or blurred photo', () => {
  const colour = recorder();
  draw.drawBackground(colour, { type: 'color', value: '#123456' }, 1000, 700);
  assert.ok(colour.calls.some((c) => c[0] === 'fillStyle' && c[1] === '#123456'));
  assert.ok(colour.calls.some((c) => c[0] === 'fillRect' && c[3] === 1000 && c[4] === 700));

  const sunset = recorder();
  draw.drawBackground(sunset, { type: 'gradient', key: 'sunset' }, 1000, 700);
  assert.ok(sunset.calls.some((c) => c[0] === 'linear'));
  assert.equal(sunset.calls.filter((c) => c[0] === 'stop').length, 3);
  const festive = recorder();
  draw.drawBackground(festive, { type: 'gradient', key: 'festive' }, 1000, 700);
  assert.ok(festive.calls.some((c) => c[0] === 'radial'));

  for (const key of Object.keys(collage.PATTERNS)) {
    const ctx = recorder();
    draw.drawBackground(ctx, { type: 'pattern', key }, 1000, 700);
    assert.ok(ctx.calls.filter((c) => c[0] === 'fillRect' || c[0] === 'arc').length > 10, key);
  }
  for (const key of Object.keys(collage.TEXTURES)) {
    const ctx = recorder();
    draw.drawBackground(ctx, { type: 'texture', key }, 1000, 700);
    assert.ok(ctx.calls.length > 20, key);
  }
  // The same pattern draws the same way twice: the page and the saved picture match.
  const one = recorder();
  const two = recorder();
  draw.drawBackground(one, { type: 'texture', key: 'kraft' }, 400, 300);
  draw.drawBackground(two, { type: 'texture', key: 'kraft' }, 400, 300);
  assert.deepEqual(one.calls, two.calls);

  const photo = recorder();
  draw.drawBackground(photo, { type: 'photo', photo: 'p' }, 1000, 700, { photo: { source: 'SRC', width: 400, height: 300 }, pixelsPerUnit: 2 });
  assert.ok(photo.calls.some((c) => c[0] === 'filter' && c[1].startsWith('blur(28px)')));
  const image = photo.calls.find((c) => c[0] === 'drawImage');
  assert.ok(image[4] >= 1000 && image[5] >= 700, 'the photo covers the page');
});

test('a caption on a photo without a polaroid is written on a white strip under the picture', () => {
  const ctx = recorder();
  const item = { ...collage.photoItem('a', 'p1', 1, { x: 0, y: 0, width: 400 }), caption: 'Farewell, Col Rao' };
  draw.drawPhoto(ctx, item, {}, 1000, 1000, 1);
  const strip = 400 * collage.CAPTION_STRIP;
  // The picture stops above the strip.
  const image = ctx.calls.find((c) => c[0] === 'drawImage');
  assert.equal(image[9], 400 - strip);
  // The strip is filled white, and the words are written in its middle.
  assert.ok(ctx.calls.some((c) => c[0] === 'fillStyle' && c[1] === '#ffffff'));
  const words = ctx.calls.find((c) => c[0] === 'fillText');
  assert.equal(words[1], 'Farewell, Col Rao');
  assert.equal(words[3], 400 - strip / 2);
  assert.ok(ctx.calls.some((c) => c[0] === 'font' && /Mulish/.test(c[1])));
});

test('a polaroid keeps its handwritten caption and has no strip', () => {
  const ctx = recorder();
  const item = { ...collage.photoItem('a', 'p1', 0.8, { x: 0, y: 0, width: 400 }), frame: 'polaroid', caption: 'Picnic' };
  assert.equal(collage.hasCaptionStrip(item), false);
  draw.drawPhoto(ctx, item, {}, 1000, 1000, 1);
  assert.ok(ctx.calls.some((c) => c[0] === 'font' && c[1].includes(draw.CAPTION_FONT)));
  assert.ok(!ctx.calls.some((c) => c[0] === 'font' && /Mulish/.test(c[1])));
});

// Lines and borders (docs/ux/invitations-posters.md, PR 3).
function shapeRecorder() {
  const ctx = recorder();
  Object.defineProperty(ctx, 'strokeStyle', { set(v) { ctx.calls.push(['strokeStyle', v]); } });
  Object.defineProperty(ctx, 'lineWidth', { set(v) { ctx.calls.push(['lineWidth', v]); } });
  for (const name of ['stroke', 'arcTo']) ctx[name] = (...args) => ctx.calls.push([name, ...args]);
  return ctx;
}

test('a rectangle is stroked inside its box, filled when asked, with rounded corners', () => {
  const ctx = shapeRecorder();
  const item = collage.shapeItem('r', 'rect', { x: 0, y: 0, width: 200, aspect: 2 }, { fill: '#fbf7ee', stroke: '#a8842f', strokeWidth: 10, radius: 20 });
  draw.drawShape(ctx, item);
  // The stroke's outer edge is the box's edge: the path starts half a stroke in.
  assert.deepEqual(ctx.calls.find((c) => c[0] === 'moveTo'), ['moveTo', 25, 5]);
  assert.equal(ctx.calls.filter((c) => c[0] === 'arcTo').length, 4);
  assert.ok(ctx.calls.some((c) => c[0] === 'fillStyle' && c[1] === '#fbf7ee'));
  assert.ok(ctx.calls.some((c) => c[0] === 'strokeStyle' && c[1] === '#a8842f'));
  assert.ok(ctx.calls.some((c) => c[0] === 'lineWidth' && c[1] === 10));
  assert.deepEqual(names(ctx).filter((n) => n === 'fill' || n === 'stroke'), ['fill', 'stroke']);
});

test('an oval fills its box; a line runs across the middle; nothing is drawn without colour', () => {
  const oval = shapeRecorder();
  draw.drawShape(oval, collage.shapeItem('o', 'ellipse', { x: 0, y: 0, width: 300, aspect: 1.5 }, { stroke: '', fill: '#a8842f' }));
  assert.deepEqual(oval.calls.find((c) => c[0] === 'ellipse').slice(1, 5), [150, 100, 150, 100]);
  assert.ok(!names(oval).includes('stroke'));

  const line = shapeRecorder();
  draw.drawShape(line, collage.shapeItem('l', 'line', { x: 0, y: 0, width: 240 }, { stroke: '#172033', strokeWidth: 6 }));
  const middle = collage.SHAPE_LIMITS.lineBox / 2;
  assert.deepEqual(line.calls.filter((c) => c[0] === 'moveTo' || c[0] === 'lineTo'), [['moveTo', 0, middle], ['lineTo', 240, middle]]);
  assert.ok(line.calls.some((c) => c[0] === 'lineWidth' && c[1] === 6));

  const blank = shapeRecorder();
  draw.drawShape(blank, collage.shapeItem('b', 'rect', { x: 0, y: 0, width: 100 }, { stroke: '', fill: '' }));
  assert.ok(!names(blank).includes('fill') && !names(blank).includes('stroke'));
});

test('a line thicker than a small shape stays inside its box: the inset is half the line drawn', () => {
  const ctx = shapeRecorder();
  // 100 × 24 with a 40-unit line: the line drawn is 12 (half the short side), inset 6.
  draw.drawShape(ctx, collage.shapeItem('r', 'rect', { x: 0, y: 0, width: 100, aspect: 100 / 24 }, { stroke: '#a8842f', strokeWidth: 40 }));
  const width = ctx.calls.find((c) => c[0] === 'lineWidth')[1];
  const [, x, y] = ctx.calls.find((c) => c[0] === 'moveTo');
  assert.equal(width, 12);
  assert.equal(y, width / 2, 'the outer edge of the line is the top of the box');
  assert.equal(x, width / 2);
});

test('words fitted to a width are drawn at the size the layout chose', () => {
  const ctx = recorder();
  const item = collage.textItem('t', 'A long heading here', { x: 0, y: 0 }, { size: 40, maxWidth: 100, maxLines: 1 });
  const layout = collage.layoutText(item, (text, font) => [...text].length * Number(/(\d+(?:\.\d+)?)px/.exec(font)[1]) / 2);
  assert.ok(layout.size < 40);
  draw.drawText(ctx, item, layout);
  const font = ctx.calls.find((c) => c[0] === 'font')[1];
  assert.match(font, new RegExp(`${layout.size}px`));
});
