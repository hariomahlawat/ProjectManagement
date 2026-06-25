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
    if (visibleCount) visibleCount.textContent = String(visible);
    if (noResults) noResults.hidden = visible !== 0;
  };

  lenses.forEach((lens) => lens.addEventListener('click', () => {
    activeLens = lens.dataset.lens || 'all';
    lenses.forEach((x) => x.classList.toggle('is-active', x === lens));
    applyLens();
  }));
  tabs.forEach((tab) => tab.addEventListener('click', applyLens));
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
