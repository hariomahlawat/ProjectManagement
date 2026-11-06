// =============================
// ABOUT PRISM (Pages/Developer.cshtml)
// Copy buttons ([data-about-copy]): copy the text, flip the icon to a tick and announce it.
// PRISM is often opened over plain http on the LAN, where navigator.clipboard does not exist
// (it needs a secure context), so fall back to a hidden textarea and execCommand('copy').
// =============================
(function () {
  'use strict';

  var toast = document.querySelector('[data-about-toast]');
  var toastTimer = 0;

  function announce(message) {
    if (!toast) {
      return;
    }
    toast.textContent = message;
    toast.classList.add('is-visible');
    window.clearTimeout(toastTimer);
    toastTimer = window.setTimeout(function () {
      toast.classList.remove('is-visible');
    }, 2200);
  }

  function copyWithTextarea(text) {
    var area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.top = '-1000px';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    var ok = false;
    try {
      ok = document.execCommand('copy');
    } catch (e) {
      ok = false;
    }
    document.body.removeChild(area);
    return ok;
  }

  function copy(text) {
    if (navigator.clipboard && window.isSecureContext) {
      return navigator.clipboard.writeText(text).then(
        function () { return true; },
        function () { return copyWithTextarea(text); }
      );
    }
    return Promise.resolve(copyWithTextarea(text));
  }

  // Scroll reveal: lower sections rise in as they enter the view. Without this script, or
  // with Reduce Motion, everything is simply shown.
  var root = document.querySelector('.about');
  var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (root && !reduceMotion && 'IntersectionObserver' in window) {
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-in');
          observer.unobserve(entry.target);
        }
      });
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.12 });
    Array.prototype.forEach.call(root.querySelectorAll('.about-reveal'), function (el) {
      observer.observe(el);
    });
    root.classList.add('is-ready');
  }

  var buttons = document.querySelectorAll('[data-about-copy]');
  Array.prototype.forEach.call(buttons, function (button) {
    var resetTimer = 0;
    button.addEventListener('click', function () {
      var text = button.getAttribute('data-about-copy') || '';
      copy(text).then(function (ok) {
        if (!ok) {
          announce('Could not copy. Select the text and press Ctrl+C.');
          return;
        }
        button.classList.add('is-copied');
        announce(button.getAttribute('data-about-copied') || 'Copied');
        window.clearTimeout(resetTimer);
        resetTimer = window.setTimeout(function () {
          button.classList.remove('is-copied');
        }, 1800);
      });
    });
  });
})();
