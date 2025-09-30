(function () {
  const ASPECT_RATIO = 4 / 3;
  const DEFAULT_SIZES = {
    xl: { width: 1600, height: 1200 },
    md: { width: 1200, height: 900 },
    sm: { width: 800, height: 600 }
  };

  function parseSize(element) {
    if (!element) {
      return null;
    }

    const sizeKey = element.getAttribute('data-photo-editor-preview');
    const width = Number.parseInt(element.getAttribute('data-width') || '', 10);
    const height = Number.parseInt(element.getAttribute('data-height') || '', 10);

    if (Number.isNaN(width) || Number.isNaN(height) || width <= 0 || height <= 0) {
      return DEFAULT_SIZES[sizeKey] || null;
    }

    return { width, height };
  }

  function clamp(value, min, max) {
    return Math.min(Math.max(value, min), max);
  }

  function formatNumber(value) {
    return Number.isFinite(value) ? String(Math.round(value)) : '';
  }

  function initPhotoEditor(editor) {
    if (!editor) {
      return;
    }

    const cropperConstructor = window.Cropper;
    if (typeof cropperConstructor !== 'function') {
      editor.classList.add('project-photo-editor--unsupported');
      return;
    }

    const inputSelector = editor.getAttribute('data-photo-editor-input');
    const fileInput = inputSelector ? document.querySelector(inputSelector) : null;
    const image = editor.querySelector('[data-photo-editor-image]');
    const selectionPlaceholder = editor.querySelector('[data-photo-editor-placeholder]');
    const previewGrid = editor.querySelector('[data-photo-editor-previews]');

    if (!fileInput || !image || !previewGrid) {
      return;
    }

    const hiddenFields = {
      x: editor.querySelector('input[data-photo-editor-field="x"]'),
      y: editor.querySelector('input[data-photo-editor-field="y"]'),
      width: editor.querySelector('input[data-photo-editor-field="width"]'),
      height: editor.querySelector('input[data-photo-editor-field="height"]')
    };

    const previews = Array.from(previewGrid.querySelectorAll('[data-photo-editor-preview]')).map((element) => ({
      element,
      image: element.querySelector('img'),
      size: parseSize(element)
    })).filter((preview) => preview.image && preview.size);

    let cropper = null;
    let selection = null;
    let activeObjectUrl = null;
    let lastDetail = null;
    let previewSequence = 0;

    function setActiveState(isActive) {
      editor.classList.toggle('is-active', !!isActive);
      if (selectionPlaceholder) {
        selectionPlaceholder.classList.toggle('d-none', !!isActive);
      }
      previewGrid.classList.toggle('d-none', !isActive);
    }

    function clearPreviews() {
      previews.forEach((preview) => {
        if (preview.image) {
          preview.image.removeAttribute('src');
        }
        if (preview.element) {
          preview.element.classList.remove('is-loaded');
        }
      });
    }

    function resetFields() {
      Object.values(hiddenFields).forEach((field) => {
        if (field) {
          field.value = '';
        }
      });
    }

    function revokeObjectUrl() {
      if (activeObjectUrl) {
        URL.revokeObjectURL(activeObjectUrl);
        activeObjectUrl = null;
      }
    }

    function updateHiddenFields(detail, scale) {
      if (!detail || !scale) {
        resetFields();
        return;
      }

      const { scaleX, scaleY, offsetX, offsetY } = scale;
      const cropX = clamp((detail.x - offsetX) * scaleX, 0, Number.MAX_SAFE_INTEGER);
      const cropY = clamp((detail.y - offsetY) * scaleY, 0, Number.MAX_SAFE_INTEGER);
      const cropWidth = clamp(detail.width * scaleX, 0, Number.MAX_SAFE_INTEGER);
      const cropHeight = clamp(detail.height * scaleY, 0, Number.MAX_SAFE_INTEGER);

      if (hiddenFields.x) hiddenFields.x.value = formatNumber(cropX);
      if (hiddenFields.y) hiddenFields.y.value = formatNumber(cropY);
      if (hiddenFields.width) hiddenFields.width.value = formatNumber(cropWidth);
      if (hiddenFields.height) hiddenFields.height.value = formatNumber(cropHeight);
    }

    function computeScale(detail) {
      if (!cropper || !selection) {
        return null;
      }

      const canvasElement = cropper.getCropperCanvas();
      const cropperImage = cropper.getCropperImage();
      if (!canvasElement || !cropperImage) {
        return null;
      }

      const canvasRect = canvasElement.getBoundingClientRect();
      const imageRect = cropperImage.getBoundingClientRect();
      const shadowRoot = cropperImage.$getShadowRoot ? cropperImage.$getShadowRoot() : null;
      const nativeImage = shadowRoot ? shadowRoot.querySelector('img') : null;
      const naturalWidth = nativeImage && nativeImage.naturalWidth ? nativeImage.naturalWidth : imageRect.width;
      const naturalHeight = nativeImage && nativeImage.naturalHeight ? nativeImage.naturalHeight : imageRect.height;

      if (!naturalWidth || !naturalHeight || !imageRect.width || !imageRect.height) {
        return null;
      }

      return {
        scaleX: naturalWidth / imageRect.width,
        scaleY: naturalHeight / imageRect.height,
        offsetX: imageRect.left - canvasRect.left,
        offsetY: imageRect.top - canvasRect.top
      };
    }

    function updatePreviews() {
      if (!selection || previews.length === 0) {
        clearPreviews();
        return;
      }

      const currentSequence = ++previewSequence;
      previews.forEach((preview) => {
        const { image: previewImage, element, size } = preview;
        if (!previewImage || !size) {
          return;
        }

        selection.$toCanvas({ width: size.width, height: size.height }).then((canvas) => {
          if (currentSequence !== previewSequence) {
            return;
          }

          if (canvas && canvas.width > 0 && canvas.height > 0) {
            try {
              previewImage.src = canvas.toDataURL('image/jpeg', 0.92);
              element.classList.add('is-loaded');
            } catch (err) {
              element.classList.remove('is-loaded');
            }
          }
        }).catch(() => {
          if (element) {
            element.classList.remove('is-loaded');
          }
        });
      });
    }

    function handleSelectionChange(detail) {
      if (!detail || detail.width <= 0 || detail.height <= 0) {
        resetFields();
        clearPreviews();
        return;
      }

      lastDetail = detail;
      const scale = computeScale(detail);
      updateHiddenFields(detail, scale);
      updatePreviews();
    }

    function bindSelectionListeners() {
      if (!selection) {
        return;
      }

      selection.aspectRatio = ASPECT_RATIO;
      selection.initialAspectRatio = ASPECT_RATIO;
      selection.initialCoverage = 1;
      selection.movable = true;
      selection.resizable = true;
      selection.zoomable = false;
      selection.keyboard = true;
      selection.precise = true;
      selection.outlined = true;

      selection.addEventListener('change', (event) => {
        handleSelectionChange(event.detail || null);
      });

      selection.addEventListener('pointerup', () => {
        if (lastDetail) {
          handleSelectionChange(lastDetail);
        }
      });
    }

    function ensureCropperReady() {
      if (cropper) {
        return;
      }

      cropper = new cropperConstructor(image, {
        viewMode: 1,
        background: false,
        autoCropArea: 1,
        responsive: true
      });

      selection = cropper.getCropperSelection();
      bindSelectionListeners();

      if (selection) {
        const detail = {
          x: selection.x,
          y: selection.y,
          width: selection.width,
          height: selection.height
        };
        handleSelectionChange(detail);
      } else {
        setActiveState(false);
      }
    }

    function resetEditor() {
      if (cropper) {
        const cropperCanvas = cropper.getCropperCanvas();
        if (cropperCanvas && cropperCanvas.parentNode) {
          cropperCanvas.parentNode.removeChild(cropperCanvas);
        }
        cropper = null;
        selection = null;
      }

      revokeObjectUrl();
      image.removeAttribute('src');
      image.style.removeProperty('display');
      lastDetail = null;
      previewSequence++;
      resetFields();
      clearPreviews();
      setActiveState(false);
    }

    function setImageSource(source, isObjectUrl) {
      if (!source) {
        resetEditor();
        return;
      }

      setActiveState(false);
      if (isObjectUrl) {
        revokeObjectUrl();
        activeObjectUrl = source;
      } else {
        revokeObjectUrl();
      }

      if (cropper) {
        try {
          cropper.replace(source);
          selection = cropper.getCropperSelection();
          window.setTimeout(() => {
            if (selection) {
              const detail = {
                x: selection.x,
                y: selection.y,
                width: selection.width,
                height: selection.height
              };
              handleSelectionChange(detail);
            }
            setActiveState(true);
          }, 0);
        } catch (err) {
          resetEditor();
          return;
        }
      } else {
        image.src = source;
        image.addEventListener('load', () => {
          ensureCropperReady();
          if (selection) {
            const detail = {
              x: selection.x,
              y: selection.y,
              width: selection.width,
              height: selection.height
            };
            handleSelectionChange(detail);
            setActiveState(true);
          }
        }, { once: true });
      }
    }

    function handleFileChange() {
      const [file] = fileInput.files || [];
      if (!file || !file.type || !file.type.startsWith('image/')) {
        const initialUrl = editor.getAttribute('data-photo-editor-initial-url');
        if (initialUrl) {
          setImageSource(initialUrl, false);
        } else {
          resetEditor();
        }
        return;
      }

      const objectUrl = URL.createObjectURL(file);
      setImageSource(objectUrl, true);
    }

    window.addEventListener('resize', () => {
      if (lastDetail) {
        handleSelectionChange(lastDetail);
      }
    });

    fileInput.addEventListener('change', handleFileChange);

    const initialUrl = editor.getAttribute('data-photo-editor-initial-url');
    if (initialUrl) {
      setImageSource(initialUrl, false);
    } else {
      resetFields();
      clearPreviews();
      setActiveState(false);
    }
  }

  function getDragAfterElement(container, pointerY) {
    const siblings = Array.from(container.querySelectorAll('[data-photo-item]:not(.is-dragging)'));
    let closest = { offset: Number.NEGATIVE_INFINITY, element: null };
    siblings.forEach((item) => {
      const box = item.getBoundingClientRect();
      const offset = pointerY - box.top - (box.height / 2);
      if (offset < 0 && offset > closest.offset) {
        closest = { offset, element: item };
      }
    });
    return closest.element;
  }

  function refreshOrdinals(container) {
    let index = 0;
    container.querySelectorAll('[data-photo-item]').forEach((item) => {
      const ordinalInput = item.querySelector('[data-photo-ordinal]');
      if (ordinalInput) {
        index += 1;
        ordinalInput.value = String(index);
      }
    });
  }

  function initReorder(container) {
    if (!container) {
      return;
    }

    const items = container.querySelectorAll('[data-photo-item]');
    if (!items.length) {
      return;
    }

    let activeItem = null;

    container.addEventListener('dragover', (event) => {
      if (!activeItem) {
        return;
      }
      event.preventDefault();
      const afterElement = getDragAfterElement(container, event.clientY);
      if (!afterElement) {
        container.appendChild(activeItem);
      } else if (afterElement !== activeItem) {
        container.insertBefore(activeItem, afterElement);
      }
    });

    container.addEventListener('drop', (event) => {
      if (!activeItem) {
        return;
      }
      event.preventDefault();
      const afterElement = getDragAfterElement(container, event.clientY);
      if (!afterElement) {
        container.appendChild(activeItem);
      } else if (afterElement !== activeItem) {
        container.insertBefore(activeItem, afterElement);
      }
      activeItem.classList.remove('is-dragging');
      activeItem = null;
      refreshOrdinals(container);
    });

    container.addEventListener('dragend', () => {
      if (activeItem) {
        activeItem.classList.remove('is-dragging');
        activeItem = null;
        refreshOrdinals(container);
      }
    });

    items.forEach((item) => {
      item.setAttribute('draggable', 'true');
      item.addEventListener('dragstart', (event) => {
        activeItem = item;
        item.classList.add('is-dragging');
        if (event.dataTransfer) {
          event.dataTransfer.effectAllowed = 'move';
          event.dataTransfer.setData('text/plain', '');
        }
      });
    });
  }

  document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('[data-photo-editor]').forEach((editor) => {
      initPhotoEditor(editor);
    });

    document.querySelectorAll('[data-photo-reorder]').forEach((container) => {
      initReorder(container);
    });
  });
})();
