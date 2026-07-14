(() => {
  'use strict';

  const context = document.querySelector('[data-erp-usage-context]');
  const moduleKey = context?.dataset?.erpUsageModule;
  const intervalSeconds = Number.parseInt(context?.dataset?.erpUsageHeartbeatSeconds || '', 10);
  const idleMinutes = Number.parseInt(context?.dataset?.erpUsageIdleMinutes || '', 10);
  const token = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content');

  if (!moduleKey || !Number.isFinite(intervalSeconds) || intervalSeconds < 60 || !token) {
    return;
  }

  const idleThresholdMs = Math.max(2, Number.isFinite(idleMinutes) ? idleMinutes : 10) * 60 * 1000;
  // Navigation is recorded by the server. An interactive heartbeat is emitted only
  // after a real browser interaction, so an untouched tab cannot appear active.
  let lastInteractionUtc = 0;
  let sending = false;

  const markInteraction = () => {
    lastInteractionUtc = Date.now();
  };

  ['click', 'keydown', 'scroll', 'touchstart', 'pointerdown'].forEach(eventName => {
    window.addEventListener(eventName, markInteraction, { passive: true, capture: false });
  });

  const sendHeartbeat = async () => {
    if (sending || document.visibilityState !== 'visible') return;
    if (lastInteractionUtc <= 0 || (Date.now() - lastInteractionUtc) > idleThresholdMs) return;

    sending = true;
    try {
      await fetch('/api/usage/heartbeat', {
        method: 'POST',
        credentials: 'same-origin',
        keepalive: true,
        headers: {
          'Content-Type': 'application/json',
          'X-CSRF-TOKEN': token
        },
        body: JSON.stringify({ moduleKey })
      });
    } catch {
      // Usage telemetry must never disturb the operational page.
    } finally {
      sending = false;
    }
  };

  window.setInterval(sendHeartbeat, intervalSeconds * 1000);
})();
