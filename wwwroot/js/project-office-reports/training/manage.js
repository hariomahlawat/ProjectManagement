(() => {
  initScheduleToggle();
  initRosterModule();
})();

function initScheduleToggle() {
  const scheduleInputs = document.querySelectorAll('input[name="Input.ScheduleMode"]');
  const dateRangeSection = document.querySelector('[data-training-schedule="date-range"]');
  const monthYearSection = document.querySelector('[data-training-schedule="month-year"]');

  function updateVisibility() {
    const selected = document.querySelector('input[name="Input.ScheduleMode"]:checked');
    const mode = selected ? selected.value : null;
    const isDateRange = mode === 'DateRange' || mode === '0';

    if (dateRangeSection) {
      dateRangeSection.classList.toggle('d-none', !isDateRange);
    }

    if (monthYearSection) {
      monthYearSection.classList.toggle('d-none', isDateRange);
    }
  }

  scheduleInputs.forEach((input) => {
    input.addEventListener('change', updateVisibility);
  });

  updateVisibility();
}

function initRosterModule() {
  const context = document.getElementById('trainingRosterContext');
  if (!context) {
    return;
  }

  const trainingId = context.dataset.trainingId;
  if (!trainingId) {
    return;
  }

  const rosterBody = document.getElementById('rosterBody');
  const btnAdd = document.getElementById('btnAddRosterRow');
  const btnPaste = document.getElementById('btnPasteRoster');
  const btnSave = document.getElementById('btnSaveRoster');
  const errorAlert = document.getElementById('rosterError');
  const officersSpan = document.getElementById('rosterOfficersCount');
  const jcosSpan = document.getElementById('rosterJcosCount');
  const orsSpan = document.getElementById('rosterOrsCount');
  const totalSpan = document.getElementById('rosterTotalCount');
  const sourceLabel = document.getElementById('rosterCounterSource');
  const hasRosterInput = document.querySelector('input[name="Input.HasRoster"]');
  const counterOfficersInput = document.querySelector('input[name="Input.CounterOfficers"]');
  const counterJcosInput = document.querySelector('input[name="Input.CounterJcos"]');
  const counterOrsInput = document.querySelector('input[name="Input.CounterOrs"]');
  const counterTotalInput = document.querySelector('input[name="Input.CounterTotal"]');
  const counterSourceInput = document.querySelector('input[name="Input.CounterSource"]');
  const rowVersionInput = document.querySelector('input[name="Input.RowVersion"]');

  const base64Payload = context.dataset.rows || '';
  if (base64Payload) {
    try {
      const json = decodeBase64(base64Payload);
      const rows = JSON.parse(json);
      renderRoster(rows);
    } catch {
      // ignore malformed payloads
    }
  }

  recalc();

  btnAdd?.addEventListener('click', () => {
    addRow();
    recalc();
  });

  btnPaste?.addEventListener('click', async () => {
    await handlePaste();
  });

  btnSave?.addEventListener('click', async () => {
    await handleSave();
  });

  rosterBody?.addEventListener('input', recalc);
  rosterBody?.addEventListener('change', recalc);
  rosterBody?.addEventListener('click', (event) => {
    const target = event.target;
    if (target instanceof HTMLElement && target.dataset.action === 'delete') {
      target.closest('tr')?.remove();
      recalc();
    }
  });

  function addRow(row) {
    if (!rosterBody) {
      return;
    }

    const data = row || {};
    const category = typeof data.category === 'number' ? data.category : 2;
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td><input class="form-control form-control-sm" data-field="armyNumber" value="${escapeHtml(data.armyNumber ?? '')}"></td>
      <td><input class="form-control form-control-sm" data-field="rank" value="${escapeHtml(data.rank ?? '')}"></td>
      <td><input class="form-control form-control-sm" data-field="name" value="${escapeHtml(data.name ?? '')}"></td>
      <td><input class="form-control form-control-sm" data-field="unitName" value="${escapeHtml(data.unitName ?? '')}"></td>
      <td>
        <select class="form-select form-select-sm" data-field="category">
          <option value="0"${category === 0 ? ' selected' : ''}>Officer</option>
          <option value="1"${category === 1 ? ' selected' : ''}>JCO</option>
          <option value="2"${category === 2 ? ' selected' : ''}>OR</option>
        </select>
      </td>
      <td class="text-end">
        <input type="hidden" data-field="id" value="${data.id ?? ''}">
        <button type="button" class="btn btn-sm btn-outline-danger" data-action="delete">Remove</button>
      </td>`;
    rosterBody.appendChild(tr);
  }

  function renderRoster(rows) {
    if (!rosterBody) {
      return;
    }

    rosterBody.innerHTML = '';
    if (Array.isArray(rows)) {
      rows.forEach((row) => addRow(row));
    }
    recalc();
    context.dataset.rows = encodeBase64(JSON.stringify(rows ?? []));
  }

  function collectRows() {
    const rows = [];
    if (!rosterBody) {
      return rows;
    }

    rosterBody.querySelectorAll('tr').forEach((tr) => {
      const idValue = tr.querySelector('[data-field="id"]')?.value || '';
      const armyNumber = tr.querySelector('[data-field="armyNumber"]')?.value?.trim() || '';
      const rank = tr.querySelector('[data-field="rank"]')?.value?.trim() || '';
      const name = tr.querySelector('[data-field="name"]')?.value?.trim() || '';
      const unitName = tr.querySelector('[data-field="unitName"]')?.value?.trim() || '';
      const category = Number(tr.querySelector('[data-field="category"]')?.value ?? 2);

      if (!idValue && !armyNumber && !rank && !name && !unitName) {
        return;
      }

      rows.push({
        id: idValue ? Number(idValue) : null,
        armyNumber,
        rank,
        name,
        unitName,
        category: Number.isNaN(category) ? 2 : category
      });
    });

    return rows;
  }

  function recalc() {
    if (!rosterBody) {
      return;
    }

    let officers = 0;
    let jcos = 0;
    let ors = 0;

    rosterBody.querySelectorAll('tr').forEach((tr) => {
      const category = Number(tr.querySelector('[data-field="category"]')?.value ?? 2);
      if (category === 0) {
        officers += 1;
      } else if (category === 1) {
        jcos += 1;
      } else {
        ors += 1;
      }
    });

    const total = officers + jcos + ors;

    if (officersSpan) officersSpan.textContent = String(officers);
    if (jcosSpan) jcosSpan.textContent = String(jcos);
    if (orsSpan) orsSpan.textContent = String(ors);
    if (totalSpan) totalSpan.textContent = String(total);
  }

  function updateCounterSummary(officers, jcos, ors, total, source) {
    if (officersSpan) officersSpan.textContent = String(officers);
    if (jcosSpan) jcosSpan.textContent = String(jcos);
    if (orsSpan) orsSpan.textContent = String(ors);
    if (totalSpan) totalSpan.textContent = String(total);

    if (sourceLabel) {
      const label = source ? source : total > 0 ? 'Roster' : 'Legacy counts';
      sourceLabel.textContent = `Source: ${label}`;
    }

    if (hasRosterInput) {
      hasRosterInput.value = total > 0 ? 'true' : 'false';
    }

    if (counterOfficersInput) counterOfficersInput.value = String(officers);
    if (counterJcosInput) counterJcosInput.value = String(jcos);
    if (counterOrsInput) counterOrsInput.value = String(ors);
    if (counterTotalInput) counterTotalInput.value = String(total);
    if (counterSourceInput) counterSourceInput.value = source || (total > 0 ? 'Roster' : 'Legacy');
  }

  async function handlePaste() {
    if (!navigator.clipboard || !navigator.clipboard.readText) {
      showError('Clipboard paste is not supported in this browser.');
      return;
    }

    try {
      const text = await navigator.clipboard.readText();
      if (!text) {
        return;
      }

      const rows = parseClipboard(text);
      if (rows.length === 0) {
        showError('No rows detected in the clipboard contents.');
        return;
      }

      rows.forEach((row) => addRow(row));
      recalc();
      showError('');
    } catch {
      showError('Clipboard access was denied. Copy the data and paste using Ctrl+V inside a cell.');
    }
  }

  function parseClipboard(text) {
    return text
      .split(/\r?\n/)
      .map((line) => line.trim())
      .filter((line) => line.length > 0)
      .map((line) => {
        const cells = line.split(/\t/);
        return {
          armyNumber: cells[0] ?? '',
          rank: cells[1] ?? '',
          name: cells[2] ?? '',
          unitName: cells[3] ?? '',
          category: inferCategoryFromText(cells[4])
        };
      });
  }

  function inferCategoryFromText(value) {
    if (!value) {
      return 2;
    }

    const normalized = String(value).trim().toLowerCase();
    if (!normalized) {
      return 2;
    }

    if (normalized.startsWith('o') || normalized.includes('off')) {
      return 0;
    }

    if (normalized.startsWith('j') || normalized.includes('jco') || normalized.includes('subedar')) {
      return 1;
    }

    return 2;
  }

  async function handleSave() {
    if (!btnSave) {
      return;
    }

    toggleBusy(true);
    showError('');

    const payload = {
      trainingId,
      rowVersion: context.dataset.rowVersion || '',
      rows: collectRows()
    };

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';

    try {
      const response = await fetch(`${window.location.pathname}?handler=UpsertRoster`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'RequestVerificationToken': token
        },
        body: JSON.stringify(payload)
      });

      if (!response.ok) {
        const message = await extractErrorMessage(response);
        showError(message ?? 'Failed to save roster. Please try again.');
        return;
      }

      const data = await response.json();

      if (!data || data.ok === false) {
        const serverMessage = data && typeof data.message === 'string' ? data.message : null;
        showError(serverMessage ?? 'Failed to save roster. Please try again.');
        return;
      }

      const newRowVersion = data?.rowVersion || '';
      context.dataset.rowVersion = newRowVersion;
      if (btnSave) {
        btnSave.dataset.rowVersion = newRowVersion;
      }
      if (rowVersionInput) {
        rowVersionInput.value = newRowVersion;
      }

      const roster = Array.isArray(data?.roster) ? data.roster : [];
      renderRoster(roster);

      if (Array.isArray(roster)) {
        context.dataset.rows = encodeBase64(JSON.stringify(roster));
      }

      const counters = data?.counters || {};
      updateCounterSummary(
        Number(counters.officers ?? 0),
        Number(counters.jcos ?? 0),
        Number(counters.ors ?? 0),
        Number(counters.total ?? 0),
        typeof counters.source === 'string' ? counters.source : undefined
      );

      showError('');
    } catch {
      showError('Failed to save roster. Please try again.');
    } finally {
      toggleBusy(false);
    }
  }

  function toggleBusy(isBusy) {
    if (!btnSave) {
      return;
    }

    btnSave.disabled = isBusy;
    btnSave.setAttribute('aria-busy', isBusy ? 'true' : 'false');
  }

  function showError(message) {
    if (!errorAlert) {
      return;
    }

    if (!message) {
      errorAlert.classList.add('d-none');
      errorAlert.textContent = '';
    } else {
      errorAlert.classList.remove('d-none');
      errorAlert.textContent = message;
    }
  }
}

function extractErrorMessage(response) {
  return response
    .text()
    .then((body) => {
      try {
        const parsed = JSON.parse(body);
        if (parsed && typeof parsed.message === 'string') {
          return parsed.message;
        }
      } catch {
        // ignore parse failures
      }
      return body && body.length > 0 ? body : null;
    })
    .catch(() => null);
}

function decodeBase64(value) {
  try {
    return atob(value);
  } catch {
    return '';
  }
}

function encodeBase64(value) {
  try {
    return btoa(value);
  } catch {
    return '';
  }
}

function escapeHtml(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}
