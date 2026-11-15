// PRISM Collage Studio — keeping keyboard focus on the control used when a panel is drawn again
// (docs/ux/collage-review-fixes.md, PR 3 and the split in PR 12).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageFocus = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  // CSS.escape where the browser has it, else the two characters an attribute value must escape.
  const escapeValue = (value) => (typeof CSS !== 'undefined' && CSS.escape
    ? CSS.escape(value)
    : String(value).replace(/["\\]/g, '\\$&'));

  /** A focus keeper for one page (root): keep(render) draws a panel and puts focus back. */
  function createFocusKeeper(root) {
    /**
     * The same control after a panel is drawn again: the element type with its data-* attributes
     * (and a radio button's name and value), which name a control and not its state.
     */
    function controlKey(element) {
      if (!element || element === root.ownerDocument.body || !root.contains(element)) return null;
      const named = [...element.attributes].filter((attribute) =>
        (attribute.name.startsWith('data-') && !attribute.name.startsWith('data-bs-'))
        || (element.type === 'radio' && (attribute.name === 'name' || attribute.name === 'value')));
      if (!named.length) return null;
      return element.tagName.toLowerCase() + named.map((attribute) => `[${attribute.name}="${escapeValue(attribute.value)}"]`).join('');
    }

    /**
     * Draws a panel again (render) and, if the focused control was replaced, puts focus back on
     * the control that takes its place, with the caret where it was, so keyboard and screen-reader
     * users stay where they were.
     */
    function keepingFocus(render) {
      const before = root.ownerDocument.activeElement;
      const key = controlKey(before);
      const caret = before && typeof before.selectionStart === 'number' ? [before.selectionStart, before.selectionEnd] : null;
      // The control is looked for again only inside the part of the page that was redrawn (the
      // nearest container that is still there), so a matching button elsewhere, such as the
      // floating toolbar's Lock for Settings' Lock, never takes the focus.
      const containers = [];
      for (let node = before?.parentElement; node && node !== root; node = node.parentElement) containers.push(node);
      render();
      if (!key || before.isConnected) return;
      const scope = containers.find((node) => node.isConnected) || root;
      const after = scope.querySelector(key);
      if (!after || after.disabled) return;
      after.focus({ preventScroll: true });
      if (caret && typeof after.setSelectionRange === 'function') {
        try { after.setSelectionRange(caret[0], caret[1]); } catch { /* not a text field */ }
      }
    }


    return Object.freeze({ controlKey, keep: keepingFocus });
  }

  return Object.freeze({ createFocusKeeper });
});
