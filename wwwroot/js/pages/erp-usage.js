(() => {
  'use strict';

  document.querySelectorAll('.admin-pagination a.disabled').forEach(link => {
    link.setAttribute('aria-disabled', 'true');
    link.removeAttribute('href');
  });

  const heatmap = document.querySelector('.erp-heatmap-scroll');
  if (heatmap) {
    heatmap.addEventListener('keydown', event => {
      if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
      event.preventDefault();
      heatmap.scrollBy({
        left: event.key === 'ArrowRight' ? 160 : -160,
        behavior: 'smooth'
      });
    });
  }
})();
