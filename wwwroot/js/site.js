import { initSparklines } from './charts/sparkline.js';
import { initDrawer } from './navigation/drawer.js';
import { initPendingApprovalsRows } from './pages/approvals-pending.js';
import { initTooltips } from './utils/tooltips.js';

function boot() {
  /* ---------- SECTION: Navigation ---------- */
  initDrawer();

  /* ---------- SECTION: Dashboards ---------- */
  initSparklines();

  /* ---------- SECTION: Approvals ---------- */
  initPendingApprovalsRows();

  /* ---------- SECTION: Utilities ---------- */
  initTooltips();
}

// Run now if DOM is ready; otherwise wait
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
