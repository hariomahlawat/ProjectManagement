// wwwroot/js/todo-widget.js

(function () {
  const RING_SELECTOR = '.mission-ring[data-percent]';

  function clampPercent(value) {
    if (Number.isNaN(value)) return null;
    if (!Number.isFinite(value)) return null;
    return Math.min(100, Math.max(0, value));
  }

  function applyRing(root) {
    if (!root) return;
    const rings = root.matches?.(RING_SELECTOR) ? [root] : root.querySelectorAll(RING_SELECTOR);
    rings.forEach((ring) => {
      const raw = ring.getAttribute('data-percent');
      if (raw == null) return;
      const parsed = Number.parseFloat(raw);
      const clamped = clampPercent(parsed);
      if (clamped == null) return;
      ring.style.setProperty('--p', String(clamped));
    });
  }

  function init(root) {
    applyRing(root || document);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => init());
  } else {
    init();
  }

  document.addEventListener('htmx:afterSwap', (event) => {
    const target = event.detail && event.detail.target;
    if (target instanceof HTMLElement) {
      init(target);
    }
  });
})();
