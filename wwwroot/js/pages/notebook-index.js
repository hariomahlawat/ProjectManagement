(() => {
  // SECTION: Notebook textarea autosize without inline scripts
  document.querySelectorAll('[data-autoresize]').forEach((textarea) => {
    const resize = () => {
      textarea.style.height = 'auto';
      textarea.style.height = `${textarea.scrollHeight}px`;
    };

    textarea.addEventListener('input', resize);
    resize();
  });

  // SECTION: Submit compact checklist toggles without inline handlers
  document.querySelectorAll('[data-submit-on-change]').forEach((input) => {
    input.addEventListener('change', () => input.form?.submit());
  });

  // SECTION: Keep editor type-specific fields in sync with the type dropdown
  const typeSelect = document.querySelector('[data-notebook-type-select]');
  const fieldGroups = Array.from(document.querySelectorAll('[data-notebook-type-fields]'));

  if (!typeSelect || !fieldGroups.length) {
    return;
  }

  const normalise = (value) => (value || '').toString().trim().toLowerCase();
  const selectedTypeName = () => {
    const option = typeSelect.options[typeSelect.selectedIndex];
    return normalise(option?.text || typeSelect.value);
  };

  const setGroupEnabled = (group, isEnabled) => {
    group.hidden = !isEnabled;
    group.querySelectorAll('input, select, textarea, button').forEach((control) => {
      control.disabled = !isEnabled;
    });
  };

  let updateFields = () => {
    const selected = selectedTypeName();

    for (const group of fieldGroups) {
      const groupTypes = normalise(group.getAttribute('data-notebook-type-fields'))
        .split(',')
        .map((value) => value.trim())
        .filter(Boolean);
      const shouldShow = groupTypes.includes(selected);

      setGroupEnabled(group, shouldShow);
    }
  };

  const sharedReminder = document.querySelector('[data-notebook-shared-reminder]');
  const sharedPriority = document.querySelector('[data-notebook-shared-priority]');

  const setSharedEnabled = (container, isEnabled) => {
    if (!container) {
      return;
    }

    container.hidden = !isEnabled;
    container.querySelectorAll('input, select, textarea').forEach((control) => {
      control.disabled = !isEnabled;
    });
  };

  const originalUpdateFields = updateFields;
  updateFields = () => {
    originalUpdateFields();
    const selected = selectedTypeName();
    setSharedEnabled(sharedReminder, selected !== 'checklist' && selected !== 'reminder');
    setSharedEnabled(sharedPriority, selected !== 'checklist' && selected !== 'reminder');
  };

  typeSelect.addEventListener('change', updateFields);
  updateFields();
})();
