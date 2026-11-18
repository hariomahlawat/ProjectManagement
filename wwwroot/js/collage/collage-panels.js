// PRISM Collage Studio — the panels: the left drawer (layouts and the rest) and the settings panel with its Settings and Layers tabs
// Moved out of the editor in docs/ux/collage-review-2-fixes.md, PR 13; collage-app.js creates it
// once, handing over the libraries it uses, `state` (the editor's values, read only), its own named
// changes, and its helpers in a few groups (ui, doc, history, messages, surface, selection,
// screens), each read when used (collage-state.js; docs/ux/collage-review-4-fixes.md, PR 11).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollagePanels = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createPanels(deps) {
    const { state, change, core, draw, stickers, ui, history, doc, messages, surface, selection, screens } = deps;
    // ---------------------------------------------------------------------------
    // Panels on the left
    // ---------------------------------------------------------------------------

    const phoneScreen = window.matchMedia?.('(max-width: 820px)');
    const layersButton = ui.root.querySelector('[data-action="toggle-props"]');
    const onPhone = () => phoneScreen?.matches === true;

    /** On a phone, the settings sheet (for the selected item, or the page) open or closed. */
    function setPropsOpen(open, tab = null) {
      ui.body.classList.toggle('is-props-open', Boolean(open));
      if (tab) showPropsTab(tab, false);
      layersButton?.setAttribute('aria-pressed', String(Boolean(open) && state.propsTab === 'layers'));
      if (open && onPhone()) setPanel(null);
      syncSheets();
    }

    /** Marks whether a sheet covers the lower half, so the page fits above it. */
    function syncSheets() {
      const sheet = onPhone() && (ui.body.classList.contains('is-props-open') || Boolean(state.panel));
      ui.body.classList.toggle('is-sheet-open', sheet);
      window.requestAnimationFrame(surface.layout);
      window.setTimeout(surface.layout, 60);
    }

    phoneScreen?.addEventListener?.('change', syncSheets);

    function setPanel(name) {
      change.setPanel(name);
      if (name && onPhone()) {
        ui.body.classList.remove('is-props-open');
        layersButton?.setAttribute('aria-pressed', 'false');
      }
      for (const button of ui.root.querySelectorAll('[data-panel]')) {
        button.setAttribute('aria-pressed', String(button.dataset.panel === name));
      }
      for (const section of ui.root.querySelectorAll('[data-drawer-panel]')) {
        section.hidden = section.dataset.drawerPanel !== name;
      }
      ui.body.classList.toggle('is-drawer-closed', !name);
      if (name === 'layouts') renderLayoutPanel();
      if (name === 'background') screens.renderBackgroundPanel();
      syncSheets();
    }

    for (const button of ui.root.querySelectorAll('[data-panel]')) {
      button.addEventListener('click', () => setPanel(state.panel === button.dataset.panel ? null : button.dataset.panel));
    }

    function renderLayoutPanel() {
      messages.keepingFocus(() => renderLayoutPanelNow());
    }

    function renderLayoutPanelNow() {
      const current = state.collage.layout;
      for (const button of ui.layoutGrid.querySelectorAll('[data-layout]')) {
        button.setAttribute('aria-pressed', String(current?.key === button.dataset.layout));
      }
      const gap = current ? current.gap : core.LAYOUT_LIMITS.defaultGap;
      const radius = current ? current.radius : (doc.selectedItem()?.radius ?? 0);
      const gapInput = ui.$('[data-layout-input="gap"]');
      const radiusInput = ui.$('[data-layout-input="radius"]');
      gapInput.value = gap;
      gapInput.disabled = !current;
      radiusInput.value = radius;
      ui.$('[data-out="gap"]').textContent = current ? core.sliderText('gap', gap) : '—';
      ui.$('[data-out="radius"]').textContent = core.sliderText('radius', radius);
      ui.$('[data-layout-hint]').textContent = current
        ? 'Changing the space puts the photos back into the layout.'
        : 'Choose a layout to set the space between photos. Rounded corners apply to every photo.';
    }

    // Whether this move of the space slider has put hand-placed or tilted photos back in the layout.
    let spaceUndid = false;

    for (const input of ui.root.querySelectorAll('[data-layout-input]')) {
      input.addEventListener('input', () => {
        const value = Number(input.value);
        const which = input.dataset.layoutInput;
        ui.$(`[data-out="${which}"]`).textContent = core.sliderText(which, value);
        if (which === 'gap' && state.collage.layout) {
          if (core.offLayout(state.collage)) spaceUndid = true;
          // The space lays the page out again; that is what the hint under the sliders says.
          change.setCollage(core.reflowLayout(state.collage, value));
        } else if (which === 'radius') {
          // Corners change only the corners: turned photos and their crops stay as they are.
          // A locked frame keeps its own corners.
          doc.edit((draft) => {
            draft.items.forEach((item) => { if (doc.isFrame(item) && !item.locked) item.radius = value; });
            if (draft.layout) draft.layout = { ...draft.layout, radius: value };
          }, 'none');
        }
        surface.syncPage();
        // Held like every other slider, so a draft written mid-drag (the page hidden) keeps it.
        history.holdEdit();
      });
      input.addEventListener('change', () => {
        history.commit();
        if (spaceUndid) messages.offerUndo('The photos went back into the layout. Undo brings back where they were.');
        spaceUndid = false;
      });
    }

    // ---------------------------------------------------------------------------
    // Settings panel
    // ---------------------------------------------------------------------------

    const escapeHtml = (text) => String(text).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

    function renderProps() {
      messages.keepingFocus(() => renderPropsNow());
    }

    function renderPropsNow() {
      if (state.propsTab === 'layers') {
        ui.props.innerHTML = layersSection();
      } else {
        renderPropsBody();
        if (doc.selectedItem()?.locked) ui.props.querySelectorAll(ui.LOCKED_CONTROLS).forEach((control) => { control.disabled = true; });
      }
      // What the tab shows is one tab panel, named by its tab, so a screen reader knows where it is.
      const panel = document.createElement('div');
      panel.id = 'clgPropsPanel';
      panel.setAttribute('role', 'tabpanel');
      panel.setAttribute('aria-labelledby', `clgTab-${state.propsTab}`);
      panel.append(...ui.props.childNodes);
      ui.props.append(panel);
      const tab = (key, label, extra = '') => `<button type="button" role="tab" class="clg-props-tab" id="clgTab-${key}" aria-controls="clgPropsPanel" data-props-tab="${key}" aria-selected="${state.propsTab === key}" tabindex="${state.propsTab === key ? 0 : -1}">${label}${extra}</button>`;
      const count = state.collage.items.length ? ` <span class="clg-props-tab__count">${state.collage.items.length}</span>` : '';
      ui.props.insertAdjacentHTML('afterbegin', `<button type="button" class="clg-sheet-close" data-action="close-props" aria-label="Close settings"><i class="bi bi-x-lg" aria-hidden="true"></i></button>
        <div class="clg-props-tabs" role="tablist" aria-label="Settings or Layers">${tab('settings', 'Settings')}${tab('layers', 'Layers', count)}</div>`);
      layersButton?.setAttribute('aria-pressed', String(ui.body.classList.contains('is-props-open') && state.propsTab === 'layers'));
    }

    function showPropsTab(key, focus = true) {
      if (key !== 'settings' && key !== 'layers') return;
      change.setPropsTab(key);
      renderPropsNow();
      if (focus) ui.props.querySelector(`[data-props-tab="${key}"]`)?.focus();
    }

    // Groups used less often fold away; one left open stays open (in this browser).
    const FOLDS_KEY = 'prism-collage-open-groups';
    const openFolds = (() => {
      try {
        const saved = JSON.parse(window.localStorage?.getItem(FOLDS_KEY) || '[]');
        return new Set(Array.isArray(saved) ? saved.filter((key) => typeof key === 'string') : []);
      } catch {
        return new Set();
      }
    })();
    // 'toggle' does not bubble, so it is heard on the way down.
    ui.props.addEventListener('toggle', (event) => {
      const group = event.target.closest?.('[data-fold]');
      if (!group) return;
      if (group.open) openFolds.add(group.dataset.fold);
      else openFolds.delete(group.dataset.fold);
      try { window.localStorage?.setItem(FOLDS_KEY, JSON.stringify([...openFolds])); } catch { /* not kept; fine */ }
    }, true);

    ui.props.addEventListener('click', (event) => {
      const tab = event.target.closest('[data-props-tab]');
      if (tab) showPropsTab(tab.dataset.propsTab);
    });
    ui.props.addEventListener('keydown', (event) => {
      const tab = event.target.closest?.('[data-props-tab]');
      if (!tab || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      // The keys move between the tabs only; they never reach the page's nudge.
      event.stopPropagation();
      showPropsTab(tab.dataset.propsTab === 'settings' ? 'layers' : 'settings');
    });

    function renderPropsBody() {
      const item = doc.selectedItem();
      if (!item) {
        ui.props.innerHTML = `
          <h2 class="clg-panel-title">Page</h2>
          <label class="clg-field"><span class="clg-field__label">Size</span>
            <select class="form-select form-select-sm" data-page-size>${core.sizesByGroup().map((group) => `<optgroup label="${escapeHtml(group.label)}">${group.keys
              .map((key) => `<option value="${key}"${key === state.collage.size ? ' selected' : ''}>${escapeHtml(core.SIZES[key].label)} · ${escapeHtml(core.SIZES[key].note)}</option>`).join('')}</optgroup>`).join('')}</select></label>
          <div class="clg-section">
            <p class="clg-section__title">How to</p>
            <ul class="clg-tips clg-for-mouse">
              <li>Click a photo to change it; drag it to move it.</li>
              <li>Drag a corner to resize, or the middle of a side to change the frame's shape. Drag the round handle above it to turn it; it clicks to straight and 45°, and Shift gives 15° steps.</li>
              <li>Double-click a photo to move it inside its frame.</li>
              <li>Pink lines show when a photo lines up with the page or another photo. Hold Alt to move freely.</li>
              <li>Ctrl+Z undoes, Delete removes, Ctrl+D duplicates, and the arrow keys nudge.</li>
            </ul>
            <ul class="clg-tips clg-for-touch">
              <li>Tap a photo to change it; drag it with one finger to move it.</li>
              <li>Drag a corner to resize it, and the round handle above it to turn it.</li>
              <li>Tap a photo twice to move it inside its frame.</li>
              <li>Two fingers on the page zoom in and out.</li>
              <li>The arrows at the top undo and redo.</li>
            </ul>
          </div>`;
        return;
      }
      if (item.kind === 'text') {
        ui.props.innerHTML = `<h2 class="clg-panel-title">Text</h2>${textSections(item)}${arrangeSections(item)}`;
        return;
      }
      if (item.kind === 'sticker') {
        const entry = stickers.find(item.sticker);
        const [low, high] = core.STICKER_LIMITS.width;
        ui.props.innerHTML = `
          <h2 class="clg-panel-title">Sticker <small>${escapeHtml(entry?.label || entry?.emoji || '')}</small></h2>
          <div class="clg-section">
            <label class="clg-field"><span class="clg-field__label">Size<output data-out="width">${core.sliderText('width', item.width)}</output></span>
              <input type="range" min="${low}" max="${high}" step="1" value="${Math.round(item.width)}" data-prop="width" aria-label="Size of the sticker, in page units"></label>
            ${entry?.kind === 'drawn' ? `<p class="clg-field__label clg-field__label--plain">Colour</p>${swatches('colour', STICKER_COLOURS, item.colour || entry.colour)}` : ''}
            ${switchRow('flip', 'Mirror', item.flip)}
            ${switchRow('shadow', 'Shadow', item.shadow)}
          </div>
          ${arrangeSections(item)}`;
        return;
      }
      if (item.kind === 'shape') {
        // Lines and borders (docs/ux/invitations-posters.md, PR 3).
        const [low, high] = core.SHAPE_LIMITS.strokeWidth;
        const forms = [['rect', 'Rectangle'], ['ellipse', 'Oval'], ['line', 'Line']]
          .map(([value, label]) => `<button type="button" class="btn btn-sm ${item.form === value ? 'btn-primary' : 'btn-outline-secondary'}" data-text-set="form" data-value="${value}" aria-pressed="${item.form === value}">${label}</button>`).join('');
        ui.props.innerHTML = `
          <h2 class="clg-panel-title">${escapeHtml(core.layerName(item))}</h2>
          <div class="clg-section">
            <div class="clg-buttons" role="group" aria-label="Shape">${forms}</div>
            <p class="clg-field__label clg-field__label--plain">${item.form === 'line' ? 'Colour' : 'Line colour'}</p>${swatches('stroke', SHAPE_COLOURS, item.stroke, item.form !== 'line')}
            <label class="clg-field"><span class="clg-field__label">Line thickness<output data-out="strokeWidth">${core.sliderText('strokeWidth', item.strokeWidth)}</output></span>
              <input type="range" min="${item.form === 'line' ? 1 : low}" max="${high}" step="1" value="${item.strokeWidth}" data-prop="strokeWidth" aria-label="Line thickness, in page units"></label>
            ${item.form === 'line' ? '' : `<p class="clg-field__label clg-field__label--plain">Fill</p>${swatches('fill', SHAPE_COLOURS, item.fill, true)}`}
          </div>
          ${arrangeSections(item)}`;
        return;
      }
      if (item.kind === 'slot') {
        ui.props.innerHTML = `
          <h2 class="clg-panel-title">Empty frame</h2>
          <p class="clg-hint">A photo goes here. Choose one, drag one from Photos, or drop one from your computer onto this frame.</p>
          <div class="clg-stack">
            <button type="button" class="btn btn-primary btn-sm" data-action="fill-slot"><i class="bi bi-plus-lg" aria-hidden="true"></i> Add a photo here</button>
            <button type="button" class="btn btn-outline-secondary btn-sm" data-action="replace"><i class="bi bi-images" aria-hidden="true"></i> Use a photo already added</button>
          </div>
          ${arrangeSections(item)}`;
        return;
      }
      const photo = doc.photos.get(item.photo);
      const adjusting = state.adjustId === item.id;
      const zoomValueNow = item.crop?.zoom || 1;
      ui.props.innerHTML = `
        <h2 class="clg-panel-title">Photo <small title="${escapeHtml(photo?.name || '')}">${escapeHtml(photo?.name || '')}</small></h2>
        <div class="clg-section">
          <p class="clg-section__title">In its frame</p>
          <div class="clg-buttons clg-buttons--two">
            <button type="button" class="btn btn-sm ${adjusting ? 'btn-warning' : 'btn-outline-secondary'}" data-action="${adjusting ? 'adjust-done' : 'adjust'}" aria-pressed="${adjusting}"><i class="bi bi-arrows-move" aria-hidden="true"></i> ${adjusting ? 'Done moving' : 'Move photo'}</button>
            <button type="button" class="btn btn-outline-secondary btn-sm" data-action="replace"><i class="bi bi-arrow-left-right" aria-hidden="true"></i> Replace</button>
          </div>
          <label class="clg-field"><span class="clg-field__label">Zoom<output data-out="zoom">${zoomValueNow.toFixed(1)}×</output></span>
            <input type="range" min="1" max="${core.MAX_CROP_ZOOM}" step="0.05" value="${zoomValueNow}" data-prop="zoom" aria-label="Zoom inside the frame"></label>
          <button type="button" class="btn btn-outline-secondary btn-sm w-100" data-action="fit-photo">Fit the frame to the whole photo</button>
        </div>
        ${finishSections(item, photo)}
        ${arrangeSections(item)}`;
    }

    const TEXT_COLOURS = ['#ffffff', '#172033', '#000000', '#c62f3a', '#e8c36a', '#ffd23f', '#1f6f43', '#0b2a8a', '#c2185b', '#7a3b1e', '#ff7043', '#6a1b9a'];
    // Formal colours first (gold, maroon, navy, black, white), then the rest.
    const SHAPE_COLOURS = ['#a8842f', '#d4b25e', '#5c1a22', '#1d2c55', '#172033', '#000000', '#ffffff', '#fbf7ee', '#1f6f43', '#c62f3a', '#0b6fa4', '#7a7f87'];
    const STICKER_COLOURS = ['#c62f3a', '#e2b13c', '#ffd23f', '#1f6f43', '#0b2a8a', '#c2185b', '#172033', '#ffffff', '#9fd3c7', '#ff7043'];
    const LABEL_COLOURS = ['#ffffff', '#fffbea', '#172033', '#ffd23f', '#c62f3a', '#1f6f43'];

    // The settings panel's sections as HTML (collage-settings.js).
    const settingsHtml = window.PrismCollageSettings.createSettingsHtml({
      core, draw, stickers, stickerSource: surface.stickerSource, escapeHtml, photos: doc.photos, FRAME_WIDTHS: ui.FRAME_WIDTHS, TEXT_COLOURS, LABEL_COLOURS,
      isOpen: (key) => openFolds.has(key)
    });
    const { arrangeSections, swatches, textSections, switchRow, finishSections } = settingsHtml;
    const layersSection = () => settingsHtml.layersSection(state.collage, state.selectedId);

    /** Puts the cursor in the selected text's words, opening its settings. */
    /**
     * Shows the Words field, and puts the cursor in it unless `focus` is false (a touch screen
     * would open its keyboard over the page at once).
     */
    function focusTextField({ focus = true } = {}) {
      const field = ui.props.querySelector('[data-text]');
      if (!field) return;
      // On a phone the Words field is in the settings sheet, which opens first.
      if (onPhone()) setPropsOpen(true);
      if (!focus) { field.scrollIntoView({ block: 'nearest' }); return; }
      field.focus();
      field.select();
    }

    ui.props.addEventListener('click', (event) => {
      const set = event.target.closest('[data-text-set]');
      if (set) {
        selection.changeSelected((item) => { item[set.dataset.textSet] = set.dataset.value; });
        return;
      }
      const toggle = event.target.closest('[data-text-toggle]');
      if (toggle) selection.changeSelected((item) => { item[toggle.dataset.textToggle] = !item[toggle.dataset.textToggle]; });
    });

    ui.props.addEventListener('input', (event) => {
      if (event.target.matches('[data-text]')) {
        const text = event.target.value.slice(0, core.TEXT_LIMITS.maxLength);
        selection.changeSelected((item) => { item.text = text; }, false);
      } else if (event.target.matches('[data-text-colour]')) {
        const value = event.target.value;
        selection.changeSelected((item) => { item[event.target.dataset.textColour] = value; }, false);
      }
    });

    ui.props.addEventListener('change', (event) => {
      if (event.target.matches('[data-text], [data-text-colour]')) history.flushNudge();
    });

    /** Puts a frame on, takes it off or changes the shape, keeping the photo's own shape. */
    function setFinish(item, name, value) {
      const pictureAspect = core.innerAspect(item);
      // A white frame put on a photo that had none gets a soft shadow, so it shows on a white page;
      // the Shadow switch takes it off.
      if (name === 'frame' && value !== 'none' && (item.frame || 'none') === 'none') item.shadow = true;
      item[name] = value;
      if (name === 'frame' && value !== 'none') item.shape = 'rect';
      if (name === 'frame' || name === 'shape') {
        item.aspect = core.limitFrameAspect({ ...item, aspect: core.outerAspectFor(item, pictureAspect) });
        item.crop = surface.clampedCrop(item);
      }
    }

    ui.props.addEventListener('click', (event) => {
      const choice = event.target.closest('[data-finish]');
      if (!choice || choice.disabled) return;
      if (doc.selectedItem()?.locked && choice.matches(ui.LOCKED_CONTROLS)) return;
      selection.changeSelected((item) => setFinish(item, choice.dataset.finish, choice.dataset.value));
    });

    ui.props.addEventListener('change', (event) => {
      const toggle = event.target.closest('[data-toggle]');
      if (toggle) selection.changeSelected((item) => { item[toggle.dataset.toggle] = toggle.checked; });
    });

    ui.props.addEventListener('input', (event) => {
      if (event.target.matches('[data-caption]')) {
        const text = event.target.value.slice(0, core.CAPTION_MAX);
        selection.changeSelected((item) => { item.caption = text; }, false);
        return;
      }
      const prop = event.target.dataset.prop;
      if (!prop) return;
      if (doc.selectedItem()?.locked && event.target.matches(ui.LOCKED_CONTROLS)) return;
      const value = Number(event.target.value);
      selection.changeSelected((item) => {
        if (prop === 'zoom') selection.zoomCrop(item, value);
        else if (prop === 'angle') item.angle = core.normaliseAngle(value);
        else item[prop] = value;
      }, false);
      const out = ui.props.querySelector(`[data-out="${prop}"]`);
      if (out) {
        out.textContent = core.sliderText(prop, value);
      }
    });

    ui.props.addEventListener('change', (event) => {
      if (event.target.matches('[data-caption]')) {
        history.flushNudge();
        return;
      }
      if (event.target.dataset.prop) {
        // One frame's corners are its own: the layout's corners, and the next layout, stay as
        // they were. The change made while it moved is recorded even when it ends where it began.
        history.changes.mark();
        history.flushNudge();
        renderLayoutPanel();
        return;
      }
      if (event.target.matches('[data-page-size]')) {
        ui.sizeSelect.value = event.target.value;
        ui.sizeSelect.dispatchEvent(new Event('change'));
      }
    });


    return { setPropsOpen, setPanel, renderLayoutPanel, renderProps, focusTextField };
  }

  return Object.freeze({ createPanels });
});
