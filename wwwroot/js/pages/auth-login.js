// =============================
// SIGN-IN PAGE
// Two small helpers for /Identity/Account/Login:
//  - a Caps Lock hint under the password field;
//  - a busy state on the submit button so a slow sign-in is not submitted twice.
// The form works without this script; it only adds feedback.
// =============================
(function () {
  'use strict';

  var form = document.querySelector('[data-auth-form]');
  if (!form) {
    return;
  }

  var submit = form.querySelector('[data-auth-submit]');
  var capsHint = form.querySelector('[data-caps-lock-hint]');
  var password = form.querySelector('input[type="password"]');

  // SECTION: Caps Lock hint (only while the password field has focus).
  function updateCaps(event) {
    if (!capsHint || typeof event.getModifierState !== 'function') {
      return;
    }
    capsHint.hidden = !event.getModifierState('CapsLock');
  }

  if (password && capsHint) {
    password.addEventListener('keydown', updateCaps);
    password.addEventListener('keyup', updateCaps);
    password.addEventListener('blur', function () {
      capsHint.hidden = true;
    });
  }

  // SECTION: Busy state. jQuery Validate cancels an invalid submit with preventDefault, so
  // the check waits a tick and only marks the button busy for a submit that goes ahead.
  function setBusy(busy) {
    if (!submit) {
      return;
    }
    submit.classList.toggle('is-busy', busy);
    submit.setAttribute('aria-disabled', busy ? 'true' : 'false');
  }

  form.addEventListener('submit', function (event) {
    if (submit && submit.classList.contains('is-busy')) {
      event.preventDefault();
      return;
    }
    window.setTimeout(function () {
      if (!event.defaultPrevented) {
        setBusy(true);
      }
    }, 0);
  });

  // Back/forward cache restores the page as it was left; clear the busy state.
  window.addEventListener('pageshow', function () {
    setBusy(false);
  });
})();
