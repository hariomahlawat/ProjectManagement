(function () {
  const table = document.getElementById('ffc-dtable');
  if (!table) {
    return;
  }

  const tbody = table.querySelector('tbody');

  function esc(value) {
    return String(value ?? '').replace(/[&<>"']/g, (match) => ({
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#39;'
    })[match]);
  }

  async function load() {
    const response = await fetch('/ProjectOfficeReports/FFC/MapTableDetailed?handler=Data', { credentials: 'same-origin' });
    if (!response.ok) {
      throw new Error('Failed to load detailed data');
    }

    /** @type {{countryIso3:string,countryName:string,projectName:string,isLinked:boolean,bucket:string,latestStage:string,externalRemark:string}[]} */
    const rows = await response.json();
    return rows;
  }

  function render(rows) {
    if (!Array.isArray(rows) || rows.length === 0) {
      tbody.innerHTML = '<tr><td colspan="7" class="text-muted">No projects found.</td></tr>';
      return;
    }

    tbody.innerHTML = rows.map((row) => `
      <tr>
        <td>${esc(row.countryName)}</td>
        <td><code>${esc((row.countryIso3 || '').toUpperCase())}</code></td>
        <td>${esc(row.projectName)}</td>
        <td>${row.isLinked ? 'Yes' : 'No'}</td>
        <td>${esc(row.bucket)}</td>
        <td>${esc(row.latestStage)}</td>
        <td>${esc(row.externalRemark)}</td>
      </tr>
    `).join('');
  }

  load()
    .then(render)
    .catch(() => {
      tbody.innerHTML = '<tr><td colspan="7" class="text-danger">Failed to load detailed table.</td></tr>';
    });
})();
