// SECTION: Shared application session-expiry notification state
let sessionExpiredShown = false;

// SECTION: Session-expiry event dispatcher
export function notifySessionExpired() {
  if (sessionExpiredShown) return;
  sessionExpiredShown = true;
  document.dispatchEvent(new CustomEvent('app:session-expired'));
}

// SECTION: Test and reauthentication reset hook
export function resetSessionExpiredNotification() {
  sessionExpiredShown = false;
}
