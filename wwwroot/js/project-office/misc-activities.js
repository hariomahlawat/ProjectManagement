document.addEventListener('DOMContentLoaded', () => {
  document.querySelectorAll('input[type="file"][data-max-size]').forEach(input => {
    input.addEventListener('change', () => {
      const maxSize = Number.parseInt(input.getAttribute('data-max-size') ?? '0', 10);
      const allowedTypes = (input.getAttribute('data-allowed-types') ?? '')
        .split(',')
        .map(value => value.trim())
        .filter(value => value.length > 0);

      const files = Array.from(input.files ?? []);
      if (files.length === 0) {
        return;
      }

      if (Number.isFinite(maxSize) && maxSize > 0) {
        const tooLarge = files.find(file => file.size > maxSize);
        if (tooLarge) {
          const limitMb = (maxSize / (1024 * 1024)).toFixed(0);
          window.alert(`File "${tooLarge.name}" exceeds the limit of ${limitMb} MB.`);
          input.value = '';
          return;
        }
      }

      if (allowedTypes.length > 0) {
        const unsupported = files.find(file => !allowedTypes.includes(file.type));
        if (unsupported) {
          window.alert(`File "${unsupported.name}" is not an allowed type.`);
          input.value = '';
        }
      }
    });
  });
});
