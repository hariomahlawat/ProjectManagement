// Asks in the shared PRISM dialog (docs/ux/confirm-dialogs.md); the browser box only if it is missing.
const askConfirm = options => (window.PrismConfirm?.show
    ? window.PrismConfirm.show(options)
    : Promise.resolve(window.confirm([options.title, options.message].filter(Boolean).join(' '))));

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

        askConfirm({
            tone: 'danger',
            title: 'Remove this video?',
            message: 'It is removed from the project video library.',
            subject: { icon: 'bi-film', name: title },
            confirmText: 'Remove video',
            cancelText: 'Keep video'
        }).then(accepted => {
            if (accepted) submitRemoval();
        });
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
