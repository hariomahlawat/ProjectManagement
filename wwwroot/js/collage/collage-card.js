// PRISM Collage Studio — invitation cards: the start screen's card designs and the Card details
// panel (docs/ux/invitations-posters.md, PR 5). collage-app.js creates it once.
//
// A card is an ordinary collage made by collage-designs.js: every part stays editable. The Card
// details form is another way to type the card's words: a field and the text item that shows it
// are the same words, so typing in either changes both, and changing the design keeps them.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageCard = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  /** The Card details form: each field's label, and whether it takes more than one line. */
  const FORM = Object.freeze([
    ['occasion', 'Occasion'],
    ['host', 'Host', true],
    ['invite', 'Invitation line'],
    ['guest', 'Guest’s name (leave empty for a line to write it on)'],
    ['at', 'Before the occasion'],
    ['when', 'Date and time'],
    ['venue', 'Venue'],
    ['dress', 'Dress'],
    ['rsvp', 'R.S.V.P.'],
    ['note', 'Note at the bottom'],
    // The Bilingual design's Hindi wording (PR 6).
    ['occasionHi', 'Occasion in Hindi'],
    ['hostHi', 'Host in Hindi', true],
    ['inviteHi', 'Invitation line in Hindi'],
    // The back of the card (PR 8).
    ['programmeTitle', 'Back: title'],
    ['programme', 'Back: programme, one line each', true],
    ['backNote', 'Back: note at the bottom']
  ]);
  const COLOUR_NAMES = Object.freeze(['Top', 'Middle', 'Bottom']);
  const OCCASIONS = Object.freeze(['Raising Day Dinner', 'Regimental Dinner', 'Change of Command', 'Farewell', 'Barakhana', 'At Home', 'Ladies’ Night', 'Passing Out Parade']);
  const DRESSES = Object.freeze(['Dress: Lounge Suit', 'Dress: Mess Dress', 'Dress: Smart Casuals', 'Dress: Ceremonial']);
  const DEFAULT_CARD_SIZE = 'a5p';

  function createCard(deps) {
    const { state, core, draw, designs, stickers, ui, doc, history, messages, surface, selection, screens, parts } = deps;
    const escapeHtml = (value) => String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const form = ui.$('[data-card-form]');
    const strip = ui.$('[data-card-designs]');
    const railButton = ui.$('[data-panel="card"]');
    const startGrid = ui.$('[data-start-designs]');
    const startSize = ui.$('[data-card-size]');
    const options = () => ({ measure: surface.measureText, newId: doc.newItemId });
    // A field's words may be on the side not showing (the back's programme while the front shows).
    const fieldsHere = (collage, field) => (designs.BACK_FIELDS.includes(field) === (collage.side === 'back') ? collage.items : collage.back?.items || []);
    const textFor = (collage, field) => fieldsHere(collage, field).find((item) => item.kind === 'text' && item.field === field) || null;

    // -------------------------------------------------------------------------
    // The start screen: a size for the card, and its designs drawn at that size
    // -------------------------------------------------------------------------

    let thumbs = 0;
    function renderStart() {
      if (!startGrid || !startSize) return;
      if (!startSize.options.length) {
        for (const key of Object.keys(core.SIZES).filter((each) => core.SIZES[each].group === 'card')) {
          const option = document.createElement('option');
          option.value = key;
          option.textContent = core.SIZES[key].label;
          startSize.appendChild(option);
        }
        // A size typed in mm (PR 5b).
        const typed = document.createElement('option');
        typed.value = 'custom';
        typed.textContent = 'Custom size…';
        startSize.appendChild(typed);
        startSize.value = DEFAULT_CARD_SIZE;
      }
      const size = core.isSize(startSize.value) ? startSize.value : DEFAULT_CARD_SIZE;
      const round = ++thumbs;
      startGrid.textContent = '';
      for (const design of Object.values(designs.DESIGNS)) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'clg-design-card';
        button.dataset.startDesign = design.key;
        button.setAttribute('aria-label', `${design.label} card, ${core.sizeOf(size).label}`);
        const ratio = core.pageHeight(size) / core.PAGE_WIDTH;
        const width = ratio >= 1 ? Math.round(150 / ratio) : 150;
        const height = Math.round(width * ratio);
        button.innerHTML = `<span class="clg-design-card__pic"><canvas width="${width * 2}" height="${height * 2}" style="width:${width}px;height:${height}px" aria-hidden="true"></canvas></span><span class="clg-design-card__body"><b></b><small></small></span>`;
        button.querySelector('b').textContent = design.label;
        button.querySelector('small').textContent = design.note;
        startGrid.appendChild(button);
        drawThumb(button.querySelector('canvas'), size, design.key, round);
      }
    }

    // A small picture of a design, drawn as Save draws: the same drawing as the page.
    let renderer = null;
    async function drawThumb(canvas, size, key, round) {
      try {
        renderer = renderer || window.PrismCollageRender.createRenderer({ core, draw, measureText: surface.measureText, fontsReady: surface.fontsReady, stickerImage: surface.stickerImage });
        const collage = designs.applyDesign(core.newCollage(size), key, designs.DEFAULT_DETAILS, { measure: surface.measureText, newId: (() => { let n = 0; return () => `t${++n}`; })() });
        const drawn = await renderer.drawCollage(collage, new Map(), canvas.width, canvas.height);
        if (round !== thumbs) return;
        const context = canvas.getContext('2d');
        context.drawImage(drawn.canvas, 0, 0);
        // A design's photo frame shows where the user's photo goes; Save leaves empty frames out.
        const scale = canvas.width / core.PAGE_WIDTH;
        for (const slot of collage.items.filter((item) => item.kind === 'slot')) {
          const w = slot.width * scale;
          const h = core.itemHeight(slot) * scale;
          const x = slot.x * scale - w / 2;
          const y = slot.y * scale - h / 2;
          const shade = context.createLinearGradient(0, y, 0, y + h);
          shade.addColorStop(0, '#c9d6e3');
          shade.addColorStop(1, '#8796a8');
          context.fillStyle = shade;
          context.fillRect(x, y, w, h);
          context.fillStyle = '#ffffff';
          context.font = `600 ${Math.max(10, Math.round(w / 12))}px Mulish, sans-serif`;
          context.textAlign = 'center';
          context.textBaseline = 'middle';
          context.fillText('Your photo', x + w / 2, y + h / 2);
        }
      } catch {
        // A picture that cannot be drawn leaves the card's name; the design still opens.
      }
    }

    // Custom size… shows the mm fields; Use adds the size to the menu and draws the designs at it.
    const customBox = ui.$('[data-card-custom]');
    let lastSize = DEFAULT_CARD_SIZE;
    startSize?.addEventListener('change', () => {
      if (startSize.value === 'custom') {
        startSize.value = lastSize;
        if (customBox) {
          customBox.hidden = false;
          customBox.querySelector('[data-card-custom-size="width"]')?.focus();
        }
        return;
      }
      lastSize = startSize.value;
      renderStart();
    });
    function useCustomCardSize() {
      const value = (name) => customBox.querySelector(`[data-card-custom-size="${name}"]`).value;
      const answer = core.customKey(value('width'), value('height'));
      const error = customBox.querySelector('[data-card-custom-error]');
      if (answer.error) {
        error.textContent = answer.error;
        error.hidden = false;
        return;
      }
      error.hidden = true;
      if (![...startSize.options].some((option) => option.value === answer.key)) {
        const option = document.createElement('option');
        option.value = answer.key;
        option.textContent = core.sizeOf(answer.key).label;
        startSize.insertBefore(option, startSize.querySelector('option[value="custom"]'));
      }
      startSize.value = answer.key;
      lastSize = answer.key;
      customBox.hidden = true;
      renderStart();
      messages.announce(`Card size: ${core.sizeOf(answer.key).label}.`);
    }
    customBox?.addEventListener('click', (event) => {
      if (event.target.closest('[data-card-custom-use]')) useCustomCardSize();
    });
    customBox?.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' && event.target.matches('[data-card-custom-size]')) { event.preventDefault(); useCustomCardSize(); }
    });
    startGrid?.addEventListener('click', (event) => {
      const button = event.target.closest('[data-start-design]');
      if (button) screens.startCollage(core.isSize(startSize.value) ? startSize.value : DEFAULT_CARD_SIZE, null, button.dataset.startDesign);
    });

    // -------------------------------------------------------------------------
    // The Card details panel
    // -------------------------------------------------------------------------

    // A press in the Card panel can write a held edit first (the editor writes edits on any
    // pointerdown), which draws the panel again; a control replaced under the pointer would lose
    // its click. So while a press is under way in the panel, drawing it waits until the press is
    // done. Listened for on the window, so it is seen before the editor's own listener.
    const panelBox = ui.$('[data-drawer-panel="card"]');
    let pressing = false;
    let waiting = false;
    window.addEventListener('pointerdown', (event) => {
      if (panelBox?.contains(event.target)) pressing = true;
    }, true);
    const pressDone = () => {
      if (!pressing) return;
      pressing = false;
      // After the click has been answered.
      window.setTimeout(() => {
        if (!waiting) return;
        waiting = false;
        refresh();
      }, 0);
    };
    window.addEventListener('pointerup', pressDone, true);
    window.addEventListener('pointercancel', pressDone, true);

    /** Shows the Card button only for a card; draws the panel again unless the user is typing in it. */
    function refresh() {
      const card = state.collage?.card;
      if (railButton) railButton.hidden = !card;
      if (!card) {
        if (state.panel === 'card') screens.setPanel('photos');
        return;
      }
      if (pressing) { waiting = true; return; }
      // Drawn again unless a field in it is being typed in; a focused button keeps its focus.
      const typing = form.contains(document.activeElement) && document.activeElement.matches('input:not([type="color"]), textarea');
      if (state.panel === 'card' && !typing) messages.keepingFocus(() => renderPanel());
      else {
        if (state.panel === 'card') syncInputs();
        renderStrip();
      }
    }

    // The field typed in since it was focused: its words are the user's, never put back.
    let typedIn = null;
    form?.addEventListener('focusin', (event) => { if (event.target.dataset.cardField) typedIn = null; });

    /** Every field shows the card's words, in place, except one being typed in. */
    function syncInputs() {
      const details = designs.detailsOf(state.collage);
      for (const input of form.querySelectorAll('[data-card-field]')) {
        if (input === document.activeElement && typedIn === input) continue;
        const value = details[input.dataset.cardField] || '';
        if (input.value !== value) input.value = value;
      }
    }

    function renderPanel() {
      const details = designs.detailsOf(state.collage);
      // Only the fields this design shows words for (the Hindi ones only for Bilingual).
      const design = state.collage?.card?.design;
      const shown = new Set([...designs.fieldsOf(design, state.collage.size), ...(state.collage.back ? designs.BACK_FIELDS : [])]);
      const colours = designs.usesColours(design) ? colourFields(details.colours) : '';
      // The logo is the front's: changed with the front showing.
      const logo = state.collage.side === 'back' ? '<p class="clg-hint">Turn to the front to change the logo.</p>' : logoSection();
      form.innerHTML = sidesSection() + logo + colours + FORM.filter(([field]) => shown.has(field)).map(([field, label, long]) => {
        const id = `clgCard-${field}`;
        const value = escapeHtml(details[field] || '');
        const input = long
          ? `<textarea class="form-control form-control-sm" id="${id}" rows="${field === 'programme' ? 5 : 2}" maxlength="${core.TEXT_LIMITS.maxLength}" data-card-field="${field}">${value}</textarea>`
          : `<input class="form-control form-control-sm" id="${id}" type="text" maxlength="${core.TEXT_LIMITS.maxLength}" value="${value}" data-card-field="${field}" autocomplete="off">`;
        const picks = field === 'occasion' ? OCCASIONS : field === 'dress' ? DRESSES : null;
        const chips = picks ? `<div class="clg-chips clg-card-picks">${picks.map((pick) => `<button type="button" class="clg-chip" data-card-pick="${field}" data-value="${escapeHtml(pick)}">${escapeHtml(pick.replace(/^Dress: /, ''))}</button>`).join('')}</div>` : '';
        return `<label class="clg-field" for="${id}"><span class="clg-field__label">${escapeHtml(label)}</span>${input}</label>${chips}`;
      }).join('');
      renderStrip();
    }

    /**
     * The design buttons, made once and then only marked: a held edit written when a button is
     * pressed redraws the panel, and a button replaced under the pointer would lose its click.
     */
    // -------------------------------------------------------------------------
    // The logo (PR 7): PRISM's SDD logo, a crest an administrator uploaded, or the user's own
    // picture, kept in this browser like a photo. Changing the design keeps it.
    // -------------------------------------------------------------------------

    const logoOf = (collage) => collage.items.find((item) => item.role === 'logo') || null;
    // The logo the choices and Your own logo… change: 'logo', or 'logo2' when the second is chosen.
    let logoTarget = 'logo';
    const targetRole = () => (logoTarget === 'logo2' && state.collage.items.some((item) => item.role === 'logo2') ? 'logo2' : 'logo');

    /** What the card's logo is, in words. */
    function logoName(item) {
      if (!item) return 'No logo';
      if (item.kind === 'photo') return `Your own logo (${doc.photos.get(item.photo)?.name || 'picture'})`;
      return stickers.find(item.sticker)?.label || 'Logo';
    }

    function logoSection() {
      const first = logoOf(state.collage);
      const second = state.collage.items.some((item) => item.role === 'logo2');
      const role = targetRole();
      const current = role === 'logo2' ? state.collage.items.find((item) => item.role === 'logo2') : first;
      // With two logos, which one the choices below change.
      const which = second ? `<div class="btn-group btn-group-sm w-100 mb-2 clg-card-logo__which" role="group" aria-label="Which logo to change">
            <button type="button" class="btn btn-outline-secondary${role === 'logo' ? ' active' : ''}" data-card-logo-target="logo" aria-pressed="${role === 'logo'}">First logo</button>
            <button type="button" class="btn btn-outline-secondary${role === 'logo2' ? ' active' : ''}" data-card-logo-target="logo2" aria-pressed="${role === 'logo2'}">Second logo</button></div>` : '';
      const choices = (stickers.groups()[0]?.items || []).map((entry) => {
        const on = current?.sticker === entry.id;
        return `<button type="button" class="clg-sticker clg-sticker--named${on ? ' is-on' : ''}" data-card-logo-use="${escapeHtml(entry.id)}" aria-pressed="${on}" title="${escapeHtml(entry.label)}" aria-label="Use ${escapeHtml(entry.label)}"><img src="${escapeHtml(surface.stickerSource({ sticker: entry.id, colour: '' }) || '')}" alt=""><span class="clg-sticker__name">${escapeHtml(entry.label)}</span></button>`;
      }).join('');
      return `<div class="clg-section clg-card-logo">
          <p class="clg-section__title">Logo</p>${which}
          <p class="clg-hint" data-card-logo-name>${escapeHtml(logoName(current))}</p>
          <div class="clg-stickers clg-card-logo__choices">${choices}</div>
          <div class="clg-stack">
            <button type="button" class="btn btn-outline-secondary btn-sm" data-card-logo="own"><i class="bi bi-upload" aria-hidden="true"></i> Your own logo…</button>
            ${first && !second ? '<button type="button" class="btn btn-outline-secondary btn-sm" data-card-logo="second"><i class="bi bi-plus-lg" aria-hidden="true"></i> Add a second logo</button>' : ''}
            ${current ? `<button type="button" class="btn btn-outline-danger btn-sm" data-card-logo="remove"><i class="bi bi-x-lg" aria-hidden="true"></i> ${!second ? 'Remove logo' : role === 'logo2' ? 'Remove second logo' : 'Remove first logo'}</button>` : ''}
          </div>
          <p class="clg-hint">Your own logo stays in this browser, like your photos. A PNG keeps its clear background.</p>
        </div>`;
    }

    /**
     * Puts `make(place)` where the chosen logo is (the first, or the second when it is chosen), or
     * where the design puts one. One Undo step.
     */
    function setLogo(make, said, role = targetRole()) {
      const current = state.collage.items.find((item) => item.role === role) || null;
      // A locked logo is not replaced: unlocked first, as everywhere else.
      if (current?.locked) { messages.sayLocked(); return; }
      const place = current || designs.roleItem(state.collage, 'logo', options());
      if (!place) return;
      selection.select(null);
      doc.edit((draft) => {
        const fresh = { ...make({ x: place.x, y: place.y, width: place.width }), role };
        const index = draft.items.findIndex((item) => item.role === role);
        if (index >= 0) draft.items.splice(index, 1, fresh);
        else {
          const own = draft.items.findIndex((item) => !item.role);
          draft.items.splice(own < 0 ? draft.items.length : own, 0, fresh);
        }
      }, 'none');
      followLogoOnBack();
      history.commit();
      surface.syncAll();
      renderPanel();
      messages.announce(said);
    }

    /**
     * The back's small logo shows the front's logo (PR 8): the same picture in the back's place,
     * or none when the front has none. Done inside the change it follows, so one Undo covers both.
     */
    function followLogoOnBack() {
      doc.edit((draft) => {
        if (!draft.back) return;
        const front = draft.items.find((item) => item.role === 'logo' && item.kind !== 'slot');
        const at = draft.back.items.findIndex((item) => item.role === 'backlogo');
        if (!front) {
          if (at >= 0) draft.back.items.splice(at, 1);
          return;
        }
        // The back's place for it: where it is now, or where the design puts it.
        const place = at >= 0 ? draft.back.items[at] : designs.backPage(draft, draft.card?.design, designs.detailsOf(draft), options()).items.find((item) => item.role === 'backlogo');
        if (!place) return;
        const small = { ...front, id: at >= 0 ? place.id : doc.newItemId(), x: place.x, y: place.y, width: place.width, angle: 0, role: 'backlogo' };
        if (at >= 0) draft.back.items.splice(at, 1, small);
        else draft.back.items.push(small);
      }, 'none');
    }

    function useLogoSticker(id) {
      const entry = stickers.find(id);
      if (!entry) return;
      setLogo((place) => core.stickerItem(doc.newItemId(), id, { ...place, aspect: entry.aspect || 1 }), `Logo: ${entry.label}.`);
    }

    // The user's own logo: PNG, JPEG or WEBP, read like a photo and kept with the draft.
    const logoFile = document.createElement('input');
    logoFile.type = 'file';
    logoFile.accept = 'image/png,image/jpeg,image/webp,.png,.jpg,.jpeg,.webp';
    logoFile.hidden = true;
    logoFile.dataset.cardLogoFile = '';
    ui.root.appendChild(logoFile);
    logoFile.addEventListener('change', () => {
      const file = logoFile.files?.[0];
      logoFile.value = '';
      if (!file) return;
      // A card left (or another opened) while the picture is read gets nothing from it.
      const forCollage = state.collageSession;
      // The logo it was chosen for, kept while the picture is read.
      const role = targetRole();
      // In turn with photo imports and other logo reads, so the photo count and the pixel budget
      // are checked with every read before it finished.
      parts.afterImports(() => readLogo(file, forCollage, role));
    });

    async function readLogo(file, forCollage, role) {
      const stillOpen = () => forCollage === state.collageSession;
      if (!stillOpen()) return;
      let type = null;
      try { type = core.sniffImageType(new Uint8Array(await file.slice(0, 16).arrayBuffer())); } catch { type = null; }
      if (!stillOpen()) return;
      if (!['png', 'jpeg', 'webp'].includes(type) || file.size > core.PHOTO_LIMITS.maxBytes) {
        messages.showAlert('That file cannot be used as a logo. Choose a PNG, JPG or WEBP picture of at most 50 MB.');
        return;
      }
      // A logo is a photo of the collage: the same limit on how many, and the same pixel budget.
      const count = parts.photosInCollage();
      if (count >= core.PHOTO_LIMITS.maxPhotos) {
        messages.showAlert(`This collage has ${core.PHOTO_LIMITS.maxPhotos} photos, the most it can hold. Remove one from the photo tray first.`);
        return;
      }
      // The pixel budget counts every working copy kept, those Undo can bring back too.
      const copies = doc.photos.size + 1;
      parts.fitBudget(copies);
      let photo;
      try {
        photo = await parts.readPhoto({ file, name: file.name || 'Logo' }, null, { count: copies });
      } catch {
        if (stillOpen()) messages.showAlert('That picture could not be read. Try another file.');
        return;
      }
      if (!stillOpen()) { window.PrismCollagePhotos.releasePhoto(photo); return; }
      doc.photos.set(photo.id, photo);
      parts.renderTray();
      // The second logo it was chosen for may have been removed meanwhile: then it is the logo.
      const into = role === 'logo2' && state.collage.items.some((item) => item.role === 'logo2') ? 'logo2' : 'logo';
      setLogo((place) => core.photoItem(doc.newItemId(), photo.id, photo.aspect, place), into === 'logo2' ? 'Your own logo is the second logo.' : 'Your own logo is on the card.', into);
    }

    // The crests and logos an administrator uploaded arrive after the page opens: the Card
    // panel's logo choices are drawn again with them.
    ui.root.addEventListener('collage:stickers-loaded', () => refresh());

    /** A second logo beside the first: the two side by side when the first is in the middle. */
    function addSecondLogo() {
      const current = logoOf(state.collage);
      if (!current) return;
      const middle = Math.abs(current.x - core.PAGE_WIDTH / 2) < core.PAGE_WIDTH * 0.05;
      const step = current.width * 0.75;
      selection.select(null);
      doc.edit((draft) => {
        const first = draft.items.find((item) => item.role === 'logo');
        const second = { ...first, id: doc.newItemId(), role: 'logo2', locked: false };
        // A locked first logo stays where it is; the second goes beside it.
        if (first.locked) {
          second.x = Math.min(core.PAGE_WIDTH - first.width / 2, first.x + first.width * 1.3);
          if (second.x - first.x < first.width) second.x = Math.max(first.width / 2, first.x - first.width * 1.3);
        } else if (middle) {
          first.x = core.PAGE_WIDTH / 2 - step;
          second.x = core.PAGE_WIDTH / 2 + step;
        } else {
          second.x = core.PAGE_WIDTH - first.x;
        }
        draft.items.splice(draft.items.indexOf(first) + 1, 0, second);
      }, 'none');
      history.commit();
      surface.syncAll();
      renderPanel();
      logoTarget = 'logo2';
      renderPanel();
      messages.announce('A second logo is on the card. The choices below now change it; First logo goes back to the first.');
    }

    /**
     * Removes the logo picked above the choices. The second goes on its own; the first, with a
     * second on the card, leaves the second as the card's logo, so the card keeps a logo to
     * change or remove.
     */
    function removeLogo() {
      const role = targetRole();
      const target = state.collage.items.find((item) => item.role === role);
      if (!target) return;
      if (target.locked) { messages.sayLocked(); return; }
      const promoting = role === 'logo' && state.collage.items.some((item) => item.role === 'logo2');
      selection.select(null);
      doc.edit((draft) => {
        draft.items = draft.items.filter((item) => item.role !== role);
        if (promoting) draft.items = draft.items.map((item) => (item.role === 'logo2' ? { ...item, role: 'logo' } : item));
      }, 'none');
      logoTarget = 'logo';
      followLogoOnBack();
      history.commit();
      surface.syncAll();
      renderPanel();
      messages.offerUndo(role === 'logo2' ? 'Second logo removed.'
        : promoting ? "First logo removed. The second is now the card's logo."
        : 'Logo removed. It stays off when you change the design.');
    }

    // -------------------------------------------------------------------------
    // Front and back (PR 8): a card may have a back with the programme. Turning it over shows
    // the back on the page to edit; Save makes a two-page PDF.
    // -------------------------------------------------------------------------

    function sidesSection() {
      if (!state.collage.back) {
        return `<div class="clg-section clg-card-sides"><p class="clg-section__title">Back of the card</p>
          <button type="button" class="btn btn-outline-secondary btn-sm w-100" data-card-side="add"><i class="bi bi-plus-lg" aria-hidden="true"></i> Add a back: programme, route and a note</button></div>`;
      }
      const back = state.collage.side === 'back';
      return `<div class="clg-section clg-card-sides"><p class="clg-section__title">Sides</p>
          <div class="clg-buttons clg-buttons--two" role="group" aria-label="Side shown">
            <button type="button" class="btn btn-sm ${back ? 'btn-outline-secondary' : 'btn-primary'}" data-card-side="front" aria-pressed="${!back}">Front</button>
            <button type="button" class="btn btn-sm ${back ? 'btn-primary' : 'btn-outline-secondary'}" data-card-side="back" aria-pressed="${back}">Back</button>
          </div>
          <button type="button" class="btn btn-link btn-sm text-danger p-0 mt-1" data-card-side="delete">Delete the back</button></div>`;
    }

    /** One Undo step that changes the card's sides, then draws the page and the panel again. */
    function changeSides(change, said, undo = false) {
      history.flushNudge();
      selection.select(null);
      doc.edit((draft) => change(draft), 'none');
      history.commit();
      surface.layout();
      surface.syncAll();
      renderPanel();
      if (undo) messages.offerUndo(said);
      else messages.announce(said);
    }

    function useSide(action) {
      const collage = state.collage;
      if (action === 'add' && !collage.back) {
        changeSides((draft) => core.flipSide(designs.addBack(draft, options())), 'The card has a back, shown now. Front and Back in Card details turn it over; Save makes a two-page PDF.');
      } else if ((action === 'front' && collage.side === 'back') || (action === 'back' && collage.back && collage.side !== 'back')) {
        changeSides((draft) => core.flipSide(draft), action === 'back' ? 'The back of the card is shown.' : 'The front of the card is shown.');
      } else if (action === 'delete' && collage.back) {
        changeSides((draft) => designs.deleteBack(draft), 'The back is deleted.', true);
      }
    }

    /** The unit's three colours, for a design with a band in them (Regimental). */
    function colourFields(text) {
      const colours = designs.colourList(text);
      return `<div class="clg-section clg-card-colours"><p class="clg-section__title">Unit colours</p><div class="clg-card-colours__row">${colours.map((colour, i) => `<label class="clg-card-colour"><input type="color" value="${colour}" data-card-colour="${i + 1}" aria-label="${COLOUR_NAMES[i]} colour of the band"><span>${COLOUR_NAMES[i]}</span></label>`).join('')}</div></div>`;
    }

    /**
     * One of the unit's colours: every part that follows it (the band, the host's words, the
     * rules) takes it, and the card keeps it for the next design. Held like a slider.
     */
    function setColour(index, value) {
      if (!/^#[0-9a-f]{6}$/i.test(value)) return;
      const colours = designs.colourList(designs.detailsOf(state.collage).colours);
      colours[index - 1] = value.toLowerCase();
      doc.edit((draft) => {
        draft.card = { ...draft.card, design: draft.card?.design || 'regimental', details: { ...designs.detailsOf(draft), colours: colours.join(' ') } };
        // Both sides follow the unit's colours: the back's title and rules use them too.
        for (const item of [...draft.items, ...(draft.back?.items || [])]) {
          for (const [prop, which] of Object.entries(item.tint || {})) {
            if (which === index) item[prop] = value.toLowerCase();
          }
        }
      }, 'none');
      surface.syncPage();
      history.holdEdit();
    }

    function renderStrip() {
      if (!strip) return;
      const current = state.collage?.card?.design;
      if (!strip.children.length) {
        strip.innerHTML = Object.values(designs.DESIGNS).map((design) => `<button type="button" class="btn btn-sm" data-card-design="${design.key}">${escapeHtml(design.label)}</button>`).join('');
      }
      for (const button of strip.querySelectorAll('[data-card-design]')) {
        const on = button.dataset.cardDesign === current;
        button.classList.toggle('btn-primary', on);
        button.classList.toggle('btn-outline-secondary', !on);
        button.setAttribute('aria-pressed', String(on));
      }
    }

    /**
     * Sets one field's words: on its text item, which keeps the side it is set by; a field
     * emptied takes its item off the card, and one filled again comes back in its design's place.
     * Kept in the draft at once and recorded for Undo when typing pauses, like words typed on
     * the page.
     */
    function setField(field, raw) {
      const text = String(raw).slice(0, core.TEXT_LIMITS.maxLength);
      const collage = state.collage;
      // A locked item is not deleted by emptying its field: unlocked first, as everywhere else.
      const current = textFor(collage, field);
      if (current?.locked && !text.trim()) {
        messages.sayLocked();
        const input = form.querySelector(`[data-card-field="${field}"]`);
        if (input) input.value = current.text;
        return;
      }
      const details = { ...designs.detailsOf(collage), [field]: text };
      const fresh = !textFor(collage, field) && text.trim() ? designs.fieldItem(collage, field, details, options()) : null;
      let removed = null;
      let edited = null;
      doc.edit((draft) => {
        // A field filled again is one the design has placed.
        const placed = [...new Set([...(draft.card?.placed || []), ...(fresh ? [field] : [])])];
        draft.card = { design: draft.card?.design || 'classic', details, placed };
        const list = fieldsHere(draft, field);
        const index = list.findIndex((item) => item.kind === 'text' && item.field === field);
        if (index >= 0 && !text.trim()) {
          removed = list[index].id;
          list.splice(index, 1);
        } else if (index >= 0) {
          const target = list[index];
          edited = target.id;
          const left = target.x - target.width / 2;
          const right = target.x + target.width / 2;
          target.text = text;
          surface.layoutTextItem(target);
          if (target.align === 'left') target.x = left + target.width / 2;
          if (target.align === 'right') target.x = right - target.width / 2;
        } else if (fresh) {
          // Among the design's parts, behind anything the user added.
          const own = list.findIndex((item) => !item.role);
          list.splice(own < 0 ? list.length : own, 0, fresh);
        }
      }, 'none');
      if (removed && state.selectedId === removed) selection.select(null);
      // The selected item's Words box shows the words just typed here, unless it is being typed in.
      if (edited && state.selectedId === edited) {
        const words = ui.props.querySelector('[data-text]');
        if (words && document.activeElement !== words) words.value = text;
      }
      surface.syncPage();
      history.holdEdit();
    }

    /** Puts the card in another design, keeping every word; one Undo step brings the last one back. */
    function useDesign(key) {
      if (!designs.DESIGNS[key] || state.collage?.card?.design === key) return;
      history.flushNudge();
      selection.select(null);
      doc.edit((draft) => designs.applyDesign(draft, key, designs.detailsOf(draft), options()), 'none');
      history.commit();
      surface.syncAll();
      renderStrip();
      messages.offerUndo(`Design: ${designs.DESIGNS[key].label}. Your words are kept.`);
    }

    form?.addEventListener('input', (event) => {
      const field = event.target.dataset.cardField;
      if (field) typedIn = event.target;
      if (field) setField(field, event.target.value);
      if (event.target.dataset.cardColour) setColour(Number(event.target.dataset.cardColour), event.target.value);
    });
    form?.addEventListener('change', (event) => {
      if (event.target.dataset.cardField || event.target.dataset.cardColour) history.flushNudge();
    });
    form?.addEventListener('click', (event) => {
      const target = event.target.closest('[data-card-logo-target]');
      if (target) {
        logoTarget = target.dataset.cardLogoTarget === 'logo2' ? 'logo2' : 'logo';
        renderPanel();
        messages.announce(logoTarget === 'logo2' ? 'The choices change the second logo.' : 'The choices change the first logo.');
        return;
      }
      const side = event.target.closest('[data-card-side]');
      if (side) { useSide(side.dataset.cardSide); return; }
      const use = event.target.closest('[data-card-logo-use]');
      if (use) { useLogoSticker(use.dataset.cardLogoUse); return; }
      const logo = event.target.closest('[data-card-logo]');
      if (logo) {
        if (logo.dataset.cardLogo === 'own') logoFile.click();
        if (logo.dataset.cardLogo === 'second') addSecondLogo();
        if (logo.dataset.cardLogo === 'remove') removeLogo();
        return;
      }
      const pick = event.target.closest('[data-card-pick]');
      if (!pick) return;
      const input = form.querySelector(`[data-card-field="${pick.dataset.cardPick}"]`);
      if (input) input.value = pick.dataset.value;
      setField(pick.dataset.cardPick, pick.dataset.value);
      history.flushNudge();
      messages.announce(`${pick.dataset.cardPick === 'occasion' ? 'Occasion' : 'Dress'}: ${pick.dataset.value.replace(/^Dress: /, '')}.`);
    });
    strip?.addEventListener('click', (event) => {
      const button = event.target.closest('[data-card-design]');
      if (button) useDesign(button.dataset.cardDesign);
    });

    return { renderStart, renderPanel, refresh, setField, useDesign };
  }

  return Object.freeze({ createCard, FORM, OCCASIONS, DRESSES, DEFAULT_CARD_SIZE });
});
