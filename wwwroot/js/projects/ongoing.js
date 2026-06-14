// -------------------- Ongoing projects filter auto-submit --------------------
(function () {
  const form = document.getElementById('ongoingProjectsFilterForm');
  if (!form) {
    return;
  }

  const categorySelect = form.querySelector('[name="ProjectCategoryId"]');
  const stageSelect = form.querySelector('[name="PresentStageCode"]');
  const stageBucketInput = form.querySelector('[name="StageBucket"]');
  const officerSelect = form.querySelector('[name="ProjectOfficerId"]');
  const stageFlowSelect = form.querySelector('[name="StageFlow"]');
  const searchInput = form.querySelector('[name="Search"]');

  function submitFilters() {
    // Use requestSubmit so HTML5 validation and normal form behaviour are preserved
    if (typeof form.requestSubmit === 'function') {
      form.requestSubmit();
    } else {
      form.submit();
    }
  }

  // SECTION: Stage bucket quick-filter interactions
  const stageBucketChips = document.querySelectorAll('.js-stage-bucket-chip');

  stageBucketChips.forEach((chip) => {
    chip.addEventListener('click', () => {
      if (!stageBucketInput) {
        return;
      }

      const selectedBucket = chip.dataset.stageBucket ?? '';
      const currentBucket = stageBucketInput.value ?? '';

      if (currentBucket === selectedBucket) {
        stageBucketInput.value = '';
      } else {
        stageBucketInput.value = selectedBucket;
        if (stageSelect) {
          stageSelect.value = '';
        }
      }

      submitFilters();
    });
  });

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
  stageSelect?.addEventListener('change', () => {
    if (stageBucketInput) {
      stageBucketInput.value = '';
    }

    submitFilters();
  });
  officerSelect?.addEventListener('change', submitFilters);
  stageFlowSelect?.addEventListener('change', submitFilters);

  // SECTION: Search submits only when Enter is pressed
  searchInput?.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      submitFilters();
    }
  });

  // SECTION: Per-card remarks expand/collapse
  const remarkPanels = document.querySelectorAll('[data-remarks-panel]');

  remarkPanels.forEach((panel) => {
    const toggleButton = panel.querySelector('[data-remarks-toggle]');
    const expandedList = panel.querySelector('[data-remarks-expanded-list]');
    const toggleLabel = panel.querySelector('[data-remarks-toggle-label]');
    const toggleChevron = panel.querySelector('[data-remarks-toggle-chevron]');

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
      if (toggleChevron) {
        toggleChevron.textContent = nextExpanded ? '▴' : '▾';
      }
    });
  });
})();
