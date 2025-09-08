function boot() {
  const searchInput = document.getElementById('userSearch');
  const table = document.getElementById('usersTable');

  if (searchInput && table) {
    searchInput.addEventListener('input', function () {
      const term = this.value.toLowerCase();
      table.querySelectorAll('tbody tr').forEach(row => {
        row.style.display = row.textContent.toLowerCase().includes(term) ? '' : 'none';
      });
    });
  }

}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
