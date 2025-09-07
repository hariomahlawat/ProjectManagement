(function () {
  function toggle(btn) {
    const targetSel = btn.getAttribute('data-target');
    const input = document.querySelector(targetSel);
    if (!input) return;

    const use = btn.querySelector('svg use');
    const isHidden = input.type === 'password';

    input.type = isHidden ? 'text' : 'password';

    if (use) {
      const base = (use.getAttribute('href') || '').split('#')[0];
      const icon = isHidden ? 'eye-slash' : 'eye';
      use.setAttribute('href', `${base}#${icon}`);
      use.setAttribute('xlink:href', `${base}#${icon}`);
    }

    btn.setAttribute('aria-pressed', String(isHidden));
    btn.setAttribute('aria-label', isHidden ? 'Hide password' : 'Show password');
    btn.setAttribute('title', isHidden ? 'Hide password' : 'Show password');

    btn.focus();
  }

  function init(container) {
    const buttons = (container || document).querySelectorAll('.password-toggle');
    buttons.forEach(btn => {
      const targetSel = btn.getAttribute('data-target');
      const input = document.querySelector(targetSel);
      const use = btn.querySelector('svg use');
      if (input && use) {
        const base = (use.getAttribute('href') || '').split('#')[0];
        const icon = input.type === 'password' ? 'eye' : 'eye-slash';
        use.setAttribute('href', `${base}#${icon}`);
        use.setAttribute('xlink:href', `${base}#${icon}`);
      }

      btn.addEventListener('click', () => toggle(btn));

      btn.addEventListener('keydown', (e) => {
        if (e.key === ' ' || e.key === 'Enter') {
          e.preventDefault();
          toggle(btn);
        }
      });
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => init());
  } else {
    init();
  }

  window.initPasswordToggles = init;
})();
