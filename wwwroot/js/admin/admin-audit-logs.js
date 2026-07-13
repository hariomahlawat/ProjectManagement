const fallbackPalette = {
  axisColor: '#64748b',
  gridColor: '#e2e8f0',
  neutral: '#94a3b8',
  accents: ['#315efb', '#d97706', '#dc2626', '#16a34a']
};

function palette() {
  const configured = window.PMTheme?.getChartPalette?.();
  return configured && Array.isArray(configured.accents) && configured.accents.length > 0
    ? configured
    : fallbackPalette;
}

function alpha(hex, opacity) {
  const match = String(hex || '').match(/^#([\da-f]{6})$/i);
  if (!match) return hex;
  const value = Number.parseInt(match[1], 16);
  return `rgba(${(value >> 16) & 255}, ${(value >> 8) & 255}, ${value & 255}, ${opacity})`;
}

function safeParse(value, fallback = []) {
  try {
    return JSON.parse(value || '');
  } catch (error) {
    console.error('Audit data could not be parsed.', error);
    return fallback;
  }
}

function setText(root, selector, value, fallback = '—') {
  const element = root.querySelector(selector);
  if (element) element.textContent = value || fallback;
}

function setOptionalSection(root, wrapperSelector, valueSelector, value) {
  const wrapper = root.querySelector(wrapperSelector);
  const target = root.querySelector(valueSelector);
  if (!wrapper || !target) return;
  const hasValue = typeof value === 'string' && value.trim().length > 0;
  wrapper.hidden = !hasValue;
  target.textContent = hasValue ? value : '';
}

function initialiseFilter() {
  const form = document.querySelector('[data-admin-audit-filter]');
  if (!form) return;
  let timer;
  for (const control of form.querySelectorAll('[data-admin-audit-submit]')) {
    control.addEventListener('change', () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => form.requestSubmit(), 120);
    });
  }
}

function initialiseChart() {
  const canvas = document.getElementById('adminAuditTrend');
  if (!canvas || !window.Chart) return;
  const rows = safeParse(canvas.dataset.series, []);
  const colors = palette();
  new window.Chart(canvas, {
    type: 'bar',
    data: {
      labels: rows.map(row => row.label),
      datasets: [{
        label: 'Audit events',
        data: rows.map(row => row.count),
        borderColor: colors.accents[0] || '#315efb',
        backgroundColor: alpha(colors.accents[0] || '#315efb', 0.72),
        borderWidth: 1,
        borderRadius: 5,
        maxBarThickness: 30
      }]
    },
    options: {
      maintainAspectRatio: false,
      plugins: { legend: { display: false } },
      scales: {
        x: { grid: { display: false }, ticks: { color: colors.axisColor, maxTicksLimit: 12 } },
        y: { beginAtZero: true, ticks: { precision: 0, color: colors.axisColor }, grid: { color: colors.gridColor } }
      }
    }
  });
}

function initialiseDrawer() {
  const drawerElement = document.getElementById('adminAuditDrawer');
  if (!drawerElement || !window.bootstrap?.Offcanvas) return;
  const drawer = window.bootstrap.Offcanvas.getOrCreateInstance(drawerElement);
  let currentRaw = '';

  for (const button of document.querySelectorAll('[data-admin-audit-detail]')) {
    button.addEventListener('click', () => {
      const source = document.getElementById(button.dataset.adminAuditDetail || '');
      if (!source) return;
      const detail = safeParse(source.textContent, null);
      if (!detail) return;

      setText(drawerElement, '[data-audit-category]', detail.category, 'Audit event');
      setText(drawerElement, '[data-audit-title]', detail.title, 'Event details');
      setText(drawerElement, '[data-audit-time]', detail.time);
      setText(drawerElement, '[data-audit-actor]', detail.actorUserId ? `${detail.actor} · ${detail.actorUserId}` : detail.actor);
      setText(drawerElement, '[data-audit-record]', detail.affectedRecord);
      setText(drawerElement, '[data-audit-source]', detail.ip);
      setText(drawerElement, '[data-audit-client]', detail.client);
      setText(drawerElement, '[data-audit-outcome]', detail.outcome);
      setText(drawerElement, '[data-audit-reason]', detail.reason);
      setText(drawerElement, '[data-audit-trace]', detail.traceId || detail.origin);

      const status = drawerElement.querySelector('[data-audit-severity]');
      if (status) {
        status.className = `admin-status-pill admin-status-pill--${detail.tone || 'neutral'}`;
        status.textContent = detail.severity || 'Information';
      }

      setOptionalSection(drawerElement, '[data-audit-message-wrap]', '[data-audit-message]', detail.message);
      setOptionalSection(drawerElement, '[data-audit-before-wrap]', '[data-audit-before]', detail.before);
      setOptionalSection(drawerElement, '[data-audit-after-wrap]', '[data-audit-after]', detail.after);
      setOptionalSection(drawerElement, '[data-audit-raw-wrap]', '[data-audit-raw]', detail.rawJson);
      currentRaw = detail.rawJson || '';

      const link = drawerElement.querySelector('[data-audit-entity-link]');
      if (link) {
        const showLink = Boolean(detail.entityLinkHref && detail.entityLinkText);
        link.hidden = !showLink;
        link.textContent = showLink ? detail.entityLinkText : '';
        if (showLink) link.setAttribute('href', detail.entityLinkHref);
        else link.removeAttribute('href');
      }

      drawer.show();
    });
  }

  const copyButton = drawerElement.querySelector('[data-audit-copy]');
  copyButton?.addEventListener('click', async () => {
    if (!currentRaw) return;
    try {
      await navigator.clipboard.writeText(currentRaw);
      const original = copyButton.textContent;
      copyButton.textContent = 'Copied';
      window.setTimeout(() => { copyButton.textContent = original; }, 1400);
    } catch (error) {
      console.error('Unable to copy audit data.', error);
    }
  });
}

function initialise() {
  initialiseFilter();
  initialiseChart();
  initialiseDrawer();
}

document.readyState === 'loading'
  ? document.addEventListener('DOMContentLoaded', initialise, { once: true })
  : initialise();
