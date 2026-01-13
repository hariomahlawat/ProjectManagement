(function () {
    if (typeof bootstrap === 'undefined') {
        return;
    }

    const tokenInput = document.querySelector('[data-admin-project-token]');
    if (!tokenInput) {
        return;
    }

    // SECTION: Error handling helpers
    function parseErrorResponse(response) {
        if (!response) {
            return Promise.resolve('Unable to complete the request.');
        }

        const contentType = response.headers.get('content-type') || '';
        if (!contentType.includes('application/json')) {
            const statusLabel = response.statusText || 'Error';
            return Promise.resolve(`HTTP ${response.status} ${statusLabel}`);
        }

        return response.json()
            .then((data) => {
                if (data && typeof data.error === 'string' && data.error.trim().length > 0) {
                    return data.error;
                }

                return 'Unable to complete the request.';
            })
            .catch(() => {
                const statusLabel = response.statusText || 'Error';
                return `HTTP ${response.status} ${statusLabel}`;
            });
    }

    // SECTION: Modal helpers
    function updateModalProject(modal, trigger) {
        if (!modal) {
            return;
        }

        const nameTarget = modal.querySelector('[data-project-name]');
        const submitButton = modal.querySelector('[data-admin-project-submit]');
        const projectId = trigger?.getAttribute('data-project-id') || '';
        const projectName = trigger?.getAttribute('data-project-name') || '';

        if (nameTarget) {
            nameTarget.textContent = projectName || 'this project';
        }

        if (submitButton) {
            submitButton.setAttribute('data-project-id', projectId);
            submitButton.setAttribute('data-project-name', projectName);
        }

        const errorContainer = modal.querySelector('[data-admin-project-error]');
        if (errorContainer) {
            errorContainer.textContent = '';
            errorContainer.classList.add('d-none');
        }
    }

    function resetPurgeModal(modal) {
        if (!modal) {
            return;
        }

        const checkbox = modal.querySelector('[data-admin-remove-assets]');
        const defaultValue = modal.getAttribute('data-remove-assets-default');
        if (checkbox instanceof HTMLInputElement) {
            checkbox.checked = defaultValue === 'true';
        }

        const errorContainer = modal.querySelector('[data-admin-project-error]');
        if (errorContainer) {
            errorContainer.textContent = '';
            errorContainer.classList.add('d-none');
        }
    }

    // SECTION: Request helpers
    async function sendModerationRequest(url, payload) {
        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': tokenInput.value
            },
            body: JSON.stringify(payload),
            credentials: 'include'
        });

        if (response.ok) {
            return null;
        }

        return parseErrorResponse(response);
    }

    // SECTION: Event wiring
    function attachModalHandlers() {
        const restoreModal = document.getElementById('projectRestoreModal');
        if (restoreModal) {
            restoreModal.addEventListener('show.bs.modal', (event) => {
                const trigger = event.relatedTarget;
                updateModalProject(restoreModal, trigger);
            });
        }

        const purgeModal = document.getElementById('projectPurgeModal');
        if (purgeModal) {
            purgeModal.addEventListener('show.bs.modal', (event) => {
                const trigger = event.relatedTarget;
                updateModalProject(purgeModal, trigger);
            });

            purgeModal.addEventListener('hidden.bs.modal', () => {
                resetPurgeModal(purgeModal);
            });
        }
    }

    // SECTION: Submission handling
    async function handleSubmit(button) {
        const action = button.getAttribute('data-action');
        const projectId = button.getAttribute('data-project-id');
        const modalEl = button.closest('.modal');
        if (!action || !projectId || !modalEl) {
            return;
        }

        const errorContainer = modalEl.querySelector('[data-admin-project-error]');
        if (errorContainer) {
            errorContainer.textContent = '';
            errorContainer.classList.add('d-none');
        }

        let endpoint = '';
        let payload = {};

        if (action === 'restore') {
            endpoint = `/api/projects/${projectId}/restore-trash`;
        } else if (action === 'purge') {
            endpoint = `/api/projects/${projectId}/purge`;
            const checkbox = modalEl.querySelector('[data-admin-remove-assets]');
            payload = {
                removeAssets: checkbox instanceof HTMLInputElement ? checkbox.checked : false
            };
        } else {
            return;
        }

        button.disabled = true;
        button.classList.add('disabled');

        try {
            const error = await sendModerationRequest(endpoint, payload);
            if (!error) {
                const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
                modal.hide();
                window.location.reload();
                return;
            }

            if (errorContainer) {
                errorContainer.textContent = error;
                errorContainer.classList.remove('d-none');
            }
        } catch (err) {
            if (errorContainer) {
                errorContainer.textContent = 'Failed to send the request.';
                errorContainer.classList.remove('d-none');
            }
        } finally {
            button.disabled = false;
            button.classList.remove('disabled');
        }
    }

    // SECTION: Bootstrap initialization
    attachModalHandlers();

    document.querySelectorAll('[data-admin-project-submit]').forEach((button) => {
        button.addEventListener('click', (event) => {
            event.preventDefault();
            if (button.disabled) {
                return;
            }
            handleSubmit(button);
        });
    });
})();
