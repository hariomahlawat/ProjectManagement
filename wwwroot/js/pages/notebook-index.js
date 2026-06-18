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

  const updateFields = () => {
    const selected = selectedTypeName();

    for (const group of fieldGroups) {
      const groupType = normalise(group.getAttribute('data-notebook-type-fields'));
      const shouldShow = groupType === selected
        || (selected === 'idea' && groupType === 'note')
        || (selected === 'draft' && groupType === 'note');

      setGroupEnabled(group, shouldShow);
    }
  };

  typeSelect.addEventListener('change', updateFields);
  updateFields();
})();
