(function () {
  const root = document.querySelector('[data-industry-partners-root]');
  if (!root) return;

  // SECTION: Anti-forgery token retrieval
  const token = document.querySelector('#industryPartnerToken input[name="__RequestVerificationToken"]')?.value;
  const statusEl = root.querySelector('[data-save-status]');
  const fields = root.querySelectorAll('[data-inline-field]');

  function setStatus(message, isError) {
    if (!statusEl) return;
    statusEl.textContent = message;
    statusEl.classList.toggle('text-danger', !!isError);
    statusEl.classList.toggle('text-success', !isError);
  }

  function debounce(fn, wait) {
    let timer;
    return function () {
      const args = arguments;
      clearTimeout(timer);
      timer = setTimeout(() => fn.apply(null, args), wait);
    };
  }

  const saveField = debounce(async (fieldEl) => {
    const partnerId = fieldEl.getAttribute('data-partner-id');
    const field = fieldEl.getAttribute('data-inline-field');
    const value = fieldEl.value;

    const body = new URLSearchParams();
    body.set('id', partnerId);
    body.set('field', field);
    body.set('value', value);

    try {
      const response = await fetch('?handler=UpdateField', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
          'RequestVerificationToken': token || ''
        },
        body: body.toString()
      });

      if (!response.ok) {
        const payload = await response.json();
        setStatus(Object.values(payload.errors || {}).flat().join(' '), true);
        return;
      }

      setStatus('Saved', false);
      setTimeout(() => setStatus('', false), 1500);
    } catch (err) {
      setStatus('Save failed. Please retry.', true);
    }
  }, 400);

  fields.forEach((field) => {
    field.addEventListener('input', function () {
      setStatus('Saving...', false);
      saveField(field);
    });
  });
})();
