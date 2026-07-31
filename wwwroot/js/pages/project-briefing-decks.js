const root = document.querySelector('[data-pbd-root]');

if (root) {
  const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
  const deckElement = root.querySelector('[data-deck-id]');
  const deckId = Number(deckElement?.dataset.deckId || 0);
  const storagePrefix = `projectBriefingDeck:${deckId}:`;

  class RequestError extends Error {
    constructor(message, status, payload) {
      super(message);
      this.name = 'RequestError';
      this.status = status;
      this.payload = payload;
    }
  }

  const requestJson = async (url, options = {}) => {
    const response = await fetch(url, {
      credentials: 'same-origin',
      ...options,
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
        'X-CSRF-TOKEN': token,
        'X-Requested-With': 'XMLHttpRequest',
        ...(options.headers || {})
      }
    });

    const contentType = response.headers.get('content-type') || '';
    const payload = contentType.includes('application/json') ? await response.json() : null;
    if (!response.ok) {
      throw new RequestError(payload?.message || payload?.title || `Request failed (${response.status}).`, response.status, payload);
    }
    return payload;
  };

  const setState = (element, message, state = '') => {
    if (!element) return;
    element.textContent = message || '';
    element.classList.remove('is-saving', 'is-saved', 'is-error', 'is-success');
    if (state) element.classList.add(`is-${state}`);
  };

  const rowVersionInputs = [...root.querySelectorAll('input[name="RowVersion"], input[name="rowVersion"]')];
  const currentRowVersion = () => rowVersionInputs[0]?.value || '';
  const updateRowVersion = (value) => {
    if (!value) return;
    rowVersionInputs.forEach((input) => { input.value = value; });
  };

  const normalize = (value) => String(value ?? '').trim().toLocaleLowerCase();
  const formatDate = (value) => {
    if (!value) return '';
    const match = String(value).match(/^(\d{4})-(\d{2})-(\d{2})/);
    if (!match) return String(value);
    const date = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
    return new Intl.DateTimeFormat('en-IN', { day: '2-digit', month: 'short', year: 'numeric' }).format(date);
  };

  // Dedicated settings drawer with canonical dirty-state tracking.
  const settingsLauncher = root.querySelector('[data-pbd-settings-open]');
  const settingsDrawer = root.querySelector('[data-pbd-settings-drawer]');
  const settingsBackdrop = root.querySelector('[data-pbd-settings-backdrop]');
  const settingsForm = root.querySelector('[data-pbd-settings-form]');
  const settingsSave = root.querySelector('[data-pbd-settings-save]');
  const settingsDirtyBadge = root.querySelector('[data-pbd-settings-dirty]');
  const settingsStatus = root.querySelector('[data-pbd-settings-status]');
  let settingsInitialState = '';
  let settingsDirty = false;
  let settingsReturnFocus = null;
  const settingsCollapsibleSections = settingsDrawer
    ? [...settingsDrawer.querySelectorAll('[data-pbd-settings-collapsible]')]
    : [];
  let restoringSettingsSections = false;
  const currentSettingsLayout = () => root.querySelector('input[name="Layout"]:checked')?.value || 'StandardBriefing';
  const settingsSectionStorageKey = () => `${storagePrefix}settingsSections:${currentSettingsLayout()}`;
  const defaultOpenSettingsSections = () => currentSettingsLayout() === 'ProjectUpdateSheet'
    ? new Set(['appearance', 'summary'])
    : new Set(['content']);

  const persistSettingsSectionState = () => {
    if (restoringSettingsSections) return;
    const openSections = settingsCollapsibleSections
      .filter((section) => !section.hidden && section.open)
      .map((section) => section.dataset.pbdSettingsSection)
      .filter(Boolean);
    sessionStorage.setItem(settingsSectionStorageKey(), JSON.stringify(openSections));
  };

  const restoreSettingsSectionState = () => {
    if (settingsCollapsibleSections.length === 0) return;
    let openSections = defaultOpenSettingsSections();
    const saved = sessionStorage.getItem(settingsSectionStorageKey());
    if (saved) {
      try {
        const parsed = JSON.parse(saved);
        if (Array.isArray(parsed)) openSections = new Set(parsed.map(String));
      } catch {
        sessionStorage.removeItem(settingsSectionStorageKey());
      }
    }
    restoringSettingsSections = true;
    settingsCollapsibleSections.forEach((section) => {
      const key = section.dataset.pbdSettingsSection || '';
      section.open = !section.hidden && openSections.has(key);
    });
    restoringSettingsSections = false;
  };

  settingsCollapsibleSections.forEach((section) => section.addEventListener('toggle', persistSettingsSectionState));

  const serializeSettings = () => {
    if (!(settingsForm instanceof HTMLFormElement)) return '';
    return [...new FormData(settingsForm).entries()]
      .filter(([name]) => name !== 'RowVersion')
      .map(([name, value]) => `${name}=${String(value).trim()}`)
      .sort()
      .join('&');
  };

  const setSettingsDirty = (dirty) => {
    settingsDirty = dirty;
    settingsDirtyBadge?.toggleAttribute('hidden', !dirty);
    if (settingsSave) settingsSave.disabled = !dirty;
    if (settingsStatus) {
      settingsStatus.textContent = dirty ? 'Unsaved settings' : 'No unsaved changes';
      settingsStatus.classList.toggle('is-dirty', dirty);
      settingsStatus.classList.remove('is-saving');
    }
  };

  const refreshSettingsDirtyState = () => setSettingsDirty(serializeSettings() !== settingsInitialState);

  const focusableSettingsElements = () => settingsDrawer
    ? [...settingsDrawer.querySelectorAll('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [href], [tabindex]:not([tabindex="-1"])')]
        .filter((element) => !element.hidden && element.getClientRects().length > 0)
    : [];

  const openSettingsDrawer = () => {
    if (!settingsDrawer || !settingsBackdrop || !settingsLauncher) return;
    settingsReturnFocus = document.activeElement;
    settingsDrawer.classList.add('is-open');
    settingsDrawer.setAttribute('aria-hidden', 'false');
    settingsBackdrop.hidden = false;
    settingsLauncher.setAttribute('aria-expanded', 'true');
    document.body.classList.add('pbd-settings-drawer-open');
    restoreSettingsSectionState();
    window.requestAnimationFrame(() => focusableSettingsElements()[0]?.focus());
  };

  const closeSettingsDrawer = ({ discard = true, force = false } = {}) => {
    if (!settingsDrawer || !settingsBackdrop || !settingsLauncher) return false;
    if (settingsDirty && !force && !window.confirm('Discard unsaved deck settings?')) return false;
    if (discard && settingsDirty && settingsForm instanceof HTMLFormElement) {
      settingsForm.reset();
      syncTemplateSettings();
      setSettingsDirty(false);
    }
    settingsDrawer.classList.remove('is-open');
    settingsDrawer.setAttribute('aria-hidden', 'true');
    settingsBackdrop.hidden = true;
    settingsLauncher.setAttribute('aria-expanded', 'false');
    document.body.classList.remove('pbd-settings-drawer-open');
    if (settingsReturnFocus instanceof HTMLElement) settingsReturnFocus.focus();
    return true;
  };

  settingsLauncher?.addEventListener('click', openSettingsDrawer);
  root.querySelectorAll('[data-pbd-settings-close]').forEach((button) => button.addEventListener('click', () => closeSettingsDrawer()));
  settingsBackdrop?.addEventListener('click', () => closeSettingsDrawer());
  settingsDrawer?.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      closeSettingsDrawer();
      return;
    }
    if (event.key !== 'Tab') return;
    const focusable = focusableSettingsElements();
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable.at(-1);
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  });
  settingsForm?.addEventListener('input', refreshSettingsDirtyState);
  settingsForm?.addEventListener('change', refreshSettingsDirtyState);
  settingsForm?.addEventListener('submit', (event) => {
    if (!settingsDirty) {
      event.preventDefault();
      return;
    }
    if (settingsSave) settingsSave.disabled = true;
    if (settingsStatus) {
      settingsStatus.textContent = 'Saving settings…';
      settingsStatus.classList.remove('is-dirty');
      settingsStatus.classList.add('is-saving');
    }
    settingsDirty = false;
  });
  window.addEventListener('beforeunload', (event) => {
    if (!settingsDirty) return;
    event.preventDefault();
    event.returnValue = '';
  });

  const confirmSettingsNavigation = () => {
    if (!settingsDirty) return true;
    if (!window.confirm('Discard unsaved deck settings and continue?')) return false;
    setSettingsDirty(false);
    return true;
  };

  document.addEventListener('click', (event) => {
    if (!settingsDirty || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    const link = event.target.closest('a[href]');
    if (!(link instanceof HTMLAnchorElement) || settingsDrawer?.contains(link)) return;
    const href = link.getAttribute('href') || '';
    if (!href || href.startsWith('#') || href.startsWith('javascript:') || link.target === '_blank' || link.hasAttribute('download')) return;
    if (!confirmSettingsNavigation()) event.preventDefault();
  }, true);

  document.addEventListener('submit', (event) => {
    if (!settingsDirty || event.defaultPrevented) return;
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || form === settingsForm || form.matches('[data-pbd-generate-form]')) return;
    if (!confirmSettingsNavigation()) event.preventDefault();
  }, true);

  // Preserve the user's working context for this deck.
  const restoreScroll = () => {
    const saved = Number(sessionStorage.getItem(`${storagePrefix}scroll`) || 0);
    if (saved > 0) window.requestAnimationFrame(() => window.scrollTo({ top: saved, behavior: 'auto' }));
  };
  window.addEventListener('pagehide', () => sessionStorage.setItem(`${storagePrefix}scroll`, String(window.scrollY)));
  restoreScroll();

  // Collapse the secondary saved-deck rail on laptop widths without losing deck context.
  const deckWorkspace = root.querySelector('.pbd-workspace');
  const savedDecksPanel = root.querySelector('[data-pbd-saved-decks]');
  const savedDecksToggle = root.querySelector('[data-pbd-decks-toggle]');
  const savedDecksMedia = window.matchMedia('(max-width: 1499px)');
  const savedDecksStorageKey = `${storagePrefix}savedDecksOpen`;

  const setSavedDecksOpen = (open, persist = true) => {
    if (!deckWorkspace || !savedDecksPanel || !savedDecksToggle) return;
    const effectiveOpen = savedDecksMedia.matches ? open : true;
    deckWorkspace.classList.toggle('is-decks-collapsed', !effectiveOpen);
    savedDecksToggle.setAttribute('aria-expanded', String(effectiveOpen));
    const activeDeckName = savedDecksToggle.dataset.pbdActiveDeckName || 'current deck';
    savedDecksToggle.title = effectiveOpen ? 'Hide shared decks' : `Show shared decks — ${activeDeckName}`;
    savedDecksToggle.setAttribute('aria-label', effectiveOpen
      ? `Hide shared decks. Current deck: ${activeDeckName}`
      : `Show shared decks. Current deck: ${activeDeckName}`);
    const label = savedDecksToggle.querySelector('span');
    if (label) label.textContent = effectiveOpen ? 'Hide decks' : 'Shared decks';
    if (persist && savedDecksMedia.matches) {
      sessionStorage.setItem(savedDecksStorageKey, effectiveOpen ? 'true' : 'false');
    }
  };

  const syncSavedDecksForViewport = () => {
    if (!savedDecksMedia.matches) {
      setSavedDecksOpen(true, false);
      return;
    }
    const saved = sessionStorage.getItem(savedDecksStorageKey);
    setSavedDecksOpen(saved === 'true', false);
  };

  savedDecksToggle?.addEventListener('click', () => {
    const currentlyOpen = savedDecksToggle.getAttribute('aria-expanded') === 'true';
    setSavedDecksOpen(!currentlyOpen);
  });
  savedDecksMedia.addEventListener?.('change', syncSavedDecksForViewport);
  syncSavedDecksForViewport();

  // Selection method tabs.
  const tabs = [...root.querySelectorAll('[data-pbd-selector-tab]')];
  const panels = [...root.querySelectorAll('[data-pbd-selector-panel]')];
  const activatePanel = (name, focus = false) => {
    tabs.forEach((tab) => {
      const active = tab.dataset.pbdSelectorTab === name;
      tab.classList.toggle('is-active', active);
      tab.setAttribute('aria-selected', String(active));
      tab.tabIndex = active ? 0 : -1;
      if (active && focus) tab.focus();
    });
    panels.forEach((panel) => {
      const active = panel.dataset.pbdSelectorPanel === name;
      panel.hidden = !active;
      panel.classList.toggle('is-active', active);
    });
    sessionStorage.setItem(`${storagePrefix}activeTab`, name);
  };

  const savedTab = sessionStorage.getItem(`${storagePrefix}activeTab`);
  if (savedTab && tabs.some((tab) => tab.dataset.pbdSelectorTab === savedTab)) activatePanel(savedTab);

  tabs.forEach((tab, index) => {
    tab.addEventListener('click', () => activatePanel(tab.dataset.pbdSelectorTab || 'quick'));
    tab.addEventListener('keydown', (event) => {
      if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      let target = index;
      if (event.key === 'ArrowRight') target = (index + 1) % tabs.length;
      if (event.key === 'ArrowLeft') target = (index - 1 + tabs.length) % tabs.length;
      if (event.key === 'Home') target = 0;
      if (event.key === 'End') target = tabs.length - 1;
      activatePanel(tabs[target].dataset.pbdSelectorTab || 'quick', true);
    });
  });


  const selectorDetails = root.querySelector('[data-pbd-selector-details]');
  root.querySelectorAll('[data-pbd-open-selector]').forEach((button) => button.addEventListener('click', () => {
    if (!(selectorDetails instanceof HTMLDetailsElement)) return;
    selectorDetails.open = true;
    selectorDetails.scrollIntoView({ behavior: 'smooth', block: 'start' });
    window.setTimeout(() => selectorDetails.querySelector('[data-pbd-selector-tab].is-active')?.focus(), 350);
  }));

  const metric = (name) => root.querySelector(`[data-pbd-metric="${name}"]`);
  const selectedTotal = root.querySelector('[data-pbd-selected-total]');
  const slideTotal = root.querySelector('[data-pbd-slide-total]');
  const slideBreakdown = root.querySelector('[data-pbd-slide-breakdown]');
  const usedGapList = root.querySelector('[data-pbd-used-gap-list]');
  const additionalGapList = root.querySelector('[data-pbd-additional-gap-list]');
  const gapSummary = root.querySelector('[data-pbd-gap-summary]');
  const gapSummaryDetail = root.querySelector('[data-pbd-gap-summary-detail]');
  const gapSummaryIcon = root.querySelector('[data-pbd-gap-summary-icon]');
  const generateButton = root.querySelector('[data-pbd-generate]');
  const activeSavedCard = root.querySelector('.pbd-saved-card.is-active small');

  const selectedLayout = currentSettingsLayout;
  const isUpdateSheet = (deck = null) => {
    const layout = deck?.layout ?? selectedLayout();
    return layout === 'ProjectUpdateSheet' || layout === 2 || layout === '2';
  };
  const includesDetailedSlides = () => {
    if (isUpdateSheet()) return true;
    const value = root.querySelector('input[name="PresentationMode"]:checked')?.value;
    return value === 'DetailedProjects' || value === 'Combined';
  };
  const includesCostRd = () => isUpdateSheet() || ['CostRdOnly', 'Both'].includes(root.querySelector('input[name="CostMode"]:checked')?.value || '');
  const includesProliferation = () => !isUpdateSheet() && ['ProliferationOnly', 'Both'].includes(root.querySelector('input[name="CostMode"]:checked')?.value || '');
  const narrativeMode = () => isUpdateSheet() ? 'ProjectBrief' : (root.querySelector('input[name="NarrativeMode"]:checked')?.value || 'CapabilityOverview');
  const includesCapabilities = () => !isUpdateSheet() && ['CapabilityOverview', 'Both'].includes(narrativeMode());
  const includesProjectBrief = () => isUpdateSheet() || ['ProjectBrief', 'Both'].includes(narrativeMode());

  const syncPreflightRequirementVisibility = () => {
    const visibility = {
      status: true,
      'cost-rd': includesCostRd(),
      proliferation: includesProliferation(),
      capability: includesDetailedSlides() && includesCapabilities(),
      'project-brief': includesDetailedSlides() && includesProjectBrief(),
      photo: includesDetailedSlides()
    };
    root.querySelectorAll('[data-pbd-requirement]').forEach((element) => {
      element.hidden = !visibility[element.dataset.pbdRequirement];
    });
  };

  const syncTemplateSettings = () => {
    const updateSheet = isUpdateSheet();
    root.querySelectorAll('[data-pbd-standard-section]').forEach((element) => { element.hidden = updateSheet; });
    root.querySelectorAll('[data-pbd-standard-settings]').forEach((element) => { element.hidden = updateSheet; });
    root.querySelectorAll('[data-pbd-update-settings]').forEach((element) => { element.hidden = !updateSheet; });
    root.querySelectorAll('[data-pbd-proliferation-column]').forEach((element) => { element.hidden = updateSheet; });
    root.querySelector('[data-pbd-presentation-design]')?.classList.toggle('is-update-sheet', updateSheet);
    const appearanceTitle = root.querySelector('[data-pbd-settings-appearance-title]');
    if (appearanceTitle) appearanceTitle.textContent = updateSheet ? 'Header branding' : 'Appearance';
    restoreSettingsSectionState();
    syncPreflightRequirementVisibility();
  };
  root.querySelectorAll('[data-pbd-layout-choice], input[name="PresentationMode"], input[name="CostMode"], input[name="NarrativeMode"]')
    .forEach((choice) => choice.addEventListener('change', syncTemplateSettings));
  syncTemplateSettings();
  settingsInitialState = serializeSettings();
  setSettingsDirty(false);
  if (root.querySelector('[data-pbd-open-settings="true"]')) {
    window.setTimeout(openSettingsDrawer, 0);
  }
  const hasCapabilityOverview = (project) => {
    const value = normalize(project?.briefDescription);
    return Boolean(value)
      && value !== 'brief description not recorded.'
      && value !== 'capability overview not recorded.';
  };
  const hasProjectBrief = (project) => {
    const value = normalize(project?.projectBrief);
    return Boolean(value) && value !== 'project brief not recorded.';
  };
  const hasSelectedNarrative = (project) =>
    (!includesCapabilities() || hasCapabilityOverview(project))
    && (!includesProjectBrief() || hasProjectBrief(project));

  const updateReadinessSummary = (deck) => {
    const readiness = deck?.readiness || {};
    const estimate = deck?.slideEstimate || {};
    const total = Number(readiness.projectCount || 0);
    if (metric('status')) metric('status').textContent = `${readiness.externalStatusAvailableCount || 0}/${total}`;
    if (metric('cost-rd')) metric('cost-rd').textContent = `${readiness.costRdAvailableCount || 0}/${total}`;
    if (metric('proliferation')) metric('proliferation').textContent = `${readiness.proliferationCostAvailableCount || 0}/${total}`;
    if (metric('capability')) metric('capability').textContent = `${readiness.capabilityOverviewAvailableCount ?? readiness.descriptionAvailableCount ?? 0}/${total}`;
    if (metric('project-brief')) metric('project-brief').textContent = `${readiness.projectBriefAvailableCount || 0}/${total}`;
    if (metric('photo')) metric('photo').textContent = `${readiness.coverPhotoAvailableCount || 0}/${total}`;
    root.querySelectorAll('[data-pbd-readiness-filter]').forEach((button) => {
      if (!(button instanceof HTMLButtonElement) || !button.classList.contains('pbd-preflight-metric')) return;
      const metricName = button.querySelector('[data-pbd-metric]')?.dataset.pbdMetric;
      const available = metricName ? Number((metric(metricName)?.textContent || '0/0').split('/')[0] || 0) : 0;
      button.disabled = total === 0 || available >= total;
      button.title = button.disabled ? 'No missing projects for this requirement' : 'Filter the project list to missing content';
    });
    if (selectedTotal) selectedTotal.textContent = String(total);
    if (generateButton) generateButton.disabled = total === 0;
    syncPreflightRequirementVisibility();

    if (slideTotal) slideTotal.textContent = `${estimate.totalSlides || 0} ${(estimate.totalSlides || 0) === 1 ? 'slide' : 'slides'}`;
    if (slideBreakdown) {
      if (isUpdateSheet(deck)) {
        const cover = deck?.includeCoverSlide === false ? 0 : 1;
        const portfolio = deck?.includePortfolioSummarySlide === false ? 0 : 1;
        slideBreakdown.textContent = `Cover ${cover} · Portfolio summary ${portfolio} · Project sheets ${estimate.projectUpdateSheetSlides || 0}`;
      } else {
        const capabilitySlides = Number(estimate.detailedProjectSlides || 0);
        const continuationSlides = Number(estimate.capabilityContinuationSlides || 0);
        const projectBriefSlides = Number(estimate.projectBriefSlides || 0);
        const parts = [
          `Cover and portfolio ${estimate.coverAndPortfolioSlides || 0}`,
          `Summary ${estimate.summarySlides || 0}`,
          `Tables ${estimate.executiveTableSlides || 0}`
        ];
        if (capabilitySlides > 0) parts.push(`Capability slides ${capabilitySlides}`);
        if (projectBriefSlides > 0) parts.push(`Project brief slides ${projectBriefSlides}`);
        if (continuationSlides > 0) parts.push(`Capability continuations ${continuationSlides}`);
        slideBreakdown.textContent = parts.join(' · ');
      }
    }

    if (activeSavedCard) {
      const suffix = total === 1 ? 'project' : 'projects';
      activeSavedCard.textContent = `${total} ${suffix} · updated just now`;
    }

    const renderGapList = (container, gaps, emptyText, emptyClass = 'is-ready') => {
      if (!container) return;
      container.replaceChildren();
      const items = gaps.length > 0
        ? gaps
        : [{ icon: emptyClass === 'is-ready' ? 'bi-check-circle' : 'bi-info-circle', label: emptyText, count: 0, className: emptyClass }];
      items.forEach((gap) => {
        const item = document.createElement('li');
        if (gap.className) item.className = gap.className;
        const content = gap.filter ? document.createElement('button') : document.createDocumentFragment();
        if (content instanceof HTMLButtonElement) {
          content.type = 'button';
          content.dataset.pbdReadinessFilter = gap.filter;
          content.title = `Show projects missing ${gap.label.toLocaleLowerCase()}`;
        }
        const icon = document.createElement('i');
        icon.className = `bi ${gap.icon}`;
        icon.setAttribute('aria-hidden', 'true');
        const label = document.createElement('span');
        label.textContent = gap.label;
        content.append(icon, label);
        if (gap.count > 0) {
          const count = document.createElement('strong');
          count.textContent = `${gap.count} missing`;
          content.append(count);
        }
        item.append(content);
        container.append(item);
      });
    };

    if (total === 0) {
      renderGapList(usedGapList, [], 'Add projects to generate a deck.', 'is-neutral');
      renderGapList(additionalGapList, [], 'No project metadata to review.', 'is-neutral');
      if (gapSummary) gapSummary.textContent = 'Add projects to run the deck preflight';
      if (gapSummaryDetail) gapSummaryDetail.textContent = 'Preflight checks begin after projects are selected.';
      if (gapSummaryIcon) gapSummaryIcon.className = 'bi bi-info-circle';
      return;
    }

    const missingStatus = total - Number(readiness.externalStatusAvailableCount || 0);
    const missingCost = total - Number(readiness.costRdAvailableCount || 0);
    const missingProliferation = total - Number(readiness.proliferationCostAvailableCount || 0);
    const missingPhoto = total - Number(readiness.coverPhotoAvailableCount || 0);
    const missingCapabilities = total - Number(readiness.capabilityOverviewAvailableCount ?? readiness.descriptionAvailableCount ?? 0);
    const missingProjectBriefs = total - Number(readiness.projectBriefAvailableCount || 0);

    const usedGaps = [];
    if (missingStatus > 0) usedGaps.push({ icon: 'bi-chat-left-text', label: 'External status', count: missingStatus, filter: 'missing-status' });
    if (includesCostRd() && missingCost > 0) usedGaps.push({ icon: 'bi-currency-rupee', label: 'Cost (R&D)', count: missingCost, filter: 'missing-cost-rd' });
    if (includesProliferation() && missingProliferation > 0) usedGaps.push({ icon: 'bi-boxes', label: 'Proliferation cost', count: missingProliferation, filter: 'missing-proliferation' });
    if (includesDetailedSlides() && missingPhoto > 0) usedGaps.push({ icon: 'bi-image', label: 'PowerPoint-ready photograph', count: missingPhoto, filter: 'missing-photo' });
    if (includesDetailedSlides() && includesCapabilities() && missingCapabilities > 0) usedGaps.push({ icon: 'bi-list-check', label: 'Capability overview', count: missingCapabilities, filter: 'missing-description' });
    if (includesDetailedSlides() && includesProjectBrief() && missingProjectBriefs > 0) usedGaps.push({ icon: 'bi-file-earmark-text', label: 'Project brief', count: missingProjectBriefs, filter: 'missing-description' });

    const additionalGaps = [];
    if (isUpdateSheet(deck)) {
      const missingArpp = total - Number(readiness.arppDetailsAvailableCount || 0);
      const missingAon = total - Number(readiness.aonDateAvailableCount || 0);
      const missingSo = total - Number(readiness.supplyOrderDateAvailableCount || 0);
      const missingJdp = total - Number(readiness.jdpAvailableCount || 0);
      const missingPdc = Math.max(0, Number(readiness.developmentProjectCount || 0) - Number(readiness.developmentPdcAvailableCount || 0));
      const missingOfficer = total - Number(readiness.projectOfficerAvailableCount || 0);
      const missingLine = total - Number(readiness.lineDirectorateAvailableCount || 0);
      if (missingArpp > 0) additionalGaps.push({ icon: 'bi-journal-text', label: 'Complete ARPP/PPP details', count: missingArpp });
      if (missingAon > 0) additionalGaps.push({ icon: 'bi-calendar-check', label: 'AoN date', count: missingAon });
      if (missingSo > 0) additionalGaps.push({ icon: 'bi-calendar2-event', label: 'Supply-order date', count: missingSo });
      if (missingJdp > 0) additionalGaps.push({ icon: 'bi-building', label: 'Linked JDP', count: missingJdp });
      if (missingPdc > 0) additionalGaps.push({ icon: 'bi-calendar-x', label: 'Development PDC', count: missingPdc });
      if (missingOfficer > 0) additionalGaps.push({ icon: 'bi-person-badge', label: 'Project Officer rank and full name', count: missingOfficer });
      if (missingLine > 0) additionalGaps.push({ icon: 'bi-diagram-3', label: 'Line Directorate', count: missingLine });
    }

    renderGapList(usedGapList, usedGaps, 'All selected-layout content is available.');
    renderGapList(
      additionalGapList,
      additionalGaps,
      isUpdateSheet(deck) ? 'Supporting project metadata is complete.' : 'No additional metadata is required by this template.',
      isUpdateSheet(deck) ? 'is-ready' : 'is-neutral');

    const projects = deck?.projects || [];
    const affectedProjectCount = projects.filter((project) => {
      const statusMissing = !String(project.externalStatus || '').trim();
      const costMissing = includesCostRd() && !project.costRd?.isAvailable;
      const proliferationMissing = includesProliferation() && !project.proliferationCost?.isAvailable;
      const photoMissing = includesDetailedSlides() && !project.hasCoverPhoto;
      const capabilityMissing = includesDetailedSlides() && includesCapabilities() && !hasCapabilityOverview(project);
      const briefMissing = includesDetailedSlides() && includesProjectBrief() && !hasProjectBrief(project);
      return statusMissing || costMissing || proliferationMissing || photoMissing || capabilityMissing || briefMissing;
    }).length;
    if (gapSummary) {
      gapSummary.textContent = affectedProjectCount === 0
        ? 'Selected content is ready'
        : `${affectedProjectCount} ${affectedProjectCount === 1 ? 'project has' : 'projects have'} content gaps`;
    }
    if (gapSummaryDetail) {
      gapSummaryDetail.textContent = usedGaps.length === 0
        ? 'All selected-layout content is available.'
        : usedGaps.map((gap) => `${gap.count} missing ${gap.label.toLocaleLowerCase()}`).join(' · ');
    }
    if (gapSummaryIcon) {
      gapSummaryIcon.className = affectedProjectCount === 0 ? 'bi bi-check-circle' : 'bi bi-exclamation-circle';
    }
  };

  // Selected-project table management.
  const sortableBody = root.querySelector('[data-pbd-sortable]');
  const selectedTableWrap = root.querySelector('[data-pbd-selected-table-wrap]');
  const emptyProjects = root.querySelector('[data-pbd-empty-projects]');
  const noFilterResults = root.querySelector('[data-pbd-no-filter-results]');
  const selectedSection = root.querySelector('[data-pbd-selected-section]');
  const selectedToolbar = root.querySelector('[data-pbd-selected-toolbar]');
  const selectedSearch = root.querySelector('[data-pbd-selected-search]');
  const selectedStage = root.querySelector('[data-pbd-selected-stage]');
  const selectedReadiness = root.querySelector('[data-pbd-selected-readiness]');
  const visibleCount = root.querySelector('[data-pbd-visible-count]');
  const clearSelectedFilters = root.querySelector('[data-pbd-clear-selected-filters]');
  const selectVisible = root.querySelector('[data-pbd-select-visible]');
  const bulkTop = root.querySelector('[data-pbd-bulk-top]');
  const bulkBottom = root.querySelector('[data-pbd-bulk-bottom]');
  const bulkRemove = root.querySelector('[data-pbd-bulk-remove]');
  const filterReorderNote = root.querySelector('[data-pbd-filter-reorder-note]');
  const sortStatus = root.querySelector('[data-pbd-sort-status]');
  let sortable = null;

  if (selectedSearch) selectedSearch.value = sessionStorage.getItem(`${storagePrefix}selectedSearch`) || '';
  if (selectedStage) selectedStage.value = sessionStorage.getItem(`${storagePrefix}selectedStage`) || '';
  if (selectedReadiness) selectedReadiness.value = sessionStorage.getItem(`${storagePrefix}selectedReadiness`) || '';

  const currentRows = () => [...(sortableBody?.querySelectorAll('tr[data-project-id]') || [])];
  const stageKey = (row) => `${row?.dataset.stageOrder || ''}:${row?.dataset.stageCode || row?.dataset.stage || ''}`;
  const sameStage = (first, second) => stageKey(first) === stageKey(second);
  const selectedRowIds = () => currentRows()
    .filter((row) => row.querySelector('[data-pbd-row-select]')?.checked)
    .map((row) => Number(row.dataset.projectId));

  const refreshBulkActions = () => {
    const count = selectedRowIds().length;
    const filtered = Boolean(normalize(selectedSearch?.value) || selectedStage?.value || selectedReadiness?.value);
    if (bulkTop) bulkTop.disabled = count === 0 || filtered;
    if (bulkBottom) bulkBottom.disabled = count === 0 || filtered;
    if (bulkRemove) bulkRemove.disabled = count === 0;
  };

  const selectedFilterState = () => ({
    term: normalize(selectedSearch?.value),
    stage: selectedStage?.value || '',
    readiness: selectedReadiness?.value || ''
  });

  const persistSelectedFilters = () => {
    if (selectedSearch) sessionStorage.setItem(`${storagePrefix}selectedSearch`, selectedSearch.value);
    if (selectedStage) sessionStorage.setItem(`${storagePrefix}selectedStage`, selectedStage.value);
    if (selectedReadiness) sessionStorage.setItem(`${storagePrefix}selectedReadiness`, selectedReadiness.value);
  };

  let filterHighlightTimer = 0;
  const revealFirstFilterMatch = (row) => {
    if (!row || row.hidden) return;
    const rect = row.getBoundingClientRect();
    const topbarHeight = Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--topbar-height')) || 56;
    const toolbarHeight = selectedToolbar?.getBoundingClientRect().height || 58;
    const clearance = topbarHeight + toolbarHeight + 54;
    const outsideViewport = rect.top < clearance || rect.bottom > window.innerHeight - 18;
    if (outsideViewport) {
      const targetTop = Math.max(0, window.scrollY + rect.top - clearance - 12);
      window.scrollTo({ top: targetTop, behavior: 'smooth' });
    }
    window.clearTimeout(filterHighlightTimer);
    row.classList.remove('is-filter-match');
    window.requestAnimationFrame(() => {
      row.classList.add('is-filter-match');
      filterHighlightTimer = window.setTimeout(() => row.classList.remove('is-filter-match'), 1450);
    });
  };

  const applySelectedFilters = ({ revealFirstMatch = false } = {}) => {
    const { term, stage, readiness } = selectedFilterState();
    const rows = currentRows();
    const visibleRows = [];
    const readinessKey = readiness.replace(/-([a-z])/g, (_, character) => character.toUpperCase());

    rows.forEach((row) => {
      const matchesText = !term || normalize(row.dataset.searchText).includes(term);
      const matchesStage = !stage || row.dataset.stageCode === stage;
      const matchesReadiness = !readiness || row.dataset[readinessKey] === 'true';
      const visible = matchesText && matchesStage && matchesReadiness;
      row.hidden = !visible;
      if (visible) visibleRows.push(row);
    });

    const filtered = Boolean(term || stage || readiness);
    const shown = visibleRows.length;
    if (visibleCount) {
      const noun = shown === 1 ? 'project' : 'projects';
      visibleCount.textContent = filtered ? `${shown} matching ${noun}` : `${shown} ${noun} shown`;
    }
    if (clearSelectedFilters) clearSelectedFilters.hidden = !filtered;
    if (noFilterResults) noFilterResults.hidden = shown > 0 || rows.length === 0;
    if (filterReorderNote) filterReorderNote.hidden = !filtered;
    if (sortable) sortable.option('disabled', filtered);
    rows.forEach((row) => {
      const handle = row.querySelector('.pbd-drag');
      if (handle) handle.disabled = filtered;
    });
    if (selectVisible) {
      selectVisible.checked = false;
      selectVisible.indeterminate = false;
    }
    refreshBulkActions();

    if (filtered && revealFirstMatch && visibleRows.length > 0) {
      window.requestAnimationFrame(() => revealFirstFilterMatch(visibleRows[0]));
    }
  };

  let selectedSearchTimer = 0;
  selectedSearch?.addEventListener('input', () => {
    persistSelectedFilters();
    window.clearTimeout(selectedSearchTimer);
    selectedSearchTimer = window.setTimeout(() => applySelectedFilters({ revealFirstMatch: true }), 220);
  });
  [selectedStage, selectedReadiness].forEach((control) => control?.addEventListener('change', () => {
    persistSelectedFilters();
    applySelectedFilters({ revealFirstMatch: true });
  }));

  clearSelectedFilters?.addEventListener('click', () => {
    if (selectedSearch) selectedSearch.value = '';
    if (selectedStage) selectedStage.value = '';
    if (selectedReadiness) selectedReadiness.value = '';
    persistSelectedFilters();
    applySelectedFilters();
    selectedSearch?.focus();
  });

  root.addEventListener('click', (event) => {
    const filterButton = event.target.closest('[data-pbd-readiness-filter]');
    if (!(filterButton instanceof HTMLButtonElement) || filterButton.disabled || !selectedReadiness) return;
    const filter = filterButton.dataset.pbdReadinessFilter || '';
    if (![...selectedReadiness.options].some((option) => option.value === filter)) return;
    if (selectedSearch) selectedSearch.value = '';
    if (selectedStage) selectedStage.value = '';
    selectedReadiness.value = filter;
    persistSelectedFilters();
    applySelectedFilters({ revealFirstMatch: true });
    selectedSection?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    window.setTimeout(() => selectedReadiness.focus(), 350);
  });

  selectVisible?.addEventListener('change', () => {
    currentRows().filter((row) => !row.hidden).forEach((row) => {
      const checkbox = row.querySelector('[data-pbd-row-select]');
      if (checkbox) checkbox.checked = selectVisible.checked;
    });
    refreshBulkActions();
  });

  const saveProjectOrder = async () => {
    if (!sortableBody || deckId <= 0) return;
    const projectIds = currentRows().map((row) => Number(row.dataset.projectId)).filter(Number.isInteger);
    setState(sortStatus, 'Saving slide order…', 'saving');
    try {
      const payload = await requestJson(root.dataset.reorderUrl, {
        method: 'POST',
        body: JSON.stringify({ deckId, projectIds, rowVersion: currentRowVersion() })
      });
      updateRowVersion(payload?.rowVersion);
      setState(sortStatus, 'Slide order saved.', 'saved');
    } catch (error) {
      setState(sortStatus, error.message || 'Slide order could not be saved.', 'error');
    }
  };

  const initialiseSortable = () => {
    if (!sortableBody || !window.Sortable || deckId <= 0) return;
    sortable?.destroy();
    sortable = window.Sortable.create(sortableBody, {
      animation: 130,
      handle: '.pbd-drag',
      ghostClass: 'pbd-sort-ghost',
      chosenClass: 'pbd-sort-chosen',
      onStart: () => setState(sortStatus, 'Reordering within present stage…', 'saving'),
      onMove: (event) => {
        const allowed = sameStage(event.dragged, event.related);
        if (!allowed) {
          setState(sortStatus, 'Projects remain grouped by maturity. Reorder only within the same stage.', 'error');
        }
        return allowed;
      },
      onEnd: (event) => {
        if (event.oldIndex === event.newIndex) {
          setState(sortStatus, 'No slide-order change.', 'saved');
          return;
        }
        saveProjectOrder();
      }
    });
    applySelectedFilters();
  };

  const disposeReadinessTooltips = (scope) => {
    if (!window.bootstrap?.Tooltip || !scope) return;
    scope.querySelectorAll('[data-pbd-readiness-tip]').forEach((element) => {
      window.bootstrap.Tooltip.getInstance(element)?.dispose();
    });
  };

  const initialiseReadinessTooltips = (scope = root) => {
    if (!window.bootstrap?.Tooltip || !scope) return;
    scope.querySelectorAll('[data-pbd-readiness-tip]').forEach((element) => {
      window.bootstrap.Tooltip.getOrCreateInstance(element, {
        container: 'body',
        boundary: 'viewport',
        placement: 'top',
        trigger: 'hover focus'
      });
    });
  };

  const createReadinessIcon = (icon, ready, title) => {
    const span = document.createElement('span');
    span.className = ready ? 'is-ready' : 'is-missing';
    span.tabIndex = 0;
    span.setAttribute('role', 'img');
    span.setAttribute('aria-label', title);
    span.dataset.pbdReadinessTip = '';
    span.dataset.bsTitle = title;
    span.title = title;
    const i = document.createElement('i');
    i.className = `bi ${icon}`;
    i.setAttribute('aria-hidden', 'true');
    span.append(i);
    return span;
  };

  initialiseReadinessTooltips();

  const buildProjectRow = (project) => {
    const capabilityReady = hasCapabilityOverview(project);
    const projectBriefReady = hasProjectBrief(project);
    const narrativeReady = hasSelectedNarrative(project);
    const narrativeTitle = includesCapabilities() && includesProjectBrief()
      ? (narrativeReady ? 'Capability overview and project brief available' : 'Capability overview or project brief is missing')
      : includesProjectBrief()
        ? (projectBriefReady ? 'Project brief available' : 'Project brief not recorded')
        : (capabilityReady ? 'Capability overview available' : 'Capability overview not recorded');
    const narrativeIcon = includesCapabilities() && includesProjectBrief()
      ? 'bi-layers'
      : includesProjectBrief() ? 'bi-file-earmark-text' : 'bi-list-check';
    const row = document.createElement('tr');
    row.dataset.projectId = String(project.projectId);
    row.dataset.searchText = [project.projectName, project.lifecycleDisplay, project.presentStage, project.projectCategory, project.technicalCategory, project.externalStatus].filter(Boolean).join(' ');
    row.dataset.stage = project.presentStage || '';
    row.dataset.stageCode = project.presentStageCode || '';
    row.dataset.stageOrder = String(project.presentStageOrder ?? 10000);
    row.dataset.missingStatus = String(!project.externalStatus);
    row.dataset.missingCostRd = String(!project.costRd?.isAvailable);
    row.dataset.missingProliferation = String(!project.proliferationCost?.isAvailable);
    row.dataset.missingPhoto = String(!project.hasCoverPhoto);
    row.dataset.missingDescription = String(!narrativeReady);
    row.dataset.missingFacts = String(!project.isUpdateSheetCoreFactsReady);

    const selectCell = document.createElement('td');
    selectCell.className = 'pbd-select-column';
    const selector = document.createElement('input');
    selector.type = 'checkbox';
    selector.dataset.pbdRowSelect = '';
    selector.setAttribute('aria-label', `Select ${project.projectName}`);
    selectCell.append(selector);

    const dragCell = document.createElement('td');
    dragCell.className = 'pbd-drag-column';
    const drag = document.createElement('button');
    drag.type = 'button';
    drag.className = 'pbd-drag';
    drag.title = `Reorder within ${project.presentStage || 'this stage'}`;
    drag.setAttribute('aria-label', `Reorder ${project.projectName} within ${project.presentStage || 'its present stage'}. Use the up or down arrow key.`);
    drag.setAttribute('aria-keyshortcuts', 'ArrowUp ArrowDown');
    drag.innerHTML = '<i class="bi bi-grip-vertical" aria-hidden="true"></i>';
    dragCell.append(drag);

    const projectCell = document.createElement('td');
    projectCell.className = 'pbd-project-name';
    const link = document.createElement('a');
    link.href = project.openUrl || '#';
    link.target = '_blank';
    link.rel = 'noopener';
    link.title = project.projectName;
    link.textContent = project.projectName;
    const meta = document.createElement('small');
    meta.textContent = `${project.lifecycleDisplay} · ${project.projectCategory || 'Not categorised'} · ${project.technicalCategory || 'No technical category'}`;
    projectCell.append(link, meta);

    const stageCell = document.createElement('td');
    const stagePill = document.createElement('span');
    stagePill.className = 'pbd-stage';
    stagePill.textContent = project.presentStage || 'Not recorded';
    stageCell.append(stagePill);

    const costCell = document.createElement('td');
    costCell.className = 'pbd-cost';
    const costValue = document.createElement('strong');
    costValue.textContent = project.costRd?.displayValue || 'Not recorded';
    costCell.append(costValue);
    if (project.costRd?.basisDisplay) {
      const basis = document.createElement('small');
      basis.textContent = project.costRd.basisDisplay;
      costCell.append(basis);
    }

    const proliferationCell = document.createElement('td');
    proliferationCell.className = 'pbd-cost';
    proliferationCell.dataset.pbdProliferationColumn = '';
    proliferationCell.hidden = isUpdateSheet();
    const proliferationValue = document.createElement('strong');
    proliferationValue.textContent = project.proliferationCost?.displayValue || 'Not recorded';
    proliferationCell.append(proliferationValue);

    const statusCell = document.createElement('td');
    statusCell.className = 'pbd-status';
    if (project.externalStatus) {
      const status = document.createElement('span');
      status.title = project.externalStatus;
      status.textContent = project.externalStatus;
      statusCell.append(status);
      if (project.externalStatusDate) {
        const date = document.createElement('small');
        date.textContent = formatDate(project.externalStatusDate);
        statusCell.append(date);
      }
    } else {
      const missing = document.createElement('span');
      missing.className = 'pbd-missing';
      missing.textContent = 'No external status recorded';
      statusCell.append(missing);
    }

    const readinessCell = document.createElement('td');
    const readiness = document.createElement('div');
    readiness.className = 'pbd-readiness-icons';
    readiness.setAttribute('aria-label', 'Project deck readiness');
    readiness.append(
      createReadinessIcon('bi-image', project.hasCoverPhoto, project.hasCoverPhoto ? 'PowerPoint-ready cover photograph available' : (project.coverPhotoReadinessReason || 'No PowerPoint-ready cover photograph')),
      createReadinessIcon('bi-chat-left-text', Boolean(project.externalStatus), project.externalStatus ? 'External status available' : 'External status missing'),
      createReadinessIcon('bi-currency-rupee', Boolean(project.costRd?.isAvailable), project.costRd?.isAvailable ? `Cost (R&D) available from ${project.costRd.basisDisplay}` : 'Cost (R&D) not recorded')
    );
    if (isUpdateSheet()) {
      readiness.append(createReadinessIcon('bi-card-checklist', Boolean(project.isUpdateSheetCoreFactsReady), project.isUpdateSheetCoreFactsReady ? 'Project update facts complete' : 'Project update facts incomplete'));
    }
    readiness.append(createReadinessIcon(narrativeIcon, narrativeReady, narrativeTitle));
    readinessCell.append(readiness);

    const actionCell = document.createElement('td');
    actionCell.className = 'pbd-row-actions';
    if (includesCapabilities()) {
      const edit = document.createElement('button');
      edit.type = 'button';
      edit.className = 'btn btn-sm btn-link';
      edit.dataset.pbdEditDescription = '';
      edit.dataset.projectId = String(project.projectId);
      edit.dataset.projectName = project.projectName;
      edit.dataset.description = project.briefDescriptionOverride || '';
      edit.title = 'Edit deck-specific capability overview';
      edit.setAttribute('aria-label', `Edit deck-specific capability overview for ${project.projectName}`);
      edit.innerHTML = '<i class="bi bi-pencil-square"></i>';
      actionCell.append(edit);
    }

    if (includesProjectBrief()) {
      const briefLink = document.createElement('a');
      briefLink.className = 'btn btn-sm btn-link';
      briefLink.href = `${project.openUrl || '#'}?content=brief#content-brief`;
      briefLink.target = '_blank';
      briefLink.rel = 'noopener';
      briefLink.title = 'Open project brief';
      briefLink.setAttribute('aria-label', `Open project brief for ${project.projectName} in a new tab`);
      briefLink.innerHTML = '<i class="bi bi-box-arrow-up-right"></i>';
      actionCell.append(briefLink);
    }

    const remove = document.createElement('button');
    remove.type = 'button';
    remove.className = 'btn btn-sm btn-link text-danger';
    remove.dataset.pbdRemoveProject = '';
    remove.dataset.projectId = String(project.projectId);
    remove.dataset.projectName = project.projectName;
    remove.title = 'Remove from deck';
    remove.setAttribute('aria-label', `Remove ${project.projectName} from deck`);
    remove.innerHTML = '<i class="bi bi-x-lg"></i>';
    actionCell.append(remove);

    row.append(selectCell, dragCell, projectCell, stageCell, costCell, proliferationCell, statusCell, readinessCell, actionCell);
    return row;
  };

  const populateStageFilter = (projects) => {
    if (!selectedStage) return;
    const selected = selectedStage.value;
    selectedStage.replaceChildren(new Option('All stages', ''));
    const stages = new Map();
    projects.forEach((project) => {
      const code = project.presentStageCode || project.presentStage || '';
      if (!code) return;
      const candidate = {
        code,
        label: project.presentStage || 'Not recorded',
        order: Number(project.presentStageOrder ?? 10000)
      };
      const current = stages.get(code);
      if (!current || candidate.order < current.order) stages.set(code, candidate);
    });
    [...stages.values()]
      .sort((a, b) => a.order - b.order || a.label.localeCompare(b.label))
      .forEach((stage) => selectedStage.append(new Option(stage.label, stage.code)));
    selectedStage.value = [...selectedStage.options].some((option) => option.value === selected) ? selected : '';
  };

  const renderSelectedProjects = (projects) => {
    if (!sortableBody) return;
    disposeReadinessTooltips(sortableBody);
    sortableBody.replaceChildren(...projects.map(buildProjectRow));
    initialiseReadinessTooltips(sortableBody);
    const hasProjects = projects.length > 0;
    if (selectedTableWrap) selectedTableWrap.hidden = !hasProjects;
    if (emptyProjects) emptyProjects.hidden = hasProjects;
    populateStageFilter(projects);
    initialiseSortable();
    refreshBulkActions();
  };

  const applyEditorState = (deck, { preserveScroll = false } = {}) => {
    if (!deck) return;
    const scrollTop = preserveScroll ? window.scrollY : null;
    updateRowVersion(deck.rowVersion);
    updateReadinessSummary(deck);
    renderSelectedProjects(deck.projects || []);
    if (scrollTop !== null) {
      window.requestAnimationFrame(() => window.scrollTo({ top: scrollTop, behavior: 'auto' }));
    }
  };

  const updateMembership = async (addProjectIds = [], removeProjectIds = [], statusElement = sortStatus) => {
    if (deckId <= 0 || (addProjectIds.length === 0 && removeProjectIds.length === 0)) return null;
    setState(statusElement, 'Saving deck membership…', 'saving');
    try {
      const payload = await requestJson(root.dataset.membershipUrl, {
        method: 'POST',
        body: JSON.stringify({ deckId, addProjectIds, removeProjectIds, rowVersion: currentRowVersion() })
      });
      applyEditorState(payload?.deck, { preserveScroll: true });
      if (searchRows.length > 0) {
        const added = new Set(addProjectIds.map(Number));
        const removed = new Set(removeProjectIds.map(Number));
        searchRows = searchRows.map((project) => ({
          ...project,
          isSelected: added.has(project.projectId)
            ? true
            : removed.has(project.projectId)
              ? false
              : project.isSelected
        }));
        renderSearchResults(searchRows);
      }
      const changes = [];
      if (payload?.addedCount) changes.push(`${payload.addedCount} added`);
      if (payload?.removedCount) changes.push(`${payload.removedCount} removed`);
      setState(statusElement, changes.length ? `Deck updated — ${changes.join(', ')}.` : 'No membership changes were required.', 'saved');
      return payload;
    } catch (error) {
      setState(statusElement, error.message || 'Deck membership could not be updated.', 'error');
      throw error;
    }
  };

  root.addEventListener('change', (event) => {
    if (event.target.matches('[data-pbd-row-select]')) refreshBulkActions();
  });

  root.addEventListener('submit', async (event) => {
    const form = event.target.closest('[data-pbd-remove-project-form]');
    if (!form) return;
    event.preventDefault();
    const projectId = Number(form.querySelector('input[name="projectId"]')?.value || 0);
    if (!projectId) return;
    const button = form.querySelector('button[type="submit"]');
    button?.setAttribute('disabled', 'disabled');
    try { await updateMembership([], [projectId]); }
    finally { button?.removeAttribute('disabled'); }
  });

  root.addEventListener('click', async (event) => {
    const remove = event.target.closest('[data-pbd-remove-project]');
    if (remove) {
      const projectId = Number(remove.dataset.projectId || 0);
      if (!projectId) return;
      remove.disabled = true;
      try { await updateMembership([], [projectId]); }
      finally { remove.disabled = false; }
      return;
    }
  });

  bulkRemove?.addEventListener('click', async () => {
    const ids = selectedRowIds();
    if (ids.length === 0) return;
    if (!window.confirm(`Remove ${ids.length} selected project${ids.length === 1 ? '' : 's'} from this deck?`)) return;
    await updateMembership([], ids);
  });

  const moveSelected = async (toTop) => {
    const selected = new Set(selectedRowIds());
    if (selected.size === 0 || !sortableBody) return;

    const stageGroups = [];
    currentRows().forEach((row) => {
      const key = stageKey(row);
      const existing = stageGroups.at(-1);
      if (!existing || existing.key !== key) stageGroups.push({ key, rows: [row] });
      else existing.rows.push(row);
    });

    stageGroups.forEach((group) => {
      const moving = group.rows.filter((row) => selected.has(Number(row.dataset.projectId)));
      const remaining = group.rows.filter((row) => !selected.has(Number(row.dataset.projectId)));
      const ordered = toTop ? [...moving, ...remaining] : [...remaining, ...moving];
      ordered.forEach((row) => sortableBody.append(row));
    });

    await saveProjectOrder();
    setState(sortStatus, `Selected projects moved to the ${toTop ? 'top' : 'bottom'} of their stage.`, 'saved');
    applySelectedFilters();
  };
  bulkTop?.addEventListener('click', () => moveSelected(true));
  bulkBottom?.addEventListener('click', () => moveSelected(false));

  sortableBody?.addEventListener('keydown', async (event) => {
    const handle = event.target.closest('.pbd-drag');
    if (!handle || !['ArrowUp', 'ArrowDown'].includes(event.key) || handle.disabled) return;
    const row = handle.closest('tr[data-project-id]');
    const target = event.key === 'ArrowUp' ? row?.previousElementSibling : row?.nextElementSibling;
    if (!(row instanceof HTMLTableRowElement) || !(target instanceof HTMLTableRowElement)) return;
    event.preventDefault();
    if (!sameStage(row, target)) {
      setState(sortStatus, 'This project is already at the edge of its present-stage group.', 'saved');
      return;
    }
    if (event.key === 'ArrowUp') sortableBody.insertBefore(row, target);
    else sortableBody.insertBefore(target, row);
    handle.focus();
    await saveProjectOrder();
  });

  initialiseSortable();

  // Manage individual membership across all projects.
  const individualForm = root.querySelector('[data-pbd-individual-form]');
  const searchInput = root.querySelector('[data-pbd-project-search]');
  const searchResults = root.querySelector('[data-pbd-search-results]');
  const searchStatus = root.querySelector('[data-pbd-search-status]');
  const membershipFilter = root.querySelector('[data-pbd-membership-filter]');
  const membershipSummary = root.querySelector('[data-pbd-selected-count]');
  const applyMembershipButton = root.querySelector('[data-pbd-apply-membership]');
  const resultBaseline = new Map();
  const resultDesired = new Map();
  let searchRows = [];
  let searchTimer = 0;
  let searchAbortController = null;

  const pendingMembershipChanges = () => {
    const add = [];
    const remove = [];
    resultDesired.forEach((desired, projectId) => {
      const baseline = resultBaseline.get(projectId);
      if (desired && !baseline) add.push(projectId);
      if (!desired && baseline) remove.push(projectId);
    });
    return { add, remove };
  };

  const updateMembershipSummary = () => {
    const { add, remove } = pendingMembershipChanges();
    const parts = [];
    if (add.length) parts.push(`${add.length} to add`);
    if (remove.length) parts.push(`${remove.length} to remove`);
    if (membershipSummary) membershipSummary.textContent = parts.length ? parts.join(' · ') : 'No pending changes';
    if (applyMembershipButton) applyMembershipButton.disabled = add.length === 0 && remove.length === 0;
  };

  const applyMembershipResultFilter = () => {
    const filter = membershipFilter?.value || 'all';
    let shown = 0;
    searchResults?.querySelectorAll('[data-project-result]').forEach((node) => {
      const id = Number(node.dataset.projectResult);
      const selected = resultDesired.get(id) === true;
      const visible = filter === 'all' || (filter === 'selected' && selected) || (filter === 'unselected' && !selected);
      node.hidden = !visible;
      if (visible) shown += 1;
    });
    setState(searchStatus, searchRows.length ? `${shown} of ${searchRows.length} matching projects shown.` : 'No projects match this search.');
  };

  const renderSearchResults = (rows) => {
    searchRows = rows;
    resultBaseline.clear();
    resultDesired.clear();
    searchResults?.replaceChildren();
    if (!rows.length) {
      setState(searchStatus, 'No projects match this search.');
      updateMembershipSummary();
      return;
    }

    rows.forEach((project) => {
      resultBaseline.set(project.projectId, Boolean(project.isSelected));
      resultDesired.set(project.projectId, Boolean(project.isSelected));
      const label = document.createElement('label');
      label.className = 'pbd-search-result';
      label.dataset.projectResult = String(project.projectId);

      const checkbox = document.createElement('input');
      checkbox.type = 'checkbox';
      checkbox.checked = Boolean(project.isSelected);
      checkbox.addEventListener('change', () => {
        resultDesired.set(project.projectId, checkbox.checked);
        label.classList.toggle('is-selected', checkbox.checked);
        badge.textContent = checkbox.checked ? 'IN DECK' : 'NOT IN DECK';
        badge.classList.toggle('is-in-deck', checkbox.checked);
        updateMembershipSummary();
        applyMembershipResultFilter();
      });

      const body = document.createElement('span');
      const heading = document.createElement('span');
      heading.className = 'pbd-search-result__heading';
      const name = document.createElement('strong');
      name.textContent = project.projectName;
      const badge = document.createElement('em');
      badge.className = project.isSelected ? 'pbd-membership-badge is-in-deck' : 'pbd-membership-badge';
      badge.textContent = project.isSelected ? 'IN DECK' : 'NOT IN DECK';
      heading.append(name, badge);
      const meta = document.createElement('small');
      meta.textContent = [project.lifecycle, project.presentStage, project.projectCategory, project.technicalCategory, project.projectOfficer].filter(Boolean).join(' · ');
      const ref = document.createElement('small');
      ref.textContent = project.caseFileNumber ? `Ref: ${project.caseFileNumber}` : 'No case-file reference';
      body.append(heading, meta, ref);
      label.append(checkbox, body);
      label.classList.toggle('is-selected', checkbox.checked);
      searchResults?.append(label);
    });
    updateMembershipSummary();
    applyMembershipResultFilter();
  };

  const searchProjects = async () => {
    const query = searchInput?.value.trim() || '';
    if (query.length < 2) {
      searchAbortController?.abort();
      searchResults?.replaceChildren();
      searchRows = [];
      setState(searchStatus, 'Enter at least two characters.');
      updateMembershipSummary();
      return;
    }

    searchAbortController?.abort();
    searchAbortController = new AbortController();
    setState(searchStatus, 'Searching…');
    try {
      const url = new URL(root.dataset.searchUrl, window.location.origin);
      url.searchParams.set('deckId', String(deckId));
      url.searchParams.set('query', query);
      const response = await fetch(url, {
        credentials: 'same-origin',
        headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
        signal: searchAbortController.signal
      });
      const payload = await response.json().catch(() => null);
      if (!response.ok) throw new Error(payload?.message || `Search failed (${response.status}).`);
      renderSearchResults(payload || []);
    } catch (error) {
      if (error.name === 'AbortError') return;
      setState(searchStatus, error.message || 'Project search could not be completed.', 'error');
    }
  };

  searchInput?.addEventListener('input', () => {
    window.clearTimeout(searchTimer);
    searchTimer = window.setTimeout(searchProjects, 260);
  });
  membershipFilter?.addEventListener('change', applyMembershipResultFilter);

  individualForm?.addEventListener('submit', async (event) => {
    event.preventDefault();
    const { add, remove } = pendingMembershipChanges();
    if (add.length === 0 && remove.length === 0) return;
    applyMembershipButton?.setAttribute('disabled', 'disabled');
    try {
      await updateMembership(add, remove, searchStatus);
    } finally {
      updateMembershipSummary();
    }
  });

  // Briefing-specific project description editor (delegated for dynamically refreshed rows).
  const descriptionModalElement = document.getElementById('pbd-description-modal');
  const descriptionModal = descriptionModalElement && window.bootstrap
    ? window.bootstrap.Modal.getOrCreateInstance(descriptionModalElement)
    : null;
  const descriptionForm = descriptionModalElement?.querySelector('[data-pbd-description-form]');
  const descriptionProjectId = descriptionModalElement?.querySelector('[data-pbd-description-project-id]');
  const descriptionValue = descriptionModalElement?.querySelector('[data-pbd-description-value]');
  const descriptionTitle = descriptionModalElement?.querySelector('[data-pbd-description-title]');
  const descriptionStatus = descriptionModalElement?.querySelector('[data-pbd-description-status]');

  root.addEventListener('click', (event) => {
    const button = event.target.closest('[data-pbd-edit-description]');
    if (!button) return;
    if (descriptionProjectId) descriptionProjectId.value = button.dataset.projectId || '';
    if (descriptionValue) descriptionValue.value = button.dataset.description || '';
    if (descriptionTitle) descriptionTitle.textContent = button.dataset.projectName || 'Capability overview';
    setState(descriptionStatus, '');
    descriptionModal?.show();
    window.setTimeout(() => descriptionValue?.focus(), 180);
  });

  descriptionForm?.addEventListener('submit', async (event) => {
    event.preventDefault();
    const projectId = Number(descriptionProjectId?.value || 0);
    if (!projectId || deckId <= 0) return;
    const submit = descriptionForm.querySelector('button[type="submit"]');
    submit?.setAttribute('disabled', 'disabled');
    setState(descriptionStatus, 'Saving…');
    try {
      const payload = await requestJson(root.dataset.descriptionUrl, {
        method: 'POST',
        body: JSON.stringify({ deckId, projectId, value: descriptionValue?.value || null, rowVersion: currentRowVersion() })
      });
      updateRowVersion(payload?.rowVersion);
      const editorButton = root.querySelector(`[data-pbd-edit-description][data-project-id="${projectId}"]`);
      if (editorButton) editorButton.dataset.description = descriptionValue?.value || '';
      setState(descriptionStatus, 'Deck-specific capability overview saved.', 'success');
      window.setTimeout(() => descriptionModal?.hide(), 450);
    } catch (error) {
      setState(descriptionStatus, error.message || 'Capability overview could not be saved.', 'error');
    } finally {
      submit?.removeAttribute('disabled');
    }
  });

  // Generate and download the PowerPoint without leaving the builder.
  const generateForm = root.querySelector('[data-pbd-generate-form]');
  const generateLabel = root.querySelector('[data-pbd-generate-label]');
  const generateProgress = root.querySelector('[data-pbd-generate-progress]');
  const generateStatus = root.querySelector('[data-pbd-generate-status]');
  const extractFileName = (header) => {
    if (!header) return 'Project_Briefing_Deck.pptx';
    const encoded = header.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
    if (encoded) return decodeURIComponent(encoded);
    return header.match(/filename="?([^";]+)"?/i)?.[1] || 'Project_Briefing_Deck.pptx';
  };

  generateForm?.addEventListener('submit', async (event) => {
    event.preventDefault();
    if (settingsDirty) {
      openSettingsDrawer();
      if (settingsStatus) {
        settingsStatus.textContent = 'Save or discard settings before generating the PowerPoint.';
        settingsStatus.classList.add('is-dirty');
      }
      return;
    }
    if (!generateButton || generateButton.disabled) return;
    generateButton.disabled = true;
    generateLabel?.classList.add('d-none');
    generateProgress?.classList.remove('d-none');
    setState(generateStatus, 'Building editable PowerPoint slides from current project data…');
    try {
      const response = await fetch(generateForm.action, {
        method: 'POST',
        credentials: 'same-origin',
        body: new FormData(generateForm),
        headers: {
          Accept: 'application/vnd.openxmlformats-officedocument.presentationml.presentation, application/problem+json, application/json',
          'X-CSRF-TOKEN': token,
          'X-Requested-With': 'XMLHttpRequest'
        }
      });
      const contentType = response.headers.get('content-type') || '';
      if (!response.ok || contentType.includes('json')) {
        const payload = contentType.includes('json') ? await response.json() : null;
        throw new Error(payload?.message || payload?.title || `PowerPoint generation failed (${response.status}).`);
      }
      const blob = await response.blob();
      const downloadUrl = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = downloadUrl;
      anchor.download = extractFileName(response.headers.get('content-disposition'));
      document.body.append(anchor);
      anchor.click();
      anchor.remove();
      window.setTimeout(() => URL.revokeObjectURL(downloadUrl), 1500);
      const slideCount = response.headers.get('X-Project-Briefing-Slides');
      setState(generateStatus, slideCount ? `PowerPoint generated successfully — ${slideCount} slides.` : 'PowerPoint generated successfully.', 'success');
    } catch (error) {
      setState(generateStatus, error.message || 'The PowerPoint deck could not be generated.', 'error');
    } finally {
      generateButton.disabled = false;
      generateLabel?.classList.remove('d-none');
      generateProgress?.classList.add('d-none');
    }
  });

  document.getElementById('pbd-new-deck-modal')?.addEventListener('shown.bs.modal', () => {
    document.querySelector('[data-pbd-new-name]')?.focus();
  });

  applySelectedFilters();
}
