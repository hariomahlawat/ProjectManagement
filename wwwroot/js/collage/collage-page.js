// PRISM Collage Studio — the page: the Konva stage and layers, drawing each item (photos, frames, text, stickers), the handles, zoom, Preview and the page's size on screen
// Moved out of the editor in docs/ux/collage-review-2-fixes.md, PR 13; collage-app.js creates it
// once, handing over the helpers it uses, `state` (the editor's values, read only) and the named
// changes this part may make (collage-state.js, docs/ux/collage-review-3-fixes.md, PR 10).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollagePage = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createPage(deps) {
    const { state, parts, change, root, core, Konva, draw, stickers, $, views, sizeSelect, stageBox, stageHost, angleTag, emptyHint, zoomValue, coarsePointer, photos, itemById, pageHeight, select, startAdjust, stopAdjust, startPan, commit, updateUndo, fontsArrived } = deps;
    // ---------------------------------------------------------------------------
    // The page (Konva)
    // ---------------------------------------------------------------------------

    const stage = new Konva.Stage({ container: stageHost, width: 10, height: 10 });
    // The page and its background are on a layer of their own, under the items, so a busy
    // texture is drawn again only when the background or the page changes.
    const backLayer = new Konva.Layer({ listening: false });
    const layer = new Konva.Layer();
    stage.add(backLayer, layer);
    const backWorld = new Konva.Group();
    const pageRect = new Konva.Rect({
      x: 0, y: 0, width: core.PAGE_WIDTH, height: 10, fill: '#ffffff', name: 'page',
      shadowColor: 'rgba(16, 24, 40, 0.28)', shadowBlur: 28, shadowOffsetY: 6, shadowForStrokeEnabled: false
    });
    const backgroundShape = new Konva.Shape({
      x: 0, y: 0, width: core.PAGE_WIDTH, height: 10,
      sceneFunc: (context) => {
        const pixelsPerUnit = state.scale * (backLayer.getCanvas().getPixelRatio?.() || 1);
        draw.drawBackground(context._context, state.collage.background, core.PAGE_WIDTH, pageHeight(), { photo: backgroundPhoto(), pixelsPerUnit });
      }
    });
    backWorld.add(pageRect, backgroundShape);
    backLayer.add(backWorld);
    // `world` is the page: 1,000 units wide, scaled and centred on the work surface.
    const world = new Konva.Group();
    const itemsGroup = new Konva.Group();
    const guides = new Konva.Group({ listening: false });
    // A hidden item is shown in the editor as a faint dashed outline, so it is not taken for lost;
    // never in Preview, and Save draws from the model, which leaves it out
    // (docs/ux/collage-review-4-fixes.md, PR 6).
    const hiddenOutlines = new Konva.Group({ listening: false });
    world.add(itemsGroup, hiddenOutlines, guides);
    layer.add(world);

    /** The photo behind the page when the background is a blurred photo: its working copy. */
    function backgroundPhoto() {
      if (state.collage.background?.type !== 'photo') return null;
      const photo = photos.get(state.collage.background.photo);
      return photo ? { source: photo.canvas, width: photo.canvas.width, height: photo.canvas.height } : null;
    }
    const nodes = new Map();

    const accent = () => getComputedStyle(document.body).getPropertyValue('--bs-primary').trim() || '#0d6efd';
    const ADJUST_COLOUR = '#f2a200';
    const LOCKED_COLOUR = '#8f9aab';
    const NORMAL_SNAPS = [0, 45, 90, 135, 180, 225, 270, 315];
    const SHIFT_SNAPS = Array.from({ length: 24 }, (_, i) => i * 15);
    // Corners keep the shape; the middle of each side changes the frame's shape.
    const CORNER_ANCHORS = ['top-left', 'top-right', 'bottom-left', 'bottom-right'];
    const ALL_ANCHORS = ['top-left', 'top-center', 'top-right', 'middle-right', 'bottom-right', 'bottom-center', 'bottom-left', 'middle-left'];
    const transformer = new Konva.Transformer({
      rotateEnabled: true,
      keepRatio: true,
      shiftBehavior: 'none',
      flipEnabled: false,
      enabledAnchors: ALL_ANCHORS,
      rotationSnaps: NORMAL_SNAPS,
      rotationSnapTolerance: core.ANGLE_SNAP.within,
      rotateAnchorOffset: coarsePointer ? 40 : 30,
      anchorSize: coarsePointer ? 14 : 12,
      anchorCornerRadius: coarsePointer ? 7 : 6,
      // On a touch screen a handle is drawn small but answers a finger across about 30 px.
      anchorStyleFunc: (anchor) => { if (coarsePointer) anchor.hitStrokeWidth(16); },
      anchorFill: '#ffffff',
      anchorStroke: accent(),
      borderStroke: accent(),
      borderStrokeWidth: 1.5,
      ignoreStroke: true,
      boundBoxFunc: (oldBox, newBox) => (Math.abs(newBox.width) < core.ITEM_LIMITS.minWidth * state.scale
        || Math.abs(newBox.height) < core.ITEM_LIMITS.minWidth * state.scale ? oldBox : newBox)
    });
    layer.add(transformer);

    /**
     * While a photo is moved in its frame, the whole photo shows faintly over the work surface, so
     * the user sees what can be brought into view. It is drawn above the page's clip, with the
     * photo node's own turn and place, and never in Preview or Save (Save draws the model alone).
     */
    const ghost = new Konva.Shape({
      name: 'adjust-ghost',
      listening: false,
      visible: false,
      sceneFunc: (context) => {
        const item = state.adjustId && itemById(state.adjustId);
        const node = item && nodes.get(item.id);
        const photo = item && photos.get(item.photo);
        // A hidden photo shows only its outline, never this faint copy.
        if (!node || !photo || photo.missing || item.hidden || state.previewing) return;
        const inner = core.innerBox(item);
        const region = core.cropRegion(photo.aspect, inner.width / inner.height, item.crop);
        const fullWidth = inner.width / region.width;
        const fullHeight = inner.height / region.height;
        const m = node.getAbsoluteTransform().getMatrix();
        const native = context._context;
        native.save();
        native.transform(m[0], m[1], m[2], m[3], m[4], m[5]);
        // A mirrored photo is mirrored here the same way drawPhoto mirrors it, about its picture area.
        if (item.flip) {
          native.translate(inner.x * 2 + inner.width, 0);
          native.scale(-1, 1);
        }
        native.globalAlpha = 0.35;
        native.drawImage(photo.canvas, inner.x - region.x * fullWidth, inner.y - region.y * fullHeight, fullWidth, fullHeight);
        native.restore();
      }
    });
    layer.add(ghost);
    transformer.moveToTop();

    function setShift(held) {
      if (held === state.shiftHeld) return;
      change.setShiftHeld(held);
      transformer.rotationSnaps(held ? SHIFT_SNAPS : NORMAL_SNAPS);
      transformer.rotationSnapTolerance(held ? core.ANGLE_SNAP.stepWithShift / 2 : core.ANGLE_SNAP.within);
    }

    /** The top of a sheet open over the page on a phone, in window pixels; null when none is. */
    function visibleSheetTop() {
      if (!document.body.contains(stageBox) || !stageBox.closest('.is-sheet-open')) return null;
      const sheet = [...root.querySelectorAll('.clg-drawer, .clg-props')].find((element) => getComputedStyle(element).position === 'fixed' && element.offsetHeight > 0);
      return sheet ? sheet.getBoundingClientRect().top : null;
    }

    function layout() {
      if (views.editor.hidden) return;
      const height = pageHeight();
      const narrow = stageBox.clientWidth < 520;
      // A slim margin at the sides, so 100% fills most of the work surface on a laptop
      // (docs/ux/collage-review-3-fixes.md, PR 8); above and below it keeps room for the
      // rotation handle of an item at the page's edge.
      const pad = narrow ? 16 : 48;
      const padX = narrow ? 8 : 24;
      // On a phone an open sheet covers the lower part: the page fits in what is left above it,
      // measured from the sheet itself (it is often shorter than the most it may take).
      const box = getComputedStyle(stageBox);
      const top = parseFloat(box.paddingTop) || 0;
      const sheetTop = visibleSheetTop();
      const usableHeight = sheetTop === null
        ? stageBox.clientHeight - top - (parseFloat(box.paddingBottom) || 0)
        : Math.max(120, sheetTop - stageBox.getBoundingClientRect().top - top);
      const fit = Math.min((stageBox.clientWidth - 2 * padX) / core.PAGE_WIDTH, (usableHeight - 2 * pad) / height);
      change.setScale(Math.max(0.05, fit * state.zoom));
      const pageW = core.PAGE_WIDTH * state.scale;
      const pageH = height * state.scale;
      // Rounded up to steps, so a pinch makes new canvases now and then, not at every move.
      const step = 128;
      // A page that fits the view keeps the view's own size (no rounding up past it, which would
      // push the page off centre on a phone).
      // A page fitted to the view can come out a hair larger than it (rounding), which must not
      // round up to the next step: the work surface would scroll, its scrollbars take room, and the
      // page would be fitted again, over and over (the page shook).
      const grown = (need, view) => (need <= view + 1 ? view : Math.max(view, Math.ceil(need / step) * step));
      const stageW = grown(pageW + 2 * padX, stageBox.clientWidth);
      const stageH = grown(pageH + 2 * pad, usableHeight);
      // Each layer stays under the browser's canvas limit at any zoom (core.canvasPixelRatio):
      // set before the stage grows, so a canvas too large is never made.
      const ratio = core.canvasPixelRatio(stageW, stageH, window.devicePixelRatio || 1);
      for (const each of [backLayer, layer]) {
        const canvas = each.getCanvas();
        if (Math.abs(canvas.getPixelRatio() - ratio) > 0.01) canvas.setPixelRatio(ratio);
      }
      stage.size({ width: stageW, height: stageH });
      stageHost.style.width = `${stageW}px`;
      stageHost.style.height = `${stageH}px`;
      world.position({ x: (stageW - pageW) / 2, y: (stageH - pageH) / 2 });
      world.scale({ x: state.scale, y: state.scale });
      pageRect.height(height);
      backgroundShape.height(height);
      backWorld.position(world.position());
      backWorld.scale(world.scale());
      backLayer.batchDraw();
      itemsGroup.clip({ x: 0, y: 0, width: core.PAGE_WIDTH, height });
      zoomValue.textContent = `${Math.round(state.zoom * 100)}%`;
      layer.batchDraw();
      parts.placeToolbar();
    }

    const resized = new ResizeObserver(() => layout());
    // The work surface's own size, not its content box: scrollbars coming and going change the
    // content box, and fitting the page again on that is what made the page shake.
    resized.observe(stageBox, { box: 'border-box' });
    // On a phone a sheet over the page can grow or shrink (a tray filling up): the page follows.
    root.querySelectorAll('.clg-drawer, .clg-props').forEach((sheet) => resized.observe(sheet));

    /** Shows the page alone, without panels or handles; Esc or Exit preview comes back. */
    function startPreview() {
      change.setPreviewing(true);
      stopAdjust(false);
      select(null);
      document.body.classList.add('clg-previewing');
      layer.listening(false);
      syncPage();
      root.querySelector('[data-preview-exit]').hidden = false;
      root.querySelector('[data-preview-exit]').focus();
      window.requestAnimationFrame(layout);
      window.setTimeout(layout, 50);
    }

    function stopPreview() {
      if (!state.previewing) return;
      change.setPreviewing(false);
      document.body.classList.remove('clg-previewing');
      layer.listening(true);
      syncPage();
      root.querySelector('[data-preview-exit]').hidden = true;
      window.requestAnimationFrame(layout);
      window.setTimeout(layout, 50);
    }

    function setZoom(value) {
      change.setZoomLevel(Math.min(4, Math.max(0.25, value)));
      layout();
    }

    /** The Konva node for an empty frame: a dashed box saying a photo can go here. */
    function makeSlotNode() {
      const group = new Konva.Group({ name: 'item', draggable: true });
      group.add(new Konva.Rect({ name: 'slot-box', fill: '#eef1f6', stroke: '#8f9aab', strokeWidth: 1.5, dash: [8, 6], strokeScaleEnabled: false }));
      group.add(new Konva.Text({ name: 'slot-label', text: '＋\nAdd photo', align: 'center', verticalAlign: 'middle', fill: '#5d6878', fontFamily: 'Mulish, "Segoe UI", sans-serif', fontStyle: '700', listening: false }));
      return group;
    }

    function applyItem(node, item) {
      // Words take the size they are measured at now, as Save draws them (centred on the item's
      // place), even when the model still holds a size from before their font arrived.
      const measured = item.kind === 'text' ? measureTextItem(item) : null;
      const width = measured ? measured.width : item.width;
      const height = measured ? measured.height : core.itemHeight(item);
      node.setAttrs({
        x: item.x,
        y: item.y,
        width,
        height,
        offsetX: width / 2,
        offsetY: height / 2,
        rotation: item.angle,
        opacity: item.opacity / 100,
        scaleX: 1,
        scaleY: 1
      });
      if (item.kind === 'slot') {
        node.findOne('.slot-box').setAttrs({ width: item.width, height, cornerRadius: item.radius || 0 });
        const size = Math.max(10, Math.min(item.width, height) * 0.12);
        node.findOne('.slot-label').setAttrs({ width: item.width, height, fontSize: size, lineHeight: 1.25 });
        return;
      }
      // A photo is drawn from the model by drawPhotoNode(); it only needs drawing again.
    }

    /**
     * Draws a photo node: its frame, shape, look and the part of the photo its frame shows, with
     * the same drawing Save uses, so the saved picture is what the page shows.
     */
    function drawPhotoNode(context, shape) {
      const item = itemById(shape.id());
      const photo = item && photos.get(item.photo);
      if (!photo) return;
      const pixelsPerUnit = state.scale * (layer.getCanvas().getPixelRatio?.() || 1);
      draw.drawPhoto(context._context, item, photo.canvas, photo.canvas.width, photo.canvas.height, pixelsPerUnit);
    }

    // Text is measured with the fonts PRISM ships (wwwroot/fonts/collage); a canvas measures it.
    const measureContext = document.createElement('canvas').getContext('2d');
    const measureText = (text, font) => {
      measureContext.font = font;
      if ('letterSpacing' in measureContext) measureContext.letterSpacing = '0px';
      return measureContext.measureText(text).width;
    };
    const textLayouts = new Map();
    const fontsAsked = new Set();

    /**
     * Lays a text item out and keeps its box (width and shape) in step with its words, so moving,
     * snapping and the handles fit the text. A font not loaded yet is asked for; the text is laid
     * out again once it arrives.
     */
    /**
     * How a text item's words lie, measured with its fonts, for drawing. It never changes the
     * model: the item's own size is set only inside a change (fitText). When a font arrives the
     * editor is told (fontsArrived), so the sizes it changes are recorded like any other change.
     */
    function measureTextItem(item) {
      const font = core.fontOf(item, 40);
      if (document.fonts && !fontsAsked.has(font)) {
        fontsAsked.add(font);
        document.fonts.load(font, item.text || 'Aअ').then(() => { textLayouts.clear(); fontsArrived(); }).catch(() => {});
      }
      const layout = core.layoutText(item, measureText);
      textLayouts.set(item.id, layout);
      return layout;
    }

    /** A text item given the size its words take: for a new item, or inside a change. */
    function fitText(item) {
      const layout = measureTextItem(item);
      item.width = layout.width;
      item.aspect = layout.width / Math.max(1, layout.height);
      return layout;
    }

    /** True when a text item's size is not the size its words now take (a font arrived). */
    function textOffSize(item) {
      const layout = measureTextItem(item);
      return Math.abs(item.width - layout.width) > 0.5 || Math.abs(item.width / item.aspect - layout.height) > 0.5;
    }

    /** Waits for every font the collage's text uses, so Save measures and draws the real fonts. */
    async function fontsReady() {
      if (!document.fonts) return;
      const fonts = new Set(state.collage.items.filter((item) => item.kind === 'text').map((item) => core.fontOf(item, 40)));
      // A polaroid's caption is written in Caveat (Kalam for Hindi).
      if (state.collage.items.some((item) => item.frame === 'polaroid' && item.caption)) fonts.add(`40px ${draw.CAPTION_FONT}`);
      // Other captions are on a strip, in Mulish (Noto Sans Devanagari for Hindi).
      if (state.collage.items.some((item) => core.hasCaptionStrip(item))) fonts.add(draw.STRIP_FONT.replace('{size}', '40'));
      await Promise.all([...fonts].map((font) => document.fonts.load(font, 'Aअ').catch(() => null)));
    }

    // The caption font is fetched once; captions drawn before it arrived are drawn again.
    if (document.fonts) document.fonts.load(`40px ${draw.CAPTION_FONT}`, 'Aअ').then(() => { if (!views.editor.hidden) syncPage(); }).catch(() => {});

    // Sticker pictures: emoji drawings from PRISM's own files, drawn stickers from their SVG in
    // the chosen colour. Each is loaded once; a sticker drawn before its picture arrived is drawn
    // again when it does.
    const stickerBase = root.dataset.stickerBase || '/img/collage/stickers/twemoji/';
    const stickerImages = new Map();

    function stickerSource(item) {
      const entry = stickers.find(item.sticker);
      if (!entry) return null;
      if (entry.kind === 'emoji') return `${stickerBase}${entry.code}.svg`;
      if (entry.kind === 'unit') return entry.url;
      return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(stickers.drawnSvg(entry.key, item.colour || entry.colour))}`;
    }

    /** The sticker's picture once it has loaded (a promise for Save), or null while loading. */
    function stickerImage(item, wait = false) {
      const src = stickerSource(item);
      if (!src) return wait ? Promise.resolve(null) : null;
      let entry = stickerImages.get(src);
      // A picture that failed (PRISM restarting, a dropped connection) is tried again: by Save at
      // once, by the page at most every ten seconds.
      if (entry?.failedAt && (wait || Date.now() - entry.failedAt > STICKER_RETRY_MS)) {
        stickerImages.delete(src);
        entry = null;
      }
      if (!entry) {
        const image = new Image();
        const made = { image, ready: false, failedAt: 0, done: null };
        made.done = new Promise((resolve) => {
          image.onload = () => { made.ready = true; resolve(image); if (!views.editor.hidden) layer.batchDraw(); };
          image.onerror = () => { made.failedAt = Date.now(); resolve(null); };
        });
        image.src = src;
        stickerImages.set(src, made);
        trimStickerImages();
        entry = made;
      }
      if (wait) return entry.done;
      return entry.ready ? entry.image : null;
    }

    const STICKER_RETRY_MS = 10000;
    // The picture each sticker showed last, kept on screen while a new colour loads.
    const lastStickerImage = new Map();

    /** Recoloured drawn stickers make a picture per colour tried; only the newest are kept. */
    function trimStickerImages() {
      if (stickerImages.size <= 80) return;
      // A picture a sticker on the page uses now is never let go, however many there are, or it
      // would be loaded again on every draw.
      const inUse = new Set(state.collage.items.filter((item) => item.kind === 'sticker').map((item) => stickerSource(item)));
      for (const key of stickerImages.keys()) {
        if (stickerImages.size <= 60) break;
        if (key.startsWith('data:') && !inUse.has(key)) stickerImages.delete(key);
      }
    }

    function drawStickerNode(context, shape) {
      const item = itemById(shape.id());
      if (!item) return;
      const ready = stickerImage(item);
      if (ready) lastStickerImage.set(item.id, ready);
      const image = ready || lastStickerImage.get(item.id);
      if (!image) return;
      const pixelsPerUnit = state.scale * (layer.getCanvas().getPixelRatio?.() || 1);
      draw.drawSticker(context._context, item, image, pixelsPerUnit);
    }

    function drawTextNode(context, shape) {
      const item = itemById(shape.id());
      if (!item) return;
      const layout = textLayouts.get(item.id) || measureTextItem(item);
      const pixelsPerUnit = state.scale * (layer.getCanvas().getPixelRatio?.() || 1);
      draw.drawText(context._context, item, layout, pixelsPerUnit);
    }

    /** The area that takes clicks: the whole frame. */
    function hitPhotoNode(context, shape) {
      context.beginPath();
      context.rect(0, 0, shape.width(), shape.height());
      context.closePath();
      context.fillStrokeShape(shape);
    }

    function makeNode(item) {
      const node = item.kind === 'slot'
        ? makeSlotNode()
        : new Konva.Shape({ name: 'item', draggable: true, sceneFunc: item.kind === 'text' ? drawTextNode : item.kind === 'sticker' ? drawStickerNode : drawPhotoNode, hitFunc: hitPhotoNode });
      node.id(item.id);
      node.setAttr('kind', item.kind);
      node.on('pointerdown', (event) => {
        if (state.adjustId === node.id()) { startPan(node, event); return; }
        if (state.adjustId) stopAdjust();
        select(node.id());
      });
      node.on('dblclick dbltap', () => {
        const target = itemById(node.id());
        if (target?.kind === 'photo') startAdjust(target.id);
        else if (target?.kind === 'slot') parts.chooseFor(target.id);
        else if (target?.kind === 'text') parts.focusTextField();
      });
      node.on('dragstart', () => { select(node.id()); parts.hideToolbar(); });
      node.on('dragmove', (event) => snapWhileMoving(node, event.evt));
      node.on('dragend', () => {
        guides.destroyChildren();
        const target = itemById(node.id());
        if (!target) return;
        target.x = node.x();
        target.y = node.y();
        commit();
      });
      node.on('transformstart', () => { change.setTransforming(true); parts.hideToolbar(); });
      node.on('transform', () => parts.showAngle(node));
      node.on('transformend', () => {
        change.setTransforming(false);
        angleTag.hidden = true;
        const target = itemById(node.id());
        if (!target) return;
        if (target.kind === 'text') {
          const [low, high] = core.TEXT_LIMITS.size;
          target.size = Math.round(Math.min(high, Math.max(low, target.size * Math.abs(node.scaleX()))));
          target.angle = core.normaliseAngle(node.rotation());
          target.x = node.x();
          target.y = node.y();
          fitText(target);
          commit();
          return;
        }
        if (target.kind === 'sticker') {
          const [low, high] = core.STICKER_LIMITS.width;
          target.width = Math.min(high, Math.max(low, target.width * Math.abs(node.scaleX())));
          target.angle = core.normaliseAngle(node.rotation());
          target.x = node.x();
          target.y = node.y();
          commit();
          return;
        }
        const width = core.clampWidth(target.width * Math.abs(node.scaleX()));
        const height = core.itemHeight(target) * Math.abs(node.scaleY());
        target.width = width;
        target.aspect = width / Math.max(1, height);
        // A framed photo squeezed flat keeps a usable picture area inside its edges.
        if (target.kind === 'photo') target.aspect = core.limitFrameAspect(target);
        if (target.crop) target.crop = clampedCrop(target);
        target.angle = core.normaliseAngle(node.rotation());
        target.x = node.x();
        target.y = node.y();
        commit();
      });
      return node;
    }

    /** The item's crop, kept inside its photo for the frame's current shape. */
    function clampedCrop(item) {
      const photo = photos.get(item.photo);
      return photo ? core.clampCrop(photo.aspect, core.innerAspect(item), item.crop) : item.crop;
    }

    function snapWhileMoving(node, event) {
      guides.destroyChildren();
      const item = itemById(node.id());
      if (!item || event?.altKey) { layer.batchDraw(); return; }
      // Words snap by the size they are drawn at (measured), like the node itself; the model may
      // still hold a size from before their font arrived.
      const asDrawn = (each) => {
        if (each.kind !== 'text') return each;
        const measured = measureTextItem(each);
        return { ...each, width: measured.width, aspect: measured.width / Math.max(1, measured.height) };
      };
      const drawn = asDrawn(item);
      const box = core.turnedHalfBox(drawn.width, core.itemHeight(drawn), drawn.angle);
      const others = state.collage.items.filter((i) => i.id !== item.id).map(asDrawn);
      const snapped = core.snapMove({ x: node.x(), y: node.y(), ...box }, state.collage.size, others, core.SNAP_PIXELS / state.scale);
      node.position({ x: snapped.x, y: snapped.y });
      for (const guide of snapped.guides) {
        const points = guide.axis === 'x' ? [guide.at, -40, guide.at, pageHeight() + 40] : [-40, guide.at, core.PAGE_WIDTH + 40, guide.at];
        guides.add(new Konva.Line({ points, stroke: '#ff3d8b', strokeWidth: 1, strokeScaleEnabled: false, dash: [6, 4] }));
      }
      layer.batchDraw();
    }

    /** Draws the model: adds, updates, orders and removes Konva nodes to match it. */
    function syncPage() {
      // Moving a photo inside its frame belongs to the selected photo only: whatever selected
      // another item (a click, a photo placed from Photos, a duplicate) ends it.
      if (state.adjustId && state.adjustId !== state.selectedId) stopAdjust(false);
      const seen = new Set();
      state.collage.items.forEach((item) => {
        let node = nodes.get(item.id);
        // A filled empty frame becomes a photo with the same id: its node is made again.
        if (node && node.getAttr('kind') !== item.kind) { node.destroy(); node = null; }
        if (!node) {
          node = makeNode(item);
          nodes.set(item.id, node);
          itemsGroup.add(node);
        }
        applyItem(node, item);
        node.draggable(state.adjustId !== item.id && !item.locked && !state.pinch);
        // Preview shows exactly what Save makes: empty frames are left out of both.
        node.visible(!item.hidden && !(state.previewing && item.kind === 'slot'));
        seen.add(item.id);
      });
      state.collage.items.forEach((item) => nodes.get(item.id).moveToTop());
      hiddenOutlines.destroyChildren();
      if (!state.previewing) {
        for (const item of state.collage.items) {
          if (!item.hidden) continue;
          const height = core.itemHeight(item);
          hiddenOutlines.add(new Konva.Rect({
            name: 'hidden-outline',
            x: item.x,
            y: item.y,
            width: item.width,
            height,
            offsetX: item.width / 2,
            offsetY: height / 2,
            rotation: item.angle,
            cornerRadius: item.radius || 0,
            stroke: '#8f9aab',
            strokeWidth: 1.5,
            dash: [6, 5],
            opacity: 0.75,
            strokeScaleEnabled: false
          }));
        }
      }
      for (const [id, node] of nodes) {
        if (!seen.has(id)) { node.destroy(); nodes.delete(id); }
      }
      if (state.selectedId && !seen.has(state.selectedId)) change.choose(null);
      if (state.adjustId && !seen.has(state.adjustId)) stopAdjust(false);
      if (state.targetId && !seen.has(state.targetId)) parts.setTarget(null);
      attachTransformer();
      emptyHint.hidden = state.collage.items.length > 0;
      backLayer.batchDraw();
      layer.batchDraw();
      parts.placeToolbar();
    }

    function attachTransformer() {
      // A hidden item keeps its settings and Layers row, but no handles on the page.
      const node = state.selectedId && !itemById(state.selectedId)?.hidden && nodes.get(state.selectedId);
      const adjusting = Boolean(state.adjustId && state.adjustId === state.selectedId);
      transformer.nodes(node ? [node] : []);
      const isText = Boolean(node && ['text', 'sticker'].includes(itemById(state.selectedId)?.kind));
      const locked = Boolean(node && itemById(state.selectedId)?.locked);
      transformer.enabledAnchors(adjusting || locked ? [] : isText ? CORNER_ANCHORS : ALL_ANCHORS);
      transformer.rotateEnabled(!adjusting && !locked);
      transformer.borderStroke(adjusting ? ADJUST_COLOUR : locked ? LOCKED_COLOUR : accent());
      transformer.borderDash(adjusting || locked ? [6, 4] : []);
      ghost.visible(adjusting && !state.previewing && !itemById(state.selectedId)?.hidden);
    }

    function syncAll() {
      sizeSelect.value = state.collage.size;
      layout();
      syncPage();
      parts.renderTray();
      parts.renderLayoutPanel();
      parts.renderProps();
      updateUndo();
    }


    return { stage, backLayer, layer, pageRect, world, guides, nodes, transformer, setShift, layout, visibleSheetTop, startPreview, stopPreview, setZoom, applyItem, measureText, textLayouts, layoutTextItem: fitText, fitText, textOffSize, fontsReady, stickerSource, stickerImage, lastStickerImage, clampedCrop, syncPage, attachTransformer, syncAll };
  }

  return Object.freeze({ createPage });
});
