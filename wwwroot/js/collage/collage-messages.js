// PRISM Collage Studio — messages: the live line for screen readers, alerts in the toast stack, and the busy line
// Moved out of the editor in docs/ux/collage-review-3-fixes.md, PR 11; collage-app.js creates it
// once, handing over the helpers it uses, `state` (the editor's values, read only) and the parts
// made later (collage-state.js).
(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollageMessages = api;
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  function createMessages(deps) {
    const { $, announcer, alertBox, busyBox } = deps;
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

    function showAlert(text) {
      alertToast?.dismiss();
      alertToast = null;
      $('[data-alert-text]').textContent = text;
      alertShown = text ? Date.now() : 0;
      if (text && window.PrismToast) {
        alertBox.hidden = true;
        const toast = window.PrismToast.show({ tone: 'warning', text, life: ALERT_LIFE });
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


    return { announce, showAlert, clearAlert, changeMadeAfterAlert, busy };
  }

  return Object.freeze({ createMessages });
});
