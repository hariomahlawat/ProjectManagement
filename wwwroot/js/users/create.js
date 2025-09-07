import { generatePassword } from '../utils/password.js';

function boot() {
  const pwdInput = document.getElementById('Input_Password');
  if (!pwdInput) return;

  const genBtn = document.getElementById('generatePwd');
  const copyBtn = document.getElementById('copyPwd');

  genBtn?.addEventListener('click', () => {
    pwdInput.value = generatePassword();
    // trigger unobtrusive validation recheck
    pwdInput.dispatchEvent(new Event('input', { bubbles: true }));
  });

  copyBtn?.addEventListener('click', async () => {
    const text = pwdInput.value || '';
    try {
      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text);
      } else {
        // HTTP / legacy fallback
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.left = '-9999px';
        document.body.appendChild(ta);
        ta.select();
        document.execCommand('copy');
        ta.remove();
      }
      copyBtn.textContent = 'Copied';
      setTimeout(() => (copyBtn.textContent = 'Copy'), 1000);
    } catch {
      copyBtn.textContent = 'Copy failed';
      setTimeout(() => (copyBtn.textContent = 'Copy'), 1000);
    }
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
