// -------------------- Ongoing projects filter auto-submit --------------------
(function () {
  const form = document.getElementById('ongoingProjectsFilterForm');
  if (!form) {
    return;
  }

  const categorySelect = form.querySelector('[name="ProjectCategoryId"]');
  const stageSelect = form.querySelector('[name="PresentStageCode"]');
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
  stageSelect?.addEventListener('change', submitFilters);
  officerSelect?.addEventListener('change', submitFilters);

  // Pressing Enter in the search box already submits the form, but this keeps behaviour explicit
  searchInput?.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      submitFilters();
    }
  });

  // SECTION: Per-card remarks expand/collapse
  const remarkPanels = document.querySelectorAll('[data-remarks-panel]');

  remarkPanels.forEach((panel) => {
    const toggleButton = panel.querySelector('[data-remarks-toggle]');
    const expandedList = panel.querySelector('[data-remarks-expanded-list]');
    const toggleLabel = panel.querySelector('[data-remarks-toggle-label]');

    if (!toggleButton || !expandedList || !toggleLabel) {
      return;
    }

    toggleButton.addEventListener('click', () => {
      const isExpanded = panel.dataset.expanded === '1';
      const nextExpanded = !isExpanded;

      panel.dataset.expanded = nextExpanded ? '1' : '0';
      expandedList.hidden = !nextExpanded;
      toggleButton.setAttribute('aria-expanded', nextExpanded ? 'true' : 'false');
      toggleLabel.textContent = nextExpanded ? 'Collapse remarks' : 'Expand remarks';
    });
  });
})();
