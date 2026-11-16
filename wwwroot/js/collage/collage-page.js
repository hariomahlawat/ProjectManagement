// PRISM Collage Studio — the page: the Konva stage and layers, drawing each item (photos, frames, text, stickers), the handles, zoom, Preview and the page's size on screen
// Moved out of the editor in docs/ux/collage-review-2-fixes.md, PR 13; collage-app.js creates it
// once, handing over the helpers it uses and `app`, the editor's view of its own state.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollagePage = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createPage(deps) {
    const { app, root, core, Konva, draw, stickers, $, views, sizeSelect, stageBox, stageHost, angleTag, adjustHint, emptyHint, zoomValue, coarsePointer, photos, itemById, pageHeight, select, hideToolbar, placeToolbar, showAngle, startAdjust, stopAdjust, startPan, commit, updateUndo } = deps;
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
        const pixelsPerUnit = app.scale * (backLayer.getCanvas().getPixelRatio?.() || 1);
        draw.drawBackground(context._context, app.collage.background, core.PAGE_WIDTH, pageHeight(), { photo: backgroundPhoto(), pixelsPerUnit });
      }
    });
    backWorld.add(pageRect, backgroundShape);
    backLayer.add(backWorld);
    // `world` is the page: 1,000 units wide, scaled and centred on the work surface.
    const world = new Konva.Group();
    const itemsGroup = new Konva.Group();
    const guides = new Konva.Group({ listening: false });
    world.add(itemsGroup, guides);
    layer.add(world);

    /** The photo behind the page when the background is a blurred photo: its working copy. */
    function backgroundPhoto() {
      if (app.collage.background?.type !== 'photo') return null;
      const photo = photos.get(app.collage.background.photo);
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
      boundBoxFunc: (oldBox, newBox) => (Math.abs(newBox.width) < core.ITEM_LIMITS.minWidth * app.scale
        || Math.abs(newBox.height) < core.ITEM_LIMITS.minWidth * app.scale ? oldBox : newBox)
    });
    layer.add(transformer);

    function setShift(held) {
      if (held === app.shiftHeld) return;
      app.shiftHeld = held;
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
      app.scale = Math.max(0.05, fit * app.zoom);
      const pageW = core.PAGE_WIDTH * app.scale;
      const pageH = height * app.scale;
      // Rounded up to steps, so a pinch makes new canvases now and then, not at every move.
      const step = 128;
      // A page that fits the view keeps the view's own size (no rounding up past it, which would
      // push the page off centre on a phone).
      const grown = (need, view) => (need <= view ? view : Math.max(view, Math.ceil(need / step) * step));
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
      world.scale({ x: app.scale, y: app.scale });
      pageRect.height(height);
      backgroundShape.height(height);
      backWorld.position(world.position());
      backWorld.scale(world.scale());
      backLayer.batchDraw();
      itemsGroup.clip({ x: 0, y: 0, width: core.PAGE_WIDTH, height });
      zoomValue.textContent = `${Math.round(app.zoom * 100)}%`;
      layer.batchDraw();
      placeToolbar();
    }

    const resized = new ResizeObserver(() => layout());
    resized.observe(stageBox);
    // On a phone a sheet over the page can grow or shrink (a tray filling up): the page follows.
    root.querySelectorAll('.clg-drawer, .clg-props').forEach((sheet) => resized.observe(sheet));

    /** Shows the page alone, without panels or handles; Esc or Exit preview comes back. */
    function startPreview() {
      app.previewing = true;
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
      if (!app.previewing) return;
      app.previewing = false;
      document.body.classList.remove('clg-previewing');
      layer.listening(true);
      syncPage();
      root.querySelector('[data-preview-exit]').hidden = true;
      window.requestAnimationFrame(layout);
      window.setTimeout(layout, 50);
    }

    function setZoom(value) {
      app.zoom = Math.min(4, Math.max(0.25, value));
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
      if (item.kind === 'text') layoutTextItem(item);
      const height = core.itemHeight(item);
      node.setAttrs({
        x: item.x,
        y: item.y,
        width: item.width,
        height,
        offsetX: item.width / 2,
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
      const pixelsPerUnit = app.scale * (layer.getCanvas().getPixelRatio?.() || 1);
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
    function layoutTextItem(item) {
      const font = core.fontOf(item, 40);
      if (document.fonts && !fontsAsked.has(font)) {
        fontsAsked.add(font);
        document.fonts.load(font, item.text || 'Aअ').then(() => { syncPage(); }).catch(() => {});
      }
      const layout = core.layoutText(item, measureText);
      item.width = layout.width;
      item.aspect = layout.width / Math.max(1, layout.height);
      textLayouts.set(item.id, layout);
      return layout;
    }

    /** Waits for every font the collage's text uses, so Save measures and draws the real fonts. */
    async function fontsReady() {
      if (!document.fonts) return;
      const fonts = new Set(app.collage.items.filter((item) => item.kind === 'text').map((item) => core.fontOf(item, 40)));
      // A polaroid's caption is written in Caveat (Kalam for Hindi).
      if (app.collage.items.some((item) => item.frame === 'polaroid' && item.caption)) fonts.add(`40px ${draw.CAPTION_FONT}`);
      // Other captions are on a strip, in Mulish (Noto Sans Devanagari for Hindi).
      if (app.collage.items.some((item) => core.hasCaptionStrip(item))) fonts.add(draw.STRIP_FONT.replace('{size}', '40'));
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
      const inUse = new Set(app.collage.items.filter((item) => item.kind === 'sticker').map((item) => stickerSource(item)));
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
      const pixelsPerUnit = app.scale * (layer.getCanvas().getPixelRatio?.() || 1);
      draw.drawSticker(context._context, item, image, pixelsPerUnit);
    }

    function drawTextNode(context, shape) {
      const item = itemById(shape.id());
      if (!item) return;
      const layout = textLayouts.get(item.id) || layoutTextItem(item);
      const pixelsPerUnit = app.scale * (layer.getCanvas().getPixelRatio?.() || 1);
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
        if (app.adjustId === node.id()) { startPan(node, event); return; }
        if (app.adjustId) stopAdjust();
        select(node.id());
      });
      node.on('dblclick dbltap', () => {
        const target = itemById(node.id());
        if (target?.kind === 'photo') startAdjust(target.id);
        else if (target?.kind === 'slot') app.chooseFor(target.id);
        else if (target?.kind === 'text') app.focusTextField();
      });
      node.on('dragstart', () => { select(node.id()); hideToolbar(); });
      node.on('dragmove', (event) => snapWhileMoving(node, event.evt));
      node.on('dragend', () => {
        guides.destroyChildren();
        const target = itemById(node.id());
        if (!target) return;
        target.x = node.x();
        target.y = node.y();
        commit();
      });
      node.on('transformstart', () => { app.transforming = true; hideToolbar(); });
      node.on('transform', () => showAngle(node));
      node.on('transformend', () => {
        app.transforming = false;
        angleTag.hidden = true;
        const target = itemById(node.id());
        if (!target) return;
        if (target.kind === 'text') {
          const [low, high] = core.TEXT_LIMITS.size;
          target.size = Math.round(Math.min(high, Math.max(low, target.size * Math.abs(node.scaleX()))));
          target.angle = core.normaliseAngle(node.rotation());
          target.x = node.x();
          target.y = node.y();
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
      const box = core.turnedHalfBox(item.width, core.itemHeight(item), item.angle);
      const others = app.collage.items.filter((i) => i.id !== item.id);
      const snapped = core.snapMove({ x: node.x(), y: node.y(), ...box }, app.collage.size, others, core.SNAP_PIXELS / app.scale);
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
      if (app.adjustId && app.adjustId !== app.selectedId) {
        app.adjustId = null;
        app.pan = null;
        adjustHint.hidden = true;
      }
      const seen = new Set();
      app.collage.items.forEach((item) => {
        let node = nodes.get(item.id);
        // A filled empty frame becomes a photo with the same id: its node is made again.
        if (node && node.getAttr('kind') !== item.kind) { node.destroy(); node = null; }
        if (!node) {
          node = makeNode(item);
          nodes.set(item.id, node);
          itemsGroup.add(node);
        }
        applyItem(node, item);
        node.draggable(app.adjustId !== item.id && !item.locked && !app.pinch);
        // Preview shows exactly what Save makes: empty frames are left out of both.
        node.visible(!item.hidden && !(app.previewing && item.kind === 'slot'));
        seen.add(item.id);
      });
      app.collage.items.forEach((item) => nodes.get(item.id).moveToTop());
      for (const [id, node] of nodes) {
        if (!seen.has(id)) { node.destroy(); nodes.delete(id); }
      }
      if (app.selectedId && !seen.has(app.selectedId)) app.selectedId = null;
      if (app.adjustId && !seen.has(app.adjustId)) stopAdjust(false);
      if (app.targetId && !seen.has(app.targetId)) app.setTarget(null);
      attachTransformer();
      emptyHint.hidden = app.collage.items.length > 0;
      backLayer.batchDraw();
      layer.batchDraw();
      placeToolbar();
    }

    function attachTransformer() {
      // A hidden item keeps its settings and Layers row, but no handles on the page.
      const node = app.selectedId && !itemById(app.selectedId)?.hidden && nodes.get(app.selectedId);
      const adjusting = Boolean(app.adjustId && app.adjustId === app.selectedId);
      transformer.nodes(node ? [node] : []);
      const isText = Boolean(node && ['text', 'sticker'].includes(itemById(app.selectedId)?.kind));
      const locked = Boolean(node && itemById(app.selectedId)?.locked);
      transformer.enabledAnchors(adjusting || locked ? [] : isText ? CORNER_ANCHORS : ALL_ANCHORS);
      transformer.rotateEnabled(!adjusting && !locked);
      transformer.borderStroke(adjusting ? ADJUST_COLOUR : locked ? LOCKED_COLOUR : accent());
      transformer.borderDash(adjusting || locked ? [6, 4] : []);
    }

    function syncAll() {
      sizeSelect.value = app.collage.size;
      layout();
      syncPage();
      app.renderTray();
      app.renderLayoutPanel();
      app.renderProps();
      updateUndo();
    }


    return { stage, backLayer, layer, pageRect, world, guides, nodes, transformer, setShift, layout, visibleSheetTop, startPreview, stopPreview, setZoom, applyItem, measureText, textLayouts, layoutTextItem, fontsReady, stickerSource, stickerImage, lastStickerImage, clampedCrop, syncPage, attachTransformer, syncAll };
  }

  return Object.freeze({ createPage });
});
