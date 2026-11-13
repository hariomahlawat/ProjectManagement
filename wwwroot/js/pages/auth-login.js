// =============================
// SIGN-IN PAGE
// Small helpers for /Identity/Account/Login:
//  - a greeting for the time of day;
//  - the point of light on the pen tip while the name is written in the opening sequence;
//  - a Caps Lock hint under the password field;
//  - a busy state on the submit button so a slow sign-in is not submitted twice (the scene
//    gets auth--sending with it, which sends a pulse down the beam).
// The form works without this script; it only adds feedback.
// =============================
(function () {
  'use strict';

  var scene = document.querySelector('[data-auth-scene]');

  // SECTION: Greeting, by this computer's clock.
  var greeting = document.querySelector('[data-auth-greeting]');
  if (greeting) {
    var hour = new Date().getHours();
    greeting.textContent = hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';
  }

  // SECTION: Pen light. auth-login.css writes the name by drawing each pen path in turn
  // (Pages/Shared/_PrismSignature.cshtml). This reads how far the stroke being drawn has got
  // from its own animation and puts the point of light there, so the two cannot drift. The
  // writing itself needs no script.
  function followPen() {
    var pen = scene && scene.classList.contains('auth--intro')
      ? scene.querySelector('.auth__sig-pen')
      : null;
    if (!pen || !window.matchMedia || !pen.getAnimations ||
        !window.matchMedia('(min-width: 961px) and (prefers-reduced-motion: no-preference)').matches) {
      return;
    }

    var runs = [];
    Array.prototype.forEach.call(scene.querySelectorAll('.auth__sig-stroke'), function (stroke) {
      var animation = stroke.getAnimations()[0];
      if (animation && animation.effect) {
        runs.push({ stroke: stroke, length: stroke.getTotalLength(), animation: animation });
      }
    });

    function frame() {
      var waiting = false;
      var drawing = null;
      runs.forEach(function (run) {
        var timing = run.animation.effect.getComputedTiming();
        var now = run.animation.currentTime;
        if (now === null || now >= timing.endTime) {
          return;
        }
        waiting = true;
        if (!drawing && now >= timing.delay && timing.progress !== null) {
          drawing = { run: run, progress: timing.progress };
        }
      });

      if (drawing) {
        var point = drawing.run.stroke.getPointAtLength(drawing.run.length * drawing.progress);
        pen.setAttribute('cx', String(point.x));
        pen.setAttribute('cy', String(point.y));
      }
      pen.style.opacity = drawing ? '1' : '0';

      if (waiting) {
        window.requestAnimationFrame(frame);
      }
    }

    if (runs.length) {
      window.requestAnimationFrame(frame);
    }
  }

  followPen();

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
    if (scene) {
      scene.classList.toggle('auth--sending', busy);
    }
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
