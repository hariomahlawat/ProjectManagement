import { initSparklines } from './charts/sparkline.js';
import { initDrawer } from './navigation/drawer.js';
import { initCreatePartnerModal } from './projects/industry-partners/create-partner-modal.js';
import { initIndustryPartnerDirectoryFilters } from './projects/industry-partners/directory-filters.js';
import { initIndustryPartnerFeedback } from './projects/industry-partners/feedback.js';
import { initLinkProjectDrawer } from './projects/industry-partners/link-project-drawer.js';
import { initTooltips } from './utils/tooltips.js';

// Section: Global bootstrap
function boot() {
  initDrawer();
  initCreatePartnerModal();
  initIndustryPartnerDirectoryFilters();
  initIndustryPartnerFeedback();
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
