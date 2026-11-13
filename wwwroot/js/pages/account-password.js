// =============================
// CHANGE PASSWORD PAGE
// Shows a Caps Lock hint while any of the password fields has focus.
// The form works without this script; it only adds feedback.
// =============================
(function () {
  'use strict';

  var form = document.querySelector('[data-account-password-form]');
  var hint = form && form.querySelector('[data-caps-lock-hint]');
  if (!hint) {
    return;
  }

  function update(event) {
    if (typeof event.getModifierState === 'function') {
      hint.hidden = !event.getModifierState('CapsLock');
    }
  }

  Array.prototype.forEach.call(form.querySelectorAll('.password-container input'), function (input) {
    input.addEventListener('keydown', update);
    input.addEventListener('keyup', update);
    input.addEventListener('blur', function () {
      hint.hidden = true;
    });
  });
})();
