// PRISM Collage Studio — the Text, Stickers and Background panels
// Moved out of the editor in docs/ux/collage-review-3-fixes.md, PR 11; collage-app.js creates it
// once, handing over the libraries it uses, `state` (the editor's values, read only), its own named
// changes, and its helpers in a few groups (ui, doc, history, messages, surface, selection,
// screens), each read when used (collage-state.js; docs/ux/collage-review-4-fixes.md, PR 11).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageAddPanels = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createAddPanels(deps) {
    const { state, parts, core, draw, stickers, library, ui, doc, messages, surface, history, selection } = deps;
    /** Ready-made headings and the plain ways to start text, from the Text panel. */
    // What the four plain starts say on their buttons (the words they add are the placeholders).
    const TEXT_START_LABELS = Object.freeze({ heading: 'A heading', subheading: 'A subheading', line: 'A line of text', hindi: 'हिंदी में लिखें' });

    const TEXT_STARTS = Object.freeze({
      heading: [core.TEXT_PLACEHOLDERS.heading, { font: 'elegant', size: 72 }],
      subheading: [core.TEXT_PLACEHOLDERS.subheading, { font: 'clean', size: 36 }],
      line: [core.TEXT_PLACEHOLDERS.line, { font: 'clean', size: 24, bold: false }],
      hindi: [core.TEXT_PLACEHOLDERS.hindi, { font: 'hindiHand', size: 48 }],
      farewell: ['Farewell', { font: 'script', size: 96, color: '#7a3b1e' }],
      birthday: ['Happy Birthday', { font: 'rounded', size: 80, color: '#c2185b', outline: 4, outlineColor: '#ffffff', curve: 35 }],
      thanks: ['Thank you!', { font: 'hand', size: 80, color: '#1f6f43' }],
      congrats: ['Congratulations', { font: 'elegant', size: 64, color: '#e8c36a', shadow: true }],
      picnic: ['PICNIC DAY', { font: 'poster', size: 96, color: '#ffffff', outline: 6, outlineColor: '#1f5f2c', shadow: true, bold: false }],
      wishes: ['Best wishes', { font: 'script', size: 80, color: '#ffffff', label: '#c62f3a' }],
      hShubh: ['शुभकामनाएँ', { font: 'hindiHand', size: 72, color: '#b0412e' }],
      hVidai: ['विदाई समारोह', { font: 'hindiFormal', size: 64, color: '#0b2a6f' }],
      hJanm: ['जन्मदिन मुबारक', { font: 'hindiHand', size: 72, color: '#c2185b' }],
      hSeva: ['सेवानिवृत्ति की शुभकामनाएँ', { font: 'hindiFormal', size: 48, color: '#e8c36a', shadow: true }],
      hDiwali: ['शुभ दीपावली', { font: 'hindiHand', size: 80, color: '#ffcc4d', glow: true }],
      hDhanya: ['धन्यवाद', { font: 'hindiHand', size: 72, color: '#1f6f43' }]
    });

    /** The Text panel: plain starts and ready-made headings, each shown in its own font. */
    function renderTextPanel() {
      messages.keepingFocus(() => renderTextPanelNow());
    }

    function renderTextPanelNow() {
      const button = (key, big) => {
        const [text, look] = TEXT_STARTS[key];
        const font = core.FONTS[look.font || 'clean'];
        const el = document.createElement('button');
        el.type = 'button';
        el.className = big ? 'clg-heading' : `clg-text-start clg-text-start--${key}`;
        el.dataset.addText = key;
        if (big) el.textContent = text;
        else {
          // A plus sign, and the words in their own font and size, as an "add" button.
          const plus = document.createElement('i');
          plus.className = 'bi bi-plus-lg';
          plus.setAttribute('aria-hidden', 'true');
          const words = document.createElement('span');
          words.textContent = TEXT_START_LABELS[key] || text;
          el.append(plus, words);
          el.setAttribute('aria-label', `Add ${(TEXT_START_LABELS[key] || text).toLowerCase()}`);
        }
        (big ? el : el.lastChild).style.fontFamily = font.css;
        el.style.fontWeight = look.bold === false ? '400' : '700';
        if (big) {
          el.style.color = look.color || '#172033';
          el.style.background = look.label || (look.color === '#ffffff' || look.color === '#ffcc4d' || look.color === '#e8c36a' ? '#1d2b45' : '#f6f2ea');
        }
        return el;
      };
      const starts = ui.root.querySelector('[data-text-starts]');
      starts.replaceChildren(...['heading', 'subheading', 'line', 'hindi'].map((key) => button(key, false)));
      ui.root.querySelector('[data-text-headings]').replaceChildren(...['farewell', 'birthday', 'thanks', 'congrats', 'picnic', 'wishes'].map((key) => button(key, true)));
      ui.root.querySelector('[data-text-headings-hi]').replaceChildren(...['hShubh', 'hVidai', 'hJanm', 'hSeva', 'hDiwali', 'hDhanya'].map((key) => button(key, true)));
    }

    /** A small picture of a background, for its button in the panel. */
    function backgroundPreview(background) {
      const canvas = document.createElement('canvas');
      canvas.width = 96;
      canvas.height = 68;
      const context = canvas.getContext('2d');
      context.scale(96 / 300, 68 / 212);
      let photo = null;
      if (background.type === 'photo') {
        const source = doc.photos.get(background.photo);
        if (source) photo = { source: source.canvas, width: source.canvas.width, height: source.canvas.height };
      }
      draw.drawBackground(context, background, 300, 212, { photo, pixelsPerUnit: 96 / 300 });
      return canvas.toDataURL('image/png');
    }

    const backgroundPreviews = new Map();

    /**
     * A closed collage's photo previews are let go, with the working copies they point at, so
     * opening one collage after another never keeps old photos in memory. Colour, gradient,
     * pattern and texture previews stay: they are small and the same for every collage.
     */
    function forgetPhotoPreviews() {
      for (const [key, preview] of backgroundPreviews) {
        if (preview.source) backgroundPreviews.delete(key);
      }
      backgroundPanelShown = '';
    }

    function backgroundTile(background, label) {
      const key = JSON.stringify(background);
      // A photo's preview is made again only if its working copy changed.
      const source = background.type === 'photo' ? doc.photos.get(background.photo)?.canvas : null;
      const cached = backgroundPreviews.get(key);
      if (!cached || cached.source !== source) backgroundPreviews.set(key, { url: backgroundPreview(background), source });
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'clg-bg-tile';
      button.dataset.background = key;
      button.setAttribute('aria-pressed', String(core.sameBackground(background, state.collage.background)));
      button.setAttribute('aria-label', label);
      button.title = label;
      const img = document.createElement('img');
      img.alt = '';
      img.src = backgroundPreviews.get(key).url;
      const name = document.createElement('span');
      name.textContent = label;
      button.append(img, name);
      return button;
    }

    /** The Background panel: colours, gradients, patterns, textures and the page's photos. */
    function renderBackgroundPanel() {
      messages.keepingFocus(() => renderBackgroundPanelNow());
    }

    let backgroundPanelShown = '';

    function renderBackgroundPanelNow() {
      const current = state.collage.background || { type: 'color', value: '#ffffff' };
      // Nothing it shows changed (the background and the page's photos): it is left as it is.
      const shownPhotos = [...new Set(state.collage.items.filter((item) => item.kind === 'photo').map((item) => item.photo))].slice(0, 9);
      const shown = JSON.stringify([current, shownPhotos.map((id) => [id, doc.photos.get(id)?.missing === true])]);
      if (shown === backgroundPanelShown) return;
      backgroundPanelShown = shown;
      const colours = ui.root.querySelector('[data-bg-colours]');
      colours.replaceChildren(...core.BACKGROUND_COLOURS.map((value) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'clg-swatch';
        button.dataset.background = JSON.stringify({ type: 'color', value });
        button.style.background = value;
        button.title = value;
        button.setAttribute('aria-label', `Colour ${value}`);
        button.setAttribute('aria-pressed', String(current.type === 'color' && current.value === value));
        return button;
      }));
      const pick = ui.root.querySelector('[data-bg-pick]');
      pick.value = current.type === 'color' ? current.value : '#ffffff';
      ui.root.querySelector('[data-bg-gradients]').replaceChildren(...Object.entries(core.GRADIENTS).map(([key, entry]) => backgroundTile({ type: 'gradient', key }, entry.label)));
      ui.root.querySelector('[data-bg-patterns]').replaceChildren(...Object.entries(core.PATTERNS).map(([key, entry]) => backgroundTile({ type: 'pattern', key }, entry.label)));
      ui.root.querySelector('[data-bg-textures]').replaceChildren(...Object.entries(core.TEXTURES).map(([key, entry]) => backgroundTile({ type: 'texture', key }, entry.label)));
      const onPage = [...new Set(state.collage.items.filter((item) => item.kind === 'photo').map((item) => item.photo))].slice(0, 9);
      // Each photo by its own name, so the user knows which one goes behind the page.
      ui.root.querySelector('[data-bg-photos]').replaceChildren(...onPage.map((id, n) => backgroundTile({ type: 'photo', photo: id }, `${doc.photos.get(id)?.name || `Photo ${n + 1}`}, blurred`)));
      ui.root.querySelector('[data-bg-photos-none]').hidden = onPage.length > 0;
    }

    function setBackground(background, record = true) {
      doc.edit((draft) => { draft.background = background; }, 'none');
      if (record) history.commit();
      else surface.backLayer.batchDraw();
      if (state.panel === 'background') renderBackgroundPanel();
    }

    let stickerGroup = 'celebration';
    let stickerGroupChosen = false;
    // 'loading', 'ready' or 'failed': whether the crests and logos have come from PRISM.
    let unitState = 'loading';

    const STICKER_NONE = {
      search: 'No sticker matches. Try another word.',
      loading: 'Getting the crests and logos from PRISM…',
      failed: 'The crests and logos could not be read from PRISM. The other stickers still work.',
      signedOut: `${library?.SIGNED_OUT_MESSAGE || 'Your PRISM session has ended.'} The other stickers still work.`,
      empty: "An administrator can add your unit's crests and logos."
    };

    /** Asks PRISM for the crests and logos once; the other stickers never wait for them. */
    function loadUnitStickers() {
      if (!library) { unitState = 'failed'; return; }
      library.loadStickers().then((list) => {
        const count = stickers.setUnit(list);
        unitState = 'ready';
        if (count > 0 && !stickerGroupChosen) stickerGroup = 'crests';
      }).catch((error) => {
        unitState = error?.reason === 'signed-out' ? 'signedOut' : 'failed';
      }).finally(() => {
        renderStickerPanel();
        // A collage opened before the list arrived draws its crests at their listed address now.
        if (!ui.views.editor.hidden) surface.syncPage();
      });
    }

    /** The Stickers panel: search, groups, and the stickers of the group or the search. */
    function renderStickerPanel() {
      messages.keepingFocus(() => renderStickerPanelNow());
    }

    function renderStickerPanelNow() {
      const box = ui.root.querySelector('[data-sticker-grid]');
      const chips = ui.root.querySelector('[data-sticker-groups]');
      const query = ui.root.querySelector('[data-sticker-search]').value.trim();
      const groups = stickers.groups();
      chips.replaceChildren(...groups.map((group) => {
        const chip = document.createElement('button');
        chip.type = 'button';
        chip.className = 'clg-chip';
        chip.dataset.stickerGroup = group.key;
        chip.textContent = group.label;
        chip.setAttribute('aria-pressed', String(!query && group.key === stickerGroup));
        return chip;
      }));
      const list = query ? stickers.search(query) : (groups.find((group) => group.key === stickerGroup) || groups[0]).items;
      box.replaceChildren(...list.map((item) => {
        const entry = stickers.find(item.id);
        const button = document.createElement('button');
        button.type = 'button';
        button.className = entry.aspect >= 1.6 ? 'clg-sticker clg-sticker--wide' : 'clg-sticker';
        button.dataset.addSticker = item.id;
        const name = entry.label || item.words.split(' ')[0];
        button.title = name;
        button.setAttribute('aria-label', `Add ${name}`);
        const img = document.createElement('img');
        img.alt = '';
        img.loading = 'lazy';
        img.src = surface.stickerSource({ sticker: item.id, colour: '' });
        button.appendChild(img);
        // A crest or logo carries its name, since its picture alone may not say which unit it is.
        if (entry.kind === 'unit') {
          button.classList.add('clg-sticker--named');
          const label = document.createElement('span');
          label.className = 'clg-sticker__name';
          label.textContent = name;
          button.appendChild(label);
        }
        return button;
      }));
      const none = ui.root.querySelector('[data-sticker-none]');
      none.hidden = list.length > 0;
      if (!list.length) {
        none.textContent = query ? STICKER_NONE.search
          : stickerGroup !== 'crests' ? STICKER_NONE.search
            : unitState === 'ready' ? STICKER_NONE.empty : STICKER_NONE[unitState];
      }
    }

    function addSticker(id) {
      const entry = stickers.find(id);
      if (!entry) return;
      const width = entry.kind === 'drawn' ? 180 : entry.kind === 'unit' ? 160 : 110;
      // Where it covers nothing yet, so stickers added one after another do not hide each other.
      const spot = core.freeSpot(state.collage.items, state.collage.size, width / 2, width / entry.aspect / 2);
      const place = { x: spot.x, y: spot.y, width, aspect: entry.aspect };
      const item = core.stickerItem(doc.newItemId(), id, place, entry.kind === 'drawn' ? { colour: entry.colour } : {});
      selection.placeNewItem(item);
      history.commit();
      messages.announce('Sticker added.');
    }

    function addText(key) {
      const start = TEXT_STARTS[key];
      if (!start) return;
      const item = core.textItem(doc.newItemId(), start[0], { x: core.PAGE_WIDTH / 2, y: doc.pageHeight() / 2 }, start[1]);
      // Measured first, then put where it covers nothing yet (not on top of the heading before it).
      surface.layoutTextItem(item);
      const place = () => {
        const spot = core.freeSpot(state.collage.items, state.collage.size, item.width / 2, core.itemHeight(item) / 2);
        item.x = spot.x;
        item.y = spot.y;
      };
      place();
      // Over a photo, dark words get a white outline so they can be read (the user can change it);
      // the outline makes them a little larger, so they are measured and placed again.
      if (!item.outline && core.overPhoto(state.collage.items, item)) {
        item.outline = 3;
        surface.layoutTextItem(item);
        place();
      }
      selection.placeNewItem(item);
      history.commit();
      messages.announce('Text added. Type the words in the Words box.');
      // A touch screen shows the Words box without opening its keyboard over the page.
      const touch = window.matchMedia?.('(pointer: coarse)').matches === true;
      window.requestAnimationFrame(() => parts.focusTextField({ focus: !touch }));
    }

    // The Text, Stickers and Background panels answer their own buttons.
    ui.root.addEventListener('click', (event) => {
      const backgroundChoice = event.target.closest('[data-background]');
      if (backgroundChoice) {
        setBackground(JSON.parse(backgroundChoice.dataset.background));
        messages.announce(`Background: ${backgroundChoice.getAttribute('aria-label')}. Ctrl+Z puts the last one back.`);
        return;
      }
      const stickerButton = event.target.closest('[data-add-sticker]');
      if (stickerButton) { addSticker(stickerButton.dataset.addSticker); return; }
      const groupChip = event.target.closest('[data-sticker-group]');
      if (groupChip) {
        stickerGroup = groupChip.dataset.stickerGroup;
        stickerGroupChosen = true;
        ui.root.querySelector('[data-sticker-search]').value = '';
        renderStickerPanel();
        return;
      }
      const textStart = event.target.closest('[data-add-text]');
      if (textStart) { addText(textStart.dataset.addText); return; }
    });


    return { renderTextPanel, forgetPhotoPreviews, renderBackgroundPanel, setBackground, loadUnitStickers, renderStickerPanel };
  }

  return Object.freeze({ createAddPanels });
});
