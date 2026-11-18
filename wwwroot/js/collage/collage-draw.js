// PRISM Collage Studio — drawing one photo with its frame, shape and look.
//
// The same function paints a photo on the screen (inside Konva) and into the saved picture, so
// what is saved is what was shown. It draws in the item's own units, with (0, 0) at its top-left
// corner; the caller has already moved and turned the canvas to the item. Nothing here touches
// the page or the network.
(function (root, factory) {
  'use strict';
  const api = factory(root.PrismCollage || (typeof require === 'function' ? require('./collage-core.js') : null));
  if (typeof module === 'object' && module.exports) {
    module.exports = api;
  } else {
    root.PrismCollageDraw = api;
  }
})(typeof self !== 'undefined' ? self : this, function (core) {
  'use strict';

  // Caveat ships with PRISM (wwwroot/fonts/collage); Kalam writes a Hindi caption.
  const CAPTION_FONT = 'Caveat, Kalam, cursive';
  // A caption on a strip under the photo: plain, easy to read, Hindi too.
  const STRIP_FONT = '600 {size}px Mulish, "Noto Sans Devanagari", sans-serif';
  const STRIP_TEXT = '#2b2f36';
  const FRAME_WHITE = '#ffffff';
  const POLAROID_WHITE = '#fffefa';
  const TAPE = 'rgba(250, 236, 180, 0.78)';
  const SHADOW = 'rgba(0, 0, 0, 0.32)';

  /** Follows a path from core.shapePath() on a 2D context. */
  function tracePath(ctx, path) {
    ctx.beginPath();
    for (const step of path) {
      const [op, ...a] = step;
      if (op === 'M') ctx.moveTo(a[0], a[1]);
      else if (op === 'L') ctx.lineTo(a[0], a[1]);
      else if (op === 'Q') ctx.quadraticCurveTo(a[0], a[1], a[2], a[3]);
      else if (op === 'C') ctx.bezierCurveTo(a[0], a[1], a[2], a[3], a[4], a[5]);
      else if (op === 'E') ctx.ellipse(a[0], a[1], a[2], a[3], 0, 0, Math.PI * 2);
      else if (op === 'R') {
        if (a[4] > 0 && typeof ctx.roundRect === 'function') ctx.roundRect(a[0], a[1], a[2], a[3], a[4]);
        else ctx.rect(a[0], a[1], a[2], a[3]);
      } else if (op === 'Z') ctx.closePath();
    }
  }

  function tracePoints(ctx, points) {
    ctx.beginPath();
    points.forEach(([x, y], i) => (i ? ctx.lineTo(x, y) : ctx.moveTo(x, y)));
    ctx.closePath();
  }

  /** Whether this canvas can apply a look (filter); older Safari cannot. */
  function canFilter(ctx) {
    return 'filter' in ctx;
  }

  /**
   * Draws a photo item. `source` is the picture (a canvas or bitmap) and `sourceWidth` and
   * `sourceHeight` its pixels. `pixelsPerUnit` turns units into device pixels, for the shadow,
   * which a canvas measures in device pixels whatever its scale.
   */
  function drawPhoto(ctx, item, source, sourceWidth, sourceHeight, pixelsPerUnit = 1) {
    const width = item.width;
    const height = width / item.aspect;
    const framed = core.isFramed(item);
    const inner = core.innerBox(item);
    const radius = item.radius || 0;
    const shape = item.shape || 'rect';
    const shadow = (on) => {
      ctx.shadowColor = on ? SHADOW : 'transparent';
      ctx.shadowBlur = on ? 14 * pixelsPerUnit : 0;
      ctx.shadowOffsetX = 0;
      ctx.shadowOffsetY = on ? 6 * pixelsPerUnit : 0;
    };

    ctx.save();
    // The frame (or, for a shaped photo, the shape) is filled first so its shadow falls behind.
    if (framed) {
      if (item.frame === 'torn') tracePoints(ctx, core.tornEdge(width, height));
      else tracePath(ctx, core.shapePath('rect', 0, 0, width, height, radius));
      shadow(item.shadow);
      ctx.fillStyle = item.frame === 'polaroid' ? POLAROID_WHITE : FRAME_WHITE;
      ctx.fill();
      shadow(false);
    } else if (item.shadow) {
      tracePath(ctx, core.shapePath(shape, inner.x, inner.y, inner.width, inner.height, radius));
      shadow(true);
      ctx.fillStyle = FRAME_WHITE;
      ctx.fill();
      shadow(false);
    }

    // A caption strip on a photo without a frame: white, under the picture.
    const strip = core.captionStripHeight(item);
    if (strip && !framed) {
      tracePath(ctx, core.shapePath('rect', 0, height - strip, width, strip, Math.min(radius, strip / 2)));
      shadow(item.shadow);
      ctx.fillStyle = FRAME_WHITE;
      ctx.fill();
      shadow(false);
    }

    // The photo, cut to its shape and to the picture area.
    ctx.save();
    const innerRadius = framed ? Math.max(0, radius - inner.x) : radius;
    tracePath(ctx, framed
      ? core.shapePath('rect', inner.x, inner.y, inner.width, inner.height, innerRadius)
      : core.shapePath(shape, inner.x, inner.y, inner.width, inner.height, innerRadius));
    ctx.clip();
    const look = core.lookFilter(item);
    if (look && canFilter(ctx)) ctx.filter = look;
    const region = core.cropRegion(sourceWidth / sourceHeight, inner.width / inner.height, item.crop);
    if (item.flip) {
      ctx.translate(inner.x * 2 + inner.width, 0);
      ctx.scale(-1, 1);
    }
    ctx.drawImage(source,
      region.x * sourceWidth, region.y * sourceHeight, region.width * sourceWidth, region.height * sourceHeight,
      inner.x, inner.y, inner.width, inner.height);
    ctx.restore();

    // A polaroid's caption, in handwriting, in its deep bottom edge.
    if (framed && item.frame === 'polaroid' && item.caption) {
      const pads = core.framePads(item);
      const size = Math.max(6, width * 0.075);
      ctx.font = `${size}px ${CAPTION_FONT}`;
      ctx.fillStyle = '#333333';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(item.caption, width / 2, height - pads.bottom / 2, width - 2 * pads.left);
    }

    // A caption on its strip, in plain letters.
    if (strip) {
      const pads = core.framePads(item);
      const size = Math.max(5, strip * 0.5);
      ctx.font = STRIP_FONT.replace('{size}', String(size));
      ctx.fillStyle = STRIP_TEXT;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      const bottomEdge = pads.bottom - strip;
      ctx.fillText(item.caption, width / 2, height - bottomEdge - strip / 2, Math.max(1, width - 2 * Math.max(pads.left, strip * 0.3)));
    }

    // Two strips of tape across the top corners.
    if (framed && item.frame === 'tape') {
      ctx.fillStyle = TAPE;
      for (const side of [-1, 1]) {
        ctx.save();
        ctx.translate(side < 0 ? width * 0.06 : width * 0.94, height * 0.02);
        ctx.rotate(side * 38 * Math.PI / 180);
        ctx.fillRect(-width * 0.15, -width * 0.04, width * 0.3, width * 0.08);
        ctx.restore();
      }
    }
    ctx.restore();
  }

  /**
   * Draws a text item from its layout (core.layoutText): an optional label behind it, then each
   * line (or, on a curve, each letter cluster) with its outline, shadow or glow. Straight lines
   * are drawn whole, so letters that join (Hindi) keep their shapes.
   */
  function drawText(ctx, item, layout, pixelsPerUnit = 1) {
    ctx.save();
    if (layout.label && item.label) {
      tracePath(ctx, core.shapePath('rect', layout.label.x, layout.label.y, layout.label.width, layout.label.height, item.size * 0.28));
      ctx.fillStyle = item.label;
      ctx.fill();
    }
    ctx.font = core.fontOf(item);
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    const gap = (item.spacing || 0) / 100 * item.size;
    if ('letterSpacing' in ctx) ctx.letterSpacing = `${gap}px`;
    const paint = (text) => {
      if (item.outline) {
        ctx.lineJoin = 'round';
        ctx.lineWidth = item.outline * 2;
        ctx.strokeStyle = item.outlineColor || '#ffffff';
        ctx.strokeText(text, 0, 0);
      }
      ctx.fillStyle = item.color;
      ctx.fillText(text, 0, 0);
    };
    for (const glyph of layout.glyphs) {
      ctx.save();
      // With letter spacing the canvas adds space after the last letter too; half of it is
      // taken back so a centred line stays centred.
      ctx.translate(glyph.x + ('letterSpacing' in ctx && !glyph.angle ? gap / 2 : 0), glyph.y);
      if (glyph.angle) ctx.rotate(glyph.angle);
      if (item.glow) {
        ctx.shadowColor = item.color;
        ctx.shadowBlur = Math.max(4, item.size * 0.35) * pixelsPerUnit;
        paint(glyph.ch);
      }
      if (item.shadow) {
        ctx.shadowColor = 'rgba(0, 0, 0, 0.45)';
        ctx.shadowBlur = Math.max(2, item.size * 0.12) * pixelsPerUnit;
        ctx.shadowOffsetY = Math.max(1, item.size * 0.06) * pixelsPerUnit;
      } else {
        ctx.shadowColor = 'transparent';
        ctx.shadowBlur = 0;
        ctx.shadowOffsetY = 0;
      }
      paint(glyph.ch);
      ctx.restore();
    }
    ctx.restore();
  }

  /**
   * Draws a sticker's picture filling the item's box, mirrored when asked, with an optional
   * shadow that follows the drawing's own outline.
   */
  function drawSticker(ctx, item, image, pixelsPerUnit = 1) {
    const width = item.width;
    const height = width / item.aspect;
    ctx.save();
    if (item.shadow) {
      ctx.shadowColor = 'rgba(0, 0, 0, 0.35)';
      ctx.shadowBlur = 10 * pixelsPerUnit;
      ctx.shadowOffsetY = 5 * pixelsPerUnit;
    }
    if (item.flip) {
      ctx.translate(width, 0);
      ctx.scale(-1, 1);
    }
    ctx.drawImage(image, 0, 0, width, height);
    ctx.restore();
  }

  /** The outline of a rectangle (rounded by `radius`) or oval, `inset` in from the item's box. */
  function traceShape(ctx, item, inset = 0) {
    const width = item.width;
    const height = width / item.aspect;
    const w = Math.max(0, width - inset * 2);
    const h = Math.max(0, height - inset * 2);
    ctx.beginPath();
    if (item.form === 'ellipse') {
      ctx.ellipse(width / 2, height / 2, w / 2, h / 2, 0, 0, Math.PI * 2);
      return;
    }
    const r = Math.max(0, Math.min(item.radius || 0, w / 2, h / 2));
    ctx.moveTo(inset + r, inset);
    ctx.lineTo(inset + w - r, inset);
    ctx.arcTo(inset + w, inset, inset + w, inset + r, r);
    ctx.lineTo(inset + w, inset + h - r);
    ctx.arcTo(inset + w, inset + h, inset + w - r, inset + h, r);
    ctx.lineTo(inset + r, inset + h);
    ctx.arcTo(inset, inset + h, inset, inset + h - r, r);
    ctx.lineTo(inset, inset + r);
    ctx.arcTo(inset, inset, inset + r, inset, r);
    ctx.closePath();
  }

  /**
   * Draws a line, rectangle or oval filling the item's box (docs/ux/invitations-posters.md,
   * PR 3). The stroke is drawn inside the box, so the saved picture and the handles agree; a
   * rectangle's corners are rounded by `radius`; a line runs across the middle of the box.
   */
  function drawShape(ctx, item) {
    const width = item.width;
    const height = width / item.aspect;
    const strokeWidth = Math.max(0, item.strokeWidth || 0);
    const stroked = Boolean(item.stroke) && strokeWidth > 0;
    ctx.save();
    if (item.form === 'line') {
      if (stroked) {
        ctx.strokeStyle = item.stroke;
        ctx.lineWidth = Math.min(strokeWidth, height);
        ctx.beginPath();
        ctx.moveTo(0, height / 2);
        ctx.lineTo(width, height / 2);
        ctx.stroke();
      }
      ctx.restore();
      return;
    }
    // The line actually drawn: never thicker than half the box's shorter side, so it stays inside
    // the box, and the inset is half of that same width.
    const lineWidth = stroked ? Math.min(strokeWidth, width / 2, height / 2) : 0;
    traceShape(ctx, item, lineWidth / 2);
    if (item.fill) {
      ctx.fillStyle = item.fill;
      ctx.fill();
    }
    if (stroked) {
      ctx.strokeStyle = item.stroke;
      ctx.lineWidth = lineWidth;
      ctx.stroke();
    }
    ctx.restore();
  }

  // ---------------------------------------------------------------------------
  // Backgrounds: every one is drawn in page units, so it looks the same on the page and in the
  // saved picture at any size. Speckles and grain come from core.steadyRandom, never Math.random.
  // ---------------------------------------------------------------------------

  function dot(ctx, x, y, r) {
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.fill();
  }

  function speckle(ctx, width, height, perArea, colours, sizes, seed) {
    const count = Math.round(width * height / perArea);
    for (let i = 0; i < count; i += 1) {
      ctx.fillStyle = colours[i % colours.length];
      const r = sizes[0] + core.steadyRandom(seed + i * 3) * (sizes[1] - sizes[0]);
      dot(ctx, core.steadyRandom(seed + i * 3 + 1) * width, core.steadyRandom(seed + i * 3 + 2) * height, r);
    }
  }

  function drawPattern(ctx, key, width, height) {
    if (key === 'gingham') {
      ctx.fillStyle = '#e9f6e5';
      ctx.fillRect(0, 0, width, height);
      ctx.fillStyle = 'rgba(76, 153, 92, 0.32)';
      for (let x = 0; x < width; x += 48) ctx.fillRect(x, 0, 24, height);
      for (let y = 0; y < height; y += 48) ctx.fillRect(0, y, width, 24);
    } else if (key === 'dots') {
      ctx.fillStyle = '#fff1f6';
      ctx.fillRect(0, 0, width, height);
      ctx.fillStyle = '#f48fb1';
      for (let y = 18; y < height + 18; y += 36) for (let x = 18; x < width + 18; x += 36) dot(ctx, x, y, 6.5);
    } else if (key === 'confetti') {
      ctx.fillStyle = '#fde7f1';
      ctx.fillRect(0, 0, width, height);
      const bits = [[0.2, 0.3, '#ff6b9a', 4], [0.7, 0.6, '#ffc93c', 4], [0.4, 0.8, '#4fc3f7', 3], [0.85, 0.15, '#8e6bff', 3]];
      for (let y = 0; y < height; y += 90) {
        for (let x = 0; x < width; x += 90) {
          for (const [fx, fy, colour, r] of bits) {
            ctx.fillStyle = colour;
            dot(ctx, x + fx * 90, y + fy * 90, r);
          }
        }
      }
    } else if (key === 'stripes') {
      ctx.fillStyle = '#1e4fa3';
      ctx.fillRect(0, 0, width, height);
      ctx.save();
      ctx.translate(width / 2, height / 2);
      ctx.rotate(-Math.PI / 4);
      const reach = Math.hypot(width, height);
      ctx.fillStyle = '#2a62c2';
      for (let x = -reach; x < reach; x += 80) ctx.fillRect(x, -reach, 40, reach * 2);
      ctx.restore();
    } else if (key === 'gridpaper') {
      ctx.fillStyle = '#fbfcff';
      ctx.fillRect(0, 0, width, height);
      ctx.fillStyle = '#dfe7f5';
      for (let x = 0; x < width; x += 28) ctx.fillRect(x, 0, 1, height);
      for (let y = 0; y < height; y += 28) ctx.fillRect(0, y, width, 1);
    } else if (key === 'stars') {
      ctx.fillStyle = '#14213d';
      ctx.fillRect(0, 0, width, height);
      speckle(ctx, width, height, 2600, ['#ffffff', 'rgba(255, 255, 255, 0.6)'], [0.6, 1.8], 11);
    }
  }

  function drawTexture(ctx, key, width, height) {
    if (key === 'paper' || key === 'chalk') {
      const g = ctx.createRadialGradient(width * 0.3, height * 0.2, 0, width * 0.5, height * 0.5, Math.max(width, height) * 0.85);
      const stops = key === 'paper' ? [[0, '#fffaf0'], [0.7, '#f3e3c8'], [1, '#e7d0a8']] : [[0, '#3c5a45'], [1, '#1f3326']];
      for (const [at, colour] of stops) g.addColorStop(at, colour);
      ctx.fillStyle = g;
      ctx.fillRect(0, 0, width, height);
      speckle(ctx, width, height, key === 'paper' ? 900 : 1400, key === 'paper' ? ['rgba(120, 90, 40, 0.07)'] : ['rgba(255, 255, 255, 0.06)'], [0.5, 1.4], key === 'paper' ? 5 : 9);
    } else if (key === 'kraft') {
      ctx.fillStyle = '#c9a26b';
      ctx.fillRect(0, 0, width, height);
      speckle(ctx, width, height, 140, ['rgba(255, 255, 255, 0.12)', 'rgba(0, 0, 0, 0.08)'], [0.5, 1.2], 3);
    } else if (key === 'linen') {
      ctx.fillStyle = '#efe9df';
      ctx.fillRect(0, 0, width, height);
      ctx.fillStyle = 'rgba(0, 0, 0, 0.035)';
      for (let x = 0; x < width; x += 4) ctx.fillRect(x, 0, 1, height);
      for (let y = 0; y < height; y += 4) ctx.fillRect(0, y, width, 1);
    } else if (key === 'wood') {
      const bands = [[6, '#a8743f'], [9, '#b07d47'], [4, '#9b6936'], [15, '#b5834c']];
      let x = 0;
      for (let i = 0; x < width; i += 1) {
        const [w, colour] = bands[i % bands.length];
        ctx.fillStyle = colour;
        ctx.fillRect(x, 0, w + 0.5, height);
        x += w;
      }
      speckle(ctx, width, height, 600, ['rgba(70, 40, 10, 0.08)'], [0.4, 1], 7);
    } else if (key === 'cork') {
      ctx.fillStyle = '#c89a63';
      ctx.fillRect(0, 0, width, height);
      speckle(ctx, width, height, 70, ['#a57442', '#dcb684'], [0.8, 1.6], 13);
    }
  }

  /**
   * Draws the page's background over the whole page (`width` × `height` units): a colour, a
   * gradient, a pattern, a texture or one of the photos, softly blurred. `photo` is
   * { source, width, height } for a photo background.
   */
  function drawBackground(ctx, background, width, height, { photo = null, pixelsPerUnit = 1 } = {}) {
    const bg = background || { type: 'color', value: '#ffffff' };
    ctx.save();
    ctx.beginPath();
    ctx.rect(0, 0, width, height);
    ctx.clip();
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, width, height);
    if (bg.type === 'color') {
      ctx.fillStyle = bg.value;
      ctx.fillRect(0, 0, width, height);
    } else if (bg.type === 'gradient' && core.GRADIENTS[bg.key]) {
      const entry = core.GRADIENTS[bg.key];
      let g;
      if (entry.radial) {
        g = ctx.createRadialGradient(width / 2, height * 0.4, 0, width / 2, height * 0.4, Math.max(width, height) * 0.75);
      } else {
        const a = entry.angle * Math.PI / 180;
        const half = (Math.abs(width * Math.cos(a)) + Math.abs(height * Math.sin(a))) / 2;
        const dx = Math.cos(a) * half;
        const dy = Math.sin(a) * half;
        g = ctx.createLinearGradient(width / 2 - dx, height / 2 - dy, width / 2 + dx, height / 2 + dy);
      }
      for (const [at, colour] of entry.stops) g.addColorStop(at, colour);
      ctx.fillStyle = g;
      ctx.fillRect(0, 0, width, height);
    } else if (bg.type === 'pattern') {
      drawPattern(ctx, bg.key, width, height);
    } else if (bg.type === 'texture') {
      drawTexture(ctx, bg.key, width, height);
    } else if (bg.type === 'photo' && photo) {
      // The photo fills the page, a little larger so its blurred edges stay off the page.
      const grow = 1.08;
      const scale = Math.max(width / photo.width, height / photo.height) * grow;
      const w = photo.width * scale;
      const h = photo.height * scale;
      if (canFilter(ctx)) ctx.filter = `blur(${Math.max(1, 14 * pixelsPerUnit)}px) brightness(0.92) saturate(1.1)`;
      ctx.drawImage(photo.source, (width - w) / 2, (height - h) / 2, w, h);
    }
    ctx.restore();
  }

  return Object.freeze({ drawPhoto, drawText, drawSticker, drawShape, traceShape, drawBackground, tracePath, canFilter, CAPTION_FONT, STRIP_FONT });
});
