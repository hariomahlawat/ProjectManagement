function bindPresets() {
  const from = document.querySelector('input[name="From"]');
  const to = document.querySelector('input[name="To"]');
  if (!from || !to) return;

  document.querySelectorAll('.log-preset').forEach(btn => {
    btn.addEventListener('click', () => {
      const days = parseInt(btn.dataset.days, 10);
      const now = new Date();
      const toStr = now.toISOString().slice(0, 10);

      let fromDate = new Date(now);
      if (days > 0) fromDate.setDate(now.getDate() - (days - 1));
      const fromStr = fromDate.toISOString().slice(0, 10);

      from.value = fromStr;
      to.value = toStr;
    });
  });
}

function bindJsonModal() {
  const jsonModal = document.getElementById('jsonModal');
  if (!jsonModal) return;

  const content = document.getElementById('jsonContent');
  const copyBtn = document.getElementById('copyJson');

  jsonModal.addEventListener('show.bs.modal', (ev) => {
    const trigger = ev.relatedTarget;
    const raw = trigger?.getAttribute('data-json') || '';
    try {
      const obj = JSON.parse(raw);
      content.textContent = JSON.stringify(obj, null, 2);
    } catch {
      content.textContent = raw;
    }
  });

  copyBtn?.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(content.textContent || '');
      copyBtn.innerText = 'Copied';
      setTimeout(() => (copyBtn.innerHTML = '<i class="bi bi-clipboard"></i> Copy'), 1000);
    } catch {}
  });
}

function initChart() {
  const el = document.getElementById('logsPerDay');
  if (!el || !window.Chart) return;

  let labels = [];
  let values = [];
  try {
    labels = JSON.parse(el.dataset.labels || '[]');
    values = JSON.parse(el.dataset.values || '[]');
  } catch {}

  if (labels.length === 0) return;
  if (labels.length === 1) { labels = [labels[0], labels[0]]; values = [values[0], values[0]]; }

  const ctx = el.getContext('2d');
  new Chart(ctx, {
    type: 'line',
    data: {
      labels,
      datasets: [{ label: 'Events', data: values, tension: 0.3, fill: false }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: { legend: { display: false } },
      scales: {
        x: { grid: { display: false } },
        y: { beginAtZero: true, grid: { color: 'rgba(0,0,0,.06)' }, ticks: { precision: 0 } }
      }
    }
  });
}

function boot() {
  bindPresets();
  bindJsonModal();
  initChart();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
