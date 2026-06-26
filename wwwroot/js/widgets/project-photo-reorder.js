(function () {
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

      const previousButton = item.querySelector('[data-photo-move="previous"]');
      const nextButton = item.querySelector('[data-photo-move="next"]');
      if (previousButton) previousButton.disabled = index === 0;
      if (nextButton) nextButton.disabled = index === total - 1;

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

    refreshOrdinals(container);
    if (items.length < 2) {
      return;
    }

    const root = container.closest('[data-photo-reorder-root]') || container;
    const form = container.closest('[data-photo-order-form]');
    const statusRegion = root.querySelector('[data-photo-reorder-status]');
    const saveButton = form ? form.querySelector('[data-photo-order-save]') : null;
    const orderIndicator = form ? form.querySelector('[data-photo-order-indicator]') : null;
    const actionBar = form ? form.querySelector('[data-photo-order-actions]') : null;
    const resetButton = form ? form.querySelector('[data-photo-order-reset]') : null;
    const initialItems = Array.from(container.querySelectorAll('[data-photo-item]'));
    const initialSignature = getOrderSignature(container);

    let activeItem = null;
    let activeHandle = null;
    let activeMode = null;
    let activeOriginalIndex = -1;
    let placeholder = null;
    let dropped = false;
    let dragFrame = null;
    let pendingPointer = null;

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
      if (actionBar) {
        actionBar.hidden = !changed;
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

      item.querySelectorAll('[data-photo-move]').forEach((button) => {
        button.addEventListener('click', () => {
          const direction = button.getAttribute('data-photo-move') === 'previous' ? -1 : 1;
          moveKeyboardItem(item, direction);
          const updated = Array.from(container.querySelectorAll('[data-photo-item]'));
          const index = updated.indexOf(item);
          const previousButton = item.querySelector('[data-photo-move="previous"]');
          const nextButton = item.querySelector('[data-photo-move="next"]');
          if (previousButton) previousButton.disabled = index <= 0;
          if (nextButton) nextButton.disabled = index >= updated.length - 1;
        });
      });

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

    if (resetButton) {
      resetButton.addEventListener('click', () => {
        initialItems.forEach((item) => container.appendChild(item));
        refreshOrdinals(container);
        syncDirtyState();
        announce('Photo order changes discarded.');
      });
    }

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

  function initRemovePhotoDialog() {
    const dialog = document.querySelector('[data-photo-remove-dialog]');
    if (!dialog || typeof dialog.showModal !== 'function') {
      document.querySelectorAll('[data-remove-photo]').forEach((button) => {
        button.addEventListener('click', () => {
          const message = button.getAttribute('data-remove-message') || 'Remove this photo?';
          if (window.confirm(message)) {
            const formId = button.getAttribute('data-remove-form');
            const form = formId ? document.getElementById(formId) : null;
            if (form) form.submit();
          }
        });
      });
      return;
    }

    const title = dialog.querySelector('[data-photo-remove-title]');
    const message = dialog.querySelector('[data-photo-remove-message]');
    const confirmButton = dialog.querySelector('[data-photo-remove-confirm]');
    let pendingForm = null;

    document.querySelectorAll('[data-remove-photo]').forEach((button) => {
      button.addEventListener('click', () => {
        const formId = button.getAttribute('data-remove-form');
        pendingForm = formId ? document.getElementById(formId) : null;
        if (title) title.textContent = button.getAttribute('data-remove-title') || 'Remove photo?';
        if (message) message.textContent = button.getAttribute('data-remove-message') || 'This photograph will be permanently removed.';
        dialog.showModal();
      });
    });

    if (confirmButton) {
      confirmButton.addEventListener('click', (event) => {
        event.preventDefault();
        const form = pendingForm;
        pendingForm = null;
        dialog.close();
        if (form) form.submit();
      });
    }

    dialog.addEventListener('close', () => {
      pendingForm = null;
    });
  }

  document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('[data-photo-reorder]').forEach((container) => {
      initReorder(container);
    });

    initRemovePhotoDialog();
  });
})();
