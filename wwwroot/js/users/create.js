import { generatePassword } from '../utils/password.js';

document.addEventListener('DOMContentLoaded', () => {
  const pwdInput = document.getElementById('Input_Password');
  document.getElementById('generatePwd').addEventListener('click', () => {
    pwdInput.value = generatePassword();
  });
  document.getElementById('copyPwd').addEventListener('click', () => {
    navigator.clipboard.writeText(pwdInput.value);
  });
});
