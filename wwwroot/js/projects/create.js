(function () {
  const isOngoing = document.getElementById('IsOngoing');
  const ongoingFields = document.getElementById('OngoingFields');
  const categorySelect = document.querySelector('[name="Input.CategoryId"]');
  const subCategorySelect = document.getElementById('SubCategoryId');

  function toggleOngoing() {
    if (!ongoingFields) {
      return;
    }

    const active = Boolean(isOngoing && isOngoing.checked);
    ongoingFields.classList.toggle('visually-hidden', !active);
  }

  async function loadSubCategories(categoryId, selectedValue) {
    if (!subCategorySelect) {
      return;
    }

    subCategorySelect.innerHTML = '<option value="">— (none) —</option>';
    subCategorySelect.disabled = true;

    if (!categoryId) {
      return;
    }

    try {
      const response = await fetch(`/api/categories/children?parentId=${encodeURIComponent(categoryId)}`, {
        credentials: 'same-origin'
      });

      if (!response.ok) {
        return;
      }

      const items = await response.json();
      if (!Array.isArray(items) || items.length === 0) {
        return;
      }

      for (const item of items) {
        const option = document.createElement('option');
        option.value = String(item.id);
        option.textContent = item.name;
        subCategorySelect.appendChild(option);
      }

      subCategorySelect.disabled = false;

      if (selectedValue) {
        subCategorySelect.value = selectedValue;
      }
    } catch (err) {
      // Network errors are ignored; the base category selection still works.
    }
  }

  isOngoing?.addEventListener('change', toggleOngoing);
  toggleOngoing();

  const preselectedSub = subCategorySelect?.dataset.selected || '';
  if (categorySelect && categorySelect.value) {
    loadSubCategories(categorySelect.value, preselectedSub);
  }

  categorySelect?.addEventListener('change', (event) => {
    const target = event.target;
    if (!(target instanceof HTMLSelectElement)) {
      return;
    }

    loadSubCategories(target.value, '');
  });
})();
