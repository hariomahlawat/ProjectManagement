import { initAsyncMultiselect } from '../../widgets/async-multiselect.js';

// Section: Link project drawer helpers
function closeLinkProjectDrawer() {
  const drawerElement = document.getElementById('linkProjectDrawer');
  if (!drawerElement || !window.bootstrap?.Offcanvas) {
    return;
  }

  const instance = window.bootstrap.Offcanvas.getInstance(drawerElement) || new window.bootstrap.Offcanvas(drawerElement);
  instance.hide();
}

// Section: Link project drawer initializer
export function initLinkProjectDrawer() {
  const drawerElement = document.getElementById('linkProjectDrawer');
  if (!drawerElement) {
    return;
  }

  // Section: Initial dropdown wiring
  initAsyncMultiselect(drawerElement);

  document.body.addEventListener('link-project-saved', closeLinkProjectDrawer);

  // Section: Refresh dropdown after HTMX swaps
  document.body.addEventListener('htmx:afterSwap', (event) => {
    if (!(event.target instanceof Element)) {
      return;
    }

    if (drawerElement.contains(event.target)) {
      initAsyncMultiselect(event.target);
    }
  });
}
