const ALLOWED_COLOURS = Object.freeze(['', 'white', 'blue', 'amber', 'green', 'rose', 'slate']);
const COLOUR_CLASSES = ALLOWED_COLOURS
  .filter(Boolean)
  .map((key) => `notebook-surface-colour-${key}`);

export function normaliseNotebookColour(value) {
  const key = String(value || '').trim().toLowerCase();
  return ALLOWED_COLOURS.includes(key) ? key : '';
}

export function applyNotebookSurfaceColour(element, value) {
  if (!element) return;
  const key = normaliseNotebookColour(value);
  element.classList.remove(...COLOUR_CLASSES);
  if (key) element.classList.add(`notebook-surface-colour-${key}`);
  element.dataset.colourValue = key;
}

export function setNotebookColourSelection(root, value) {
  if (!root) return;
  const key = normaliseNotebookColour(value);
  root.dataset.colourValue = key;
  root.querySelectorAll('[data-colour-choice]').forEach((choice) => {
    const selected = normaliseNotebookColour(choice.dataset.colourChoice) === key;
    choice.classList.toggle('is-selected', selected);
    choice.setAttribute('aria-checked', String(selected));
  });
}

export function closeNotebookColourPickers(scope = document, except = null) {
  scope.querySelectorAll('[data-notebook-colour-picker]').forEach((picker) => {
    if (picker === except) return;
    const popover = picker.querySelector('[data-colour-picker-popover]');
    const toggle = picker.querySelector('[data-colour-picker-toggle]');
    if (popover) popover.hidden = true;
    if (toggle) toggle.setAttribute('aria-expanded', 'false');
  });
}

export function initNotebookColourPicker(root, options = {}) {
  if (!root) throw new Error('Notebook colour picker root is required.');
  const toggle = root.querySelector('[data-colour-picker-toggle]');
  const popover = root.querySelector('[data-colour-picker-popover]');
  if (!toggle || !popover) throw new Error('Notebook colour picker markup is incomplete.');

  let value = normaliseNotebookColour(options.value ?? root.dataset.colourValue);
  let busy = false;
  setNotebookColourSelection(root, value);

  const close = () => {
    popover.hidden = true;
    toggle.setAttribute('aria-expanded', 'false');
  };

  const open = () => {
    closeNotebookColourPickers(document, root);
    popover.hidden = false;
    toggle.setAttribute('aria-expanded', 'true');
    popover.querySelector('.is-selected,[data-colour-choice]')?.focus?.();
  };

  const setValue = (next, { notify = false } = {}) => {
    const normalised = normaliseNotebookColour(next);
    const previous = value;
    value = normalised;
    setNotebookColourSelection(root, value);
    if (notify && previous !== value) options.onSelect?.(value, previous);
  };

  toggle.addEventListener('click', (event) => {
    event.preventDefault();
    event.stopPropagation();
    if (busy) return;
    popover.hidden ? open() : close();
  });

  popover.addEventListener('click', async (event) => {
    const choice = event.target.closest('[data-colour-choice]');
    if (!choice || busy) return;
    event.preventDefault();
    event.stopPropagation();
    const next = normaliseNotebookColour(choice.dataset.colourChoice);
    const previous = value;
    setValue(next);
    close();
    if (previous === next) return;
    busy = true;
    root.classList.add('is-busy');
    try {
      await options.onSelect?.(next, previous);
    } catch (error) {
      setValue(previous);
      throw error;
    } finally {
      busy = false;
      root.classList.remove('is-busy');
    }
  });

  root.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && !popover.hidden) {
      event.preventDefault();
      close();
      toggle.focus();
    }
  });

  return {
    open,
    close,
    getValue: () => value,
    setValue,
    setBusy(next) {
      busy = Boolean(next);
      root.classList.toggle('is-busy', busy);
      toggle.disabled = busy;
    }
  };
}
