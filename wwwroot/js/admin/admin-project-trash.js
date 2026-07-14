(() => {
    'use strict';

    const modal = document.getElementById('projectRecoveryModal');
    const form = document.getElementById('projectRecoveryForm');
    if (!modal || !form) return;

    const actionInput = form.querySelector('[data-project-command-action]');
    const idInput = form.querySelector('[data-project-command-id]');
    const title = modal.querySelector('[data-project-operation-title]');
    const eyebrow = modal.querySelector('[data-project-operation-eyebrow]');
    const summaryName = modal.querySelector('[data-project-summary-name]');
    const summaryDetail = modal.querySelector('[data-project-summary-detail]');
    const restorePanel = modal.querySelector('[data-project-restore-panel]');
    const purgePanel = modal.querySelector('[data-project-purge-panel]');
    const reason = form.querySelector('[name="Command.Reason"]');
    const confirmation = form.querySelector('[data-project-confirm-input]');
    const confirmationLabel = modal.querySelector('[data-project-confirm-label]');
    const removeAssets = form.querySelector('[data-project-remove-assets]');
    const acknowledge = form.querySelector('[data-project-acknowledge]');
    const submit = modal.querySelector('[data-project-submit]');

    const setText = (selector, value) => {
        const node = modal.querySelector(selector);
        if (node) node.textContent = value || '0';
    };

    modal.addEventListener('show.bs.modal', event => {
        const trigger = event.relatedTarget;
        if (!(trigger instanceof HTMLElement)) return;

        const action = trigger.dataset.recoveryProjectAction || 'restore';
        const projectId = trigger.dataset.projectId || '';
        const projectName = trigger.dataset.projectName || 'Selected project';
        const isPurge = action === 'purge';

        if (actionInput) actionInput.value = action;
        if (idInput) idInput.value = projectId;
        if (title) title.textContent = isPurge ? 'Delete project permanently' : 'Restore project';
        if (eyebrow) eyebrow.textContent = isPurge ? 'Controlled destruction' : 'Recovery operation';
        if (summaryName) summaryName.textContent = projectName;
        if (summaryDetail) summaryDetail.textContent = isPurge
            ? 'Verify dependencies, record the authority and confirm the exact project name.'
            : 'The project and its existing records will return to the portfolio.';
        if (restorePanel) restorePanel.hidden = isPurge;
        if (purgePanel) purgePanel.hidden = !isPurge;
        if (confirmationLabel) confirmationLabel.textContent = projectName;
        if (reason) reason.value = '';
        if (confirmation) confirmation.value = '';
        if (acknowledge) acknowledge.checked = false;
        if (removeAssets) removeAssets.checked = trigger.dataset.defaultAssets === 'true';

        setText('[data-project-documents]', trigger.dataset.documents);
        setText('[data-project-photos]', trigger.dataset.photos);
        setText('[data-project-videos]', trigger.dataset.videos);
        setText('[data-project-storage]', trigger.dataset.storage);

        if (submit) {
            submit.className = `btn ${isPurge ? 'btn-danger' : 'btn-primary'}`;
            submit.innerHTML = isPurge
                ? '<i class="bi bi-trash"></i> Delete permanently'
                : '<i class="bi bi-arrow-counterclockwise"></i> Restore project';
        }
    });

    form.addEventListener('submit', () => {
        if (!(submit instanceof HTMLButtonElement)) return;
        submit.disabled = true;
        submit.dataset.originalHtml = submit.innerHTML;
        submit.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Processing…';
    });

    modal.addEventListener('hidden.bs.modal', () => {
        if (!(submit instanceof HTMLButtonElement)) return;
        submit.disabled = false;
        if (submit.dataset.originalHtml) submit.innerHTML = submit.dataset.originalHtml;
    });
})();
