import { generatePassword } from '../utils/password.js';

function boot() {
  const pwdInput = document.getElementById('NewPassword');
  const genBtn = document.getElementById('genPwd');
  const copyBtn = document.getElementById('cpyPwd');
  if (!pwdInput || !genBtn || !copyBtn) return;

  genBtn.addEventListener('click', () => {
    pwdInput.value = generatePassword();
    pwdInput.dispatchEvent(new Event('input', { bubbles: true })); // keep client validation synced
  });

  copyBtn.addEventListener('click', async () => {
    const text = pwdInput.value || '';
    try {
      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text);
      } else {
        const ta = document.createElement('textarea');
        ta.value = text; ta.style.position = 'fixed'; ta.style.left = '-9999px';
        document.body.appendChild(ta); ta.select(); document.execCommand('copy'); ta.remove();
      }
      copyBtn.textContent = 'Copied';
    } catch {
      copyBtn.textContent = 'Copy failed';
    }
    setTimeout(() => (copyBtn.textContent = 'Copy'), 1000);
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
