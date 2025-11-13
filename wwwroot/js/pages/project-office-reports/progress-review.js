(function () {
  'use strict';

  // =========================================================
  // SECTION: DOM helpers
  // =========================================================
  const ready = (fn) => {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', fn, { once: true });
      return;
    }
    fn();
  };

  const toIsoDate = (date) => {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  };

  // =========================================================
  // SECTION: Filter defaults
  // =========================================================
  const ensureDefaultDates = (form) => {
    const fromInput = form.querySelector('#from');
    const toInput = form.querySelector('#to');
    if (!fromInput || !toInput) {
      return;
    }

    const hasFrom = Boolean(fromInput.value);
    const hasTo = Boolean(toInput.value);

    if (hasFrom && hasTo) {
      return;
    }

    const today = new Date();
    const start = new Date(today);
    start.setDate(start.getDate() - 29);

    if (!hasFrom) {
      fromInput.value = toIsoDate(start);
    }

    if (!hasTo) {
      toInput.value = toIsoDate(today);
    }
  };

  // =========================================================
  // SECTION: Quick action bindings
  // =========================================================
  const bindExportButton = () => {
    const button = document.querySelector('[data-action="export-pdf"]');
    if (!button) {
      return;
    }

    button.addEventListener('click', () => {
      window.print();
    });
  };

  const copyToClipboard = async (text) => {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      await navigator.clipboard.writeText(text);
      return;
    }

    const tempInput = document.createElement('textarea');
    tempInput.value = text;
    tempInput.setAttribute('readonly', '');
    tempInput.style.position = 'absolute';
    tempInput.style.left = '-9999px';
    document.body.appendChild(tempInput);
    tempInput.select();
    document.execCommand('copy');
    document.body.removeChild(tempInput);
  };

  const bindCopyLinkButton = () => {
    const button = document.querySelector('[data-action="copy-link"]');
    if (!button) {
      return;
    }

    const form = document.getElementById('progress-review-filter');
    let feedbackTimer;
    const provideFeedback = () => {
      button.classList.add('is-copied');
      if (feedbackTimer) {
        window.clearTimeout(feedbackTimer);
      }
      feedbackTimer = window.setTimeout(() => {
        button.classList.remove('is-copied');
      }, 1200);
    };

    const buildUrl = () => {
      const url = new URL(window.location.href);
      const fromInput = form?.querySelector('#from');
      const toInput = form?.querySelector('#to');

      if (fromInput && fromInput.value) {
        url.searchParams.set('from', fromInput.value);
      } else {
        url.searchParams.delete('from');
      }

      if (toInput && toInput.value) {
        url.searchParams.set('to', toInput.value);
      } else {
        url.searchParams.delete('to');
      }

      return url.toString();
    };

    button.addEventListener('click', async () => {
      const shareUrl = buildUrl();

      try {
        await copyToClipboard(shareUrl);
        provideFeedback();
      } catch (error) {
        console.warn('Unable to copy link', error);
      }
    });
  };

  ready(() => {
    const form = document.getElementById('progress-review-filter');
    if (!form) {
      return;
    }

    ensureDefaultDates(form);
    bindExportButton();
    bindCopyLinkButton();
  });
})();
