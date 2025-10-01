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

  function getItemCaption(item, fallbackIndex) {
    if (!item) {
      return 'photo';
    }

    const caption = item.getAttribute('data-photo-caption');
    if (caption && caption.trim().length > 0) {
      return caption.trim();
    }

    if (typeof fallbackIndex === 'number') {
      return `Photo ${fallbackIndex + 1}`;
    }

    return 'photo';
  }

  function refreshOrdinals(container) {
    const items = Array.from(container.querySelectorAll('[data-photo-item]'));
    const total = items.length;

    items.forEach((item, index) => {
      const position = index + 1;
      const ordinalInput = item.querySelector('[data-photo-ordinal]');
      const caption = getItemCaption(item, index);
      if (ordinalInput) {
        ordinalInput.value = String(position);
        ordinalInput.setAttribute('aria-label', `Display position for ${caption}`);
      }
      item.setAttribute('aria-setsize', String(total));
      item.setAttribute('aria-posinset', String(position));
      item.setAttribute('aria-label', `${caption} – position ${position} of ${total}`);
    });

    return { items, total };
  }

  function initReorder(container) {
    if (!container) {
      return;
    }

    const items = Array.from(container.querySelectorAll('[data-photo-item]'));
    if (!items.length) {
      return;
    }

    const root = container.closest('[data-photo-reorder-root]') || container;
    const statusRegion = root.querySelector('[data-photo-reorder-status]');
    let activeItem = null;

    refreshOrdinals(container);
    if (statusRegion) {
      statusRegion.textContent = '';
    }
    container.setAttribute('aria-expanded', 'false');

    function announce(message) {
      if (statusRegion) {
        statusRegion.textContent = message;
      }
    }

    function updateExpandedState() {
      container.setAttribute('aria-expanded', activeItem ? 'true' : 'false');
    }

    function activateItem(item) {
      if (activeItem && activeItem !== item) {
        deactivateItem(activeItem);
      }
      activeItem = item;
      if (activeItem) {
        activeItem.classList.add('is-dragging');
        activeItem.setAttribute('aria-grabbed', 'true');
      }
      updateExpandedState();
    }

    function deactivateItem(item, options = {}) {
      if (!item) {
        return;
      }
      item.classList.remove('is-dragging');
      item.setAttribute('aria-grabbed', 'false');
      if (options.restoreFocus) {
        item.focus();
      }
      if (activeItem === item) {
        activeItem = null;
      }
      updateExpandedState();
    }

    function announcePickup(item) {
      const caption = getItemCaption(item);
      announce(`${caption} selected. Use arrow keys to change position, Enter to drop, or Escape to cancel.`);
    }

    function announceDrop(item, position, total) {
      const caption = getItemCaption(item);
      announce(`${caption} placed in position ${position} of ${total}.`);
    }

    function announceCancel(item) {
      const caption = getItemCaption(item);
      announce(`Cancelled moving ${caption}.`);
    }

    function announceBoundary(item, direction) {
      const caption = getItemCaption(item);
      announce(`${caption} is already at the ${direction < 0 ? 'start' : 'end'} of the list.`);
    }

    function moveItem(item, direction) {
      const currentItems = Array.from(container.querySelectorAll('[data-photo-item]'));
      const index = currentItems.indexOf(item);
      if (index === -1) {
        return;
      }

      const newIndex = index + direction;
      if (newIndex < 0 || newIndex >= currentItems.length) {
        announceBoundary(item, direction);
        return;
      }

      if (direction > 0) {
        const reference = currentItems[newIndex].nextElementSibling;
        container.insertBefore(item, reference || null);
      } else {
        const reference = currentItems[newIndex];
        container.insertBefore(item, reference);
      }

      const { items: updatedItems, total } = refreshOrdinals(container);
      const position = updatedItems.indexOf(item) + 1;
      item.focus();
      announce(`${getItemCaption(item)} moved to position ${position} of ${total}.`);
    }

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
      const item = activeItem;
      const afterElement = getDragAfterElement(container, event.clientY);
      if (!afterElement) {
        container.appendChild(item);
      } else if (afterElement !== item) {
        container.insertBefore(item, afterElement);
      }
      const { items: updatedItems, total } = refreshOrdinals(container);
      const position = updatedItems.indexOf(item) + 1;
      deactivateItem(item, { restoreFocus: true });
      announceDrop(item, position, total);
    });

    container.addEventListener('dragend', () => {
      if (!activeItem) {
        return;
      }
      const item = activeItem;
      deactivateItem(item, { restoreFocus: true });
      refreshOrdinals(container);
      announceCancel(item);
    });

    items.forEach((item) => {
      item.setAttribute('draggable', 'true');
      item.addEventListener('dragstart', (event) => {
        activateItem(item);
        if (event.dataTransfer) {
          event.dataTransfer.effectAllowed = 'move';
          event.dataTransfer.setData('text/plain', '');
        }
        announcePickup(item);
      });
      item.addEventListener('keydown', (event) => {
        const key = event.key;
        if (key === ' ' || key === 'Spacebar' || key === 'Enter') {
          event.preventDefault();
          if (activeItem === item) {
            const { items: updatedItems, total } = refreshOrdinals(container);
            const position = updatedItems.indexOf(item) + 1;
            deactivateItem(item, { restoreFocus: true });
            announceDrop(item, position, total);
          } else {
            if (activeItem && activeItem !== item) {
              const previous = activeItem;
              deactivateItem(previous);
              announceCancel(previous);
            }
            activateItem(item);
            announcePickup(item);
          }
        } else if ((key === 'Escape' || key === 'Esc') && activeItem === item) {
          event.preventDefault();
          deactivateItem(item, { restoreFocus: true });
          announceCancel(item);
        } else if (activeItem === item && (key === 'ArrowUp' || key === 'ArrowLeft' || key === 'ArrowDown' || key === 'ArrowRight')) {
          event.preventDefault();
          const direction = key === 'ArrowUp' || key === 'ArrowLeft' ? -1 : 1;
          moveItem(item, direction);
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
