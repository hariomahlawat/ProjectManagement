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
    // The Bilingual design's Hindi wording (PR 6) sits right under its English.
    ['host', 'Host', true],
    ['hostHi', 'Host in Hindi', true],
    ['invite', 'Invitation line'],
    ['inviteHi', 'Invitation line in Hindi'],
    ['guest', 'Guest’s name (leave empty for a line to write it on)'],
    ['at', 'Before the occasion'],
    ['occasion', 'Occasion'],
    ['occasionHi', 'Occasion in Hindi'],
    // A folded card's cover (cold review 9, M2): empty shows the occasion.
    ['coverTitle', 'Front cover title'],
    ['when', 'Date and time'],
    ['venue', 'Venue'],
    ['dress', 'Dress'],
    ['rsvp', 'R.S.V.P.'],
    ['note', 'Note at the bottom'],
    // The back of the card (PR 8).
    ['programmeTitle', 'Back: title'],
    ['programme', 'Back: programme, one line each', true],
    ['backNote', 'Back: note at the bottom'],
    // Pocket cards (PR 9b).
    ['passTitle', 'Title of the pass'],
    ['vehicle', 'Words before the vehicle number line'],
    ['replyTitle', 'Title of the reply card'],
    ['replyName', 'Words under the name line'],
    ['attend', 'Will or will not attend'],
    ['replyCount', 'Words under the number line'],
    ['replyBy', 'Reply by'],
    // Posters (PR 11).
    ['kicker', 'Small heading above'],
    ['title', 'Heading'],
    ['subtitle', 'Line under the heading'],
    ['lines', 'Details, one line each', true],
    ['contact', 'Contact line at the bottom']
  ]);
  // A poster's fields in the order it reads: the heading first, the contact line last.
  const POSTER_ORDER = Object.freeze(['kicker', 'title', 'subtitle', 'when', 'venue', 'lines', 'contact']);
  const POSTER_FORM = Object.freeze([
    ...POSTER_ORDER.map((field) => FORM.find(([key]) => key === field)).filter(Boolean),
    ...FORM.filter(([key]) => !POSTER_ORDER.includes(key))
  ]);
  const COLOUR_NAMES = Object.freeze(['Top', 'Middle', 'Bottom']);
  const OCCASIONS = Object.freeze(['Raising Day Dinner', 'Regimental Dinner', 'Change of Command Parade', 'Farewell Dinner', 'Barakhana', 'At Home', 'Ladies’ Night', 'Passing Out Parade']);
  const DRESSES = Object.freeze(['Dress: Lounge Suit', 'Dress: Mess Dress', 'Dress: Smart Casuals', 'Dress: Ceremonial']);
  // Each quick pick's occasion in Hindi, for the Bilingual design's Hindi column.
  const OCCASIONS_HI = Object.freeze({
    'Raising Day Dinner': 'स्थापना दिवस रात्रिभोज',
    'Regimental Dinner': 'रेजिमेंटल रात्रिभोज',
    'Change of Command Parade': 'कमान परिवर्तन परेड',
    'Farewell Dinner': 'विदाई रात्रिभोज',
    Barakhana: 'बड़ा खाना',
    'At Home': 'एट होम',
    'Ladies’ Night': 'लेडीज़ नाइट',
    'Passing Out Parade': 'पासिंग आउट परेड'
  });
  // The word before an occasion that does not take "the": "at an At Home" (cold review 5). Only
  // the words a design starts with are changed; words the user typed stay theirs.
  const AT_FOR = Object.freeze({ 'At Home': 'at an' });
  const AT_WORDS = Object.freeze(['at the', 'at an']);
  const DEFAULT_CARD_SIZE = 'a5p';

  function createCard(deps) {
    const { state, core, draw, designs, stickers, ui, doc, history, messages, surface, selection, screens, parts, print = window.PrismCollagePrint } = deps;
    const escapeHtml = (value) => String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const form = ui.$('[data-card-form]');
    const strip = ui.$('[data-card-designs]');
    const railButton = ui.$('[data-panel="card"]');
    const startGrid = ui.$('[data-start-designs]');
    const startSize = ui.$('[data-card-size]');
    const posterGrid = ui.$('[data-start-posters]');
    const posterSize = ui.$('[data-poster-size]');
    const pocketList = ui.$('[data-start-pockets]');
    // The pocket designs on the start screen (UX review 8, M6), each with the pocket size it opens
    // on: the car pass on the visiting card, the RSVP card on its own size, the others on A7.
    const POCKET_STARTS = Object.freeze([['pass', 'passl', 'bi-car-front'], ['reply', 'in355p', 'bi-envelope-check'], ['programmecard', 'a7p', 'bi-list-ol'], ['pocketinvite', 'a7p', 'bi-envelope-paper']]);
    const isPoster = () => Boolean(designs.DESIGNS[state.collage?.card?.design]?.poster);
    const options = () => ({ measure: surface.measureText, newId: doc.newItemId });
    // A field's words may be on the side not showing (the back's programme while the front shows).
    // A folded card's inside fields are on its back (PR 9c).
    const onBack = (collage, field) => designs.sideOf(collage, field) === 'back';
    const fieldsHere = (collage, field) => (onBack(collage, field) === (collage.side === 'back') ? collage.items : collage.back?.items || []);
    const textFor = (collage, field) => fieldsHere(collage, field).find((item) => item.kind === 'text' && item.field === field) || null;

    // -------------------------------------------------------------------------
    // The start screen: a size for the card, and its designs drawn at that size
    // -------------------------------------------------------------------------

    let thumbs = 0;
    function renderStart() {
      if (!startGrid || !startSize) return;
      if (!startSize.options.length) {
        // Cards and pocket cards, each in its own group; the designs offered follow the size.
        for (const group of core.sizesByGroup().filter((each) => ['card', 'pocket', 'folded'].includes(each.key))) {
          const list = document.createElement('optgroup');
          list.label = group.label;
          for (const key of group.keys) {
            const option = document.createElement('option');
            option.value = key;
            option.textContent = core.SIZES[key].label;
            list.appendChild(option);
          }
          startSize.appendChild(list);
        }
        // A size typed in mm (PR 5b).
        const typed = document.createElement('option');
        typed.value = 'custom';
        typed.textContent = 'Custom size…';
        startSize.appendChild(typed);
        startSize.value = DEFAULT_CARD_SIZE;
      }
      const size = core.isSize(startSize.value) ? startSize.value : DEFAULT_CARD_SIZE;
      // The pocket designs, offered beside the cards while a full-size card is chosen (with a
      // pocket size chosen they are the designs themselves).
      const pocketHint = ui.$('[data-card-pocket-hint]');
      if (pocketHint) pocketHint.hidden = core.sizeOf(size).group === 'pocket';
      if (pocketList && !pocketList.children.length) {
        for (const [key, sizeKey, icon] of POCKET_STARTS) {
          const design = designs.DESIGNS[key];
          if (!design) continue;
          const button = document.createElement('button');
          button.type = 'button';
          button.className = 'clg-pocket-card';
          button.dataset.startPocket = key;
          button.dataset.pocketSize = sizeKey;
          button.innerHTML = `<i class="bi ${icon}" aria-hidden="true"></i><span><b></b><small></small></span>`;
          button.querySelector('b').textContent = design.label;
          button.querySelector('small').textContent = core.sizeOf(sizeKey).note;
          button.setAttribute('aria-label', `${design.label}, ${core.sizeOf(sizeKey).label}`);
          pocketList.appendChild(button);
        }
      }
      const round = ++thumbs;
      if (posterGrid && posterSize) renderPosters(round);
      startGrid.textContent = '';
      for (const design of designs.designsFor(size)) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'clg-design-card';
        button.dataset.startDesign = design.key;
        const named = /\bcard\b/i.test(design.label) || /\bpass\b/i.test(design.label) ? design.label : `${design.label} card`;
        // The size is said too, unless it is the design's own name (the car pass on a car pass size).
        button.setAttribute('aria-label', core.sizeOf(size).label === design.label ? named : `${named}, ${core.sizeOf(size).label}`);
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

    /** The Posters row: each poster design drawn at the chosen poster size. */
    function renderPosters(round) {
      const size = posterSize.value || 'a3p';
      posterGrid.textContent = '';
      for (const design of designs.designsFor(size, 'poster')) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'clg-design-card';
        button.dataset.startPoster = design.key;
        button.setAttribute('aria-label', `${design.label} poster, ${core.sizeOf(size).label}`);
        const width = Math.round(150 / (core.pageHeight(size) / core.PAGE_WIDTH));
        const height = 150;
        button.innerHTML = `<span class="clg-design-card__pic"><canvas width="${width * 2}" height="${height * 2}" style="width:${width}px;height:${height}px" aria-hidden="true"></canvas></span><span class="clg-design-card__body"><b></b><small></small></span>`;
        button.querySelector('b').textContent = design.label;
        button.querySelector('small').textContent = design.note;
        posterGrid.appendChild(button);
        drawThumb(button.querySelector('canvas'), size, design.key, round);
      }
    }

    // A small picture of a design, drawn as Save draws: the same drawing as the page.
    let renderer = null;
    /** A design's picture on the start screen, with the wording it opens with. */
    async function drawThumb(canvas, size, key, round) {
      const details = designs.startDetails(key);
      try {
        renderer = renderer || window.PrismCollageRender.createRenderer({ core, draw, measureText: surface.measureText, fontsReady: surface.fontsReady, stickerImage: surface.stickerImage });
        const collage = designs.applyDesign(core.newCollage(size), key, details, { measure: surface.measureText, newId: (() => { let n = 0; return () => `t${++n}`; })() });
        // The design's own fonts first (a script heading, Bebas on a poster), so the picture is
        // drawn in them, not in the fallback.
        if (document.fonts) {
          const fonts = new Set(collage.items.filter((item) => item.kind === 'text').map((item) => core.fontOf(item, 40)));
          await Promise.all([...fonts].map((font) => document.fonts.load(font, 'Aअ').catch(() => null)));
          if (round !== thumbs) return;
        }
        const drawn = await renderer.drawCollage(collage, new Map(), canvas.width, canvas.height);
        if (round !== thumbs) { renderer.release(drawn.canvas); return; }
        const context = canvas.getContext('2d');
        context.drawImage(drawn.canvas, 0, 0);
        renderer.release(drawn.canvas);
        // A design's photo frame shows where the user's photo goes; Save leaves empty frames out.
        const scale = canvas.width / core.PAGE_WIDTH;
        for (const slot of collage.items.filter((item) => item.kind === 'slot')) {
          const w = slot.width * scale;
          const h = core.itemHeight(slot) * scale;
          const x = slot.x * scale - w / 2;
          const y = slot.y * scale - h / 2;
          if (slot.role === 'logo') {
            // A poster's logo place: a light dashed outline, so the heading shows through.
            context.setLineDash([3, 3]);
            context.strokeStyle = '#8796a8';
            context.lineWidth = 1;
            context.strokeRect(x, y, w, h);
            context.setLineDash([]);
            context.fillStyle = '#5b6b7d';
            context.font = `600 ${Math.max(9, Math.round(w / 5))}px Mulish, sans-serif`;
            context.textAlign = 'center';
            context.textBaseline = 'middle';
            context.fillText('Logo', x + w / 2, y + h / 2);
            continue;
          }
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
    /** A custom card smaller than A6 is warned of on the start screen: its smallest words get tiny. */
    function showSmallHint() {
      const hint = ui.$('[data-card-small]');
      if (hint) hint.hidden = !(core.isSize(startSize.value) && startSize.value.startsWith('mm:') && core.smallCard(startSize.value));
    }
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
      showSmallHint();
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
      showSmallHint();
      renderStart();
      messages.announce(`Card size: ${core.sizeOf(answer.key).label}.${core.smallCard(answer.key) ? ' This card is smaller than A6, so its smallest words will print very small.' : ''}`);
    }
    /** Cancel or Escape: the box closes and the menu keeps the size it had; focus goes back to it. */
    function closeCustomBox() {
      customBox.hidden = true;
      customBox.querySelector('[data-card-custom-error]').hidden = true;
      startSize.value = lastSize;
      startSize.focus();
    }
    customBox?.addEventListener('click', (event) => {
      if (event.target.closest('[data-card-custom-use]')) useCustomCardSize();
      if (event.target.closest('[data-card-custom-cancel]')) closeCustomBox();
    });
    customBox?.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' && event.target.matches('[data-card-custom-size]')) { event.preventDefault(); useCustomCardSize(); }
      if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); closeCustomBox(); }
    });
    posterSize?.addEventListener('change', () => renderStart());
    posterGrid?.addEventListener('click', (event) => {
      const button = event.target.closest('[data-start-poster]');
      if (button) screens.startCollage(posterSize.value || 'a3p', null, button.dataset.startPoster);
    });
    pocketList?.addEventListener('click', (event) => {
      const button = event.target.closest('[data-start-pocket]');
      if (button) screens.startCollage(button.dataset.pocketSize, null, button.dataset.startPocket);
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
    // The control in the panel that last had focus, and whether a redraw waits for focus to land.
    let lastFocused = null;
    let landing = false;
    let landed = false;

    function refresh() {
      const card = state.collage?.card;
      if (railButton) railButton.hidden = !card;
      if (!card) {
        if (state.panel === 'card') screens.setPanel('photos');
        return;
      }
      if (pressing) { waiting = true; return; }
      // Focus passing from one field to the next (Tab after typing) is on the page for a moment:
      // drawn now, the panel would replace the field Tab is going to. Drawn once it has landed.
      if (state.panel === 'card' && !landed && document.activeElement === document.body && form.contains(lastFocused)) {
        if (!landing) {
          landing = true;
          // Once, after focus has had its turn: still on the page (a click away), it is drawn then.
          setTimeout(() => { landing = false; landed = true; refresh(); landed = false; }, 0);
        }
        return;
      }
      const word = isPoster() ? 'Poster' : 'Card';
      const title = ui.$('#clgCardTitle');
      if (title) title.textContent = `${word} details`;
      const hint = ui.$('[data-card-hint]');
      if (hint) hint.textContent = `Type here, or click any words on the ${word.toLowerCase()}. Changing the design keeps every word.`;
      const label = railButton?.querySelector('span');
      if (label) label.textContent = word;
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
    // Ctrl+Z in a field is the Studio's Undo (words typed are recorded for it), as on the page;
    // Ctrl+Y or Ctrl+Shift+Z its Redo. The browser's own undo is lost once the panel is redrawn.
    form?.addEventListener('keydown', (event) => {
      if (!(event.ctrlKey || event.metaKey) || event.altKey || !event.target.matches('[data-card-field], [data-card-guests]')) return;
      const key = event.key.toLowerCase();
      const which = key === 'z' && !event.shiftKey ? 'undo' : (key === 'y' || (key === 'z' && event.shiftKey)) ? 'redo' : null;
      if (!which) return;
      event.preventDefault();
      event.stopPropagation();
      history.flushNudge();
      // The field shows the words Undo brings back, though it has focus.
      typedIn = null;
      const button = ui.root.querySelector(`[data-action="${which}"]`);
      undoing = true;
      try {
        if (button && !button.disabled) button.click();
      } finally {
        undoing = false;
      }
    });
    form?.addEventListener('focusin', (event) => { lastFocused = event.target; if (event.target.dataset.cardField) typedIn = null; });
    // While a field is typed in, a phone's sheet is shorter, so the card above stays readable.
    form?.addEventListener('focusin', (event) => {
      if (event.target.matches('input:not([type="color"]), textarea')) ui.root.classList.add('is-card-typing');
    });
    form?.addEventListener('focusout', () => {
      setTimeout(() => { if (!form.contains(document.activeElement)) ui.root.classList.remove('is-card-typing'); }, 0);
    });

    /** Every field shows the card's words, in place, except one being typed in. */
    function syncInputs() {
      const details = designs.detailsOf(state.collage);
      for (const input of form.querySelectorAll('[data-card-field]')) {
        if (input === document.activeElement && typedIn === input) continue;
        const value = details[input.dataset.cardField] || '';
        if (input.value !== value) input.value = value;
      }
      // The guest list box follows the list (after Undo too), but never while it is typed in.
      const guestsBox = form.querySelector('[data-card-guests]');
      if (guestsBox && (guestsBox !== document.activeElement || undoing)) {
        const value = (state.collage.card?.guests || []).join(String.fromCharCode(10));
        if (guestsBox.value !== value) guestsBox.value = value;
      }
    }
    // An Undo or Redo pressed in the panel: the box with focus shows what it brings back.
    let undoing = false;

    function renderPanel() {
      const details = designs.detailsOf(state.collage);
      // Only the fields this design shows words for (the Hindi ones only for Bilingual).
      const design = state.collage?.card?.design;
      const shown = new Set(designs.fieldsOnCard(state.collage));
      const colours = designs.usesColours(design) ? colourFields(details.colours) : '';
      // The logo is the front's: changed with the front showing.
      const logo = state.collage.side === 'back' ? '<p class="clg-hint">Turn to the front to change the logo.</p>' : logoSection();
      const poster = isPoster();
      const fields = (poster ? POSTER_FORM : FORM).filter(([field]) => shown.has(field)).map(([field, label, long]) => {
        const id = `clgCard-${field}`;
        const value = escapeHtml(details[field] || '');
        const input = long
          ? `<textarea class="form-control form-control-sm" id="${id}" rows="${field === 'programme' ? 5 : field === 'host' ? 3 : 2}" maxlength="${core.TEXT_LIMITS.maxLength}" data-card-field="${field}">${value}</textarea>`
          : `<input class="form-control form-control-sm" id="${id}" type="text" maxlength="${core.TEXT_LIMITS.maxLength}" value="${value}" data-card-field="${field}" autocomplete="off"${designs.FALLBACKS[field] ? ' placeholder="Same as the occasion"' : ''}>`;
        const picks = field === 'occasion' ? OCCASIONS : field === 'dress' ? DRESSES : null;
        const chips = picks ? `<div class="clg-chips clg-card-picks">${picks.map((pick) => `<button type="button" class="clg-chip" data-card-pick="${field}" data-value="${escapeHtml(pick)}" aria-pressed="${(details[field] || '') === pick}">${escapeHtml(pick.replace(/^Dress: /, ''))}</button>`).join('')}</div>` : '';
        const note = field === 'occasionHi' ? '<p class="clg-hint clg-card-hindi-note">An occasion quick pick fills this in too. Other English is not translated: an old Hindi line is emptied for you to type.</p>'
          : /Hi$/.test(field) ? '<p class="clg-hint clg-card-hindi-note">Not translated for you: when the English changes, this is emptied for you to type the Hindi.</p>' : '';
        return `<label class="clg-field" for="${id}"><span class="clg-field__label">${escapeHtml(label)}</span>${input}</label>${chips}${note}`;
      }).join('');
      // A poster asks which logo first, above everything, until it is answered (PR 11).
      form.innerHTML = posterQuestion() + photoPrompt() + `<div class="clg-section clg-card-words"><p class="clg-section__title">Words</p>${fields}</div>`
        + logo + (poster && !state.collage.back ? '' : sidesSection()) + colours + (poster || !shown.has('guest') ? '' : guestSection());
      renderStrip();
    }

    // -------------------------------------------------------------------------
    // The logo (PR 7): PRISM's SDD logo, a crest an administrator uploaded, or the user's own
    // picture, kept in this browser like a photo. Changing the design keeps it.
    // -------------------------------------------------------------------------

    const logoOf = (collage) => collage.items.find((item) => item.role === 'logo') || null;
    // The logo the choices and Your own logo… change: 'logo', or 'logo2' when the second is chosen.
    let logoTarget = 'logo';
    // Add a second logo with the SDD logo first (UX review 8, M4): the second is chosen from the
    // crests or the user's own, never a copy of the first. Holds the card it was asked on.
    let choosingSecond = null;
    const isSdd = (item) => item?.kind === 'sticker' && item.sticker === 'logo:sdd';
    const choosing = () => choosingSecond !== null && choosingSecond === state.collageSession
      && Boolean(logoOf(state.collage)) && logoOf(state.collage).kind !== 'slot' && !state.collage.items.some((item) => item.role === 'logo2');
    const targetRole = () => (logoTarget === 'logo2' && state.collage.items.some((item) => item.role === 'logo2') ? 'logo2' : 'logo');

    /** What the card's logo is, in words. */
    function logoName(item) {
      if (!item || item.kind === 'slot') {
        if (!isPoster()) return 'No logo';
        return designs.detailsOf(state.collage).logoMode === 'none' ? 'No logo on this poster.' : 'No logo chosen yet. Save closes up its place, as with No logo.';
      }
      if (item.kind === 'photo') return `Your own logo (${doc.photos.get(item.photo)?.name || 'picture'})`;
      return stickers.find(item.sticker)?.label || 'Logo';
    }

    /** A poster's question, at the top of its panel until a logo, No logo or Decide later is chosen. */
    function posterQuestion() {
      const first = logoOf(state.collage)?.kind === 'slot' ? null : logoOf(state.collage);
      if (!isPoster() || first || designs.detailsOf(state.collage).logoMode) return '';
      return `<div class="clg-section clg-card-question" role="group" aria-labelledby="clgPosterAsk"><p class="clg-section__title" id="clgPosterAsk">Which logo goes on this poster?</p>
          <p class="clg-hint">Use your own, pick a crest under Logo below, or choose:</p>
          <div class="clg-stack"><button type="button" class="btn btn-outline-primary btn-sm" data-card-logo="own"><i class="bi bi-upload" aria-hidden="true"></i> Your own logo…</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" data-card-logo="none">No logo</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" data-card-logo="later">Decide later</button></div></div>`;
    }

    /**
     * The design's photo frame (UX review 8): while it is empty, a short prompt, not a question
     * that must be answered: add a photo, or No photo, and the design closes the gap (an empty
     * frame saved leaves a hole). With No photo chosen, a line saying so and how to add one.
     */
    function photoPrompt() {
      if (state.collage.side === 'back' || !designs.hasPhotoFrame(state.collage)) return '';
      const word = isPoster() ? 'poster' : 'card';
      const frame = state.collage.items.find((item) => item.role === 'photo');
      if (frame && frame.kind !== 'slot') return '';
      if (frame) {
        return `<div class="clg-section clg-card-photo" role="group" aria-labelledby="clgPhotoAsk"><p class="clg-section__title" id="clgPhotoAsk">Photo</p>
          <p class="clg-hint">The photo frame is empty. Add a photo, or choose No photo and the design closes the gap.</p>
          <div class="clg-stack"><button type="button" class="btn btn-outline-primary btn-sm" data-card-photo="add"><i class="bi bi-image" aria-hidden="true"></i> Add a photo…</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" data-card-photo="none">No photo</button></div></div>`;
      }
      // The frame deleted on the page leaves a hole until the design closes it (code review 8).
      if (designs.detailsOf(state.collage).photoMode !== 'none') {
        return `<div class="clg-section clg-card-photo" role="group" aria-labelledby="clgPhotoAsk"><p class="clg-section__title" id="clgPhotoAsk">Photo</p>
          <p class="clg-hint">The photo frame was removed, which leaves a gap. Choose No photo and the design closes it, or add a photo.</p>
          <div class="clg-stack"><button type="button" class="btn btn-outline-primary btn-sm" data-card-photo="none">No photo</button>
          <button type="button" class="btn btn-outline-secondary btn-sm" data-card-photo="add"><i class="bi bi-image" aria-hidden="true"></i> Add a photo…</button></div></div>`;
      }
      return `<div class="clg-section clg-card-photo" role="group" aria-labelledby="clgPhotoAsk"><p class="clg-section__title" id="clgPhotoAsk">Photo</p>
          <p class="clg-hint" data-card-photo-name>No photo on this ${word}. The design uses the space.</p>
          <div class="clg-stack"><button type="button" class="btn btn-outline-secondary btn-sm" data-card-photo="add"><i class="bi bi-image" aria-hidden="true"></i> Add a photo…</button></div></div>`;
    }

    /**
     * No photo ('none'): the empty frame goes and the parts round it move into its space; or the
     * frame back (''), the parts back round it. Only the frame and the parts it moves change.
     */
    function setPhotoMode(mode) {
      if (mode === 'none' && state.collage.items.some((item) => item.role === 'photo' && item.locked)) {
        messages.sayLocked();
        return;
      }
      selection.select(null);
      doc.edit((draft) => designs.setPhotoMode(draft, mode, options()), 'none');
      history.commit();
      surface.syncAll();
      renderPanel();
      if (mode === 'none') messages.offerUndo(`No photo on this ${isPoster() ? 'poster' : 'card'}. The design closes the gap.`);
    }

    /** Add a photo…: the frame back if No photo was chosen, then the photo chosen for it. */
    function addPhoto() {
      if (!state.collage.items.some((item) => item.role === 'photo')) {
        // No photo chosen, or the frame deleted: the design's frame comes back first.
        if (designs.detailsOf(state.collage).photoMode === 'none') setPhotoMode('');
        else {
          // Only the frame comes back, where the design puts it, behind the rest: the user's
          // fonts, colours and places stay as they are (Codex on #3455).
          const frame = designs.roleItem(state.collage, 'photo', options());
          if (frame) {
            selection.select(null);
            doc.edit((draft) => { draft.items = [{ ...frame, id: doc.newItemId() }, ...draft.items]; }, 'none');
            history.commit();
            surface.syncAll();
            renderPanel();
          }
        }
      }
      const frame = state.collage.items.find((item) => item.role === 'photo' && item.kind === 'slot');
      if (frame) parts.chooseFor(frame.id);
    }

    function logoSection() {
      const first = logoOf(state.collage)?.kind === 'slot' ? null : logoOf(state.collage);
      const mode = designs.detailsOf(state.collage).logoMode;
      // A poster asks which logo, until one is chosen, none is, or it is left for later (PR 11).
      const asking = isPoster() && !first && !mode;
      const secondItem = state.collage.items.find((item) => item.role === 'logo2') || null;
      const second = Boolean(secondItem);
      const role = targetRole();
      const current = role === 'logo2' ? state.collage.items.find((item) => item.role === 'logo2') : first;
      const picking = choosing();
      // The other logo on the card: the one the choices may not copy (never two the same).
      const other = picking ? first : second ? (role === 'logo2' ? first : secondItem) : null;
      // With two logos, which one the choices below change.
      const which = second ? `<div class="btn-group btn-group-sm w-100 mb-2 clg-card-logo__which" role="group" aria-label="Which logo to change">
            <button type="button" class="btn btn-outline-secondary${role === 'logo' ? ' active' : ''}" data-card-logo-target="logo" aria-pressed="${role === 'logo'}">First logo</button>
            <button type="button" class="btn btn-outline-secondary${role === 'logo2' ? ' active' : ''}" data-card-logo-target="logo2" aria-pressed="${role === 'logo2'}">Second logo</button></div>` : '';
      const choices = (stickers.groups()[0]?.items || []).map((entry) => {
        const on = !picking && current?.sticker === entry.id;
        const taken = other?.kind === 'sticker' && other.sticker === entry.id;
        const name = taken ? `${entry.label} is already the ${role === 'logo2' || picking ? 'first' : 'second'} logo` : `Use ${entry.label}`;
        return `<button type="button" class="clg-sticker clg-sticker--named${on ? ' is-on' : ''}" data-card-logo-use="${escapeHtml(entry.id)}" aria-pressed="${on}"${taken ? ' disabled' : ''} title="${escapeHtml(taken ? name : entry.label)}" aria-label="${escapeHtml(name)}"><img src="${escapeHtml(surface.stickerSource({ sticker: entry.id, colour: '' }) || '')}" alt=""><span class="clg-sticker__name">${escapeHtml(entry.label)}</span></button>`;
      }).join('');
      const prompt = picking
        ? `<p class="clg-hint clg-card-logo__pick" data-card-logo-pick role="status">Choose the second logo: a crest below, or Your own logo…</p>`
        : `<p class="clg-hint" data-card-logo-name>${escapeHtml(logoName(current))}</p>`;
      return `<div class="clg-section clg-card-logo">
          <p class="clg-section__title">Logo</p>${which}
          ${prompt}
          <div class="clg-stickers clg-card-logo__choices">${choices}</div>
          <div class="clg-stack">
            ${asking ? '' : '<button type="button" class="btn btn-outline-secondary btn-sm" data-card-logo="own"><i class="bi bi-upload" aria-hidden="true"></i> Your own logo…</button>'}
            ${first && !second && !picking ? '<button type="button" class="btn btn-outline-secondary btn-sm" data-card-logo="second"><i class="bi bi-plus-lg" aria-hidden="true"></i> Add a second logo</button>' : ''}
            ${picking ? '<button type="button" class="btn btn-outline-secondary btn-sm" data-card-logo="cancel-second">Cancel the second logo</button>' : ''}
            ${current && !picking ? `<button type="button" class="btn btn-outline-danger btn-sm" data-card-logo="remove"><i class="bi bi-x-lg" aria-hidden="true"></i> ${!second ? 'Remove logo' : role === 'logo2' ? 'Remove second logo' : 'Remove first logo'}</button>` : ''}
            ${isPoster() && !first && mode !== 'none' && !asking ? '<button type="button" class="btn btn-outline-secondary btn-sm" data-card-logo="none">No logo</button>' : ''}
          </div>
          ${secondItem ? `<p class="clg-hint clg-card-logo__second" data-card-logo2-name>Second logo: ${escapeHtml(logoName(secondItem))}. Pick First logo or Second logo above to change or remove one; choose it on the page to move or resize it.</p>` : ''}
          <p class="clg-hint">Your own logo stays in this browser, like your photos. A PNG keeps its clear background.</p>
        </div>`;
    }

    /**
     * Puts `make(place)` where the chosen logo is (the first, or the second when it is chosen), or
     * where the design puts one. One Undo step.
     */
    function setLogo(make, said, role = targetRole()) {
      // A poster that had no logo gets its design back with the logo's place (PR 11).
      const wasNone = isPoster() && designs.detailsOf(state.collage).logoMode === 'none';
      const before = state.collage;
      const current = state.collage.items.find((item) => item.role === role) || null;
      // A locked logo is not replaced: unlocked first, as everywhere else.
      if (current?.locked) { messages.sayLocked(); return; }
      const place = current || designs.roleItem(state.collage, 'logo', options());
      if (!place) return;
      selection.select(null);
      doc.edit((draft) => {
        if (draft.card?.details?.logoMode === 'none') draft.card = { ...draft.card, details: { ...draft.card.details, logoMode: '' } };
        const fresh = { ...make({ x: place.x, y: place.y, width: place.width }), role };
        // A wide logo gets the place's area, not only its width (a crest's place is narrow).
        const shape = fresh.width / core.itemHeight(fresh);
        if (isPoster()) {
          // On a poster, sized from the design's place, never taller than it (the place is near the top edge).
          const designPlace = designs.roleItem(draft, 'logo', options()) || place;
          fresh.width = designs.posterLogoWidth(designPlace.width, designPlace.width / core.itemHeight(designPlace), shape);
        } else fresh.width = designs.logoWidth(place.width, place.width / core.itemHeight(place), shape);
        const index = draft.items.findIndex((item) => item.role === role);
        if (index >= 0) draft.items.splice(index, 1, fresh);
        else {
          const own = draft.items.findIndex((item) => !item.role);
          draft.items.splice(own < 0 ? draft.items.length : own, 0, fresh);
        }
      }, 'none');
      // The heading moves back down to make room; nothing else changes.
      if (wasNone) doc.edit((draft) => designs.moveForLogo(before, draft, options()), 'none');
      fitRoundel(before.items.some((item) => item.role === 'logo' && item.kind !== 'slot'));
      followLogoOnBack();
      history.commit();
      surface.syncAll();
      renderPanel();
      messages.announce(said);
    }

    /**
     * The design's roundel behind the logo (Regimental) follows the logos: hidden with none, as
     * wide as both when there are two, back to its design size with one. Inside the same change.
     */
    /** The roundel follows the logos; `hadLogo` says whether the card had a logo before the change. */
    function fitRoundel(hadLogo = true) {
      const base = designs.roleItem(state.collage, 'roundel', options());
      doc.edit((draft) => designs.fitRoundel(draft.items, base, { hadLogo }), 'none');
      // The light tile behind a crest on a dark design (UX review 8, M3) follows the logos too,
      // and so does Ceremonial's white box: round both logos, or back to its design size with one.
      doc.edit((draft) => { draft.items = designs.logoTileItems(draft, options()); }, 'none');
      const box = designs.roleItem(state.collage, 'logobox', options());
      doc.edit((draft) => { draft.items = designs.fitLogoBox(draft.items, box); }, 'none');
    }

    /**
     * The back's small logo shows the front's logo (PR 8): the same picture in the back's place,
     * or none when the front has none. Done inside the change it follows, so one Undo covers both.
     */
    function followLogoOnBack() {
      doc.edit((draft) => designs.followLogo(draft, options(), { add: true }), 'none');
    }

    function useLogoSticker(id) {
      const entry = stickers.find(id);
      if (!entry) return;
      const make = (place) => core.stickerItem(doc.newItemId(), id, { ...place, aspect: entry.aspect || 1 });
      if (choosing()) {
        if (logoOf(state.collage).sticker === id) { messages.announce(`${entry.label} is already the first logo. Choose another for the second.`); return; }
        placeSecondLogo(make, `Second logo: ${entry.label}. The choices below now change it; First logo goes back to the first.`);
        return;
      }
      // Never two logos the same: the other logo's picture is not offered for this one.
      const role = targetRole();
      const other = state.collage.items.find((item) => item.role === (role === 'logo2' ? 'logo' : 'logo2'));
      if (other?.kind === 'sticker' && other.sticker === id) { messages.announce(`${entry.label} is already on the card as the other logo.`); return; }
      setLogo(make, `Logo: ${entry.label}.`);
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
      // The logo it was chosen for, kept while the picture is read: the second, while it is being chosen.
      const role = choosing() ? 'second' : targetRole();
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
      // The logo is the front's: a card turned over while the picture was read keeps it in the
      // tray, to be chosen again from the front.
      if (state.collage.side === 'back') {
        messages.showAlert('The card was turned over while your logo was read. It is in the photo tray: turn to the front and choose Your own logo again.');
        return;
      }
      const make = (place) => core.photoItem(doc.newItemId(), photo.id, photo.aspect, place);
      // Chosen as the second logo: it goes beside the first, if the card still has one and no second.
      if (role === 'second' && logoOf(state.collage) && logoOf(state.collage).kind !== 'slot' && !state.collage.items.some((item) => item.role === 'logo2')) {
        placeSecondLogo(make, 'Your own logo is the second logo. The choices below now change it; First logo goes back to the first.');
        return;
      }
      // The second logo it was chosen for may have been removed meanwhile: then it is the logo.
      const into = role === 'logo2' && state.collage.items.some((item) => item.role === 'logo2') ? 'logo2' : 'logo';
      setLogo((place) => core.photoItem(doc.newItemId(), photo.id, photo.aspect, place), into === 'logo2' ? 'Your own logo is the second logo.' : 'Your own logo is on the card.', into);
    }

    // The crests and logos an administrator uploaded arrive after the page opens: the Card
    // panel's logo choices are drawn again with them.
    ui.root.addEventListener('collage:stickers-loaded', () => refresh());

    /**
     * Add a second logo (UX review 8, M4: it used to copy the first). Beside a crest or the user's
     * own logo, the SDD logo; with the SDD logo first, the choice opens for a crest or the user's
     * own, and the second goes on the card when one is picked.
     */
    function addSecondLogo() {
      const current = logoOf(state.collage);
      if (!current || current.kind === 'slot') return;
      if (!isSdd(current)) {
        placeSecondLogo((place) => core.stickerItem(doc.newItemId(), 'logo:sdd', { ...place, aspect: designs.SDD_ASPECT }), 'The SDD logo is the second logo. The choices below now change it; First logo goes back to the first.');
        return;
      }
      choosingSecond = state.collageSession;
      renderPanel();
      form.querySelector('.clg-card-logo__choices button:not([disabled]), [data-card-logo="own"]')?.focus();
      messages.announce('Choose the second logo: a crest from the choices, or Your own logo.');
    }

    /** The second logo, `make(place)`, as tall as the first and beside it: the two share the middle when the first is there. */
    function placeSecondLogo(make, said) {
      const current = logoOf(state.collage);
      if (!current) return;
      choosingSecond = null;
      selection.select(null);
      doc.edit((draft) => {
        const first = draft.items.find((item) => item.role === 'logo');
        const made = make({ x: first.x, y: first.y, width: first.width });
        // As tall as the first, in its own shape.
        const gap = first.width * 0.35;
        // A wide picture is made narrower (and so shorter) until the pair fits inside 80% of the
        // page's width, so neither logo is cut off at the edge (Codex on #3456).
        const room = core.PAGE_WIDTH * 0.8 - first.width - gap;
        const width = Math.min(core.itemHeight(first) * (made.width / core.itemHeight(made)), Math.max(first.width * 0.5, room));
        const second = { ...made, role: 'logo2', y: first.y, width, angle: 0, locked: false };
        // Beside the first, clear of the words and on the page (designs.secondLogoPlace); a
        // locked first logo is never moved.
        const place = designs.secondLogoPlace(draft.items, first, second, { width: core.PAGE_WIDTH, height: core.pageHeight(draft.size) });
        Object.assign(first, { x: place.firstX, y: place.firstY, width: place.firstWidth });
        Object.assign(second, { x: place.x, y: place.y, width: place.width });
        draft.items.splice(draft.items.indexOf(first) + 1, 0, second);
      }, 'none');
      fitRoundel();
      history.commit();
      surface.syncAll();
      logoTarget = 'logo2';
      renderPanel();
      messages.announce(said);
    }

    /** A poster's logo choice that is not a picture: none (the design closes the gap) or later. */
    function setLogoMode(mode) {
      selection.select(null);
      // Only the logo and the parts it moves change: the user's fonts, colours and places stay.
      doc.edit((draft) => designs.setLogoMode(draft, mode, options()), 'none');
      // The back's small logo follows, in the same Undo step: none with No logo.
      followLogoOnBack();
      history.commit();
      surface.syncAll();
      renderPanel();
      if (mode === 'none') messages.offerUndo('No logo on this poster. The heading moves up into the space.');
      else messages.announce('The logo can be chosen later. Until then Save closes up its place, as with No logo.');
    }

    /**
     * Removes the logo picked above the choices. The second goes on its own; the first, with a
     * second on the card, leaves the second as the card's logo, in the logo's place, so the card
     * keeps a logo to change or remove. A locked logo stays until it is unlocked.
     */
    function removeLogo() {
      const role = targetRole();
      const target = state.collage.items.find((item) => item.role === role);
      if (!target) return;
      if (target.locked) { messages.sayLocked(); return; }
      if (role === 'logo2') { removeSecondLogo(); return; }
      if (state.collage.items.some((item) => item.role === 'logo2')) { promoteSecond(); return; }
      // A poster's only logo removed: "No logo", so its design closes the gap (PR 11).
      if (isPoster()) { setLogoMode('none'); return; }
      selection.select(null);
      doc.edit((draft) => {
        draft.items = draft.items.filter((item) => item.role !== 'logo');
      }, 'none');
      logoTarget = 'logo';
      fitRoundel();
      followLogoOnBack();
      history.commit();
      surface.syncAll();
      renderPanel();
      messages.offerUndo('Logo removed. It stays off when you change the design.');
    }

    // Whether the design's logo sits in the middle of the page (two logos then share the middle).
    const inMiddle = (item) => Boolean(item) && Math.abs(item.x - core.PAGE_WIDTH / 2) < core.PAGE_WIDTH * 0.05;

    /** The first of two logos removed: the second becomes the card's logo, in the logo's place. */
    function promoteSecond() {
      const place = designs.roleItem(state.collage, 'logo', options());
      selection.select(null);
      doc.edit((draft) => {
        const first = draft.items.find((item) => item.role === 'logo');
        const second = draft.items.find((item) => item.role === 'logo2');
        const x = inMiddle(place) ? place.x : first.x;
        const promoted = second.locked ? { ...second, role: 'logo' } : { ...second, role: 'logo', x, y: first.y };
        draft.items = draft.items.filter((item) => item !== second);
        draft.items.splice(draft.items.findIndex((item) => item.role === 'logo'), 1, promoted);
      }, 'none');
      logoTarget = 'logo';
      fitRoundel();
      followLogoOnBack();
      history.commit();
      surface.syncAll();
      renderPanel();
      messages.offerUndo("First logo removed. The second is now the card's logo.");
    }

    function removeSecondLogo() {
      const second = state.collage.items.find((item) => item.role === 'logo2');
      if (!second) return;
      if (second.locked) { messages.sayLocked(); return; }
      const place = designs.roleItem(state.collage, 'logo', options());
      selection.select(null);
      doc.edit((draft) => {
        draft.items = draft.items.filter((item) => item.role !== 'logo2');
        const first = draft.items.find((item) => item.role === 'logo');
        // The first logo goes back to the middle when the two shared it.
        if (first && !first.locked && inMiddle(place)) first.x = place.x;
      }, 'none');
      logoTarget = 'logo';
      fitRoundel();
      history.commit();
      surface.syncAll();
      renderPanel();
      messages.offerUndo('Second logo removed.');
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
      // A folded card is printed both sides: its outside and its inside, always both.
      const folded = designs.isFolded(state.collage.size);
      const [front, rear] = folded ? ['Outside', 'Inside'] : ['Front', 'Back'];
      return `<div class="clg-section clg-card-sides"><p class="clg-section__title">Sides</p>
          <div class="clg-buttons clg-buttons--two" role="group" aria-label="Side shown">
            <button type="button" class="btn btn-sm ${back ? 'btn-outline-secondary' : 'btn-primary'}" data-card-side="front" aria-pressed="${!back}">${front}</button>
            <button type="button" class="btn btn-sm ${back ? 'btn-primary' : 'btn-outline-secondary'}" data-card-side="back" aria-pressed="${back}">${rear}</button>
          </div>
          ${folded ? `<p class="clg-hint" data-card-halves>${back ? 'Inside: the invitation is the left half (host, invitation line and guest); the details are the right half.' : 'Outside: the right half is the front cover, with the logo and the occasion; the left half is the back cover.'}</p><p class="clg-hint">Print both sides, flipping on the short edge, then fold down the middle.</p>` : '<button type="button" class="btn btn-link btn-sm text-danger p-0 mt-1" data-card-side="delete">Delete the back</button>'}</div>`;
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

    // -------------------------------------------------------------------------
    // The guest list (PR 10): names pasted, or read from a .csv or .txt file, kept with the
    // draft in this browser only. Save as PDF can then make one card for each guest.
    // -------------------------------------------------------------------------

    function guestSection() {
      const names = state.collage.card?.guests || [];
      const count = names.length;
      return `<div class="clg-section clg-card-guests">
          <p class="clg-section__title">Guest list</p>
          <label class="clg-field"><span class="clg-field__label">One name a line, up to ${core.GUEST_LIMITS.names}</span>
            <textarea class="form-control form-control-sm" rows="4" data-card-guests placeholder="Brig P. Singh, SM and Mrs Singh">${escapeHtml(names.join(String.fromCharCode(10)))}</textarea></label>
          <div class="clg-stack">
            <button type="button" class="btn btn-outline-secondary btn-sm" data-card-guest="file"><i class="bi bi-filetype-csv" aria-hidden="true"></i> Names from a .csv or .txt file…</button>
            ${count ? '<button type="button" class="btn btn-link btn-sm text-danger p-0" data-card-guest="clear">Clear names</button>' : ''}
          </div>
          <p class="clg-hint" data-card-guest-count>${guestCountText(count)}</p>
        </div>`;
    }

    // What reading the last list given found: names past the limit, other columns. It belongs to
    // that list only (cold review 6): another draft, or Undo to another list, shows none of it.
    const NO_NOTES = Object.freeze({ over: 0, firstColumn: false, column: 0, extraCells: false, headings: [], noNameColumn: false });
    let guestNotes = { names: '', session: null, ...NO_NOTES };
    const notesFor = (names) => (guestNotes.session === state.collageSession && guestNotes.names === (names || []).join(String.fromCharCode(10)) ? guestNotes : NO_NOTES);
    const ordinal = (index) => ['first', 'second', 'third', 'fourth', 'fifth'][index] || `number ${index + 1}`;

    /**
     * The line under the guest list: how many guests, how many names were left out, and what was
     * taken for headings (code review 9), so a guest read as a heading shows before Save.
     * Headings and names are the user's (or a file's) words: escaped, never markup.
     */
    function guestCountText(count) {
      const notes = notesFor(state.collage.card?.guests);
      const quoted = (list) => list.map((words) => `“${escapeHtml(words)}”`).join(', ');
      const read = notes.headings.length ? ` First line read as column headings: ${quoted(notes.headings)}.` : '';
      if (!count) {
        if (notes.noNameColumn) return `No names were read.${read} None of them is a person's name: head the names' column Name (or Rank and Name), or leave the heading row out.`;
        return 'Leave empty to print the card as it is, with a line for the name. The names stay in this browser.';
      }
      const left = notes.over ? ` Only the first ${core.GUEST_LIMITS.names} are kept: ${notes.over} name${notes.over === 1 ? ' was' : 's were'} left out.` : '';
      const which = notes.column > 0 ? `The first column holds only serial numbers, so the ${ordinal(notes.column)} column was used` : '';
      const column = notes.firstColumn ? ` ${which || 'Only the first column was used'}; give the list a heading row (Rank, Name) to join columns.`
        : which ? ` ${which}.`
        : notes.extraCells ? ' Some rows have a comma inside a name without quotes ("Col X, VSM"): put such names in quotes so all of it is used.' : '';
      // The first names as they will print, so a wrong column shows before Save.
      const first = quoted((state.collage.card?.guests || []).slice(0, 3));
      return `${count} guest${count === 1 ? '' : 's'}: Save as PDF can make a card for each, with the name on it. First on the cards: ${first}${count > 3 ? ', …' : '.'}${read}${left}${column} The names stay in this browser.`;
    }

    /** Sets the guest list from text (pasted, typed, or a file's); kept with the draft, recorded for Undo. */
    function setGuests(text, { csv = false, held = false } = {}) {
      const { names: guests, over, firstColumnOnly, column, extraCells, headings, noNameColumn } = print.readGuests(text, { csv });
      guestNotes = { names: guests.join(String.fromCharCode(10)), session: state.collageSession, over, firstColumn: firstColumnOnly, column, extraCells, headings, noNameColumn };
      doc.edit((draft) => {
        const card = { ...(draft.card || { design: 'classic', details: {} }) };
        if (guests.length) card.guests = guests;
        else delete card.guests;
        draft.card = card;
      }, 'none');
      if (held) history.holdEdit();
      else history.commit();
      const count = form.querySelector('[data-card-guest-count]');
      // guestCountText escapes the names itself, so it is set as markup, never shown escaped.
      if (count) count.innerHTML = guestCountText(guests.length);
      if (over && !held) messages.showAlert(`Only the first ${core.GUEST_LIMITS.names} names are kept. ${over} name${over === 1 ? ' was' : 's were'} left out: make a second card for them.`);
      else if (over) messages.announce(`Only the first ${core.GUEST_LIMITS.names} names are kept. ${over} left out.`);
      return guests;
    }

    const guestFile = document.createElement('input');
    guestFile.type = 'file';
    guestFile.accept = '.csv,.txt,text/csv,text/plain';
    guestFile.hidden = true;
    guestFile.dataset.cardGuestFile = '';
    ui.root.appendChild(guestFile);
    guestFile.addEventListener('change', async () => {
      const file = guestFile.files?.[0];
      guestFile.value = '';
      if (!file) return;
      if (file.size > 1000000) { messages.showAlert('That file is too large for a guest list. Use a .csv or .txt file of at most 1 MB.'); return; }
      // A card left (or another opened) while the file is read gets nothing from it.
      const forCollage = state.collageSession;
      let text = '';
      try {
        // UTF-8, UTF-16 (a spreadsheet's "Unicode Text") or ANSI (Windows-1252).
        text = print.decodeText(await file.arrayBuffer());
      } catch { if (forCollage === state.collageSession) messages.showAlert('That file could not be read.'); return; }
      if (forCollage !== state.collageSession) return;
      const guests = setGuests(text, { csv: /\.csv$/i.test(file.name) || file.type === 'text/csv' });
      renderPanel();
      messages.announce(guests.length ? `${guests.length} names read from ${file.name}.` : `No names were found in ${file.name}.`);
    });

    /** The unit's three colours, for a design with a band in them (Regimental). */
    function colourFields(text) {
      const colours = designs.colourList(text);
      // What else each colour changes besides its band, from the card's own parts (cold review 6).
      const uses = colourUses(state.collage);
      const said = COLOUR_NAMES.map((name, i) => (uses[i].length ? `${name}: ${uses[i].join(', ')}` : '')).filter(Boolean);
      const hint = said.length ? `<p class="clg-hint clg-card-colours__uses">Besides the band, each colour also sets: ${escapeHtml(said.join('; '))}.</p>` : '';
      return `<div class="clg-section clg-card-colours"><p class="clg-section__title">Unit colours</p><div class="clg-card-colours__row">${colours.map((colour, i) => `<label class="clg-card-colour"><input type="color" value="${colour}" data-card-colour="${i + 1}" aria-label="${COLOUR_NAMES[i]} colour of the band"><span>${COLOUR_NAMES[i]}</span></label>`).join('')}</div>${hint}</div>`;
    }

    /** For each unit colour, the parts other than the bands that follow it, in plain words. */
    function colourUses(collage) {
      // What each part is, in plain words, is the designs' (designs.COLOUR_USES).
      const names = designs.COLOUR_USES;
      const labels = Object.fromEntries(FORM.map(([field, label]) => [field, label.toLowerCase().replace(/^back: /, 'back ')]));
      const uses = [[], [], []];
      for (const item of [...(collage?.items || []), ...(collage?.back?.items || [])]) {
        if (!item.tint || /^band\d$/.test(item.role || '')) continue;
        const what = item.field ? `the ${labels[item.field] || item.field}` : names[item.role];
        if (!what) continue;
        for (const index of new Set(Object.values(item.tint))) {
          if (uses[index - 1] && !uses[index - 1].includes(what)) uses[index - 1].push(what);
        }
      }
      return uses;
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
            // A locked part stays exactly as it is.
            if (which === index && !item.locked && ['fill', 'stroke', 'color'].includes(prop)) item[prop] = value.toLowerCase();
          }
        }
      }, 'none');
      surface.syncPage();
      history.holdEdit();
    }

    /**
     * The design buttons, made once and then only marked: a held edit written when a button is
     * pressed redraws the panel, and a button replaced under the pointer would lose its click.
     */
    function renderStrip() {
      if (!strip) return;
      const current = state.collage?.card?.design;
      // The designs for the card's kind (pocket or not); drawn again only when that changes.
      const kind = isPoster() ? 'poster' : undefined;
      const offered = designs.designsFor(state.collage.size, kind).map((design) => design.key).join(' ');
      if (strip.dataset.offered !== offered) {
        strip.dataset.offered = offered;
        strip.innerHTML = designs.designsFor(state.collage.size, kind).map((design) => `<button type="button" class="btn btn-sm" data-card-design="${design.key}">${escapeHtml(design.label)}</button>`).join('');
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
      // A cover title emptied shows the occasion again (cold review 9, M2): its item takes the
      // occasion's words rather than going. Fields that follow this one (the cover title, while it
      // shows the occasion) take the new words too.
      const from = designs.FALLBACKS[field];
      const shown = from && !text.trim() ? String(details[from] || '') : text;
      if (from && !text.trim()) details[field] = '';
      const followers = Object.keys(designs.FALLBACKS).filter((each) => designs.FALLBACKS[each] === field && details[each] === '' && text.trim());
      const fresh = !textFor(collage, field) && shown.trim() ? designs.fieldItem(collage, field, details, options()) : null;
      // Words still at the width their design gave them take the width the design gives the new
      // words (no word left alone on a line: cold review 9, L1); words the user resized keep theirs.
      // A guest's name is set as the design sets that name (cold review 9, M5).
      const guestLook = field === 'guest' && current && shown.trim() ? designs.guestStyle(collage, current, shown, options()) : null;
      const widths = new Map();
      for (const each of [field, ...followers]) {
        const item = textFor(collage, each);
        const before = item && designs.fieldItem(collage, each, designs.detailsOf(collage), options());
        if (!before || Math.abs(before.maxWidth - item.maxWidth) > 0.5) continue;
        const after = designs.fieldItem(collage, each, each === field ? details : { ...details, [each]: '' }, options());
        if (after) widths.set(each, after.maxWidth);
      }
      let removed = null;
      let edited = null;
      doc.edit((draft) => {
        // A field filled again is one the design has placed.
        const placed = [...new Set([...(draft.card?.placed || []), ...(fresh ? [field] : [])])];
        draft.card = { ...draft.card, design: draft.card?.design || 'classic', details, placed };
        const list = fieldsHere(draft, field);
        const index = list.findIndex((item) => item.kind === 'text' && item.field === field);
        // Words keep the edge the design sets them by; a block the design centres (the back's
        // left-aligned programme) keeps its middle, so it never grows past the border (code
        // review 9, H1). A field the design does not place keeps the edge it is aligned to.
        const retext = (target, words) => {
          const left = target.x - target.width / 2;
          const right = target.x + target.width / 2;
          const bottom = target.y + core.itemHeight(target) / 2;
          target.text = words;
          if (widths.has(target.field)) target.maxWidth = widths.get(target.field);
          if (guestLook && target.field === 'guest') for (const key of ['font', 'italic', 'size', 'maxLines', 'minSize']) target[key] = guestLook[key];
          surface.layoutTextItem(target);
          const edge = designs.DESIGNS[draft.card?.design] && designs.fieldsOnCard(draft).includes(target.field) ? designs.fieldAnchor(draft, target.field) : target.align;
          if (edge === 'left') target.x = left + target.width / 2;
          if (edge === 'right') target.x = right - target.width / 2;
          // A guest's name keeps its last line on the line drawn under it: a second line grows upwards.
          if (target.field === 'guest') target.y = bottom - core.itemHeight(target) / 2;
        };
        for (const follower of followers) {
          const item = textFor(draft, follower);
          if (item && !item.locked) retext(item, text);
        }
        if (index >= 0 && !shown.trim()) {
          removed = list[index].id;
          list.splice(index, 1);
        } else if (index >= 0) {
          edited = list[index].id;
          retext(list[index], shown);
          // A longer occasion keeps "at the" and its rule clear of it (cold review 9, L3).
          if (field === 'occasion' || field === 'at') designs.clearOccasion(list, surface.measureText, { shrink: false });
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
        if (words && document.activeElement !== words) words.value = shown;
      }
      surface.syncPage();
      history.holdEdit();
    }

    /** Puts the card in another design, keeping every word; one Undo step brings the last one back. */
    function useDesign(key) {
      if (!designs.DESIGNS[key] || state.collage?.card?.design === key) return;
      history.flushNudge();
      selection.select(null);
      // The Hindi follows the English where it can, and is emptied where it would say something else.
      doc.edit((draft) => designs.applyDesign(draft, key, designs.hindiInStep(designs.detailsOf(draft), OCCASIONS_HI), options()), 'none');
      history.commit();
      surface.syncAll();
      renderStrip();
      const hindiEmpty = designs.fieldsOnCard(state.collage).some((field) => /Hi$/.test(field)) && ['occasionHi', 'hostHi', 'inviteHi'].some((field) => !designs.detailsOf(state.collage)[field]);
      messages.offerUndo(`Design: ${designs.DESIGNS[key].label}. Your words are kept.${hindiEmpty ? ' Type the Hindi lines to match your English.' : ''}`);
    }

    form?.addEventListener('input', (event) => {
      const field = event.target.dataset.cardField;
      if (field) typedIn = event.target;
      if (field) setField(field, event.target.value);
      // A quick pick shows pressed only while its words are the field's.
      if (field) for (const chip of form.querySelectorAll(`[data-card-pick="${field}"]`)) chip.setAttribute('aria-pressed', String(chip.dataset.value === event.target.value));
      if (event.target.dataset.cardColour) setColour(Number(event.target.dataset.cardColour), event.target.value);
      if (event.target.matches('[data-card-guests]')) setGuests(event.target.value, { held: true });
    });
    form?.addEventListener('change', (event) => {
      // English changed on a card with Hindi lines: a Hindi line still saying the sample's words
      // is emptied (or follows an occasion quick pick), so the two languages never disagree.
      const english = event.target.dataset.cardField;
      if (['occasion', 'host', 'invite'].includes(english) && designs.fieldsOnCard(state.collage).some((field) => /Hi$/.test(field))) {
        const before = designs.detailsOf(state.collage);
        const after = designs.hindiInStep(before, OCCASIONS_HI);
        for (const field of ['occasionHi', 'hostHi', 'inviteHi']) {
          if (after[field] === before[field] || !designs.fieldsOnCard(state.collage).includes(field)) continue;
          const input = form.querySelector(`[data-card-field="${field}"]`);
          if (input) input.value = after[field];
          setField(field, after[field]);
        }
      }
      if (event.target.dataset.cardField || event.target.dataset.cardColour || event.target.matches('[data-card-guests]')) history.flushNudge();
    });
    form?.addEventListener('click', (event) => {
      const target = event.target.closest('[data-card-logo-target]');
      if (target) {
        logoTarget = target.dataset.cardLogoTarget === 'logo2' ? 'logo2' : 'logo';
        renderPanel();
        messages.announce(logoTarget === 'logo2' ? 'The choices change the second logo.' : 'The choices change the first logo.');
        return;
      }
      const guest = event.target.closest('[data-card-guest]');
      if (guest) {
        if (guest.dataset.cardGuest === 'file') guestFile.click();
        if (guest.dataset.cardGuest === 'clear') {
          setGuests('');
          renderPanel();
          messages.offerUndo('The guest list is cleared.');
        }
        return;
      }
      const side = event.target.closest('[data-card-side]');
      if (side) { useSide(side.dataset.cardSide); return; }
      const photo = event.target.closest('[data-card-photo]');
      if (photo) {
        if (photo.dataset.cardPhoto === 'none') setPhotoMode('none');
        if (photo.dataset.cardPhoto === 'add') addPhoto();
        return;
      }
      const use = event.target.closest('[data-card-logo-use]');
      if (use) { useLogoSticker(use.dataset.cardLogoUse); return; }
      const logo = event.target.closest('[data-card-logo]');
      if (logo) {
        if (logo.dataset.cardLogo === 'own') logoFile.click();
        if (logo.dataset.cardLogo === 'second') addSecondLogo();
        if (logo.dataset.cardLogo === 'cancel-second') {
          choosingSecond = null;
          renderPanel();
          form.querySelector('[data-card-logo="second"]')?.focus();
          messages.announce('No second logo.');
        }
        if (logo.dataset.cardLogo === 'remove') removeLogo();
        if (logo.dataset.cardLogo === 'none') setLogoMode('none');
        if (logo.dataset.cardLogo === 'later') setLogoMode('later');
        return;
      }
      const pick = event.target.closest('[data-card-pick]');
      if (!pick) return;
      const input = form.querySelector(`[data-card-field="${pick.dataset.cardPick}"]`);
      if (input) input.value = pick.dataset.value;
      setField(pick.dataset.cardPick, pick.dataset.value);
      // On a Bilingual card the Hindi occasion follows the quick pick, so both columns agree.
      const hindi = pick.dataset.cardPick === 'occasion' ? OCCASIONS_HI[pick.dataset.value] : null;
      if (hindi && designs.fieldsOnCard(state.collage).includes('occasionHi')) {
        const hindiInput = form.querySelector('[data-card-field="occasionHi"]');
        if (hindiInput) hindiInput.value = hindi;
        setField('occasionHi', hindi);
      }
      if (pick.dataset.cardPick === 'occasion' && AT_WORDS.includes(designs.detailsOf(state.collage).at)) {
        const at = AT_FOR[pick.dataset.value] || 'at the';
        const atInput = form.querySelector('[data-card-field="at"]');
        if (atInput) atInput.value = at;
        setField('at', at);
      }
      for (const chip of form.querySelectorAll(`[data-card-pick="${pick.dataset.cardPick}"]`)) chip.setAttribute('aria-pressed', String(chip === pick));
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
