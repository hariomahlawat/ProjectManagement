function boot() {
  const from = document.querySelector('input[name="From"]');
  const to = document.querySelector('input[name="To"]');
  if (!from || !to) return;

  document.querySelectorAll('.log-preset').forEach(btn => {
    btn.addEventListener('click', () => {
      const days = parseInt(btn.dataset.days, 10);
      const now = new Date();
      const toStr = now.toISOString().slice(0, 10);
      let fromDate = new Date(now);
      if (days > 0) {
        fromDate.setDate(now.getDate() - (days - 1));
      }
      const fromStr = fromDate.toISOString().slice(0, 10);
      from.value = fromStr;
      to.value = toStr;
    });
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
