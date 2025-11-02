(function () {
    const input = document.querySelector('[data-docrepo-upload-input]');
    const frame = document.querySelector('[data-docrepo-preview-frame]');
    const placeholder = document.querySelector('[data-docrepo-preview-placeholder]');

    if (!input || !frame) {
        return;
    }

    const showPlaceholder = () => {
        frame.classList.add('d-none');
        frame.removeAttribute('src');
        if (placeholder) {
            placeholder.classList.remove('d-none');
        }
    };

    const showFrame = (url) => {
        if (placeholder) {
            placeholder.classList.add('d-none');
        }
        frame.classList.remove('d-none');
        frame.src = url;
        frame.onload = () => URL.revokeObjectURL(url);
    };

    if (!frame.getAttribute('src')) {
        frame.classList.add('d-none');
    } else if (placeholder) {
        placeholder.classList.add('d-none');
    }

    input.addEventListener('change', () => {
        const file = input.files && input.files[0];
        if (!file) {
            showPlaceholder();
            return;
        }

        const url = URL.createObjectURL(file);
        showFrame(url);
    });
})();
