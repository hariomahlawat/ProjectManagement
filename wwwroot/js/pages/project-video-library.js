const dialog = document.querySelector('[data-video-remove-dialog]');
const removeForm = document.querySelector('[data-video-remove-form]');
const titleTarget = dialog?.querySelector('[data-video-remove-title]');
const confirmButton = dialog?.querySelector('[data-video-remove-confirm]');
let pendingVideoId = '';

for (const button of document.querySelectorAll('[data-video-remove]')) {
    button.addEventListener('click', () => {
        pendingVideoId = button.dataset.videoId ?? '';
        const title = button.dataset.videoTitle ?? 'This video';

        if (titleTarget) {
            titleTarget.textContent = title;
        }

        if (dialog && typeof dialog.showModal === 'function') {
            dialog.showModal();
            return;
        }

        if (window.confirm(`Remove “${title}”?`)) {
            submitRemoval();
        }
    });
}

confirmButton?.addEventListener('click', submitRemoval);

dialog?.addEventListener('close', () => {
    pendingVideoId = '';
});

function submitRemoval() {
    if (!removeForm || !pendingVideoId) {
        return;
    }

    const idInput = removeForm.querySelector('input[name="videoId"]');
    if (!(idInput instanceof HTMLInputElement)) {
        return;
    }

    idInput.value = pendingVideoId;
    removeForm.requestSubmit();
}
