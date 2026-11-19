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
      if (name === 'card') screens.renderCardPanel();
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
      // Layouts with a frame for each photo the collage has are marked (docs/ux/collage-usability-plan.md, PR 3).
      const kept = [...doc.photos.entries()].filter(([, photo]) => !photo.removed).map(([id]) => id);
      const count = core.photosToLayOut(state.collage, kept);
      const fits = new Set(core.layoutsThatFit(count));
      for (const button of ui.layoutGrid.querySelectorAll('[data-layout]')) {
        button.setAttribute('aria-pressed', String(current?.key === button.dataset.layout));
        const fit = fits.has(button.dataset.layout);
        let mark = button.querySelector('[data-layout-fit]');
        if (fit && !mark) {
          mark = document.createElement('small');
          mark.className = 'clg-layout-fit';
          mark.dataset.layoutFit = '';
          button.appendChild(mark);
        }
        if (mark) {
          mark.hidden = !fit;
          // Short, to stay on one line in the narrow buttons; the button's name says it in full.
          mark.textContent = fit ? `Fits all ${count}` : '';
        }
        const entry = core.LAYOUTS[button.dataset.layout];
        if (entry) button.setAttribute('aria-label', `${entry.label}, ${entry.cells.length} photos${fit ? `; fits your ${count} photos` : ''}`);
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

    // Single quotes too, so text is safe inside any attribute.
    const escapeHtml = (text) => String(text).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

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

    /** A size typed in mm, from 50 to 300 a side (docs/ux/invitations-posters.md, PR 5b). */
    function customSizeFields() {
      const [low, high] = core.CUSTOM_LIMITS.mm;
      const size = core.sizeOf(state.collage.size);
      // The page's own size to start from, when it is one a custom size can be (an A3 poster is not).
      const [w, h] = size.mm && size.mm.every((side) => side >= low && side <= high) ? size.mm : ['', ''];
      const field = (name, value, label) => `<input type="number" class="form-control form-control-sm" min="${low}" max="${high}" step="0.1" inputmode="decimal" value="${value}" data-custom-size="${name}" aria-label="${label} in mm">`;
      // Folded away until wanted: open for a page that already has a custom size.
      return `<details class="clg-custom-size" data-custom-fold${size.group === 'custom' ? ' open' : ''}>
            <summary class="clg-field__label" id="clgCustomSizeLabel">Custom size, ${low} to ${high} mm a side</summary>
            <div class="clg-custom-size__row" role="group" aria-labelledby="clgCustomSizeLabel">${field('width', w, 'Width')}<span aria-hidden="true">×</span>${field('height', h, 'Height')}<span>mm</span>
              <button type="button" class="btn btn-outline-secondary btn-sm" data-action-custom-size>Use</button></div>
            <p class="clg-hint clg-custom-size__error" data-custom-size-error role="alert" hidden></p>
          </details>`;
    }

    /** Uses the size typed in mm, or says why it cannot be used. */
    function useCustomSize() {
      const value = (name) => ui.props.querySelector(`[data-custom-size="${name}"]`)?.value;
      const answer = core.customKey(value('width'), value('height'));
      const error = ui.props.querySelector('[data-custom-size-error]');
      if (answer.error) {
        error.textContent = answer.error;
        error.hidden = false;
        return;
      }
      error.hidden = true;
      ui.showSize(answer.key);
      ui.sizeSelect.dispatchEvent(new Event('change'));
    }

    ui.props.addEventListener('click', (event) => {
      if (event.target.closest('[data-action-custom-size]')) useCustomSize();
    });
    ui.props.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' && event.target.matches('[data-custom-size]')) { event.preventDefault(); useCustomSize(); }
    });

    /**
     * One line of help for what is open, shown while nothing is selected; every tip is in the Tips
     * dialog (docs/ux/collage-usability-plan.md, PR 6).
     */
    function helpLine() {
      if (state.collage.card) {
        return `<p class="clg-hint" data-help-line>Type the words in Card details, or <span class="clg-for-mouse">click</span><span class="clg-for-touch">tap</span> anything on the card to move or restyle it.</p>`;
      }
      return `<p class="clg-hint" data-help-line><span class="clg-for-mouse">Click a photo to change it; drag it to move it.</span><span class="clg-for-touch">Tap a photo to change it; drag it to move it.</span></p>`;
    }

    function renderPropsBody() {
      const item = doc.selectedItem();
      if (!item) {
        ui.props.innerHTML = `
          <h2 class="clg-panel-title">Page</h2>
          <label class="clg-field"><span class="clg-field__label">Size</span>
            <select class="form-select form-select-sm" data-page-size>${core.sizesByGroup().map((group) => `<optgroup label="${escapeHtml(group.label)}">${group.keys
              .map((key) => `<option value="${key}"${key === state.collage.size ? ' selected' : ''}>${escapeHtml(core.SIZES[key].label)} · ${escapeHtml(core.SIZES[key].note)}</option>`).join('')}</optgroup>`).join('')}${core.SIZES[state.collage.size] ? '' : `<optgroup label="Custom"><option value="${escapeHtml(state.collage.size)}" selected>${escapeHtml(core.sizeOf(state.collage.size).label)}</option></optgroup>`}</select></label>
          ${customSizeFields()}
          <div class="clg-section clg-help-line">
            ${helpLine()}
            <button type="button" class="btn btn-link btn-sm clg-help-open" data-action="help" aria-haspopup="dialog"><i class="bi bi-question-circle" aria-hidden="true"></i> Tips</button>
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
      isOpen: (key) => openFolds.has(key),
      cardField: (item) => (item?.field ? screens.cardFieldLabel(item.field) : null)
    });
    const { arrangeSections, swatches, textSections, switchRow, finishSections } = settingsHtml;
    /** A colour the user picks by hand stays: the part no longer follows a unit colour for it. */
    function untint(item, prop) {
      if (!item.tint || !(prop in item.tint)) return;
      const { [prop]: dropped, ...rest } = item.tint;
      if (Object.keys(rest).length) item.tint = rest;
      else delete item.tint;
    }
    const layersSection = () => settingsHtml.layersSection(state.collage, state.selectedId);

    /** Puts the cursor in the selected text's words, opening its settings. */
    /**
     * Shows the Words field, and puts the cursor in it unless `focus` is false (a touch screen
     * would open its keyboard over the page at once).
     */
    function focusTextField({ focus = true } = {}) {
      // A card's words are typed in Card details: double-click or Enter goes there (PR 5).
      const item = doc.selectedItem();
      if (focus && item?.field && screens.cardFieldLabel(item.field)) { screens.editCardField(item.field); return; }
      const field = ui.props.querySelector('[data-text]');
      if (!field) return;
      // On a phone the Words field is in the settings sheet, which opens first.
      if (onPhone()) setPropsOpen(true);
      if (!focus) { field.scrollIntoView({ block: 'nearest' }); return; }
      field.focus();
      field.select();
    }

    ui.props.addEventListener('click', (event) => {
      const edit = event.target.closest('[data-card-edit]');
      if (edit) { screens.editCardField(edit.dataset.cardEdit); return; }
      const set = event.target.closest('[data-text-set]');
      if (set) {
        selection.changeSelected((item) => { item[set.dataset.textSet] = set.dataset.value; untint(item, set.dataset.textSet); });
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
        selection.changeSelected((item) => { item[event.target.dataset.textColour] = value; untint(item, event.target.dataset.textColour); }, false);
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
        ui.showSize(event.target.value);
        ui.sizeSelect.dispatchEvent(new Event('change'));
      }
    });


    return { setPropsOpen, setPanel, renderLayoutPanel, renderProps, focusTextField };
  }

  return Object.freeze({ createPanels });
});
