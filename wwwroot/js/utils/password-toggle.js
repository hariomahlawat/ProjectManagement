export function initPasswordToggles() {
  document.querySelectorAll('.password-container').forEach(container => {
    const input = container.querySelector('input');
    const btn = container.querySelector('.password-toggle');
    if (!input || !btn) return;
    const showIcon = btn.querySelector('.eye-open');
    const hideIcon = btn.querySelector('.eye-closed');
    const setVisible = visible => {
      input.type = visible ? 'text' : 'password';
      btn.setAttribute('aria-pressed', String(visible));
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

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initPasswordToggles, { once: true });
} else {
  initPasswordToggles();
}
