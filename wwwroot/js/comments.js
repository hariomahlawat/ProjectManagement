document.addEventListener('DOMContentLoaded', () => {
  const fileInputs = document.querySelectorAll('input[type="file"][data-max-size]');
  fileInputs.forEach(input => {
    input.addEventListener('change', () => {
      const maxSize = Number.parseInt(input.getAttribute('data-max-size') ?? '0', 10);
      if (!Number.isFinite(maxSize) || maxSize <= 0) {
        return;
      }

      const files = Array.from(input.files ?? []);
      const tooLarge = files.find(file => file.size > maxSize);
      if (tooLarge) {
        const sizeMb = (maxSize / (1024 * 1024)).toFixed(0);
        window.alert(`File "${tooLarge.name}" exceeds the limit of ${sizeMb} MB.`);
        input.value = '';
      }
    });
  });

  const commentConfirmForms = document.querySelectorAll('form[data-comments-confirm]');
  commentConfirmForms.forEach(form => {
    form.addEventListener('submit', event => {
      const message = form.getAttribute('data-comments-confirm');
      if (message && !window.confirm(message)) {
        event.preventDefault();
      }
    });
  });

  const genericConfirmForms = document.querySelectorAll('form[data-confirm]:not([data-comments-confirm])');
  genericConfirmForms.forEach(form => {
    form.addEventListener('submit', event => {
      const message = form.getAttribute('data-confirm');
      if (message && !window.confirm(message)) {
        event.preventDefault();
      }
    });
  });
});
