// =============================
// BRIEFING STUDIO FOCUS VIEW
// Loaded synchronously in <head> on the Studio only (like theme-preference.js), so a remembered
// focus view is on <html> before first paint and the PRISM top bar never flashes. The CSS hides
// the top bar and the workspace rail while html[data-pbd-focus] is set, from 576px up; phones
// ignore it and show no Focus button.
// The choice is per person in this browser (localStorage, as the theme and the rail fold are).
// Escape is deliberately not a way out: it belongs to dialogs and Present.
// Control: the button with data-pbd-focus-toggle. A change fires `pbd:focus-change` on window so
// the Studio can measure its height again.
// =============================
(function (global) {
  'use strict';

  var STORAGE_KEY = 'prism.briefingStudio.focus';
  var root = global.document.documentElement;

  function readPreference() {
    try {
      return global.localStorage.getItem(STORAGE_KEY) === 'on';
    } catch (e) {
      return false;
    }
  }

  function writePreference(on) {
    try {
      if (on) global.localStorage.setItem(STORAGE_KEY, 'on');
      else global.localStorage.removeItem(STORAGE_KEY);
    } catch (e) {
      // The view still changes on this page; it just will not be remembered.
    }
  }

  function isOn() {
    return root.hasAttribute('data-pbd-focus');
  }

  function syncButtons() {
    var on = isOn();
    var buttons = global.document.querySelectorAll('[data-pbd-focus-toggle]');
    for (var i = 0; i < buttons.length; i++) {
      var button = buttons[i];
      button.setAttribute('aria-pressed', on ? 'true' : 'false');
      button.title = on
        ? 'Show the PRISM top bar and workspace navigation again'
        : 'Hide the PRISM top bar and workspace navigation so the Studio has the whole window';
      var label = button.querySelector('[data-pbd-focus-label]');
      if (label) label.textContent = on ? 'Exit focus' : 'Focus';
      var iconNode = button.querySelector('[data-pbd-focus-icon]');
      if (iconNode) {
        iconNode.classList.toggle('bi-fullscreen', !on);
        iconNode.classList.toggle('bi-fullscreen-exit', on);
      }
    }
  }

  function apply(on) {
    if (on) root.setAttribute('data-pbd-focus', '');
    else root.removeAttribute('data-pbd-focus');
    syncButtons();
  }

  function set(on) {
    on = Boolean(on);
    if (on === isOn()) return;
    apply(on);
    writePreference(on);
    var event;
    if (typeof global.CustomEvent === 'function') {
      event = new global.CustomEvent('pbd:focus-change', { detail: { on: on } });
    } else {
      event = global.document.createEvent('CustomEvent');
      event.initCustomEvent('pbd:focus-change', false, false, { on: on });
    }
    global.dispatchEvent(event);
  }

  function bind() {
    syncButtons();
    global.document.addEventListener('click', function (event) {
      var target = event.target;
      var button = target && target.closest ? target.closest('[data-pbd-focus-toggle]') : null;
      if (!button) return;
      event.preventDefault();
      set(!isOn());
      // The button stays where it was (the header is kept), so keyboard focus stays on it; a
      // layout change must not drop it to <body>.
      if (global.document.activeElement !== button) button.focus();
    });
  }

  apply(readPreference());

  if (global.document.readyState === 'loading') {
    global.document.addEventListener('DOMContentLoaded', bind);
  } else {
    bind();
  }

  global.PrismBriefingFocus = { isOn: isOn, set: set };
})(window);
