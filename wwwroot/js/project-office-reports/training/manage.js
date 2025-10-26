(() => {
  initScheduleToggle();
  initLegacyToggle();
  initRosterModule();
})();

function initLegacyToggle() {
  const toggle = document.querySelector('input[name="Input.IsLegacyRecord"]');
  if (!toggle) {
    return;
  }

  const toggleContainer = toggle.closest('[data-legacy-toggle]');
  const countsCard = document.querySelector('[data-legacy-counts]');
  const legacyFields = document.querySelector('[data-legacy-fields]');
  const rosterCard = document.querySelector('[data-legacy-roster]');
  const hint = document.querySelector('[data-legacy-hint]');

  function updateState() {
    const isLegacy = toggle.checked;

    if (toggleContainer) {
      toggleContainer.classList.toggle('text-primary', isLegacy);
    }

    if (countsCard) {
      countsCard.classList.toggle('border', isLegacy);
      countsCard.classList.toggle('border-primary-subtle', isLegacy);
    }

    if (legacyFields) {
      legacyFields.classList.toggle('d-none', !isLegacy);
    }

    if (rosterCard) {
      rosterCard.classList.toggle('d-none', isLegacy);
    }

    if (hint) {
      hint.classList.toggle('d-none', !isLegacy);
    }
  }

  toggle.addEventListener('change', updateState);
  updateState();
}

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

  const rosterBody = document.getElementById('rosterBody');
  const btnAdd = document.getElementById('btnAddRosterRow');
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
  const rosterPayloadInput = document.querySelector('input[name="Input.RosterPayload"]');
  const form = context.closest('form');

  const base64Payload = context.dataset.rows || rosterPayloadInput?.value || '';
  if (base64Payload) {
    try {
      const json = decodeBase64(base64Payload);
      const rows = JSON.parse(json);
      renderRoster(rows);
    } catch {
      // ignore malformed payloads
    }
  } else {
    syncRosterPayload([]);
  }

  recalc();

  btnAdd?.addEventListener('click', () => {
    addRow();
    recalc();
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

  form?.addEventListener('submit', () => {
    recalc();
  });

  function addRow(row) {
    if (!rosterBody) {
      return;
    }

    const data = row || {};
    const category = normalizeCategoryValue(data?.category);
    const idValue = typeof data?.id === 'number' && Number.isFinite(data.id)
      ? String(data.id)
      : typeof data?.id === 'string'
        ? data.id
        : '';
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
        <input type="hidden" data-field="id" value="${escapeHtml(idValue)}">
        <button type="button" class="btn btn-sm btn-outline-danger" data-action="delete">Remove</button>
      </td>`;
    rosterBody.appendChild(tr);
  }

  function renderRoster(rows) {
    if (!rosterBody) {
      return;
    }

    const normalized = normalizeRows(rows);
    rosterBody.innerHTML = '';
    normalized.forEach((row) => addRow(row));
    const counts = countRoster(normalized);
    syncRosterPayload(normalized);
    updateCounterSummary(counts.officers, counts.jcos, counts.ors, counts.total);
  }

  function collectRows() {
    if (!rosterBody) {
      return [];
    }

    const rawRows = [];
    rosterBody.querySelectorAll('tr').forEach((tr) => {
      const idValue = tr.querySelector('[data-field="id"]')?.value ?? '';
      const armyNumber = tr.querySelector('[data-field="armyNumber"]')?.value?.trim() ?? '';
      const rank = tr.querySelector('[data-field="rank"]')?.value?.trim() ?? '';
      const name = tr.querySelector('[data-field="name"]')?.value?.trim() ?? '';
      const unitName = tr.querySelector('[data-field="unitName"]')?.value?.trim() ?? '';
      const categoryValue = tr.querySelector('[data-field="category"]')?.value;

      if (!idValue && !armyNumber && !rank && !name && !unitName) {
        return;
      }

      rawRows.push({
        id: idValue,
        armyNumber,
        rank,
        name,
        unitName,
        category: categoryValue
      });
    });

    return normalizeRows(rawRows);
  }

  function syncRosterPayload(rows) {
    const normalized = normalizeRows(rows);
    const payload = encodeBase64(JSON.stringify(normalized));
    context.dataset.rows = payload;
    if (rosterPayloadInput) {
      rosterPayloadInput.value = payload;
    }
    return normalized;
  }

  function recalc() {
    if (!rosterBody) {
      return;
    }

    const normalized = syncRosterPayload(collectRows());
    const counts = countRoster(normalized);
    updateCounterSummary(counts.officers, counts.jcos, counts.ors, counts.total);
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

  function normalizeRows(rows) {
    if (!Array.isArray(rows)) {
      return [];
    }

    return rows.map((row) => {
      const idValue = row && Object.prototype.hasOwnProperty.call(row, 'id') ? row.id : null;
      let id = null;
      if (typeof idValue === 'number' && Number.isFinite(idValue)) {
        id = idValue;
      } else if (typeof idValue === 'string') {
        const trimmed = idValue.trim();
        if (trimmed.length > 0 && !Number.isNaN(Number(trimmed))) {
          id = Number(trimmed);
        }
      }

      const armyNumberValue = row?.armyNumber;
      const rankValue = row?.rank;
      const nameValue = row?.name;
      const unitNameValue = row?.unitName;

      return {
        id,
        armyNumber:
          typeof armyNumberValue === 'string'
            ? armyNumberValue
            : armyNumberValue != null
              ? String(armyNumberValue)
              : '',
        rank:
          typeof rankValue === 'string'
            ? rankValue
            : rankValue != null
              ? String(rankValue)
              : '',
        name:
          typeof nameValue === 'string'
            ? nameValue
            : nameValue != null
              ? String(nameValue)
              : '',
        unitName:
          typeof unitNameValue === 'string'
            ? unitNameValue
            : unitNameValue != null
              ? String(unitNameValue)
              : '',
        category: normalizeCategoryValue(row?.category)
      };
    });
  }

  function normalizeCategoryValue(value) {
    const numeric = Number(value);
    if (Number.isNaN(numeric)) {
      return 2;
    }

    if (numeric === 0 || numeric === 1) {
      return numeric;
    }

    return 2;
  }

  function countRoster(rows) {
    const counters = { officers: 0, jcos: 0, ors: 0 };

    rows.forEach((row) => {
      if (row.category === 0) {
        counters.officers += 1;
      } else if (row.category === 1) {
        counters.jcos += 1;
      } else {
        counters.ors += 1;
      }
    });

    counters.total = counters.officers + counters.jcos + counters.ors;
    return counters;
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
