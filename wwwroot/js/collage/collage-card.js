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
    ['note', 'Note at the bottom']
  ]);
  const OCCASIONS = Object.freeze(['Raising Day Dinner', 'Regimental Dinner', 'Change of Command', 'Farewell', 'Barakhana', 'At Home', 'Ladies’ Night', 'Passing Out Parade']);
  const DRESSES = Object.freeze(['Dress: Lounge Suit', 'Dress: Mess Dress', 'Dress: Smart Casuals', 'Dress: Ceremonial']);
  const DEFAULT_CARD_SIZE = 'a5p';

  function createCard(deps) {
    const { state, core, draw, designs, ui, doc, history, messages, surface, selection, screens } = deps;
    const escapeHtml = (value) => String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const form = ui.$('[data-card-form]');
    const strip = ui.$('[data-card-designs]');
    const railButton = ui.$('[data-panel="card"]');
    const startGrid = ui.$('[data-start-designs]');
    const startSize = ui.$('[data-card-size]');
    const options = () => ({ measure: surface.measureText, newId: doc.newItemId });
    const textFor = (collage, field) => collage.items.find((item) => item.kind === 'text' && item.field === field) || null;

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
        startSize.value = DEFAULT_CARD_SIZE;
      }
      const size = startSize.value || DEFAULT_CARD_SIZE;
      const round = ++thumbs;
      startGrid.textContent = '';
      for (const design of Object.values(designs.DESIGNS)) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'clg-design-card';
        button.dataset.startDesign = design.key;
        button.setAttribute('aria-label', `${design.label} card, ${core.SIZES[size].label}`);
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
        canvas.getContext('2d').drawImage(drawn.canvas, 0, 0);
      } catch {
        // A picture that cannot be drawn leaves the card's name; the design still opens.
      }
    }

    startSize?.addEventListener('change', () => renderStart());
    startGrid?.addEventListener('click', (event) => {
      const button = event.target.closest('[data-start-design]');
      if (button) screens.startCollage(startSize.value || DEFAULT_CARD_SIZE, null, button.dataset.startDesign);
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
      else renderStrip();
    }

    function renderPanel() {
      const details = designs.detailsOf(state.collage);
      form.innerHTML = FORM.map(([field, label, long]) => {
        const id = `clgCard-${field}`;
        const value = escapeHtml(details[field] || '');
        const input = long
          ? `<textarea class="form-control form-control-sm" id="${id}" rows="2" maxlength="${core.TEXT_LIMITS.maxLength}" data-card-field="${field}">${value}</textarea>`
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
        const index = draft.items.findIndex((item) => item.kind === 'text' && item.field === field);
        if (index >= 0 && !text.trim()) {
          removed = draft.items[index].id;
          draft.items.splice(index, 1);
        } else if (index >= 0) {
          const target = draft.items[index];
          edited = target.id;
          const left = target.x - target.width / 2;
          const right = target.x + target.width / 2;
          target.text = text;
          surface.layoutTextItem(target);
          if (target.align === 'left') target.x = left + target.width / 2;
          if (target.align === 'right') target.x = right - target.width / 2;
        } else if (fresh) {
          // Among the design's parts, behind anything the user added.
          const own = draft.items.findIndex((item) => !item.role);
          draft.items.splice(own < 0 ? draft.items.length : own, 0, fresh);
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
      if (field) setField(field, event.target.value);
    });
    form?.addEventListener('change', (event) => {
      if (event.target.dataset.cardField) history.flushNudge();
    });
    form?.addEventListener('click', (event) => {
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
