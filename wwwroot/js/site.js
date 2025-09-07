import { initSparklines } from './charts/sparkline.js';

function boot() {
  initSparklines();
}

// Run now if DOM is ready; otherwise wait
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
