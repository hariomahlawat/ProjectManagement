import { generatePassword } from '../utils/password.js';

document.addEventListener('DOMContentLoaded', () => {
  const pwdInput = document.getElementById('NewPassword');
  document.getElementById('genPwd').addEventListener('click', () => {
    pwdInput.value = generatePassword();
  });
  document.getElementById('cpyPwd').addEventListener('click', () => {
    navigator.clipboard.writeText(pwdInput.value);
  });
});
