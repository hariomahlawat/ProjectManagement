// =============================
// THEME INTRODUCTION
// A one-time card telling a signed-in user that a Dark theme exists and where to change it.
// Light stays the default: nothing changes unless the user chooses. Remembered per browser,
// like the theme itself. The card appears on up to three page loads until the user answers or
// closes it; never over an open dialog or panel, never while a form field has focus, never to
// someone who has already used the theme switch, and never when the browser cannot remember
// the answer (it would otherwise come back on every page).
// =============================
(function (global) {
  'use strict';

  var INTRO_KEY = 'prism.themeIntro';
  var THEME_KEY = 'prism.theme';
  var DONE = 'done';
  var MAX_SHOWS = 3;
  var SHOW_DELAY_MS = 1200;
  var document = global.document;

  // undefined when storage is unavailable.
  function read(key) {
    try {
      return global.localStorage.getItem(key);
    } catch (e) {
      return undefined;
    }
  }

  function write(value) {
    try {
      global.localStorage.setItem(INTRO_KEY, value);
      return true;
    } catch (e) {
      return false;
    }
  }

  function isBusy() {
    var active = document.activeElement;
    if (active && typeof active.matches === 'function'
        && active.matches('input, textarea, select, [contenteditable=""], [contenteditable="true"]')) {
      return true;
    }
    return !!document.querySelector('.modal.show, .offcanvas.show, dialog[open]');
  }

  function setup(card, theme) {
    var text = card.querySelector('[data-theme-intro-text]');
    var ask = card.querySelector('[data-theme-intro-ask]');
    var done = card.querySelector('[data-theme-intro-done]');
    var keep = card.querySelector('[data-theme-intro-keep]');

    function close() {
      write(DONE);
      card.hidden = true;
    }

    card.addEventListener('click', function (event) {
      var target = event.target && typeof event.target.closest === 'function' ? event.target : null;
      if (!target) return;

      var choice = target.closest('[data-theme-intro-choose]');
      if (choice) {
        var preference = choice.getAttribute('data-theme-intro-choose');
        write(DONE);
        theme.set(preference);
        if (text) {
          text.textContent = preference === 'system'
            ? 'PRISM now follows your Windows setting (System).'
            : 'Dark theme is on.';
        }
        if (ask) ask.hidden = true;
        if (done) done.hidden = false;
        if (keep) keep.focus();
        return;
      }
      if (target.closest('[data-theme-intro-undo]')) {
        theme.set('light');
        close();
        return;
      }
      if (target.closest('[data-theme-intro-close]')) {
        close();
      }
    });

    // Escape closes it from inside the card or from the page itself (the card does not take
    // focus when it appears). Escape in a field, the chat or a menu stays theirs.
    document.addEventListener('keydown', function (event) {
      if (event.key !== 'Escape' || event.defaultPrevented || card.hidden
          || card.classList.contains('is-stepped-aside')) {
        return;
      }
      var active = document.activeElement;
      if (!active || active === document.body || active === document.documentElement || card.contains(active)) {
        close();
      }
    });

    // Choosing a theme in the user menu answers the card too.
    document.addEventListener('click', function (event) {
      var target = event.target;
      if (target && typeof target.closest === 'function' && target.closest('[data-theme-preference-value]')) {
        close();
      }
    });

    // The card sits where the user menu opens; step aside while any dropdown is open.
    document.addEventListener('shown.bs.dropdown', function () {
      card.classList.add('is-stepped-aside');
    });
    document.addEventListener('hidden.bs.dropdown', function () {
      card.classList.remove('is-stepped-aside');
    });
  }

  function init() {
    var card = document.getElementById('pmThemeIntro');
    var theme = global.PMThemePreference;
    if (!card || !theme) return;

    var state = read(INTRO_KEY);
    if (state === undefined || state === DONE) return;
    // A stored theme means the user has already found and used the switch.
    if (read(THEME_KEY)) {
      write(DONE);
      return;
    }
    var shows = parseInt(state, 10) || 0;
    if (shows >= MAX_SHOWS) {
      write(DONE);
      return;
    }

    setup(card, theme);
    global.setTimeout(function () {
      // Answered during the delay (the profile menu's theme switch, another tab): stay away.
      if (read(INTRO_KEY) === DONE || read(THEME_KEY)) return;
      // Busy now: try again on a later page.
      if (isBusy()) return;
      if (!write(String(shows + 1))) return;
      card.hidden = false;
    }, SHOW_DELAY_MS);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})(window);
