(() => {
  'use strict';

  const root = document.querySelector('.cpw');
  if (!root) return;

  const filterToggle = root.querySelector('[data-filter-toggle]');
  const filterPanel = root.querySelector('[data-filter-panel]');
  filterToggle?.addEventListener('click', () => {
    const open = filterPanel.classList.toggle('is-open');
    filterToggle.setAttribute('aria-expanded', String(open));
  });

  const tabs = [...root.querySelectorAll('[data-view]')];
  const panels = [...root.querySelectorAll('[data-view-panel]')];
  const setView = (view) => {
    tabs.forEach((tab) => {
      const active = tab.dataset.view === view;
      tab.classList.toggle('is-active', active);
      tab.setAttribute('aria-selected', String(active));
    });
    panels.forEach((panel) => {
      const active = panel.dataset.viewPanel === view;
      panel.classList.toggle('is-active', active);
      panel.hidden = !active;
    });
    root.querySelectorAll('[data-portfolio-control]').forEach((control) => {
      control.hidden = view !== 'portfolio';
    });
    sessionStorage.setItem('completedProjectsView', view);
  };
  tabs.forEach((tab) => tab.addEventListener('click', () => setView(tab.dataset.view)));
  setView(sessionStorage.getItem('completedProjectsView') || root.dataset.defaultView || 'portfolio');

  const lenses = [...root.querySelectorAll('[data-lens]')];
  const visibleCount = root.querySelector('[data-visible-count]');
  const noResults = root.querySelector('.cpw-no-lens-results');
  let activeLens = 'all';

  const applyLens = () => {
    let visible = 0;
    root.querySelectorAll('[data-project]').forEach((project) => {
      const matches = activeLens === 'all' || project.dataset[activeLens] === 'true';
      project.hidden = !matches;
      if (matches && project.closest('[data-view-panel]:not([hidden])')) visible++;
    });

    const activePanel = root.querySelector('[data-view-panel]:not([hidden])');
    visible = activePanel ? [...activePanel.querySelectorAll('[data-project]')].filter((x) => !x.hidden).length : 0;
    root.querySelectorAll('[data-project-group]').forEach((group) => {
      const hasVisible = [...group.querySelectorAll('[data-project]')].some((project) => !project.hidden);
      group.hidden = !hasVisible;
    });
    if (visibleCount) visibleCount.textContent = String(visible);
    if (noResults) noResults.hidden = visible !== 0;
  };


  const portfolioGrid = root.querySelector('.cpw-portfolio-grid');
  const sortSelect = root.querySelector('[data-card-sort]');
  const groupSelect = root.querySelector('[data-card-group]');
  const originalCards = portfolioGrid ? [...portfolioGrid.querySelectorAll(':scope > [data-project]')] : [];

  const compareCards = (a, b, mode) => {
    const nameA = a.dataset.name || '';
    const nameB = b.dataset.name || '';
    const yearA = Number(a.dataset.year || 0);
    const yearB = Number(b.dataset.year || 0);
    const valueA = Number(a.dataset.value || -1);
    const valueB = Number(b.dataset.value || -1);
    const gapsA = Number(a.dataset.gaps || 0);
    const gapsB = Number(b.dataset.gaps || 0);

    switch (mode) {
      case 'newest': return (yearB - yearA) || nameA.localeCompare(nameB);
      case 'oldest': return (yearA - yearB) || nameA.localeCompare(nameB);
      case 'value': return (valueB - valueA) || nameA.localeCompare(nameB);
      case 'gaps': return (gapsB - gapsA) || nameA.localeCompare(nameB);
      default: return nameA.localeCompare(nameB);
    }
  };

  const renderPortfolio = () => {
    if (!portfolioGrid || !originalCards.length) return;
    const sortMode = sortSelect?.value || 'name';
    const groupMode = groupSelect?.value || 'none';
    const cards = [...originalCards].sort((a, b) => compareCards(a, b, sortMode));
    portfolioGrid.replaceChildren();

    if (groupMode === 'none') {
      cards.forEach((card) => portfolioGrid.appendChild(card));
      return;
    }

    const attr = groupMode === 'age' ? 'ageGroup' : 'readinessGroup';
    const preferredOrder = groupMode === 'age'
      ? ['0–5 years', '6–10 years', '11–15 years', '16+ years', 'Year not recorded']
      : ['Ready', 'Technology refresh', 'ToT action pending', 'Assessment incomplete', 'Other'];
    const groups = new Map();
    cards.forEach((card) => {
      const key = card.dataset[attr] || 'Other';
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(card);
    });

    preferredOrder.filter((key) => groups.has(key)).forEach((key) => {
      const section = document.createElement('section');
      section.className = 'cpw-group-section';
      section.dataset.projectGroup = key;
      const heading = document.createElement('div');
      heading.className = 'cpw-group-heading';
      heading.innerHTML = `<strong>${key}</strong><span>${groups.get(key).length} project${groups.get(key).length === 1 ? '' : 's'}</span>`;
      section.appendChild(heading);
      groups.get(key).forEach((card) => section.appendChild(card));
      portfolioGrid.appendChild(section);
    });
  };

  sortSelect?.addEventListener('change', () => {
    sessionStorage.setItem('completedProjectsSort', sortSelect.value);
    renderPortfolio();
    applyLens();
  });
  groupSelect?.addEventListener('change', () => {
    sessionStorage.setItem('completedProjectsGroup', groupSelect.value);
    renderPortfolio();
    applyLens();
  });
  if (sortSelect) sortSelect.value = sessionStorage.getItem('completedProjectsSort') || 'name';
  if (groupSelect) groupSelect.value = sessionStorage.getItem('completedProjectsGroup') || 'none';

  lenses.forEach((lens) => lens.addEventListener('click', () => {
    activeLens = lens.dataset.lens || 'all';
    lenses.forEach((x) => x.classList.toggle('is-active', x === lens));
    applyLens();
  }));
  tabs.forEach((tab) => tab.addEventListener('click', applyLens));
  renderPortfolio();
  applyLens();

  const drawer = root.querySelector('[data-drawer]');
  const drawerBody = root.querySelector('[data-drawer-body]');
  const backdrop = root.querySelector('[data-drawer-backdrop]');
  const closeDrawer = () => {
    drawer?.classList.remove('is-open');
    drawer?.setAttribute('aria-hidden', 'true');
    if (backdrop) backdrop.hidden = true;
    document.body.classList.remove('cpw-drawer-open');
  };
  const openDrawer = (id) => {
    const template = document.getElementById(`cpw-project-${id}`);
    if (!template || !drawer || !drawerBody) return;
    drawerBody.replaceChildren(template.content.cloneNode(true));
    drawer.classList.add('is-open');
    drawer.setAttribute('aria-hidden', 'false');
    if (backdrop) backdrop.hidden = false;
    document.body.classList.add('cpw-drawer-open');
    drawer.querySelector('button')?.focus();
  };

  root.addEventListener('click', (event) => {
    const opener = event.target.closest('[data-open-project]');
    if (opener) openDrawer(opener.dataset.openProject);
    if (event.target.closest('[data-close-drawer]')) closeDrawer();
  });
  backdrop?.addEventListener('click', closeDrawer);
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape') closeDrawer();
  });
})();
