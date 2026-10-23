// =============================
// WHAT'S NEW
// One-time cards that introduce a new feature to signed-in users (Pages/Shared/_WhatsNew.cshtml):
// Chat first, then the Dark theme. One card at a time, in page order: the next appears only
// once the one before has been answered, and not before the next day, so a new user is not
// handed one introduction after another. Each is remembered per browser under its own key
// (data-whats-new) and appears on up to three page loads until the user answers or closes it.
// A card never appears over an open dialog or panel, never while a form field has focus, never
// to someone who has already used the feature (data-whats-new-skip-if-stored lists the storage
// keys that feature writes), never while the chat contact list is open (it sits in the same
// corner), and never when the browser cannot remember the answer (it would otherwise come back
// on every page).
//
// Actions inside a card:
//   data-whats-new-theme="dark|system"  choose a theme, then show the card's confirmation
//   data-whats-new-undo                 back to Light, then close
//   data-whats-new-open-chat            close, open the chat contact list, point at the chat button
//   data-whats-new-close                close ("Not now", "Keep it", the close button)
// data-whats-new-answered-by names controls elsewhere on the page (a CSS selector) whose use
// also answers the card, e.g. the theme switch in the profile menu.
// =============================
(function (global) {
  'use strict';

  var DONE = 'done';
  var LAST_ANSWERED_KEY = 'prism.whatsNew.lastAnswered';
  var GAP_MS = 20 * 60 * 60 * 1000;
  var MAX_SHOWS = 3;
  var SHOW_DELAY_MS = 1200;
  var POINT_MS = 2400;
  var document = global.document;

  // undefined when storage is unavailable.
  function read(key) {
    try {
      return global.localStorage.getItem(key);
    } catch (e) {
      return undefined;
    }
  }

  function write(key, value) {
    try {
      global.localStorage.setItem(key, value);
      return true;
    } catch (e) {
      return false;
    }
  }

  function skipKeys(card) {
    return (card.getAttribute('data-whats-new-skip-if-stored') || '').split(/\s+/).filter(Boolean);
  }

  // The user has already used the feature: the card has nothing to tell them.
  function featureUsed(card) {
    return skipKeys(card).some(function (key) { return !!read(key); });
  }

  function isBusy() {
    var active = document.activeElement;
    if (active && typeof active.matches === 'function'
        && active.matches('input, textarea, select, [contenteditable=""], [contenteditable="true"]')) {
      return true;
    }
    return !!document.querySelector('.modal.show, .offcanvas.show, dialog[open], [data-chat-root].has-buddy');
  }

  function pointAtChat() {
    var launchers = document.querySelectorAll('[data-chat-launcher]');
    for (var i = 0; i < launchers.length; i++) {
      launchers[i].classList.add('is-pointed');
    }
    global.setTimeout(function () {
      for (var j = 0; j < launchers.length; j++) {
        launchers[j].classList.remove('is-pointed');
      }
    }, POINT_MS);
  }

  function setup(card) {
    var key = card.getAttribute('data-whats-new');
    var text = card.querySelector('[data-whats-new-text]');
    var ask = card.querySelector('[data-whats-new-ask]');
    var done = card.querySelector('[data-whats-new-done]');
    var keep = card.querySelector('[data-whats-new-keep]');
    var answeredBy = card.getAttribute('data-whats-new-answered-by');

    function answered() {
      write(key, DONE);
      write(LAST_ANSWERED_KEY, String(Date.now()));
    }

    function close() {
      answered();
      card.hidden = true;
    }

    card.addEventListener('click', function (event) {
      var target = event.target && typeof event.target.closest === 'function' ? event.target : null;
      if (!target) return;

      var themeChoice = target.closest('[data-whats-new-theme]');
      if (themeChoice && global.PMThemePreference) {
        var preference = themeChoice.getAttribute('data-whats-new-theme');
        answered();
        global.PMThemePreference.set(preference);
        if (text) text.textContent = themeChoice.getAttribute('data-whats-new-confirm') || text.textContent;
        if (ask) ask.hidden = true;
        if (done) done.hidden = false;
        if (keep) keep.focus();
        return;
      }
      if (target.closest('[data-whats-new-undo]')) {
        if (global.PMThemePreference) global.PMThemePreference.set('light');
        close();
        return;
      }
      if (target.closest('[data-whats-new-open-chat]')) {
        close();
        pointAtChat();
        if (global.PMChat && typeof global.PMChat.toggleBuddy === 'function') {
          global.PMChat.toggleBuddy(true);
        }
        return;
      }
      if (target.closest('[data-whats-new-close]')) {
        close();
      }
    });

    // Using the feature elsewhere on the page answers the card too.
    if (answeredBy) {
      document.addEventListener('click', function (event) {
        var target = event.target;
        if (!card.hidden && target && typeof target.closest === 'function' && target.closest(answeredBy)) {
          close();
        }
      });
    }

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

    // The card sits where the profile menu opens; step aside while any dropdown is open.
    document.addEventListener('shown.bs.dropdown', function () {
      card.classList.add('is-stepped-aside');
    });
    document.addEventListener('hidden.bs.dropdown', function () {
      card.classList.remove('is-stepped-aside');
    });
  }

  // The first card still to be answered, or null. Cards already answered, used or shown three
  // times are marked done on the way.
  function nextCard(cards) {
    for (var i = 0; i < cards.length; i++) {
      var card = cards[i];
      var key = card.getAttribute('data-whats-new');
      var state = read(key);
      if (state === undefined) return null;
      if (state === DONE) continue;
      if (featureUsed(card) || (parseInt(state, 10) || 0) >= MAX_SHOWS) {
        write(key, DONE);
        continue;
      }
      return card;
    }
    return null;
  }

  // A card was answered less than a day ago: the next one waits.
  function tooSoon() {
    var last = parseInt(read(LAST_ANSWERED_KEY), 10);
    return last > 0 && Date.now() - last < GAP_MS;
  }

  function init() {
    if (tooSoon()) return;
    var cards = Array.prototype.slice.call(document.querySelectorAll('[data-whats-new]'));
    var card = nextCard(cards);
    if (!card) return;
    var key = card.getAttribute('data-whats-new');
    var shows = parseInt(read(key), 10) || 0;

    setup(card);
    global.setTimeout(function () {
      // Answered during the delay (the feature itself, another tab): stay away.
      if (read(key) === DONE || featureUsed(card)) return;
      // Busy now: try again on a later page.
      if (isBusy()) return;
      if (!write(key, String(shows + 1))) return;
      card.hidden = false;
    }, SHOW_DELAY_MS);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})(window);
