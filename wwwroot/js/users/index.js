document.addEventListener('DOMContentLoaded', () => {
  const searchInput = document.getElementById('userSearch');
  searchInput.addEventListener('input', function () {
    const term = this.value.toLowerCase();
    document.querySelectorAll('#usersTable tbody tr').forEach(row => {
      const text = row.textContent.toLowerCase();
      row.style.display = text.includes(term) ? '' : 'none';
    });
  });

  document.querySelectorAll('.delete-user-btn').forEach(btn => {
    btn.addEventListener('click', e => {
      const username = btn.dataset.username;
      if (!confirm(`Delete user ${username}?`)) {
        e.preventDefault();
      }
    });
  });
});
