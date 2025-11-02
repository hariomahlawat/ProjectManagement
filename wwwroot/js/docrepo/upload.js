(function () {
    const input = document.getElementById('pdfInput');
    const frame = document.getElementById('pdfPreview');
    if (!input || !frame) {
        return;
    }

    input.addEventListener('change', () => {
        const file = input.files && input.files[0];
        if (!file) {
            frame.removeAttribute('src');
            return;
        }

        const url = URL.createObjectURL(file);
        frame.src = url;
        frame.onload = () => URL.revokeObjectURL(url);
    });
})();
