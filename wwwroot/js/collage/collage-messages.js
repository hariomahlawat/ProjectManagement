// PRISM Collage Studio — messages: the live line for screen readers, alerts in the toast stack, and the busy line
// Moved out of the editor in docs/ux/collage-review-3-fixes.md, PR 11; collage-app.js creates it
// once, handing over the elements it writes to.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageMessages = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createMessages(deps) {
    const { $, announcer, alertBox, busyBox, views, obstacles = [] } = deps;
    function announce(text) {
      announcer.textContent = '';
      window.setTimeout(() => { announcer.textContent = text; }, 30);
    }

    /**
     * A message the user should read: shown in PRISM's toast stack with the Undo offers (one place at
     * the bottom, never over Preview), gone after a few seconds or at the user's next change. The
     * text also stays in the page's own alert line, which shows only when the toasts are missing.
     */
    const ALERT_LIFE = 9000;
    let alertToast = null;
    let alertShown = 0;

    /**
     * `action` is an optional { label, run }: one button on the toast that puts the problem right
     * (docs/ux/collage-usability-plan.md, PR 3). Without the toasts the text alone is shown.
     */
    function showAlert(text, { action = null } = {}) {
      alertToast?.dismiss();
      alertToast = null;
      $('[data-alert-text]').textContent = text;
      alertShown = text ? Date.now() : 0;
      if (text && window.PrismToast) {
        alertBox.hidden = true;
        const toast = window.PrismToast.show({ tone: 'warning', text, life: ALERT_LIFE, ...(action ? { action } : {}) });
        alertToast = toast;
        // The toast decides when it goes (it waits while the tab is hidden or someone reads it); the
        // page's own text follows it.
        const host = toast.element?.parentNode;
        if (host) {
          const watch = new MutationObserver(() => {
            if (toast.element.isConnected) return;
            watch.disconnect();
            if (alertToast === toast) clearAlert();
          });
          watch.observe(host, { childList: true });
        }
        return;
      }
      alertBox.hidden = !text;
    }

    /** The message is done with: the user has moved on, or it has faded. */
    function clearAlert() {
      if (!alertShown) return;
      alertToast?.dismiss();
      alertToast = null;
      alertShown = 0;
      $('[data-alert-text]').textContent = '';
      alertBox.hidden = true;
    }

    /** The user's next change ends a message shown before it (not one shown by this change). */
    function changeMadeAfterAlert() {
      if (alertShown && Date.now() - alertShown > 400) clearAlert();
    }

    function busy(text) {
      busyBox.hidden = !text;
      if (text) $('[data-busy-text]').textContent = text;
    }

    // ---------------------------------------------------------------------------
    // PRISM's toast stack in the editor (docs/ux/collage-review-4-fixes.md, PR 3)
    // ---------------------------------------------------------------------------

    /**
     * The stack sits at the bottom centre of every PRISM page. In the editor it is lifted, by the
     * CSS variable --clg-toast-lift on the body (collage.css), above whatever of `obstacles` (the
     * selected item's toolbar, the phone's bottom bar) is in its way, so those stay usable.
     */
    const TOAST_GAP = 8;
    const TOAST_PEEK = 10;
    let toastLift = 0;
    const toastStack = () => document.querySelector('[data-prism-toasts]');

    function keepToastsClear() {
      const stack = toastStack();
      let lift = 0;
      if (stack && views && !views.editor.hidden) {
        const live = stack.querySelectorAll('[data-prism-toast]:not(.is-leaving)').length;
        const rect = stack.getBoundingClientRect();
        if (live && rect.height) {
          // Where the stack is without a lift; the toasts behind the front one peek out above it.
          const box = {
            left: rect.left,
            right: rect.right,
            top: rect.top + toastLift - Math.min(live - 1, 2) * TOAST_PEEK,
            bottom: rect.bottom + toastLift
          };
          const half = window.innerHeight / 2;
          const blocks = obstacles
            .filter((element) => element && !element.hidden && element.getClientRects().length)
            .map((element) => element.getBoundingClientRect())
            // Only what is in the lower half: the stack never climbs over the page's top half.
            .filter((r) => r.width && r.height && r.top > half)
            .sort((a, b) => b.bottom - a.bottom);
          for (const r of blocks) {
            const overlaps = r.left < box.right && r.right > box.left
              && r.top < box.bottom - lift + TOAST_GAP && r.bottom > box.top - lift - TOAST_GAP;
            if (overlaps) lift = Math.max(lift, box.bottom - r.top + TOAST_GAP);
          }
        }
      }
      if (lift === toastLift) return;
      toastLift = lift;
      document.body.style.setProperty('--clg-toast-lift', `${Math.round(lift)}px`);
    }

    /** While a dialog is open the toasts wait behind it, dimmed (collage.css). */
    function dimToastsUnderDialogs() {
      document.body.classList.toggle('clg-dialog-open', Boolean(document.querySelector('dialog[open]')));
    }

    /** Leaving the editor: its toasts and message go too (a deleted draft's Undo stays). */
    function clearEditorToasts() {
      clearAlert();
      const stack = toastStack();
      if (!stack || !window.PrismToast?.dismiss) return;
      for (const toast of stack.querySelectorAll('[data-prism-toast]:not([data-draft-undo])')) window.PrismToast.dismiss(toast);
    }

    if (views && typeof MutationObserver !== 'undefined') {
      let watchedStack = null;
      const watch = new MutationObserver(() => {
        const stack = toastStack();
        if (stack && stack !== watchedStack) {
          watchedStack = stack;
          watch.observe(stack, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
        }
        keepToastsClear();
      });
      // The stack is made when the first toast shows; the body's class says which view is shown.
      watch.observe(document.body, { childList: true, attributes: true, attributeFilter: ['class'] });
      for (const element of obstacles) {
        if (element) watch.observe(element, { attributes: true, attributeFilter: ['hidden', 'style', 'class'] });
      }
      window.addEventListener('resize', keepToastsClear);
      new MutationObserver(dimToastsUnderDialogs).observe(document.body, { subtree: true, attributes: true, attributeFilter: ['open'] });
      dimToastsUnderDialogs();
    }

    return { announce, showAlert, clearAlert, changeMadeAfterAlert, busy, clearEditorToasts, keepToastsClear };
  }

  return Object.freeze({ createMessages });
});
