// Client-side bootstrap validation
(() => {
  'use strict';
  const forms = document.querySelectorAll('.needs-validation');
  Array.from(forms).forEach(form => {
    form.addEventListener('submit', evt => {
      if (!form.checkValidity()) {
        evt.preventDefault();
        evt.stopPropagation();
      }
      form.classList.add('was-validated');
    }, false);
  });
  const loginAnchor = document.querySelector('a[href="#login-card"]');
  const userInput = document.getElementById('lpUserName');
  const focusUser = () => {
    userInput?.focus();
  };
  if (window.location.hash === '#login-card') {
    focusUser();
  }
  loginAnchor?.addEventListener('click', focusUser);
})();
