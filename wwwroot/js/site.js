import { initPasswordToggles } from './utils/password-toggle.js';
import { initSparklines } from './charts/sparkline.js';

document.addEventListener('DOMContentLoaded', () => {
  initPasswordToggles();
  initSparklines();
});
