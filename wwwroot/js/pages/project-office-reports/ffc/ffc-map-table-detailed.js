(function () {
  // SECTION: DOM references
  const table = document.getElementById('ffc-dtable');
  if (!table) {
    return;
  }

  const tbody = table.tBodies[0];
  if (!tbody) {
    return;
  }

  // SECTION: Formatting helpers
  const escapeMap = {
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;'
  };

  const costFormatter = new Intl.NumberFormat('en-IN', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2
  });

  const quantityFormatter = new Intl.NumberFormat('en-IN', {
    maximumFractionDigits: 0
  });

  function esc(value) {
    return String(value ?? '').replace(/[&<>"']/g, (match) => escapeMap[match]);
  }

  function formatCost(value) {
    if (typeof value !== 'number' || Number.isNaN(value)) {
      return '—';
    }

    return costFormatter.format(value);
  }

  function formatQuantity(value) {
    if (typeof value !== 'number' || Number.isNaN(value)) {
      return '';
    }

    return quantityFormatter.format(value);
  }

  // SECTION: Data access
  async function loadGroups() {
    const response = await fetch('?handler=Data', { credentials: 'same-origin' });
    if (!response.ok) {
      throw new Error('Failed to load detailed data');
    }

    /** @type {{countryName:string,countryIso3:string,year:number,overallRemarks:string|null,projects:{serialNumber:number,projectName:string,costInCr:number|null,quantity:number,bucket:string,progressRemark:string|null}[]}[]} */
    const groups = await response.json();
    return groups;
  }

  // SECTION: Rendering
  function render(groups) {
    if (!Array.isArray(groups) || groups.length === 0) {
      tbody.innerHTML = '<tr><td colspan="7" class="text-muted text-center">No project units found.</td></tr>';
      return;
    }

    const rows = [];

    groups.forEach((group) => {
      const projectRows = Array.isArray(group.projects) ? group.projects : [];
      if (projectRows.length === 0) {
        return;
      }

      rows.push(
        `<tr class="table-light">
          <td colspan="7">
            <strong>${esc(group.countryName)} – ${esc(group.year)}</strong>
            <span class="ms-2 text-muted">${esc(group.countryIso3)}</span>
          </td>
        </tr>`
      );

      const overallRemarks = esc(group.overallRemarks || '');
      const rowspan = projectRows.length;

      projectRows.forEach((project, index) => {
        const serialNumber = typeof project.serialNumber === 'number' ? project.serialNumber : index + 1;
        const cost = formatCost(project.costInCr);
        const qty = formatQuantity(project.quantity);
        const status = esc(project.bucket || '');
        const progress = esc(project.progressRemark || '');
        const name = esc(project.projectName || '');

        rows.push(
          `<tr>
            <td class="text-muted">${serialNumber}</td>
            <td>${name}</td>
            <td class="text-end">${cost}</td>
            <td class="text-end">${qty}</td>
            <td>${status}</td>
            <td>${progress}</td>
            ${index === 0 ? `<td rowspan="${rowspan}" class="align-top">${overallRemarks}</td>` : ''}
          </tr>`
        );
      });
    });

    if (rows.length === 0) {
      tbody.innerHTML = '<tr><td colspan="7" class="text-muted text-center">No project units found.</td></tr>';
      return;
    }

    tbody.innerHTML = rows.join('');
  }

  loadGroups()
    .then(render)
    .catch(() => {
      tbody.innerHTML = '<tr><td colspan="7" class="text-danger text-center">Failed to load detailed table.</td></tr>';
    });
})();
