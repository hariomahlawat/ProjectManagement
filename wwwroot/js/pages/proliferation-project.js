(() => {
  const root = document.querySelector('[data-page="proliferation-project"]');
  if (!root) return;

  const formatter = new Intl.NumberFormat();
  const dateFormatter = new Intl.DateTimeFormat(undefined, {
    year: 'numeric',
    month: 'short',
    day: '2-digit'
  });

  const formatDate = (value) => {
    const parts = String(value || '').split('-').map(Number);
    if (parts.length !== 3 || parts.some((x) => !Number.isFinite(x))) return '—';
    return dateFormatter.format(new Date(Date.UTC(parts[0], parts[1] - 1, parts[2])));
  };

  const createCell = (text, className = '') => {
    const cell = document.createElement('td');
    if (className) cell.className = className;
    cell.textContent = text;
    return cell;
  };

  const renderEntries = (host, entries) => {
    host.replaceChildren();
    if (!Array.isArray(entries) || entries.length === 0) {
      const empty = document.createElement('p');
      empty.className = 'text-muted small mb-0';
      empty.textContent = 'No approved detailed entries are available.';
      host.append(empty);
      return;
    }

    const wrapper = document.createElement('div');
    wrapper.className = 'table-responsive mt-2';
    const table = document.createElement('table');
    table.className = 'table table-sm align-middle pf-detail-table mb-0';
    const thead = document.createElement('thead');
    const headRow = document.createElement('tr');
    ['Date', 'Receiving unit', 'Quantity', 'Remarks'].forEach((label, index) => {
      const th = document.createElement('th');
      th.scope = 'col';
      th.textContent = label;
      if (index === 2) th.className = 'text-end';
      headRow.append(th);
    });
    thead.append(headRow);

    const tbody = document.createElement('tbody');
    entries.forEach((entry) => {
      const row = document.createElement('tr');
      row.append(
        createCell(formatDate(entry.proliferationDate), 'text-nowrap'),
        createCell(entry.unitName || '—'),
        createCell(formatter.format(Number(entry.quantity) || 0), 'text-end fw-semibold'),
        createCell(entry.remarks || '—', 'text-muted')
      );
      tbody.append(row);
    });

    table.append(thead, tbody);
    wrapper.append(table);
    host.append(wrapper);
  };

  const activeControllers = new Set();

  root.querySelectorAll('[data-entry-loader]').forEach((loader) => {
    const button = loader.querySelector('[data-entry-toggle]');
    const host = loader.querySelector('[data-entry-host]');
    if (!button || !host) return;

    let loaded = false;
    let open = false;
    let controller = null;

    button.addEventListener('click', async () => {
      open = !open;
      button.setAttribute('aria-expanded', open ? 'true' : 'false');
      const countLabel = button.textContent.trim().replace(/^(View|Hide)\s+/i, '');
      button.textContent = `${open ? 'Hide' : 'View'} ${countLabel}`;
      host.classList.toggle('d-none', !open);
      if (!open || loaded) return;

      controller?.abort();
      controller = new AbortController();
      activeControllers.add(controller);
      host.textContent = 'Loading detailed entries…';
      button.disabled = true;

      try {
        const params = new URLSearchParams({
          source: loader.dataset.source || '',
          year: loader.dataset.year || ''
        });
        const response = await fetch(`/api/proliferation/groups/${encodeURIComponent(loader.dataset.projectId || '')}/entries?${params}`, {
          headers: { Accept: 'application/json' },
          signal: controller.signal
        });
        if (!response.ok) throw new Error('Unable to load detailed entries.');
        renderEntries(host, await response.json());
        loaded = true;
      } catch (error) {
        if (error.name === 'AbortError') return;
        host.textContent = error.message || 'Unable to load detailed entries.';
      } finally {
        activeControllers.delete(controller);
        button.disabled = false;
      }
    });
  });

  window.addEventListener('pagehide', () => {
    activeControllers.forEach((controller) => controller.abort());
    activeControllers.clear();
  }, { once: true });
})();
