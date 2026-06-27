import { initSparklines } from './charts/sparkline.js';
import { initDrawer } from './navigation/drawer.js';
import { initPendingApprovalsRows } from './pages/approvals-pending.js';
import { initTooltips } from './utils/tooltips.js';

/**
 * Runs one shared-page initializer without allowing an optional feature to
 * disable critical shell behaviour such as primary navigation.
 *
 * @param {string} name Human-readable initializer name for diagnostics.
 * @param {() => void} initializer Initializer to execute.
 */
function runInitializer(name, initializer) {
  try {
    initializer();
  } catch (error) {
    // Keep the application shell operational and retain actionable diagnostics.
    console.error(`[PRISM] Failed to initialise ${name}.`, error);
  }
}

function boot() {
  // Navigation is deliberately initialised first because it is core shell
  // functionality and must not depend on any page-specific feature.
  runInitializer('navigation drawer', initDrawer);
  runInitializer('sparklines', initSparklines);
  runInitializer('pending approvals filters', initPendingApprovalsRows);
  runInitializer('tooltips', initTooltips);
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
