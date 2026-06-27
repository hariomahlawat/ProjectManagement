(function () {
  function formatBytes(bytes) {
    if (!Number.isFinite(bytes) || bytes <= 0) return '0 bytes';
    const units = ['bytes', 'KB', 'MB', 'GB'];
    let value = bytes;
    let index = 0;
    while (value >= 1024 && index < units.length - 1) {
      value /= 1024;
      index += 1;
    }
    return index === 0 ? `${bytes} ${units[index]}` : `${value.toFixed(value >= 10 ? 0 : 1)} ${units[index]}`;
  }

  function readImageDimensions(file) {
    if ('createImageBitmap' in window) {
      return window.createImageBitmap(file).then((bitmap) => {
        const dimensions = { width: bitmap.width, height: bitmap.height };
        if (typeof bitmap.close === 'function') bitmap.close();
        return dimensions;
      });
    }

    return new Promise((resolve, reject) => {
      const objectUrl = URL.createObjectURL(file);
      const image = new Image();
      image.addEventListener('load', () => {
        const dimensions = { width: image.naturalWidth, height: image.naturalHeight };
        URL.revokeObjectURL(objectUrl);
        resolve(dimensions);
      }, { once: true });
      image.addEventListener('error', () => {
        URL.revokeObjectURL(objectUrl);
        reject(new Error('The selected image could not be read.'));
      }, { once: true });
      image.src = objectUrl;
    });
  }

  function setInputFile(input, file) {
    if (typeof DataTransfer !== 'function') {
      return false;
    }

    try {
      const transfer = new DataTransfer();
      transfer.items.add(file);
      input.files = transfer.files;
      input.dispatchEvent(new Event('change', { bubbles: true }));
      return true;
    } catch (_) {
      return false;
    }
  }

  function initPhotoUpload(form) {
    const input = form.querySelector('[data-photo-file-input]');
    const dropzone = form.querySelector('[data-photo-dropzone]');
    const summary = form.querySelector('[data-photo-file-summary]');
    const nameElement = form.querySelector('[data-photo-file-name]');
    const metaElement = form.querySelector('[data-photo-file-meta]');
    const removeButton = form.querySelector('[data-photo-file-remove]');
    const errorElement = form.querySelector('[data-photo-file-error]');
    const submitButton = form.querySelector('[data-photo-submit]');
    const coverToggle = form.querySelector('[data-photo-cover-toggle]');
    const coverWarning = form.querySelector('[data-photo-cover-warning]');
    const editor = form.querySelector('[data-photo-editor]');

    if (!input || !dropzone || !summary || !nameElement || !metaElement || !submitButton) {
      return;
    }

    const maxFileSize = Number.parseInt(form.dataset.maxFileSize || '0', 10);
    const fileRequired = form.dataset.fileRequired !== 'false';
    const allowedTypes = (form.dataset.allowedContentTypes || '')
      .split(',')
      .map((value) => value.trim().toLowerCase())
      .filter(Boolean);
    let selectionSequence = 0;
    let dragDepth = 0;

    function showError(message) {
      if (!errorElement) return;
      errorElement.textContent = message;
      errorElement.hidden = !message;
    }

    function updateCoverWarning() {
      if (!coverWarning || !coverToggle) return;
      coverWarning.hidden = !coverToggle.checked;
    }

    function resetSummary() {
      summary.hidden = true;
      nameElement.textContent = 'Selected photo';
      metaElement.textContent = '';
      submitButton.disabled = fileRequired;
      dropzone.hidden = false;
      form.classList.remove('has-selected-photo');
    }

    function clearInput() {
      selectionSequence += 1;
      input.value = '';
      resetSummary();
      showError('');
      input.dispatchEvent(new CustomEvent('project-photo-file-cleared', { bubbles: true }));
    }

    function validateFile(file) {
      if (!file) return fileRequired ? 'Choose a photo to continue.' : '';
      const type = (file.type || '').toLowerCase();
      const hasSupportedExtension = /\.(jpe?g|png|webp)$/i.test(file.name || '');
      if (type && !type.startsWith('image/')) return 'Choose a valid image file.';
      if (!type && !hasSupportedExtension) return 'Choose a valid JPEG, PNG or WebP image.';
      if (allowedTypes.length && type && !allowedTypes.includes(type)) {
        return 'This image format is not supported. Choose a JPEG, PNG or WebP file.';
      }
      if (maxFileSize > 0 && file.size > maxFileSize) {
        return `This file is ${formatBytes(file.size)}. The maximum permitted size is ${formatBytes(maxFileSize)}.`;
      }
      return '';
    }

    async function processSelection() {
      const [file] = input.files || [];
      const currentSequence = ++selectionSequence;
      const validationMessage = validateFile(file);
      if (validationMessage) {
        if (file) {
          input.value = '';
          input.dispatchEvent(new CustomEvent('project-photo-file-cleared', { bubbles: true }));
        }
        resetSummary();
        showError(validationMessage);
        return;
      }

      showError('');
      nameElement.textContent = file.name;
      metaElement.textContent = `${formatBytes(file.size)} · Checking image dimensions…`;
      summary.hidden = false;
      dropzone.hidden = true;
      form.classList.add('has-selected-photo');
      submitButton.disabled = false;

      try {
        const dimensions = await readImageDimensions(file);
        if (currentSequence !== selectionSequence) return;
        if (!dimensions.width || !dimensions.height) throw new Error('Invalid dimensions');
        metaElement.textContent = `${formatBytes(file.size)} · ${dimensions.width} × ${dimensions.height} px`;
      } catch (_) {
        if (currentSequence !== selectionSequence) return;
        input.value = '';
        resetSummary();
        showError('The selected image could not be decoded. Choose another JPEG, PNG or WebP file.');
        input.dispatchEvent(new CustomEvent('project-photo-file-cleared', { bubbles: true }));
      }
    }

    input.addEventListener('change', processSelection);
    dropzone.addEventListener('keydown', (event) => {
      if (event.key !== 'Enter' && event.key !== ' ') return;
      event.preventDefault();
      input.click();
    });
    removeButton?.addEventListener('click', clearInput);
    coverToggle?.addEventListener('change', updateCoverWarning);
    updateCoverWarning();

    ['dragenter', 'dragover'].forEach((eventName) => {
      dropzone.addEventListener(eventName, (event) => {
        event.preventDefault();
        if (eventName === 'dragenter') dragDepth += 1;
        dropzone.classList.add('is-dragover');
        if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
      });
    });

    dropzone.addEventListener('dragleave', (event) => {
      event.preventDefault();
      dragDepth = Math.max(0, dragDepth - 1);
      if (dragDepth === 0) dropzone.classList.remove('is-dragover');
    });

    dropzone.addEventListener('drop', (event) => {
      event.preventDefault();
      dragDepth = 0;
      dropzone.classList.remove('is-dragover');
      const files = Array.from(event.dataTransfer?.files || []);
      const file = files.find((candidate) => (candidate.type || '').startsWith('image/')) || files[0];
      if (!file) {
        showError('No file was detected. Choose an image from this device.');
        return;
      }
      if (!setInputFile(input, file)) {
        showError('This browser cannot place a dropped file into the upload field. Use “choose a file” instead.');
      }
    });

    editor?.addEventListener('project-photo-editor:error', (event) => {
      const message = event.detail?.message || 'The selected image could not be previewed.';
      const hadSelectedFile = Boolean(input.files && input.files.length);
      input.value = '';
      resetSummary();
      showError(message);
      if (hadSelectedFile) {
        input.dispatchEvent(new CustomEvent('project-photo-file-cleared', { bubbles: true }));
      }
    });

    form.addEventListener('submit', (event) => {
      const [file] = input.files || [];
      const validationMessage = validateFile(file);
      if (validationMessage) {
        event.preventDefault();
        showError(validationMessage);
        dropzone.hidden = false;
        dropzone.focus({ preventScroll: true });
        return;
      }

      submitButton.disabled = true;
      submitButton.classList.add('is-busy');
      const busyLabel = submitButton.dataset.busyLabel || 'Saving…';
      submitButton.innerHTML = `<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> ${busyLabel}`;
    });

    resetSummary();
  }

  document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('[data-photo-upload-form]').forEach(initPhotoUpload);
  });
})();
