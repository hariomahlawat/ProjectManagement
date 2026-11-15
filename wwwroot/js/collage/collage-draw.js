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
      tracePath(ctx, core.shapePath(shape, 0, 0, width, height, radius));
      shadow(true);
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

  return Object.freeze({ drawPhoto, drawText, drawSticker, tracePath, canFilter, CAPTION_FONT });
});
