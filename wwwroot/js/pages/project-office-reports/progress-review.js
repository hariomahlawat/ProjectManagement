(function () {
  'use strict';

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

  const bindCopyLink = (form) => {
    const button = form.querySelector('[data-action="copy-link"]');
    if (!button) {
      return;
    }

    button.addEventListener('click', async () => {
      const url = new URL(window.location.href);
      const from = form.querySelector('#from');
      const to = form.querySelector('#to');
      if (from && from.value) {
        url.searchParams.set('from', from.value);
      }
      if (to && to.value) {
        url.searchParams.set('to', to.value);
      }

      try {
        await navigator.clipboard.writeText(url.toString());
        button.classList.remove('btn-outline-secondary');
        button.classList.add('btn-success');
        setTimeout(() => {
          button.classList.remove('btn-success');
          button.classList.add('btn-outline-secondary');
        }, 1200);
      }
      catch (error) {
        console.warn('Copy failed', error);
      }
    });
  };

  const bindExportButton = () => {
    const button = document.querySelector('[data-action="export-pdf"]');
    if (!button) {
      return;
    }

    button.addEventListener('click', () => {
      window.print();
    });
  };

  ready(() => {
    const form = document.getElementById('progress-review-filter');
    if (!form) {
      return;
    }

    ensureDefaultDates(form);
    bindCopyLink(form);
    bindExportButton();
  });
})();
