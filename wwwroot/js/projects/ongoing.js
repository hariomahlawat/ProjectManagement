// -------------------- Ongoing projects filter auto-submit --------------------
(function () {
  const form = document.getElementById('ongoingProjectsFilterForm');
  if (!form) {
    return;
  }

  const categorySelect = form.querySelector('[name="ProjectCategoryId"]');
  const officerSelect = form.querySelector('[name="ProjectOfficerId"]');
  const searchInput = form.querySelector('[name="Search"]');

  function submitFilters() {
    // Use requestSubmit so HTML5 validation and normal form behaviour are preserved
    if (typeof form.requestSubmit === 'function') {
      form.requestSubmit();
    } else {
      form.submit();
    }
  }

  // SECTION: KPI chip interactions
  const kpiChips = document.querySelectorAll('.js-kpi-chip');

  kpiChips.forEach((chip) => {
    chip.addEventListener('click', () => {
      if (!categorySelect) {
        return;
      }

      const categoryId = chip.dataset.categoryId ?? '';
      categorySelect.value = categoryId;
      submitFilters();
    });
  });

  categorySelect?.addEventListener('change', submitFilters);
  officerSelect?.addEventListener('change', submitFilters);

  // Pressing Enter in the search box already submits the form, but this keeps behaviour explicit
  searchInput?.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      submitFilters();
    }
  });
})();
