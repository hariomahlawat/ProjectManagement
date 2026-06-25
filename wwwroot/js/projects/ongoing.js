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

  // SECTION: Search auto-applies after a short pause; Enter remains immediate
  let searchTimer = null;
  searchInput?.addEventListener('input', () => {
    window.clearTimeout(searchTimer);
    searchTimer = window.setTimeout(submitFilters, 450);
  });

  searchInput?.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      window.clearTimeout(searchTimer);
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



  // SECTION: Bring the current stage into view on first render
  document.querySelectorAll('.briefing-timeline__rail[data-auto-focus-current="true"]').forEach((rail) => {
    const current = rail.querySelector('[data-current-stage="true"]');
    if (!current) return;

    const desiredLeft = Math.max(0, current.offsetLeft - Math.round(rail.clientWidth * 0.32));
    rail.scrollLeft = desiredLeft;
  });

  // SECTION: Mouse drag scrolling for complete lifecycle rails
  document.querySelectorAll('.briefing-timeline__rail').forEach((rail) => {
    let isDown = false;
    let startX = 0;
    let startScrollLeft = 0;

    rail.addEventListener('pointerdown', (event) => {
      if (event.pointerType === 'mouse' && event.button !== 0) return;
      isDown = true;
      startX = event.clientX;
      startScrollLeft = rail.scrollLeft;
      rail.classList.add('is-dragging');
      rail.setPointerCapture?.(event.pointerId);
    });

    rail.addEventListener('pointermove', (event) => {
      if (!isDown) return;
      rail.scrollLeft = startScrollLeft - (event.clientX - startX);
    });

    const endDrag = (event) => {
      if (!isDown) return;
      isDown = false;
      rail.classList.remove('is-dragging');
      if (rail.hasPointerCapture?.(event.pointerId)) {
        rail.releasePointerCapture(event.pointerId);
      }
    };

    rail.addEventListener('pointerup', endDrag);
    rail.addEventListener('pointercancel', endDrag);
    rail.addEventListener('mouseleave', () => {
      if (isDown) {
        isDown = false;
        rail.classList.remove('is-dragging');
      }
    });
  });
})();
