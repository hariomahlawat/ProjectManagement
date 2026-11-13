// =============================
// SIGN-IN PAGE
// Small helpers for /Identity/Account/Login:
//  - a greeting for the time of day;
//  - the opening sequence's traced wordmark (light writes PRISM ERP);
//  - a Caps Lock hint under the password field;
//  - a busy state on the submit button so a slow sign-in is not submitted twice (the scene
//    gets auth--sending with it, which sends a pulse down the beam).
// The form works without this script; it only adds feedback.
// =============================
(function () {
  'use strict';

  var SVG_NS = 'http://www.w3.org/2000/svg';
  var scene = document.querySelector('[data-auth-scene]');

  // SECTION: Greeting, by this computer's clock.
  var greeting = document.querySelector('[data-auth-greeting]');
  if (greeting) {
    var hour = new Date().getHours();
    greeting.textContent = hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';
  }

  // SECTION: Traced wordmark. One outlined SVG letter is laid exactly over each letter of
  // the real text; auth-login.css draws the outlines in turn, tints them and fades them out
  // as the real text fades in. Skipped on the narrow layout, under Reduce Motion, and when
  // the page was slow enough that the rest of the sequence has already moved on.
  function traceWordmark() {
    var wordmark = scene && scene.classList.contains('auth--intro')
      ? scene.querySelector('[data-auth-wordmark]')
      : null;
    var text = wordmark && wordmark.querySelector('.auth__wordmark-text');
    if (!text || !window.matchMedia || !document.createRange ||
        !window.matchMedia('(min-width: 961px) and (prefers-reduced-motion: no-preference)').matches ||
        window.performance.now() > 1300) {
      return;
    }

    var box = wordmark.getBoundingClientRect();
    var fontSize = parseFloat(window.getComputedStyle(wordmark).fontSize) || 0;
    if (!box.width || !fontSize) {
      return;
    }

    // An empty inline-block sits on the baseline, so its bottom edge is the baseline.
    var probe = document.createElement('span');
    probe.style.display = 'inline-block';
    text.insertBefore(probe, text.firstChild);
    var baseline = probe.getBoundingClientRect().bottom - box.top;
    text.removeChild(probe);

    var svg = document.createElementNS(SVG_NS, 'svg');
    svg.setAttribute('class', 'auth__trace');
    svg.setAttribute('aria-hidden', 'true');
    svg.setAttribute('focusable', 'false');

    var gradient = document.createElementNS(SVG_NS, 'linearGradient');
    gradient.setAttribute('id', 'authTraceSpectrum');
    gradient.setAttribute('gradientUnits', 'userSpaceOnUse');
    for (var s = 0; s < 6; s++) {
      var stop = document.createElementNS(SVG_NS, 'stop');
      stop.setAttribute('offset', String(s / 5));
      stop.setAttribute('class', 'lp-stop lp-stop--spec-' + (s + 1));
      gradient.appendChild(stop);
    }
    var defs = document.createElementNS(SVG_NS, 'defs');
    defs.appendChild(gradient);
    svg.appendChild(defs);

    var range = document.createRange();
    var walker = document.createTreeWalker(text, NodeFilter.SHOW_TEXT);
    var count = 0;
    var right = 0;
    var node;
    while ((node = walker.nextNode())) {
      var weight = window.getComputedStyle(node.parentElement).fontWeight;
      for (var i = 0; i < node.data.length; i++) {
        if (!node.data.charAt(i).trim()) {
          continue;
        }
        range.setStart(node, i);
        range.setEnd(node, i + 1);
        var rect = range.getBoundingClientRect();
        var letter = document.createElementNS(SVG_NS, 'text');
        letter.setAttribute('class', 'auth__trace-letter');
        letter.setAttribute('x', String(rect.left - box.left));
        letter.setAttribute('y', String(baseline));
        letter.setAttribute('font-weight', weight);
        letter.setAttribute('fill', 'url(#authTraceSpectrum)');
        letter.setAttribute('stroke', 'url(#authTraceSpectrum)');
        letter.style.setProperty('--auth-i', String(count++));
        letter.textContent = node.data.charAt(i);
        svg.appendChild(letter);
        right = Math.max(right, rect.right - box.left);
      }
    }
    if (!count) {
      return;
    }

    gradient.setAttribute('x1', '0');
    gradient.setAttribute('x2', String(right));
    // Longer than any letter's outline at this size, so each one draws from nothing.
    svg.style.setProperty('--auth-dash', String(Math.round(fontSize * 9)));

    var pen = document.createElement('span');
    pen.className = 'auth__pen';
    pen.setAttribute('aria-hidden', 'true');
    pen.style.top = (baseline - fontSize * 0.36) + 'px';
    pen.style.setProperty('--auth-trace-width', right + 'px');

    svg.addEventListener('animationend', function (event) {
      if (event.target === svg) {
        svg.remove();
        pen.remove();
      }
    });

    wordmark.appendChild(svg);
    wordmark.appendChild(pen);
    wordmark.classList.add('is-tracing');
  }

  // Letter positions are only right once Mulish has loaded.
  if (document.fonts && document.fonts.ready) {
    document.fonts.ready.then(traceWordmark);
  }

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
