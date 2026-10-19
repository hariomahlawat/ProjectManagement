(function () {
    // --- Requeue ALL ---
    const allBtn = document.getElementById('requeueAllBtn');
    const allStatus = document.getElementById('requeueAllStatus');
    const allForm = document.getElementById('requeueAllForm');

    if (allForm && allBtn) {
        allForm.addEventListener('submit', function () {
            allBtn.disabled = true;
            allBtn.textContent = 'Re-queuing…';
            if (allStatus) {
                allStatus.classList.remove('d-none');
            }
        });
    }

    // --- Individual rows ---
    const itemForms = document.querySelectorAll('.requeue-item-form');
    itemForms.forEach(f => {
        f.addEventListener('submit', function () {
            const btn = f.querySelector('button[type="submit"]');
            if (btn) {
                btn.disabled = true;
                btn.textContent = 'Re-queuing…';
            }
        });
    });
})();
