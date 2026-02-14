(function () {
  // SECTION: Root wiring
  const root = document.querySelector('[data-description-preview-root]');
  if (!root) {
    return;
  }

  const previewTrigger = root.querySelector('[data-description-preview-trigger]');
  const markdownInput = root.querySelector('[data-description-markdown-input]');
  const previewContainer = root.querySelector('[data-description-preview-content]');
  const previewError = root.querySelector('[data-description-preview-error]');
  const tokenInput = root.querySelector('[data-description-preview-token] input[name="__RequestVerificationToken"]');
  const previewUrl = root.getAttribute('data-preview-url');

  if (!previewTrigger || !markdownInput || !previewContainer || !previewUrl) {
    return;
  }

  // SECTION: Preview rendering
  async function renderPreview() {
    if (previewError) {
      previewError.classList.add('d-none');
      previewError.textContent = '';
    }

    if (!tokenInput?.value) {
      previewContainer.innerHTML = '<span class="text-muted">Preview unavailable.</span>';
      if (previewError) {
        previewError.textContent = 'Preview unavailable (missing antiforgery token). Reload page.';
        previewError.classList.remove('d-none');
      }

      return;
    }

    previewContainer.textContent = 'Loading preview...';

    const body = new URLSearchParams();
    body.set('description', markdownInput.value || '');

    try {
      const response = await fetch(previewUrl, {
        method: 'POST',
        credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
          RequestVerificationToken: tokenInput?.value || ''
        },
        body: body.toString()
      });

      if (!response.ok) {
        throw new Error('Preview request failed.');
      }

      const payload = await response.json();
      const html = typeof payload.html === 'string' ? payload.html : '';
      previewContainer.innerHTML = html || '<span class="text-muted">—</span>';
    } catch (_err) {
      previewContainer.innerHTML = '<span class="text-muted">Preview unavailable.</span>';
      if (previewError) {
        previewError.textContent = 'Could not load preview. Please continue editing and try again.';
        previewError.classList.remove('d-none');
      }

      const writeTabTrigger = root.querySelector('#description-write-tab');
      if (writeTabTrigger && window.bootstrap?.Tab) {
        window.bootstrap.Tab.getOrCreateInstance(writeTabTrigger).show();
      }
    }
  }

  previewTrigger.addEventListener('shown.bs.tab', renderPreview);
})();
