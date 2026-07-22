(() => {
  const root = document.querySelector('[data-page="proliferation-manage"]');
  if (!root) return;

  const tabs = [...root.querySelectorAll('[data-workspace]')];
  const panels = [...root.querySelectorAll('[data-workspace-panel]')].filter((x) => x.tagName !== 'BUTTON');
  const headerTitle = root.querySelector('.pf-page-title');
  const headerSubtitle = root.querySelector('.pf-page-subtitle');
  const newActions = root.querySelector('.pf-manage-new-actions');
  const initial = root.dataset.initialWorkspace || 'records';

  const copy = {
    records: {
      title: 'Manage records',
      subtitle: 'Add annual quantities or detailed entries and review existing records.'
    },
    approvals: {
      title: 'Pending approval',
      subtitle: 'Review submitted proliferation records and decide them with full record context.'
    },
    'counting-rules': {
      title: 'Counting rules',
      subtitle: 'Review source defaults and manage deliberate project-year exceptions.'
    },
    'data-quality': {
      title: 'Data-quality review',
      subtitle: 'Correct malformed historical values and review possible duplicate records.'
    }
  };

  const setUrl = (workspace) => {
    const url = new URL(window.location.href);
    if (workspace === 'records') url.searchParams.delete('workspace');
    else url.searchParams.set('workspace', workspace);
    window.history.replaceState({}, '', url);
  };

  const activate = (workspace, options = {}) => {
    const tab = tabs.find((x) => x.dataset.workspace === workspace) || tabs[0];
    if (!tab) return;
    const selected = tab.dataset.workspace || 'records';
    const targetPanel = tab.dataset.workspacePanel || selected;

    tabs.forEach((item) => {
      const active = item === tab;
      item.classList.toggle('active', active);
      item.setAttribute('aria-selected', active ? 'true' : 'false');
      item.tabIndex = active ? 0 : -1;
    });

    panels.forEach((panel) => {
      const active = panel.dataset.workspacePanel === targetPanel;
      panel.classList.toggle('d-none', !active);
      panel.hidden = !active;
    });

    const text = copy[selected] || copy.records;
    if (headerTitle) headerTitle.textContent = text.title;
    if (headerSubtitle) headerSubtitle.textContent = text.subtitle;
    if (newActions) newActions.classList.toggle('d-none', selected === 'approvals');

    root.dataset.activeWorkspace = selected;
    if (options.updateUrl !== false) setUrl(selected);
    window.dispatchEvent(new CustomEvent('proliferation:workspacechange', {
      detail: { workspace: selected, panel: targetPanel }
    }));
  };

  tabs.forEach((tab) => {
    tab.addEventListener('click', () => activate(tab.dataset.workspace || 'records'));
    tab.addEventListener('keydown', (event) => {
      if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      const index = tabs.indexOf(tab);
      let next = index;
      if (event.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length;
      if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
      if (event.key === 'Home') next = 0;
      if (event.key === 'End') next = tabs.length - 1;
      tabs[next]?.focus();
      activate(tabs[next]?.dataset.workspace || 'records');
    });
  });

  const refreshBadges = async () => {
    const pendingBadge = document.querySelector('#pf-pending-badge');
    if (pendingBadge) {
      try {
        const response = await fetch('/api/proliferation/list?approvalStatus=pending&page=1&pageSize=10', { headers: { Accept: 'application/json' } });
        if (response.ok) {
          const data = await response.json();
          const total = Number(data.total) || 0;
          pendingBadge.textContent = String(total);
          pendingBadge.classList.toggle('d-none', total === 0);
        }
      } catch { /* Non-blocking badge. */ }
    }

    const qualityBadge = document.querySelector('#pf-quality-badge');
    if (qualityBadge) {
      try {
        const response = await fetch('/api/proliferation/data-quality?page=1&pageSize=10', { headers: { Accept: 'application/json' } });
        if (response.ok) {
          const data = await response.json();
          const correctionsRequired = (Number(data.invalidDateOrYearCount) || 0)
            + (Number(data.missingUnitCount) || 0)
            + (Number(data.invalidQuantityCount) || 0);
          const duplicates = Number(data.possibleDuplicateCount) || 0;
          qualityBadge.textContent = String(correctionsRequired);
          qualityBadge.classList.toggle('d-none', correctionsRequired === 0);
          qualityBadge.title = duplicates > 0
            ? `${correctionsRequired} corrections required; ${duplicates} possible duplicates to review.`
            : `${correctionsRequired} corrections required.`;
        }
      } catch { /* Non-blocking badge. */ }
    }
  };

  window.addEventListener('proliferation:dataqualitychanged', refreshBadges);
  window.addEventListener('proliferation:recordchanged', refreshBadges);
  activate(initial, { updateUrl: false });
  refreshBadges();
})();
