import { initSparklines } from './charts/sparkline.js';
import { initDrawer } from './navigation/drawer.js';

function boot() {
  initDrawer();
  initSparklines();
}

// Run now if DOM is ready; otherwise wait
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
