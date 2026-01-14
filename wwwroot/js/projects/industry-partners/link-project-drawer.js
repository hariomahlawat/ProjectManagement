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
  if (!document.getElementById('linkProjectDrawer')) {
    return;
  }

  document.body.addEventListener('link-project-saved', closeLinkProjectDrawer);
}
