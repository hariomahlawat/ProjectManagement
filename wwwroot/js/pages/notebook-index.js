(() => {
  // SECTION: Notebook textarea autosize without inline scripts
  document.querySelectorAll('[data-autoresize]').forEach((textarea) => {
    const resize = () => { textarea.style.height = 'auto'; textarea.style.height = `${textarea.scrollHeight}px`; };
    textarea.addEventListener('input', resize);
    resize();
  });
  document.querySelectorAll('[data-submit-on-change]').forEach((input) => {
    input.addEventListener('change', () => input.form?.submit());
  });
})();
