export function initPasswordToggles() {
  document.querySelectorAll('.password-toggle').forEach(btn => {
    const container = btn.closest('.password-container');
    if (!container) return;
    const input = container.querySelector('input');
    if (!input) return;
    const showIcon = btn.querySelector('.eye-open');
    const hideIcon = btn.querySelector('.eye-closed');
    const setVisible = visible => {
      input.type = visible ? 'text' : 'password';
      btn.setAttribute('aria-pressed', visible);
      btn.setAttribute('aria-label', visible ? 'Hide password' : 'Show password');
      if (showIcon && hideIcon) {
        showIcon.hidden = visible;
        hideIcon.hidden = !visible;
      }
    };
    btn.addEventListener('click', () => {
      const visible = input.type === 'password';
      setVisible(visible);
    });
    setVisible(false);
  });
}

// initialize toggles on load
initPasswordToggles();
