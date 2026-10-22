// -------------------- Ongoing projects filter auto-submit --------------------
(function () {
  const form = document.getElementById('ongoingProjectsFilterForm');
  if (!form) {
    return;
  }

  const stageSelect = form.querySelector('[name="PresentStageCode"]');
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

  // SECTION: Multi-select chips. Each selected chip is one hidden input; a click adds or
  // removes its own value and reloads, so stage and category chips combine freely.
  function toggleHiddenValue(name, value) {
    const existing = Array.from(form.querySelectorAll('input[type="hidden"]'))
      .find((input) => input.name === name && input.value === value);
    if (existing) {
      existing.remove();
      return;
    }

    const input = document.createElement('input');
    input.type = 'hidden';
    input.name = name;
    input.value = value;
    form.appendChild(input);
  }

  document.querySelectorAll('.js-stage-bucket-chip').forEach((chip) => {
    chip.addEventListener('click', () => {
      const bucket = chip.dataset.stageBucket;
      if (!bucket) {
        return;
      }

      toggleHiddenValue('Stage', bucket);
      submitFilters();
    });
  });

  document.querySelectorAll('.js-kpi-chip').forEach((chip) => {
    chip.addEventListener('click', () => {
      const categoryId = chip.dataset.categoryId;
      if (!categoryId) {
        return;
      }

      toggleHiddenValue('Category', categoryId);
      submitFilters();
    });
  });

  stageSelect?.addEventListener('change', submitFilters);
  officerSelect?.addEventListener('change', submitFilters);
  stageFlowSelect?.addEventListener('change', submitFilters);

  // SECTION: Search is explicit. Typing never reloads the page; Enter applies the query.
  searchInput?.addEventListener('keydown', (event) => {
    if (event.key !== 'Enter' || event.isComposing || event.repeat) {
      return;
    }

    event.preventDefault();
    submitFilters();
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
      toggleLabel.textContent = nextExpanded ? 'Hide earlier updates' : 'View earlier updates';
      if (toggleChevron) {
        toggleChevron.textContent = nextExpanded ? '▴' : '▾';
      }
    });
  });



  // SECTION: Fall back to the placeholder when a cover photo cannot be loaded
  document.querySelectorAll('.ongoing-card__thumb-img').forEach((img) => {
    const showPlaceholder = () => {
      const placeholder = document.createElement('div');
      placeholder.className = 'ongoing-card__thumb-placeholder';
      placeholder.setAttribute('aria-hidden', 'true');
      const icon = document.createElement('i');
      icon.className = 'bi bi-image';
      placeholder.appendChild(icon);
      img.replaceWith(placeholder);
    };

    if (img.complete && img.naturalWidth === 0) {
      showPlaceholder();
      return;
    }

    img.addEventListener('error', showPlaceholder, { once: true });
  });

  // SECTION: Bring the current stage into view on first render
  document.querySelectorAll('.briefing-timeline__rail[data-auto-focus-current="true"]').forEach((rail) => {
    const current = rail.querySelector('[data-current-stage="true"]');
    if (!current) return;

    // Measure against the rail itself: offsetLeft is relative to the nearest positioned
    // ancestor, which is the card, so cards scrolled to inconsistent positions.
    const offset = current.getBoundingClientRect().left - rail.getBoundingClientRect().left + rail.scrollLeft;
    rail.scrollLeft = Math.max(0, offset - Math.round(rail.clientWidth * 0.32));
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
