// PRISM Collage Studio — the settings panel's sections as HTML: arranging (size, angle, corners,
// see-through, order, line up, lock, hide), Layers, text, and a photo's frame, shape and look
// (docs/ux/collage-review-fixes.md, PR 13). Every value written into the HTML is escaped or
// comes from PRISM's own lists. It draws nothing and keeps no state.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageSettings = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  /**
   * core, draw and stickers are collage-core.js, collage-draw.js and collage-stickers.js;
   * stickerSource(item) is a sticker's picture address; photos is the collage's photo Map;
   * the colour lists and frame widths are the editor's. isOpen(key) says whether a folded group
   * was left open (docs/ux/collage-review-2-fixes.md, PR 8).
   */
  function createSettingsHtml({ core, draw, stickers, stickerSource, escapeHtml, photos, FRAME_WIDTHS, TEXT_COLOURS, LABEL_COLOURS, isOpen = () => false }) {
    /** A group used less often, folded away until opened; it stays open once opened. */
    function fold(key, title, inner) {
      return `
        <details class="clg-section clg-fold" data-fold="${key}"${isOpen(key) ? ' open' : ''}>
          <summary class="clg-fold__title">${escapeHtml(title)}</summary>
          <div class="clg-fold__body">${inner}</div>
        </details>`;
    }

    function arrangeSections(item) {
      const framed = item.kind === 'photo' || item.kind === 'slot';
      const [low, high] = FRAME_WIDTHS;
      const size = !framed ? '' : `
        <div class="clg-section">
          <label class="clg-field"><span class="clg-field__label">Size<output data-out="width">${Math.round(item.width)}</output></span>
            <input type="range" min="${low}" max="${high}" step="1" value="${Math.round(Math.min(high, Math.max(low, item.width)))}" data-prop="width" aria-label="Width of the ${item.kind === 'slot' ? 'frame' : 'photo'}, in page units"></label>
        </div>`;
      const corners = item.kind === 'text' || item.kind === 'sticker' ? '' : `
          <label class="clg-field"><span class="clg-field__label">Rounded corners<output data-out="radius">${item.radius || 0}</output></span>
            <input type="range" min="0" max="${core.LAYOUT_LIMITS.radiusMax}" step="1" value="${item.radius || 0}" data-prop="radius" aria-label="Rounded corners, in page units"></label>`;
      // Size, and lock, hide, duplicate and delete, are always in view; the rest fold away.
      return `${size}
        ${fold('angle', 'Angle', `
          <label class="clg-field"><span class="clg-field__label">Turn<output data-out="angle">${Math.round(item.angle)}°</output></span>
            <input type="range" min="-180" max="180" step="1" value="${Math.round(item.angle)}" data-prop="angle" aria-label="Angle in degrees"></label>
          <div class="clg-buttons">
            <button type="button" class="btn btn-outline-secondary btn-sm" data-action="rotate-left">−15°</button>
            <button type="button" class="btn btn-outline-secondary btn-sm" data-action="straighten">Straight</button>
            <button type="button" class="btn btn-outline-secondary btn-sm" data-action="rotate-right">+15°</button>
          </div>`)}
        ${fold('see-through', corners ? 'Corners and see-through' : 'See-through', `
          ${corners}
          <label class="clg-field"><span class="clg-field__label">See-through<output data-out="opacity">${item.opacity}%</output></span>
            <input type="range" min="10" max="100" step="1" value="${item.opacity}" data-prop="opacity" aria-label="How solid it is, in percent"></label>`)}
        ${fold('order', 'Order', `
          <div class="clg-buttons clg-buttons--two">
            <button type="button" class="btn btn-outline-secondary btn-sm" data-action="forward">Bring forward</button>
            <button type="button" class="btn btn-outline-secondary btn-sm" data-action="backward">Send backward</button>
            <button type="button" class="btn btn-outline-secondary btn-sm" data-action="front">To the front</button>
            <button type="button" class="btn btn-outline-secondary btn-sm" data-action="back">To the back</button>
          </div>`)}
        ${fold('line-up', 'Line up on the page', `
          <div class="clg-buttons">
            ${core.ALIGN_PLACES.map((place) => `<button type="button" class="btn btn-outline-secondary btn-sm" data-align="${place}"${item.locked ? ' disabled' : ''}>${place.charAt(0).toUpperCase() + place.slice(1)}</button>`).join('')}
          </div>`)}
        <div class="clg-section">
          <div class="clg-buttons clg-buttons--two">
            <button type="button" class="btn btn-outline-secondary btn-sm" data-action="lock" aria-pressed="${Boolean(item.locked)}"><i class="bi ${item.locked ? 'bi-unlock' : 'bi-lock'}" aria-hidden="true"></i> ${item.locked ? 'Unlock' : 'Lock'}</button>
            <button type="button" class="btn btn-outline-secondary btn-sm" data-action="hide" aria-pressed="${Boolean(item.hidden)}"><i class="bi ${item.hidden ? 'bi-eye' : 'bi-eye-slash'}" aria-hidden="true"></i> ${item.hidden ? 'Show' : 'Hide'}</button>
            <button type="button" class="btn btn-outline-secondary btn-sm" data-action="duplicate"><i class="bi bi-copy" aria-hidden="true"></i> Duplicate</button>
            <button type="button" class="btn btn-outline-danger btn-sm" data-action="delete"${item.locked ? ' disabled' : ''}><i class="bi bi-trash3" aria-hidden="true"></i> Delete</button>
          </div>
          ${item.locked ? '<p class="clg-hint">Locked: it cannot be moved, resized, turned, given another frame or shape, or deleted until it is unlocked.</p>' : ''}
          ${item.hidden ? '<p class="clg-hint">Hidden: it is not on the page or in the saved picture until it is shown again.</p>' : ''}
        </div>`;
    }

    /** The Layers list: every item, the front one first, to select, hide or lock. */
    function layersSection(collage, selectedId) {
      if (!collage.items.length) return '';
      const rows = [...collage.items].reverse().map((item) => {
        let thumb = '<span class="clg-layer__thumb clg-layer__thumb--icon" aria-hidden="true"><i class="bi bi-square"></i></span>';
        let name = core.layerName(item);
        if (item.kind === 'photo') {
          const photo = photos.get(item.photo);
          thumb = `<img class="clg-layer__thumb" src="${photo?.thumbnail || ''}" alt="">`;
          name = photo?.name || name;
        } else if (item.kind === 'sticker') {
          const entry = stickers.find(item.sticker);
          thumb = `<img class="clg-layer__thumb clg-layer__thumb--contain" src="${stickerSource(item) || ''}" alt="">`;
          name = entry?.label || entry?.words?.split(' ')[0] || name;
        } else if (item.kind === 'text') {
          thumb = '<span class="clg-layer__thumb clg-layer__thumb--icon" aria-hidden="true"><i class="bi bi-fonts"></i></span>';
        }
        return `<li class="clg-layer${item.id === selectedId ? ' is-on' : ''}${item.hidden ? ' is-hidden' : ''}">
            <button type="button" class="clg-layer__pick" data-layer-select="${item.id}" aria-current="${item.id === selectedId}">${thumb}<span class="clg-layer__name">${escapeHtml(name)}</span></button>
            <button type="button" class="clg-layer__tool" data-layer-hide="${item.id}" aria-pressed="${Boolean(item.hidden)}" title="${item.hidden ? 'Show' : 'Hide'}" aria-label="${item.hidden ? 'Show' : 'Hide'} ${escapeHtml(name)}"><i class="bi ${item.hidden ? 'bi-eye-slash' : 'bi-eye'}" aria-hidden="true"></i></button>
            <button type="button" class="clg-layer__tool" data-layer-lock="${item.id}" aria-pressed="${Boolean(item.locked)}" title="${item.locked ? 'Unlock' : 'Lock'}" aria-label="${item.locked ? 'Unlock' : 'Lock'} ${escapeHtml(name)}"><i class="bi ${item.locked ? 'bi-lock-fill' : 'bi-unlock'}" aria-hidden="true"></i></button>
          </li>`;
      }).join('');
      return `
        <div class="clg-section">
          <p class="clg-section__title">Layers</p>
          <ul class="clg-layers">${rows}</ul>
          <p class="clg-hint">The top of the list is in front.</p>
        </div>`;
    }

    function swatches(name, colours, current, withNone = false) {
      const none = withNone ? `<button type="button" class="clg-swatch clg-swatch--none" data-text-set="${name}" data-value="" aria-pressed="${!current}" title="None" aria-label="None"></button>` : '';
      const list = colours.map((c) => `<button type="button" class="clg-swatch" data-text-set="${name}" data-value="${c}" aria-pressed="${current === c}" style="background:${c}" title="${c}" aria-label="Colour ${c}"></button>`).join('');
      const pick = `<input type="color" class="clg-swatch-pick" value="${/^#[0-9a-f]{6}$/i.test(current) ? current : '#ffffff'}" data-text-colour="${name}" aria-label="Any colour">`;
      return `<div class="clg-swatches">${none}${list}${pick}</div>`;
    }

    /** Words, font and effects of a text item. */
    function textSections(item) {
      const fonts = Object.entries(core.FONTS).map(([key, font]) => `<button type="button" class="clg-font${item.font === key ? ' is-on' : ''}" data-text-set="font" data-value="${key}" aria-pressed="${item.font === key}" style='font-family:${font.css}'>${escapeHtml(font.label)}</button>`).join('');
      const aligns = [['left', 'Left'], ['center', 'Centre'], ['right', 'Right']].map(([v, l]) => `<button type="button" class="btn btn-sm ${item.align === v ? 'btn-primary' : 'btn-outline-secondary'}" data-text-set="align" data-value="${v}" aria-pressed="${item.align === v}">${l}</button>`).join('');
      const range = (prop, label, [min, max], unit = '') => `<label class="clg-field"><span class="clg-field__label">${label}<output data-out="${prop}">${item[prop]}${unit}</output></span>
            <input type="range" min="${min}" max="${max}" step="1" value="${item[prop]}" data-prop="${prop}" aria-label="${label}"></label>`;
      return `
        <label class="clg-field"><span class="clg-field__label">Words</span>
          <textarea class="form-control form-control-sm clg-text-field" rows="3" maxlength="${core.TEXT_LIMITS.maxLength}" data-text>${escapeHtml(item.text)}</textarea></label>
        <p class="clg-hint">Type Hindi with the Windows Hindi keyboard, or paste it.</p>
        <div class="clg-section">
          <p class="clg-section__title">Font</p>
          <div class="clg-fonts">${fonts}</div>
          ${range('size', 'Size', core.TEXT_LIMITS.size)}
          <div class="clg-choices">
            <button type="button" class="btn btn-sm ${item.bold ? 'btn-primary' : 'btn-outline-secondary'}" data-text-toggle="bold" aria-pressed="${item.bold}"><b>B</b> Bold</button>
            <button type="button" class="btn btn-sm ${item.italic ? 'btn-primary' : 'btn-outline-secondary'}" data-text-toggle="italic" aria-pressed="${item.italic}"><i>I</i> Italic</button>
          </div>
          <div class="clg-choices clg-choices--gap">${aligns}</div>
          ${range('spacing', 'Letter spacing', core.TEXT_LIMITS.spacing)}
          <p class="clg-field__label clg-field__label--plain">Colour</p>
          ${swatches('color', TEXT_COLOURS, item.color)}
        </div>
        ${fold('effects', 'Effects: curve, outline, shadow, label', `
          ${range('curve', 'Curve', core.TEXT_LIMITS.curve)}
          ${range('outline', 'Outline', core.TEXT_LIMITS.outline)}
          <p class="clg-field__label clg-field__label--plain">Outline colour</p>
          ${swatches('outlineColor', TEXT_COLOURS, item.outlineColor)}
          ${switchRow('shadow', 'Shadow', item.shadow)}
          ${switchRow('glow', 'Glow', item.glow)}
          <p class="clg-field__label clg-field__label--plain">Label behind the words${item.curve ? ' (straight text only)' : ''}</p>
          ${swatches('label', LABEL_COLOURS, item.label, true)}`)}`;
    }

    /** Buttons that choose one value of a photo's finish (frame, shape or look). */
    function choiceButtons(name, entries, current, disabled = () => false) {
      return entries.map(([value, label]) => `<button type="button" class="btn btn-sm ${value === current ? 'btn-primary' : 'btn-outline-secondary'}" data-finish="${name}" data-value="${value}" aria-pressed="${value === current}"${disabled(value) ? ' disabled' : ''}>${escapeHtml(label)}</button>`).join('');
    }

    function switchRow(name, label, on) {
      return `<label class="form-check form-switch clg-switch"><input class="form-check-input" type="checkbox" role="switch" data-toggle="${name}"${on ? ' checked' : ''}><span class="form-check-label">${escapeHtml(label)}</span></label>`;
    }

    /** Frame, shape and look: the photo's finish, the same on the page and in the saved picture. */
    function finishSections(item, photo) {
      const shape = item.shape || 'rect';
      const frame = item.frame || 'none';
      const square = shape === 'rect';
      const look = item.look || 'none';
      const filterOk = draw.canFilter(document.createElement('canvas').getContext('2d'));
      const looks = Object.entries(core.LOOKS).map(([value, entry]) => `
            <button type="button" class="clg-look${value === look ? ' is-on' : ''}" data-finish="look" data-value="${value}" aria-pressed="${value === look}">
              <img src="${photo?.thumbnail || ''}" alt="" style="filter:${entry.filter || 'none'}"><span>${escapeHtml(entry.label)}</span>
            </button>`).join('');
      const polaroid = square && frame === 'polaroid';
      return `
        <div class="clg-section">
          <label class="clg-field"><span class="clg-field__label">Caption <span class="clg-field__optional">(optional)</span></span>
            <input type="text" class="form-control form-control-sm" maxlength="${core.CAPTION_MAX}" value="${escapeHtml(item.caption || '')}" placeholder="Last day at office" data-caption aria-describedby="clgCaptionHint"></label>
          <p class="clg-hint" id="clgCaptionHint">${polaroid ? 'Written by hand in the Polaroid’s white edge.' : 'Shown on a white strip under the photo.'} Up to ${core.CAPTION_MAX} letters.</p>
        </div>
        <div class="clg-section">
          <p class="clg-section__title">Frame</p>
          <div class="clg-choices">${choiceButtons('frame', Object.entries(core.FRAMES).map(([k, v]) => [k, v.label]), frame, (v) => !square && v !== 'none')}</div>
          ${square ? '' : '<p class="clg-hint">Frames go with the Square shape.</p>'}
          ${switchRow('shadow', 'Shadow', item.shadow)}
        </div>
        <div class="clg-section">
          <p class="clg-section__title">Shape</p>
          <div class="clg-choices">${choiceButtons('shape', Object.entries(core.SHAPES), shape)}</div>
        </div>
        <div class="clg-section">
          <p class="clg-section__title">Look</p>
          ${filterOk ? '' : '<p class="clg-hint">This browser cannot show looks; use Edge or Chrome to change them.</p>'}
          <div class="clg-looks">${looks}</div>
        </div>
        ${fold('adjust', 'Brightness, contrast, warmth, mirror', `
          <label class="clg-field"><span class="clg-field__label">Brightness<output data-out="bright">${item.bright ?? 100}%</output></span>
            <input type="range" min="${core.ADJUST_LIMITS.bright[0]}" max="${core.ADJUST_LIMITS.bright[1]}" step="1" value="${item.bright ?? 100}" data-prop="bright" aria-label="Brightness, in percent"></label>
          <label class="clg-field"><span class="clg-field__label">Contrast<output data-out="contrast">${item.contrast ?? 100}%</output></span>
            <input type="range" min="${core.ADJUST_LIMITS.contrast[0]}" max="${core.ADJUST_LIMITS.contrast[1]}" step="1" value="${item.contrast ?? 100}" data-prop="contrast" aria-label="Contrast, in percent"></label>
          <label class="clg-field"><span class="clg-field__label">Warmth<output data-out="warmth">${item.warmth ?? 0}</output></span>
            <input type="range" min="${core.ADJUST_LIMITS.warmth[0]}" max="${core.ADJUST_LIMITS.warmth[1]}" step="1" value="${item.warmth ?? 0}" data-prop="warmth" aria-label="Warmth: below 0 cooler, above 0 warmer"></label>
          ${switchRow('flip', 'Mirror the photo', item.flip)}
          <button type="button" class="btn btn-outline-secondary btn-sm" data-action="reset-look">Reset look</button>`)}`;
    }

    return Object.freeze({ arrangeSections, layersSection, swatches, textSections, choiceButtons, switchRow, finishSections, fold });
  }

  return Object.freeze({ createSettingsHtml });
});
