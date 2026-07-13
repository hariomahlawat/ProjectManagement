import { generatePassword } from '../utils/password.js';

function boot() {
  const pwdInput = document.getElementById('Input_Password')
    ?? document.getElementById('NewPassword');
  const genBtn = document.getElementById('generatePwd')
    ?? document.getElementById('genPwd');
  const copyBtn = document.getElementById('copyPwd')
    ?? document.getElementById('cpyPwd');
  if (!pwdInput || !genBtn || !copyBtn) return;

  const originalCopyMarkup = copyBtn.innerHTML;

  genBtn.addEventListener('click', () => {
    const generatedLength = Number.parseInt(pwdInput.dataset.generatedLength || '16', 10);
    const requiredUnique = Number.parseInt(pwdInput.dataset.requiredUnique || '1', 10);
    pwdInput.value = generatePassword(
      Number.isFinite(generatedLength) ? generatedLength : 16,
      Number.isFinite(requiredUnique) ? requiredUnique : 1,
    );
    pwdInput.dispatchEvent(new Event('input', { bubbles: true }));
  });

  copyBtn.addEventListener('click', async () => {
    const text = pwdInput.value || '';
    try {
      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text);
      } else {
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.style.position = 'fixed';
        textarea.style.left = '-9999px';
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand('copy');
        textarea.remove();
      }
      copyBtn.textContent = 'Copied';
    } catch {
      copyBtn.textContent = 'Copy failed';
    }
    setTimeout(() => { copyBtn.innerHTML = originalCopyMarkup; }, 1000);
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
