/* ---------- SECTION: Pending approvals row navigation ---------- */

const INTERACTIVE_SELECTOR = 'a, button, input, select, textarea, label';

function isInteractiveElement(target) {
  return target.closest(INTERACTIVE_SELECTOR) !== null;
}

function getRowTarget(row) {
  const url = row?.dataset?.approvalsUrl;
  return url && url.trim().length > 0 ? url : null;
}

function handleRowClick(event) {
  const row = event.currentTarget;
  if (isInteractiveElement(event.target)) {
    return;
  }

  const target = getRowTarget(row);
  if (target) {
    window.location.assign(target);
  }
}

function handleRowKeydown(event) {
  if (event.key !== 'Enter' && event.key !== ' ') {
    return;
  }

  if (isInteractiveElement(event.target)) {
    return;
  }

  event.preventDefault();
  const row = event.currentTarget;
  const target = getRowTarget(row);
  if (target) {
    window.location.assign(target);
  }
}

export function initPendingApprovalsRows() {
  /* ---------- SECTION: DOM bindings ---------- */
  const rows = document.querySelectorAll('[data-approvals-row="true"]');
  if (!rows.length) {
    return;
  }

  rows.forEach((row) => {
    row.addEventListener('click', handleRowClick);
    row.addEventListener('keydown', handleRowKeydown);
  });
}
