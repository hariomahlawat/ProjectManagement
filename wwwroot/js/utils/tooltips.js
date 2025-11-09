// SECTION: Constants
const TOOLTIP_SELECTOR = '[data-bs-toggle="tooltip"]';

// SECTION: State
let listenersRegistered = false;

// SECTION: Helpers
function getBootstrapLibrary() {
  const { bootstrap } = window;
  if (!bootstrap || typeof bootstrap.Tooltip !== 'function') {
    return undefined;
  }
  return bootstrap;
}

function resolveContext(root) {
  if (!root || typeof root.querySelectorAll !== 'function') {
    return document;
  }
  return root;
}

function applyTooltips(root) {
  const context = resolveContext(root);
  const bootstrapLib = getBootstrapLibrary();
  if (!bootstrapLib) {
    return;
  }

  const elements = context.querySelectorAll(TOOLTIP_SELECTOR);
  elements.forEach((element) => {
    bootstrapLib.Tooltip.getOrCreateInstance(element);
  });
}

function handleDynamicUpdate(event) {
  applyTooltips(event?.target);
}

// SECTION: Public API
export function refreshTooltips(root = document) {
  applyTooltips(root);
}

export function initTooltips() {
  applyTooltips(document);

  if (!listenersRegistered) {
    listenersRegistered = true;
    document.addEventListener('htmx:afterSwap', handleDynamicUpdate);
    document.addEventListener('htmx:afterSettle', handleDynamicUpdate);
  }
}

// SECTION: Global exposure
window.projectManagement = window.projectManagement || {};
window.projectManagement.tooltips = {
  init: initTooltips,
  refresh: refreshTooltips,
};
