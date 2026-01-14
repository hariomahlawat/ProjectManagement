import { initSparklines } from './charts/sparkline.js';
import { initDrawer } from './navigation/drawer.js';
import { initLinkProjectDrawer } from './projects/industry-partners/link-project-drawer.js';
import { initTooltips } from './utils/tooltips.js';

// Section: Global bootstrap
function boot() {
  initDrawer();
  initLinkProjectDrawer();
  initSparklines();
  initTooltips();
}

// Section: DOM readiness
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
