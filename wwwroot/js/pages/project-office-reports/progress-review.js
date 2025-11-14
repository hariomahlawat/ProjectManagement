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
  const bindPrintButton = () => {
    const button = document.querySelector('[data-action="print"]');
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
    bindPrintButton();
  });
})();
