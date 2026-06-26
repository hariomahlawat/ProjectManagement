(function () {
  function initAutoSubmit() {
    document.querySelectorAll('select[data-auto-submit], input[data-auto-submit]').forEach((element) => {
      if (element.dataset.autoSubmitBound === 'true') {
        return;
      }

      const configuredEvent = element.getAttribute('data-auto-submit');
      const eventName = configuredEvent && configuredEvent !== 'true' ? configuredEvent : 'change';
      element.dataset.autoSubmitBound = 'true';
      element.addEventListener(eventName, () => {
        if (element.form) {
          element.form.requestSubmit ? element.form.requestSubmit() : element.form.submit();
        }
      });
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initAutoSubmit);
  } else {
    initAutoSubmit();
  }
})();
