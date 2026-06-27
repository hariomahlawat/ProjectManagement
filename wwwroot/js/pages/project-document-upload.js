(function () {
  function formatBytes(bytes) {
    const units = ['B', 'KB', 'MB', 'GB'];
    let value = Number(bytes) || 0;
    let index = 0;
    while (value >= 1024 && index < units.length - 1) {
      value /= 1024;
      index += 1;
    }
    return `${value.toFixed(value >= 10 || index === 0 ? 0 : 1)} ${units[index]}`;
  }

  function extensionOf(fileName) {
    const dot = (fileName || '').lastIndexOf('.');
    return dot >= 0 ? fileName.slice(dot + 1).toLowerCase() : '';
  }

  function defaultTitle(fileName) {
    const name = (fileName || 'Document')
      .replace(/\.[^.]+$/, '')
      .replace(/[_-]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
    return name || 'Document';
  }

  function init(form) {
    const input = form.querySelector('[data-document-file-input]');
    const dropzone = form.querySelector('[data-document-dropzone]');
    const selected = form.querySelector('[data-document-selected]');
    const list = form.querySelector('[data-document-selected-list]');
    const count = form.querySelector('[data-document-selected-count]');
    const addMore = form.querySelector('[data-document-add-more]');
    const error = form.querySelector('[data-document-file-error]');
    const submit = form.querySelector('[data-document-submit]');
    if (!(input instanceof HTMLInputElement) || !dropzone || !selected || !list || !(submit instanceof HTMLButtonElement)) return;

    const maxSize = Number.parseInt(form.dataset.maxFileSize || '0', 10);
    const allowedTypes = new Set((form.dataset.allowedTypes || '').split(',').map((v) => v.trim().toLowerCase()).filter(Boolean));
    const allowedExtensions = new Set((form.dataset.allowedExtensions || '').split(',').map((v) => v.trim().replace(/^\./, '').toLowerCase()).filter(Boolean));
    let items = [];
    let dragDepth = 0;

    function showError(message) {
      if (!(error instanceof HTMLElement)) return;
      error.textContent = message || '';
      error.hidden = !message;
    }

    function validate(file) {
      if (!file || file.size <= 0) return 'One of the selected files is empty.';
      const ext = extensionOf(file.name);
      const type = (file.type || '').toLowerCase();
      if (allowedExtensions.size && !allowedExtensions.has(ext)) return `${file.name} is not a supported document.`;
      if (allowedTypes.size && type && type !== 'application/octet-stream' && !allowedTypes.has(type)) return `${file.name} is not a supported document.`;
      if (maxSize > 0 && file.size > maxSize) return `${file.name} is ${formatBytes(file.size)}. The maximum is ${formatBytes(maxSize)}.`;
      return '';
    }

    function syncInput() {
      if (typeof DataTransfer !== 'function') return;
      const transfer = new DataTransfer();
      items.forEach((item) => transfer.items.add(item.file));
      input.files = transfer.files;
    }

    function render() {
      list.innerHTML = '';
      items.forEach((item, index) => {
        const row = document.createElement('div');
        row.className = 'project-document-file';

        const icon = document.createElement('span');
        icon.className = 'project-document-file__icon';
        icon.setAttribute('aria-hidden', 'true');
        icon.innerHTML = '<i class="bi bi-file-earmark-text"></i>';

        const body = document.createElement('div');
        body.className = 'project-document-file__body';

        const label = document.createElement('label');
        label.className = 'visually-hidden';
        label.htmlFor = `document-title-${index}`;
        label.textContent = `Document title for ${item.file.name}`;

        const title = document.createElement('input');
        title.id = `document-title-${index}`;
        title.type = 'text';
        title.className = 'form-control form-control-sm project-document-file__title';
        title.name = `Input.FileTitles[${index}]`;
        title.maxLength = 200;
        title.value = item.title;
        title.placeholder = 'Document title';
        title.addEventListener('input', () => {
          item.title = title.value;
        });

        const meta = document.createElement('div');
        meta.className = 'project-document-file__name';
        meta.textContent = `${item.file.name} · ${formatBytes(item.file.size)}`;
        body.append(label, title, meta);

        const remove = document.createElement('button');
        remove.type = 'button';
        remove.className = 'btn btn-sm btn-outline-danger project-document-file__remove';
        remove.innerHTML = '<i class="bi bi-x-lg" aria-hidden="true"></i> Remove';
        remove.setAttribute('aria-label', `Remove ${item.file.name}`);
        remove.addEventListener('click', () => {
          items.splice(index, 1);
          syncInput();
          render();
        });

        row.append(icon, body, remove);
        list.appendChild(row);
      });

      selected.hidden = items.length === 0;
      dropzone.hidden = items.length > 0;
      if (count) count.textContent = items.length === 1 ? '1 file selected' : `${items.length} files selected`;
      submit.disabled = items.length === 0;
    }

    function addFiles(fileList) {
      const errors = [];
      for (const file of Array.from(fileList || [])) {
        const message = validate(file);
        if (message) {
          errors.push(message);
          continue;
        }

        const duplicate = items.some((item) =>
          item.file.name === file.name &&
          item.file.size === file.size &&
          item.file.lastModified === file.lastModified);
        if (!duplicate) {
          items.push({ file, title: defaultTitle(file.name) });
        }
      }

      showError(errors.join(' '));
      syncInput();
      render();
    }

    dropzone.addEventListener('click', () => input.click());
    dropzone.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        input.click();
      }
    });
    addMore?.addEventListener('click', () => input.click());
    input.addEventListener('change', () => addFiles(input.files));

    ['dragenter', 'dragover'].forEach((name) => {
      dropzone.addEventListener(name, (event) => {
        event.preventDefault();
        if (name === 'dragenter') dragDepth += 1;
        dropzone.classList.add('is-dragover');
      });
    });
    dropzone.addEventListener('dragleave', () => {
      dragDepth = Math.max(0, dragDepth - 1);
      if (!dragDepth) dropzone.classList.remove('is-dragover');
    });
    dropzone.addEventListener('drop', (event) => {
      event.preventDefault();
      dragDepth = 0;
      dropzone.classList.remove('is-dragover');
      addFiles(event.dataTransfer?.files);
    });

    form.addEventListener('submit', (event) => {
      if (!items.length && !(input.files && input.files.length)) {
        event.preventDefault();
        showError('Choose at least one document.');
        dropzone.hidden = false;
        dropzone.focus();
        return;
      }

      for (const item of items) {
        if (!item.title.trim()) {
          item.title = defaultTitle(item.file.name);
        }
      }

      submit.disabled = true;
      submit.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Submitting…';
    });

    submit.disabled = true;
  }

  document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('[data-document-upload-form]').forEach(init);
  });
})();
