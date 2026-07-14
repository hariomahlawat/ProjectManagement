(() => {
  'use strict';

  document.querySelectorAll('[data-admin-category-tree]').forEach(tree => {
    const rows = Array.from(tree.querySelectorAll('[data-category-row]'));
    if (rows.length === 0) return;

    const key = `prism.admin.category-tree.${window.location.pathname}`;
    const hasActiveSearch = Boolean(tree.querySelector('input[name="q"]')?.value.trim());
    let collapsed = new Set();
    try {
      const stored = JSON.parse(window.localStorage.getItem(key) || '[]');
      if (Array.isArray(stored)) collapsed = new Set(stored.map(String));
    } catch {
      collapsed = new Set();
    }
    if (hasActiveSearch) collapsed.clear();

    const byParent = new Map();
    rows.forEach(row => {
      const parentId = row.dataset.parentId || '';
      if (!byParent.has(parentId)) byParent.set(parentId, []);
      byParent.get(parentId).push(row);
    });

    const descendants = id => {
      const result = [];
      const pending = [...(byParent.get(String(id)) || [])];
      while (pending.length) {
        const row = pending.shift();
        result.push(row);
        pending.push(...(byParent.get(row.dataset.categoryId || '') || []));
      }
      return result;
    };

    const save = () => {
      try { window.localStorage.setItem(key, JSON.stringify([...collapsed])); } catch { /* storage unavailable */ }
    };

    const apply = () => {
      rows.forEach(row => { row.hidden = false; });
      collapsed.forEach(id => descendants(id).forEach(row => { row.hidden = true; }));
      rows.forEach(row => {
        const button = row.querySelector('[data-admin-tree-toggle]');
        if (!button) return;
        const isCollapsed = collapsed.has(row.dataset.categoryId || '');
        button.setAttribute('aria-expanded', String(!isCollapsed));
        button.setAttribute('aria-label', `${isCollapsed ? 'Expand' : 'Collapse'} ${row.querySelector('.admin-category-cell__copy strong')?.textContent || 'category'}`);
        button.querySelector('i')?.classList.toggle('bi-chevron-right', isCollapsed);
        button.querySelector('i')?.classList.toggle('bi-chevron-down', !isCollapsed);
      });
    };

    tree.addEventListener('click', event => {
      const toggle = event.target.closest('[data-admin-tree-toggle]');
      if (!toggle) return;
      const row = toggle.closest('[data-category-row]');
      const id = row?.dataset.categoryId;
      if (!id) return;
      if (collapsed.has(id)) collapsed.delete(id); else collapsed.add(id);
      save();
      apply();
    });

    tree.querySelector('[data-admin-tree-expand]')?.addEventListener('click', () => {
      collapsed.clear(); save(); apply();
    });
    tree.querySelector('[data-admin-tree-collapse]')?.addEventListener('click', () => {
      rows.filter(row => row.querySelector('[data-admin-tree-toggle]')).forEach(row => collapsed.add(row.dataset.categoryId || ''));
      save(); apply();
    });

    apply();
  });
})();
