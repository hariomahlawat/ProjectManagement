// =============================
// :has() FALLBACK FOR OLDER BROWSERS
// Edge and Chrome before 105 (still found on offline LAN machines that are not updated) do not
// understand :has(), and drop every rule that uses it. The few :has() rules that carry meaning
// (which option is chosen when its radio is hidden, where keyboard focus is, a page that must
// not scroll behind an open viewer) get a twin rule under html.pm-no-has, driven by the state
// classes this script keeps up to date. Browsers that understand :has() never run it, so their
// pages are unchanged.
// =============================
(function (global) {
  'use strict';

  // container: the element the :has() rule styles; className: the class this script sets on it;
  // test: what the :has() looks for inside it, as a selector or a function of the container
  // (focus: true means "holds keyboard focus").
  var RULES = [
    { container: 'html', className: 'pm-has-pbd-viewer-open', test: '.pbd-viewer[open]' },
    { container: 'body', className: 'pm-has-briefing-decks', test: '.command-workspace--briefing-decks' },
    { container: '.pbd-design', className: 'pm-has-checked', test: 'input:checked' },
    {
      container: '.photos-viewer__stage',
      className: 'pm-has-strip',
      // .photos-viewer__strip:not([hidden]), read from the property.
      test: function (stage) {
        var strip = stage.querySelector('.photos-viewer__strip');
        return !!strip && !strip.hidden;
      },
    },
    { container: '.photos-library', className: 'pm-has-scrubber', test: '[data-photos-scrubber]' },
    { container: '.todo-row', className: 'pm-has-done-checked', test: '.js-done-checkbox:checked' },
    {
      container: '.pbd-design, .brochure-artwork-option, .pf-analysis-choice, .identity-group-face, .analytics-filter',
      className: 'pm-has-focus-visible',
      focus: true,
    },
  ];

  function supportsHas(win) {
    try {
      return !!(win.CSS && win.CSS.supports && win.CSS.supports('selector(:has(*))'));
    } catch (error) {
      return false;
    }
  }

  function install(win, options) {
    var doc = win.document;
    var force = options && options.force;
    if (!force && supportsHas(win)) return null;
    doc.documentElement.classList.add('pm-no-has');

    // :focus-visible arrived in Chromium 86; before that, any focus counts.
    var focusSelector = ':focus-visible';
    try { doc.documentElement.matches(focusSelector); } catch (error) { focusSelector = ':focus'; }

    function holds(element, rule) {
      if (rule.focus) {
        var active = doc.activeElement;
        if (!active || active === doc.body || !element.contains(active)) return false;
        try { return active.matches(focusSelector); } catch (error) { return true; }
      }
      return typeof rule.test === 'function' ? rule.test(element) : element.querySelector(rule.test) !== null;
    }

    function update() {
      RULES.forEach(function (rule) {
        var elements = rule.container === 'html' ? [doc.documentElement]
          : rule.container === 'body' ? (doc.body ? [doc.body] : [])
            : Array.prototype.slice.call(doc.querySelectorAll(rule.container));
        elements.forEach(function (element) {
          element.classList.toggle(rule.className, holds(element, rule));
        });
      });
    }

    var pending = false;
    function schedule() {
      if (pending) return;
      pending = true;
      (win.requestAnimationFrame || function (callback) { return win.setTimeout(callback, 16); })(function () {
        pending = false;
        update();
      });
    }

    ['change', 'input', 'click', 'focusin', 'focusout', 'keyup'].forEach(function (type) {
      doc.addEventListener(type, schedule, true);
    });
    if (win.MutationObserver) {
      // Our own class changes are not watched (attributeFilter), so updating cannot loop.
      new win.MutationObserver(schedule).observe(doc.documentElement, {
        subtree: true,
        childList: true,
        attributes: true,
        attributeFilter: ['open', 'hidden', 'checked'],
      });
    }
    if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', update);
    update();
    return { update: update, rules: RULES };
  }

  global.PrismHasFallback = { install: install, supportsHas: supportsHas, rules: RULES };
  if (global.document) install(global);
})(typeof window !== 'undefined' ? window : this);
