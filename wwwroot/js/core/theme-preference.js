// =============================
// THEME PREFERENCE (Light / Dark / System)
// Loaded synchronously in <head>, before the stylesheets paint, so the chosen theme is on
// <html> before first paint. Light is the default and leaves <html> without data-bs-theme,
// exactly as before the dark theme existed. The choice is per browser (localStorage).
// Controls: any element with data-theme-preference-value="light|dark|system".
// =============================
(function (global) {
  'use strict';

  var STORAGE_KEY = 'prism.theme';
  var DEFAULT_PREFERENCE = 'light';
  var PREFERENCES = ['light', 'dark', 'system'];
  var root = global.document.documentElement;
  var darkQuery = typeof global.matchMedia === 'function'
    ? global.matchMedia('(prefers-color-scheme: dark)')
    : null;
  var printing = false;

  function normalise(value) {
    return PREFERENCES.indexOf(value) >= 0 ? value : DEFAULT_PREFERENCE;
  }

  // Storage can be unavailable (private windows, blocked site data); fall back to Light.
  function readPreference() {
    try {
      return normalise(global.localStorage.getItem(STORAGE_KEY));
    } catch (e) {
      return DEFAULT_PREFERENCE;
    }
  }

  function writePreference(preference) {
    try {
      global.localStorage.setItem(STORAGE_KEY, preference);
    } catch (e) {
      // The choice still applies to this page; it just will not persist.
    }
  }

  function resolve(preference) {
    if (preference === 'system') {
      return darkQuery && darkQuery.matches ? 'dark' : 'light';
    }
    return preference;
  }

  function syncControls(preference) {
    var controls = global.document.querySelectorAll('[data-theme-preference-value]');
    for (var i = 0; i < controls.length; i++) {
      var selected = controls[i].getAttribute('data-theme-preference-value') === preference;
      controls[i].setAttribute('aria-pressed', selected ? 'true' : 'false');
      controls[i].classList.toggle('active', selected);
    }
  }

  function apply(preference) {
    // Printouts stay light whatever the screen theme is.
    if (resolve(preference) === 'dark' && !printing) {
      root.setAttribute('data-bs-theme', 'dark');
    } else {
      root.removeAttribute('data-bs-theme');
    }
    syncControls(preference);
  }

  function setPreference(value) {
    var preference = normalise(value);
    writePreference(preference);
    apply(preference);
    return preference;
  }

  apply(readPreference());

  global.document.addEventListener('click', function (event) {
    var target = event.target;
    var control = target && typeof target.closest === 'function'
      ? target.closest('[data-theme-preference-value]')
      : null;
    if (!control) {
      return;
    }
    event.preventDefault();
    setPreference(control.getAttribute('data-theme-preference-value'));
  });

  global.document.addEventListener('DOMContentLoaded', function () {
    syncControls(readPreference());
  });

  // Another tab changed the choice.
  global.addEventListener('storage', function (event) {
    if (event.key === STORAGE_KEY) {
      apply(readPreference());
    }
  });

  if (darkQuery) {
    var onSystemChange = function () {
      if (readPreference() === 'system') {
        apply('system');
      }
    };
    if (typeof darkQuery.addEventListener === 'function') {
      darkQuery.addEventListener('change', onSystemChange);
    } else if (typeof darkQuery.addListener === 'function') {
      darkQuery.addListener(onSystemChange);
    }
  }

  global.addEventListener('beforeprint', function () {
    printing = true;
    apply(readPreference());
  });
  global.addEventListener('afterprint', function () {
    printing = false;
    apply(readPreference());
  });

  global.PMThemePreference = {
    get: readPreference,
    set: setPreference,
    resolved: function () { return resolve(readPreference()); }
  };
})(window);
