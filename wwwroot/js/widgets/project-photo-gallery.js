(function () {
  const DEFAULT_SIZES = {
    xl: { width: 1600, height: 1200 },
    md: { width: 1200, height: 900 },
    sm: { width: 800, height: 600 }
  };
  const GRID_PREVIEW_MAX_EDGE = 320;

  function normalizeSize(size) {
    if (!size || !isPositiveNumber(size.width) || !isPositiveNumber(size.height)) {
      return null;
    }

    const width = Math.max(1, Math.round(size.width));
    const height = Math.max(1, Math.round(size.height));
    return { width, height };
  }

  function computeGridSize(size) {
    const normalized = normalizeSize(size);
    if (!normalized) {
      return null;
    }

    const { width, height } = normalized;
    const maxEdge = GRID_PREVIEW_MAX_EDGE;
    if (width <= maxEdge && height <= maxEdge) {
      return normalized;
    }

    const scale = Math.min(maxEdge / width, maxEdge / height);
    const scaledWidth = Math.max(1, Math.round(width * scale));
    const scaledHeight = Math.max(1, Math.round(height * scale));
    return { width: scaledWidth, height: scaledHeight };
  }

  function applyCanvasToImage(imageElement, containerElement, canvas) {
    if (!imageElement || !containerElement || !canvas) {
      return;
    }

    try {
      const dataUrl = canvas.toDataURL('image/jpeg', 0.92);
      imageElement.src = dataUrl;
      containerElement.classList.add('is-loaded');
    } catch (err) {
      containerElement.classList.remove('is-loaded');
    }
  }

  function clearPreviewImage(imageElement, containerElement) {
    if (imageElement) {
      imageElement.removeAttribute('src');
    }
    if (containerElement) {
      containerElement.classList.remove('is-loaded');
    }
  }

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

  function isPositiveNumber(value) {
    return Number.isFinite(value) && value > 0;
  }

  function firstPositive(...values) {
    for (const value of values) {
      if (isPositiveNumber(value)) {
        return value;
      }
    }
    return null;
  }

  function clampCropBox(rawX, rawY, rawWidth, rawHeight, naturalWidth, naturalHeight) {
    if (!isPositiveNumber(naturalWidth) || !isPositiveNumber(naturalHeight)) {
      return null;
    }

    if (!Number.isFinite(rawX) || !Number.isFinite(rawY) || !Number.isFinite(rawWidth) || !Number.isFinite(rawHeight)) {
      return null;
    }

    const rawRight = rawX + rawWidth;
    const rawBottom = rawY + rawHeight;

    if (!Number.isFinite(rawRight) || !Number.isFinite(rawBottom)) {
      return null;
    }

    const clampedLeft = clamp(rawX, 0, naturalWidth);
    const clampedTop = clamp(rawY, 0, naturalHeight);
    const clampedRight = clamp(rawRight, 0, naturalWidth);
    const clampedBottom = clamp(rawBottom, 0, naturalHeight);

    const clampedWidth = clampedRight - clampedLeft;
    const clampedHeight = clampedBottom - clampedTop;

    if (clampedWidth <= 0 || clampedHeight <= 0) {
      return null;
    }

    return {
      x: clampedLeft,
      y: clampedTop,
      width: clampedWidth,
      height: clampedHeight
    };
  }

  function formatNumber(value) {
    return Number.isFinite(value) ? String(Math.round(value)) : '';
  }

  function quantizeCropBox(cropBox, naturalWidth, naturalHeight) {
    // SECTION: Quantize crop box by rounding to integers and clamping to bounds.
    if (!cropBox || !isPositiveNumber(naturalWidth) || !isPositiveNumber(naturalHeight)) {
      return null;
    }

    const safeWidth = Math.floor(naturalWidth);
    const safeHeight = Math.floor(naturalHeight);

    const clampInt = (value, min, max) => {
      if (!Number.isFinite(value)) return min;
      const rounded = Math.round(value);
      return Math.min(Math.max(rounded, min), max);
    };

    const x = clampInt(cropBox.x, 0, Math.max(0, safeWidth - 1));
    const y = clampInt(cropBox.y, 0, Math.max(0, safeHeight - 1));

    const maxWidth = Math.max(1, safeWidth - x);
    const maxHeight = Math.max(1, safeHeight - y);

    const width = clampInt(cropBox.width, 1, maxWidth);
    const height = clampInt(cropBox.height, 1, maxHeight);

    return { x, y, width, height };
  }

  function initPhotoEditor(editor) {
    if (!editor) {
      return;
    }

    const cropperConstructor = window.Cropper?.default ?? window.Cropper;
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
    const dirtyFieldSelector = editor.getAttribute('data-photo-editor-dirty-field');
    const dirtyField = dirtyFieldSelector ? document.querySelector(dirtyFieldSelector) : editor.querySelector('[data-photo-editor-dirty]');

    function setCropDirty(isDirty) {
      if (dirtyField) {
        dirtyField.value = isDirty ? 'true' : 'false';
      }
    }

    const previews = Array.from(previewGrid.querySelectorAll('[data-photo-editor-preview]')).map((element) => {
      const itemElement = element.closest('[data-photo-preview-item]');
      const expandedPanel = itemElement ? itemElement.querySelector('[data-photo-preview-expanded]') : null;
      const expandedImage = expandedPanel ? expandedPanel.querySelector('[data-photo-preview-large]') : null;
      const openButton = element.querySelector('[data-photo-preview-open]');
      const closeButton = expandedPanel ? expandedPanel.querySelector('[data-photo-preview-close]') : null;
      const previewImage = element.querySelector('[data-photo-preview-thumb]') || element.querySelector('img');
      const size = parseSize(element);
      return {
        key: element.getAttribute('data-photo-editor-preview') || '',
        element,
        itemElement,
        image: previewImage,
        size,
        gridSize: computeGridSize(size),
        expandedPanel,
        expandedImage,
        openButton,
        closeButton,
        isOpen: false,
        fullSequence: 0
      };
    }).filter((preview) => preview.image && preview.size);

    const expandToggle = editor.querySelector('[data-photo-editor-toggle]');
    const expandToggleLabel = expandToggle ? expandToggle.querySelector('[data-photo-editor-toggle-label]') : null;
    const expandedClass = 'project-photo-editor--expanded';
    const pageExpandedClass = 'has-expanded-project-photo-editor';
    let isExpanded = false;
    let backdrop = null;

    function ensureBackdrop() {
      if (backdrop && backdrop.parentNode) {
        return backdrop;
      }

      if (!backdrop) {
        backdrop = document.createElement('div');
        backdrop.className = 'project-photo-editor-backdrop';
        backdrop.addEventListener('click', () => {
          collapseExpansion();
        });
      }

      document.body.appendChild(backdrop);
      return backdrop;
    }

    function removeBackdrop() {
      if (backdrop && backdrop.parentNode) {
        backdrop.parentNode.removeChild(backdrop);
      }
    }

    function syncExpansionState(expanded) {
      const nextState = !!expanded;
      if (nextState === isExpanded) {
        return;
      }

      isExpanded = nextState;
      editor.classList.toggle(expandedClass, isExpanded);

      if (isExpanded) {
        document.documentElement.classList.add(pageExpandedClass);
        document.body.classList.add(pageExpandedClass);
        ensureBackdrop();
      } else {
        document.documentElement.classList.remove(pageExpandedClass);
        document.body.classList.remove(pageExpandedClass);
        removeBackdrop();
      }

      if (expandToggle) {
        expandToggle.setAttribute('aria-expanded', isExpanded ? 'true' : 'false');
      }

      if (expandToggleLabel) {
        expandToggleLabel.textContent = isExpanded ? 'Exit full-screen' : 'Expand editor';
      }

      window.requestAnimationFrame(() => {
        window.dispatchEvent(new Event('resize'));
      });
    }

    function collapseExpansion() {
      if (!isExpanded) {
        return;
      }

      syncExpansionState(false);
      if (expandToggle) {
        expandToggle.focus({ preventScroll: true });
      }
    }

    let cropper = null;
    let selection = null;
    let isModernCropper = false;
    let legacyListenerCleanup = null;
    let activeObjectUrl = null;
    let sourceSequence = 0;
    let cropInteractionReady = false;
    let userCropInteractionActive = false;
    let lastDetail = null;
    let previewSequence = 0;
    let activePreview = null;

    function closePreview(preview, restoreFocus) {
      if (!preview || !preview.isOpen) {
        return;
      }

      preview.isOpen = false;
      preview.fullSequence += 1;

      if (preview.itemElement) {
        preview.itemElement.classList.remove('is-expanded');
      }

      clearPreviewImage(preview.expandedImage, preview.expandedPanel);

      if (preview.expandedPanel) {
        preview.expandedPanel.classList.remove('is-visible');
        preview.expandedPanel.setAttribute('aria-hidden', 'true');
      }

      if (activePreview === preview) {
        activePreview = null;
        document.documentElement.classList.remove('has-project-photo-preview-dialog');
        document.body.classList.remove('has-project-photo-preview-dialog');
      }

      if (restoreFocus && preview.openButton) {
        preview.openButton.focus({ preventScroll: true });
      }
    }

    function closeActivePreview(restoreFocus) {
      if (activePreview) {
        closePreview(activePreview, restoreFocus);
      }
    }

    function requestPreviewCanvas(size) {
      const normalized = normalizeSize(size);
      if (!normalized) {
        return Promise.reject(new Error('Invalid preview dimensions'));
      }

      if (selection && typeof selection.$toCanvas === 'function') {
        try {
          const result = selection.$toCanvas({ width: normalized.width, height: normalized.height });
          return Promise.resolve(result).then((canvas) => {
            if (canvas && canvas.width > 0 && canvas.height > 0) {
              return canvas;
            }
            throw new Error('Empty canvas');
          });
        } catch (err) {
          return Promise.reject(err);
        }
      }

      if (cropper && typeof cropper.getCroppedCanvas === 'function') {
        let canvas = null;
        try {
          canvas = cropper.getCroppedCanvas({ width: normalized.width, height: normalized.height });
        } catch (err) {
          return Promise.reject(err);
        }

        if (canvas && canvas.width > 0 && canvas.height > 0) {
          return Promise.resolve(canvas);
        }

        return Promise.reject(new Error('Empty canvas'));
      }

      return Promise.reject(new Error('Preview unavailable'));
    }

    function renderFullPreview(preview) {
      if (!preview || !preview.isOpen || !preview.size) {
        return;
      }

      const requestId = preview.fullSequence + 1;
      preview.fullSequence = requestId;

      clearPreviewImage(preview.expandedImage, preview.expandedPanel);

      requestPreviewCanvas(preview.size).then((canvas) => {
        if (!canvas || preview.fullSequence !== requestId || !preview.isOpen) {
          return;
        }

        if (preview.expandedImage && preview.expandedPanel) {
          applyCanvasToImage(preview.expandedImage, preview.expandedPanel, canvas);
        }
      }).catch(() => {
        if (preview.fullSequence === requestId && preview.expandedPanel) {
          preview.expandedPanel.classList.remove('is-loaded');
        }
      });
    }

    function openPreview(preview) {
      if (!preview || preview.isOpen) {
        return;
      }

      if (activePreview && activePreview !== preview) {
        closePreview(activePreview, false);
      }

      preview.isOpen = true;
      activePreview = preview;

      document.documentElement.classList.add('has-project-photo-preview-dialog');
      document.body.classList.add('has-project-photo-preview-dialog');

      if (preview.itemElement) {
        preview.itemElement.classList.add('is-expanded');
      }

      if (preview.expandedPanel) {
        preview.expandedPanel.classList.add('is-visible');
        preview.expandedPanel.setAttribute('aria-hidden', 'false');
      }

      clearPreviewImage(preview.expandedImage, preview.expandedPanel);

      renderFullPreview(preview);

      window.setTimeout(() => {
        if (preview.closeButton) {
          preview.closeButton.focus({ preventScroll: true });
        } else if (preview.expandedPanel && typeof preview.expandedPanel.focus === 'function') {
          preview.expandedPanel.focus({ preventScroll: true });
        }
      }, 0);
    }

    previews.forEach((preview) => {
      if (preview.openButton) {
        preview.openButton.addEventListener('click', (event) => {
          event.preventDefault();
          if (preview.isOpen) {
            closePreview(preview, true);
          } else {
            openPreview(preview);
          }
        });
      }

      if (preview.closeButton) {
        preview.closeButton.addEventListener('click', (event) => {
          event.preventDefault();
          closePreview(preview, true);
        });
      }

      if (preview.expandedPanel) {
        preview.expandedPanel.addEventListener('click', (event) => {
          if (event.target === preview.expandedPanel) {
            event.preventDefault();
            closePreview(preview, true);
          }
        });
      }
    });

    if (expandToggle) {
      expandToggle.addEventListener('click', (event) => {
        event.preventDefault();
        syncExpansionState(!isExpanded);
      });
    }

    document.addEventListener('keydown', (event) => {
      if (event.key === 'Escape' || event.key === 'Esc') {
        if (activePreview && activePreview.isOpen) {
          event.preventDefault();
          closeActivePreview(true);
          return;
        }
      }

      if (!isExpanded) {
        return;
      }

      if (event.key === 'Escape' || event.key === 'Esc') {
        event.preventDefault();
        collapseExpansion();
      }
    });

    const cropSection = editor.closest('[data-photo-crop-section]');

    function setActiveState(isActive) {
      const active = !!isActive;
      editor.classList.toggle('is-active', active);
      if (selectionPlaceholder) {
        selectionPlaceholder.classList.toggle('d-none', active);
      }
      previewGrid.classList.toggle('d-none', !active);
      if (cropSection) {
        cropSection.hidden = !active;
        cropSection.setAttribute('aria-hidden', active ? 'false' : 'true');
      }
    }

    function clearPreviews() {
      closeActivePreview(false);
      previews.forEach((preview) => {
        preview.fullSequence += 1;
        preview.isOpen = false;
        if (preview.itemElement) {
          preview.itemElement.classList.remove('is-expanded');
        }
        if (preview.expandedPanel) {
          preview.expandedPanel.classList.remove('is-visible');
          preview.expandedPanel.setAttribute('aria-hidden', 'true');
        }
        clearPreviewImage(preview.image, preview.element);
        clearPreviewImage(preview.expandedImage, preview.expandedPanel);
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
      if (!detail) {
        resetFields();
        clearPreviews();
        return false;
      }

      const naturalWidth = firstPositive(scale && scale.naturalWidth, detail.naturalWidth, image && image.naturalWidth);
      const naturalHeight = firstPositive(scale && scale.naturalHeight, detail.naturalHeight, image && image.naturalHeight);

      if (!isPositiveNumber(naturalWidth) || !isPositiveNumber(naturalHeight)) {
        resetFields();
        clearPreviews();
        return false;
      }

      let cropBox = null;

      if (scale && Number.isFinite(scale.scaleX) && Number.isFinite(scale.scaleY) && scale.scaleX > 0 && scale.scaleY > 0 && Number.isFinite(scale.offsetX) && Number.isFinite(scale.offsetY)) {
        if (Number.isFinite(detail.x) && Number.isFinite(detail.y) && Number.isFinite(detail.width) && Number.isFinite(detail.height)) {
          const rawX = (detail.x - scale.offsetX) * scale.scaleX;
          const rawY = (detail.y - scale.offsetY) * scale.scaleY;
          const rawWidth = detail.width * scale.scaleX;
          const rawHeight = detail.height * scale.scaleY;
          cropBox = clampCropBox(rawX, rawY, rawWidth, rawHeight, naturalWidth, naturalHeight);
        }
      } else {
        const rawX = Number.isFinite(detail.naturalX) ? detail.naturalX : detail.x;
        const rawY = Number.isFinite(detail.naturalY) ? detail.naturalY : detail.y;
        const rawWidth = Number.isFinite(detail.naturalWidth) ? detail.naturalWidth : detail.width;
        const rawHeight = Number.isFinite(detail.naturalHeight) ? detail.naturalHeight : detail.height;

        cropBox = clampCropBox(rawX, rawY, rawWidth, rawHeight, naturalWidth, naturalHeight);
      }

      if (!cropBox) {
        resetFields();
        clearPreviews();
        return false;
      }

      const quantizedCrop = quantizeCropBox(cropBox, naturalWidth, naturalHeight) || cropBox;

      if (hiddenFields.x) hiddenFields.x.value = formatNumber(quantizedCrop.x);
      if (hiddenFields.y) hiddenFields.y.value = formatNumber(quantizedCrop.y);
      if (hiddenFields.width) hiddenFields.width.value = formatNumber(quantizedCrop.width);
      if (hiddenFields.height) hiddenFields.height.value = formatNumber(quantizedCrop.height);

      return true;
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
        offsetY: imageRect.top - canvasRect.top,
        naturalWidth,
        naturalHeight
      };
    }

    function updatePreviews() {
      if (previews.length === 0) {
        clearPreviews();
        return;
      }

      const currentSequence = ++previewSequence;
      previews.forEach((preview) => {
        const { image: previewImage, element, gridSize } = preview;
        if (!previewImage || !element || !gridSize) {
          clearPreviewImage(previewImage, element);
          return;
        }

        element.classList.remove('is-loaded');

        requestPreviewCanvas(gridSize).then((canvas) => {
          if (currentSequence !== previewSequence) {
            return;
          }

          if (canvas && canvas.width > 0 && canvas.height > 0) {
            applyCanvasToImage(previewImage, element, canvas);
          } else {
            clearPreviewImage(previewImage, element);
          }
        }).catch(() => {
          if (currentSequence === previewSequence) {
            clearPreviewImage(previewImage, element);
          }
        });
      });

      previews.forEach((preview) => {
        if (preview.isOpen) {
          renderFullPreview(preview);
        }
      });
    }

    function applyCropDetail(detail, scale) {
      if (!detail || detail.width <= 0 || detail.height <= 0) {
        lastDetail = null;
        resetFields();
        clearPreviews();
        return;
      }

      lastDetail = detail;
      if (!updateHiddenFields(detail, scale || null)) {
        return;
      }
      updatePreviews();
    }

    function handleSelectionChange(detail, markDirty = false) {
      if (markDirty && cropInteractionReady) {
        setCropDirty(true);
      }
      const scale = isModernCropper ? computeScale(detail) : null;
      applyCropDetail(detail, scale);
    }

    function handleLegacyCrop(detail, markDirty = false) {
      if (markDirty && cropInteractionReady) {
        setCropDirty(true);
      }
      applyCropDetail(detail, null);
      if (detail && detail.width > 0 && detail.height > 0) {
        setActiveState(true);
      }
    }

    function bindSelectionListeners() {
      if (!selection) {
        return;
      }

      // SECTION: Unlock aspect ratio for freeform crop selection.
      selection.aspectRatio = NaN;
      selection.initialAspectRatio = NaN;
      selection.initialCoverage = 1;
      selection.movable = true;
      selection.resizable = true;
      selection.zoomable = false;
      selection.keyboard = true;
      selection.precise = true;
      selection.outlined = true;

      selection.addEventListener('pointerdown', () => {
        userCropInteractionActive = true;
      });

      selection.addEventListener('change', (event) => {
        handleSelectionChange(event.detail || null, userCropInteractionActive);
      });

      selection.addEventListener('pointerup', () => {
        if (lastDetail) {
          handleSelectionChange(lastDetail, true);
        }
        userCropInteractionActive = false;
      });

      selection.addEventListener('pointercancel', () => {
        userCropInteractionActive = false;
      });

      selection.addEventListener('keydown', (event) => {
        if (['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key)) {
          setCropDirty(true);
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

      isModernCropper = cropper && typeof cropper.getCropperSelection === 'function' && typeof cropper.getCropperCanvas === 'function' && typeof cropper.getCropperImage === 'function';

      if (isModernCropper) {
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
          setActiveState(true);
        } else {
          setActiveState(false);
        }
      } else {
        selection = null;
        if (cropper && typeof cropper.setAspectRatio === 'function') {
          // SECTION: Ensure legacy cropper allows arbitrary aspect ratios.
          cropper.setAspectRatio(NaN);
        }
        if (legacyListenerCleanup) {
          legacyListenerCleanup();
        }

        const handleReady = () => {
          if (cropper && typeof cropper.getData === 'function') {
            handleLegacyCrop(cropper.getData(true));
          }
          setActiveState(true);
        };

        const handleCropEvent = (event) => {
          handleLegacyCrop(event && event.detail ? event.detail : null, true);
        };

        image.addEventListener('ready', handleReady);
        image.addEventListener('crop', handleCropEvent);

        legacyListenerCleanup = () => {
          image.removeEventListener('ready', handleReady);
          image.removeEventListener('crop', handleCropEvent);
          legacyListenerCleanup = null;
        };

        if (cropper && typeof cropper.getData === 'function') {
          handleLegacyCrop(cropper.getData(true));
        } else {
          setActiveState(false);
        }
      }
    }

    function resetEditor() {
      collapseExpansion();
      cropInteractionReady = false;
      userCropInteractionActive = false;

      if (cropper) {
        if (typeof cropper.destroy === 'function') {
          cropper.destroy();
        } else {
          const cropperCanvas = typeof cropper.getCropperCanvas === 'function' ? cropper.getCropperCanvas() : null;
          if (cropperCanvas && cropperCanvas.parentNode) {
            cropperCanvas.parentNode.removeChild(cropperCanvas);
          }
        }
        cropper = null;
        selection = null;
        isModernCropper = false;
      }

      if (legacyListenerCleanup) {
        legacyListenerCleanup();
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

    function setImageSource(source) {
      const requestId = ++sourceSequence;
      resetEditor();

      if (!source) {
        return;
      }

      let settled = false;
      const handleLoad = () => {
        if (settled || requestId !== sourceSequence) {
          return;
        }
        settled = true;

        editor.classList.remove('has-load-error');
        ensureCropperReady();

        window.requestAnimationFrame(() => {
          if (requestId !== sourceSequence) {
            return;
          }

          if (isModernCropper) {
            selection = cropper ? cropper.getCropperSelection() : null;
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
          } else if (cropper && typeof cropper.getData === 'function') {
            handleLegacyCrop(cropper.getData(true));
            setActiveState(true);
          }
          cropInteractionReady = true;
        });
      };

      const handleError = () => {
        if (settled || requestId !== sourceSequence) {
          return;
        }
        settled = true;

        editor.classList.add('has-load-error');
        resetFields();
        clearPreviews();
        setActiveState(false);
        editor.dispatchEvent(new CustomEvent('project-photo-editor:error', {
          bubbles: true,
          detail: { message: 'The selected image could not be previewed. Choose another JPEG, PNG or WebP file.' }
        }));
      };

      image.addEventListener('load', handleLoad, { once: true });
      image.addEventListener('error', handleError, { once: true });
      image.src = source;

      if (image.complete) {
        if (image.naturalWidth > 0 && image.naturalHeight > 0) {
          window.queueMicrotask(handleLoad);
        } else {
          window.queueMicrotask(handleError);
        }
      }
    }

    function handleFileChange() {
      const [file] = fileInput.files || [];
      if (!file || !file.type || !file.type.startsWith('image/')) {
        setCropDirty(false);
        const initialUrl = editor.getAttribute('data-photo-editor-initial-url');
        if (initialUrl) {
          setImageSource(initialUrl);
        } else {
          sourceSequence += 1;
          resetEditor();
        }
        return;
      }

      setCropDirty(true);
      const reader = new FileReader();
      const requestId = ++sourceSequence;

      reader.addEventListener('load', () => {
        if (requestId !== sourceSequence || typeof reader.result !== 'string') {
          return;
        }
        setImageSource(reader.result);
      }, { once: true });

      reader.addEventListener('error', () => {
        if (requestId !== sourceSequence) {
          return;
        }
        editor.dispatchEvent(new CustomEvent('project-photo-editor:error', {
          bubbles: true,
          detail: { message: 'The selected file could not be read. Choose another image and try again.' }
        }));
        resetEditor();
      }, { once: true });

      reader.readAsDataURL(file);
    }

    window.addEventListener('resize', () => {
      if (!cropper) {
        return;
      }

      if (isModernCropper) {
        if (lastDetail) {
          handleSelectionChange(lastDetail);
        }
      } else if (typeof cropper.getData === 'function') {
        handleLegacyCrop(cropper.getData(true));
      }
    });

    fileInput.addEventListener('change', handleFileChange);
    fileInput.addEventListener('project-photo-file-cleared', handleFileChange);

    const initialUrl = editor.getAttribute('data-photo-editor-initial-url');
    setCropDirty(false);
    if (initialUrl) {
      setImageSource(initialUrl);
    } else {
      resetFields();
      clearPreviews();
      setActiveState(false);
    }
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
      const positionLabel = item.querySelector('[data-photo-position-label]');
      const dragHandle = item.querySelector('[data-photo-drag-handle]');
      const caption = getItemCaption(item, index);

      if (ordinalInput) {
        ordinalInput.value = String(position);
        ordinalInput.setAttribute('aria-label', `Display position for ${caption}`);
      }
      if (positionLabel) {
        positionLabel.textContent = `Position ${position}`;
      }
      if (dragHandle) {
        dragHandle.setAttribute('aria-label', `Move ${caption}, currently position ${position} of ${total}`);
      }

      item.setAttribute('aria-setsize', String(total));
      item.setAttribute('aria-posinset', String(position));
      item.setAttribute('aria-label', `${caption} – position ${position} of ${total}`);
    });

    return { items, total };
  }

  function getOrderSignature(container) {
    return Array.from(container.querySelectorAll('[data-photo-item]'))
      .map((item) => item.getAttribute('data-photo-id') || '')
      .join('|');
  }

  function getGridInsertion(container, clientX, clientY, activeItem, placeholder) {
    const candidates = Array.from(container.querySelectorAll('[data-photo-item]'))
      .filter((item) => item !== activeItem && item !== placeholder && item.offsetParent !== null);

    if (!candidates.length) {
      return { element: null, before: false };
    }

    let nearest = null;
    let nearestDistance = Number.POSITIVE_INFINITY;

    candidates.forEach((item) => {
      const rect = item.getBoundingClientRect();
      const centerX = rect.left + (rect.width / 2);
      const centerY = rect.top + (rect.height / 2);
      const distance = Math.hypot(clientX - centerX, clientY - centerY);
      if (distance < nearestDistance) {
        nearestDistance = distance;
        nearest = { item, rect, centerX, centerY };
      }
    });

    if (!nearest) {
      return { element: null, before: false };
    }

    const verticalThreshold = nearest.rect.height * 0.45;
    const sameVisualRow = Math.abs(clientY - nearest.centerY) <= verticalThreshold;
    const before = sameVisualRow
      ? clientX < nearest.centerX
      : clientY < nearest.centerY;

    return { element: nearest.item, before };
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
    const form = container.closest('[data-photo-order-form]');
    const statusRegion = root.querySelector('[data-photo-reorder-status]');
    const saveButton = form ? form.querySelector('[data-photo-order-save]') : null;
    const orderIndicator = form ? form.querySelector('[data-photo-order-indicator]') : null;
    const initialSignature = getOrderSignature(container);

    let activeItem = null;
    let activeHandle = null;
    let activeMode = null;
    let activeOriginalIndex = -1;
    let placeholder = null;
    let dropped = false;
    let dragFrame = null;
    let pendingPointer = null;

    refreshOrdinals(container);
    container.setAttribute('aria-expanded', 'false');

    function announce(message) {
      if (statusRegion) {
        statusRegion.textContent = '';
        window.requestAnimationFrame(() => {
          statusRegion.textContent = message;
        });
      }
    }

    function syncDirtyState() {
      const changed = getOrderSignature(container) !== initialSignature;
      if (saveButton) {
        saveButton.disabled = !changed;
      }
      if (orderIndicator) {
        orderIndicator.textContent = changed ? 'Unsaved order changes' : 'Order unchanged';
        orderIndicator.classList.toggle('is-dirty', changed);
      }
      if (form) {
        form.classList.toggle('has-order-changes', changed);
      }
      return changed;
    }

    function createPlaceholder(item) {
      const rect = item.getBoundingClientRect();
      const nextPlaceholder = document.createElement('div');
      nextPlaceholder.className = 'pm-photo-grid-placeholder';
      nextPlaceholder.setAttribute('aria-hidden', 'true');
      nextPlaceholder.style.minHeight = `${Math.max(180, Math.round(rect.height))}px`;
      return nextPlaceholder;
    }

    function setActiveState(item, handle, mode) {
      activeItem = item;
      activeHandle = handle;
      activeMode = mode;
      activeOriginalIndex = Array.from(container.querySelectorAll('[data-photo-item]')).indexOf(item);
      dropped = false;
      item.classList.add('is-dragging');
      item.setAttribute('aria-grabbed', 'true');
      container.setAttribute('aria-expanded', 'true');

      if (mode === 'pointer') {
        placeholder = createPlaceholder(item);
        item.insertAdjacentElement('afterend', placeholder);
        window.setTimeout(() => {
          if (activeItem === item && activeMode === 'pointer') {
            item.classList.add('is-drag-source-hidden');
          }
        }, 0);
      }
    }

    function restoreOriginalPosition(item) {
      const remainingItems = Array.from(container.querySelectorAll('[data-photo-item]')).filter((candidate) => candidate !== item);
      if (activeOriginalIndex < 0 || activeOriginalIndex >= remainingItems.length) {
        container.appendChild(item);
      } else {
        container.insertBefore(item, remainingItems[activeOriginalIndex]);
      }
    }

    function clearActiveState({ restoreFocus = false, restoreOriginal = false } = {}) {
      if (!activeItem) {
        return;
      }

      const item = activeItem;
      const handle = activeHandle;

      item.classList.remove('is-drag-source-hidden');
      if (activeMode === 'pointer' && placeholder) {
        if (restoreOriginal) {
          placeholder.remove();
          restoreOriginalPosition(item);
        } else {
          container.insertBefore(item, placeholder);
          placeholder.remove();
        }
      } else if (restoreOriginal) {
        restoreOriginalPosition(item);
      }

      item.classList.remove('is-dragging');
      item.setAttribute('aria-grabbed', 'false');
      activeItem = null;
      activeHandle = null;
      activeMode = null;
      activeOriginalIndex = -1;
      placeholder = null;
      pendingPointer = null;
      if (dragFrame) {
        window.cancelAnimationFrame(dragFrame);
        dragFrame = null;
      }
      container.setAttribute('aria-expanded', 'false');
      refreshOrdinals(container);
      syncDirtyState();

      if (restoreFocus && handle && typeof handle.focus === 'function') {
        handle.focus({ preventScroll: true });
      }
    }

    function moveKeyboardItem(item, direction) {
      const currentItems = Array.from(container.querySelectorAll('[data-photo-item]'));
      const currentIndex = currentItems.indexOf(item);
      const nextIndex = currentIndex + direction;
      if (currentIndex < 0 || nextIndex < 0 || nextIndex >= currentItems.length) {
        announce(`${getItemCaption(item)} is already at the ${direction < 0 ? 'start' : 'end'} of the gallery.`);
        return;
      }

      if (direction > 0) {
        currentItems[nextIndex].insertAdjacentElement('afterend', item);
      } else {
        container.insertBefore(item, currentItems[nextIndex]);
      }

      const { items: updatedItems, total } = refreshOrdinals(container);
      const position = updatedItems.indexOf(item) + 1;
      syncDirtyState();
      announce(`${getItemCaption(item)} moved to position ${position} of ${total}.`);
    }

    function processPointerMove() {
      dragFrame = null;
      if (!activeItem || activeMode !== 'pointer' || !placeholder || !pendingPointer) {
        return;
      }

      const { clientX, clientY } = pendingPointer;
      const insertion = getGridInsertion(container, clientX, clientY, activeItem, placeholder);
      if (!insertion.element) {
        container.appendChild(placeholder);
        return;
      }

      if (insertion.before) {
        container.insertBefore(placeholder, insertion.element);
      } else {
        insertion.element.insertAdjacentElement('afterend', placeholder);
      }
    }

    container.addEventListener('dragover', (event) => {
      if (!activeItem || activeMode !== 'pointer') {
        return;
      }
      event.preventDefault();
      pendingPointer = { clientX: event.clientX, clientY: event.clientY };
      if (!dragFrame) {
        dragFrame = window.requestAnimationFrame(processPointerMove);
      }
    });

    container.addEventListener('drop', (event) => {
      if (!activeItem || activeMode !== 'pointer') {
        return;
      }
      event.preventDefault();
      dropped = true;
      const item = activeItem;
      const caption = getItemCaption(item);
      clearActiveState({ restoreFocus: true });
      const updatedItems = Array.from(container.querySelectorAll('[data-photo-item]'));
      const position = updatedItems.indexOf(item) + 1;
      announce(`${caption} placed in position ${position} of ${updatedItems.length}.`);
    });

    items.forEach((item) => {
      const handle = item.querySelector('[data-photo-drag-handle]') || item;
      handle.setAttribute('draggable', 'true');

      handle.addEventListener('dragstart', (event) => {
        if (activeItem && activeItem !== item) {
          clearActiveState();
        }
        setActiveState(item, handle, 'pointer');
        if (event.dataTransfer) {
          event.dataTransfer.effectAllowed = 'move';
          event.dataTransfer.setData('text/plain', item.getAttribute('data-photo-id') || 'photo');
          try {
            event.dataTransfer.setDragImage(item, Math.min(item.offsetWidth / 2, 160), 28);
          } catch (_) {
            // Browser may not support a custom drag image.
          }
        }
        announce(`${getItemCaption(item)} selected for moving.`);
      });

      handle.addEventListener('dragend', () => {
        if (!activeItem || activeMode !== 'pointer') {
          return;
        }
        const itemBeingMoved = activeItem;
        const caption = getItemCaption(itemBeingMoved);
        const wasDropped = dropped;
        clearActiveState({ restoreFocus: true, restoreOriginal: !wasDropped });
        if (!wasDropped) {
          announce(`Moving ${caption} was cancelled.`);
        }
      });

      handle.addEventListener('keydown', (event) => {
        const key = event.key;
        if (key === ' ' || key === 'Spacebar' || key === 'Enter') {
          event.preventDefault();
          if (activeItem === item && activeMode === 'keyboard') {
            const { items: updatedItems, total } = refreshOrdinals(container);
            const position = updatedItems.indexOf(item) + 1;
            clearActiveState({ restoreFocus: true });
            announce(`${getItemCaption(item)} placed in position ${position} of ${total}.`);
          } else {
            if (activeItem) {
              clearActiveState();
            }
            setActiveState(item, handle, 'keyboard');
            announce(`${getItemCaption(item)} selected. Use the arrow keys to move it, Enter to place it, or Escape to cancel.`);
          }
          return;
        }

        if ((key === 'Escape' || key === 'Esc') && activeItem === item) {
          event.preventDefault();
          clearActiveState({ restoreFocus: true, restoreOriginal: true });
          announce(`Moving ${getItemCaption(item)} was cancelled.`);
          return;
        }

        if (activeItem === item && activeMode === 'keyboard' && ['ArrowUp', 'ArrowLeft', 'ArrowDown', 'ArrowRight'].includes(key)) {
          event.preventDefault();
          const direction = key === 'ArrowUp' || key === 'ArrowLeft' ? -1 : 1;
          moveKeyboardItem(item, direction);
        }
      });
    });

    if (form) {
      form.addEventListener('submit', () => {
        refreshOrdinals(container);
        if (saveButton) {
          saveButton.disabled = true;
        }
        if (orderIndicator) {
          orderIndicator.textContent = 'Saving order…';
        }
      });
    }

    syncDirtyState();
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
