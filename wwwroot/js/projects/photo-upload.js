(function () {
    const form = document.querySelector('[data-photo-upload-form]');
    if (!form) {
        return;
    }

    const dropZone = form.querySelector('[data-photo-upload-drop]');
    const input = form.querySelector('[data-photo-upload-input]');
    const status = form.querySelector('[data-photo-upload-status]');
    const list = form.querySelector('[data-photo-upload-list]');
    const captionField = form.querySelector('[data-photo-upload-caption]');
    const multiNote = form.querySelector('[data-photo-upload-multi-note]');
    const prompt = form.querySelector('[data-photo-upload-prompt]');
    const maxSize = parseInt(input?.dataset.maxSize || '0', 10) || Number.POSITIVE_INFINITY;
    const maxCount = parseInt(input?.dataset.maxCount || '0', 10) || 0;

    const rowVersionField = form.querySelector('input[name="Input.RowVersion"]');
    const projectIdField = form.querySelector('input[name="Input.ProjectId"]');
    const setCoverField = form.querySelector('input[name="Input.SetAsCover"]');
    const linkTotField = form.querySelector('input[name="Input.LinkToTot"]');

    function formatSize(bytes) {
        if (!Number.isFinite(bytes) || bytes <= 0) {
            return '0 KB';
        }
        const units = ['B', 'KB', 'MB', 'GB'];
        let size = bytes;
        let unitIndex = 0;
        while (size >= 1024 && unitIndex < units.length - 1) {
            size /= 1024;
            unitIndex += 1;
        }
        return `${size.toFixed(unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`;
    }

    function updateStatus(message, isError = false) {
        if (status) {
            status.textContent = message || '';
            status.classList.toggle('text-danger', !!isError);
        }
    }

    function setDropZoneActive(active) {
        if (!dropZone) {
            return;
        }
        dropZone.classList.toggle('border-primary', active);
        dropZone.classList.toggle('bg-light', active);
    }

    function setMultiState(isMulti) {
        form.classList.toggle('has-multiple-photo-files', isMulti);
        if (captionField) {
            captionField.toggleAttribute('disabled', isMulti);
        }
        if (multiNote) {
            multiNote.hidden = !isMulti;
        }
        const editor = form.querySelector('[data-photo-editor]');
        if (editor) {
            editor.classList.toggle('project-photo-editor--disabled', isMulti);
        }
        if (prompt) {
            prompt.textContent = isMulti ? 'Drag & drop photos or browse (multiple selected)' : 'Drag & drop photos or browse';
        }
    }

    function validateFiles(files) {
        if (!files || files.length === 0) {
            updateStatus('', false);
            return true;
        }

        if (maxCount && files.length > maxCount) {
            updateStatus(`Select up to ${maxCount} photos at a time.`, true);
            return false;
        }

        for (const file of files) {
            if (file.size && file.size > maxSize) {
                updateStatus(`${file.name} exceeds the ${formatSize(maxSize)} limit.`, true);
                return false;
            }
            if (file.type && !file.type.toLowerCase().startsWith('image/')) {
                updateStatus(`${file.name} is not recognised as an image.`, true);
                return false;
            }
        }

        updateStatus(`${files.length} photo${files.length > 1 ? 's' : ''} selected.`, false);
        return true;
    }

    function refreshSelection() {
        const files = input?.files ? Array.from(input.files) : [];
        const isMulti = files.length > 1;
        setMultiState(isMulti);
        validateFiles(files);
        if (list) {
            list.hidden = true;
            list.innerHTML = '';
        }
    }

    function handleDrop(event) {
        event.preventDefault();
        setDropZoneActive(false);
        if (!input || !event.dataTransfer) {
            return;
        }
        const files = Array.from(event.dataTransfer.files || []);
        if (files.length === 0) {
            return;
        }
        if (!validateFiles(files)) {
            input.value = '';
            return;
        }
        if (typeof DataTransfer !== 'undefined') {
            const transfer = new DataTransfer();
            files.forEach((file) => transfer.items.add(file));
            input.files = transfer.files;
        }
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function handleBrowse() {
        input?.click();
    }

    function handleKeydown(event) {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            handleBrowse();
        }
    }

    function createProgressItem(file) {
        const container = document.createElement('div');
        container.className = 'mb-3';
        container.setAttribute('data-photo-upload-item', '');

        const header = document.createElement('div');
        header.className = 'd-flex justify-content-between align-items-center';
        const name = document.createElement('span');
        name.className = 'fw-semibold';
        name.textContent = file.name;
        const state = document.createElement('span');
        state.className = 'text-muted small';
        state.textContent = 'Waiting';
        state.setAttribute('data-photo-upload-state', '');
        header.appendChild(name);
        header.appendChild(state);

        const progressWrapper = document.createElement('div');
        progressWrapper.className = 'progress mt-2';
        progressWrapper.setAttribute('role', 'progressbar');
        progressWrapper.setAttribute('aria-valuemin', '0');
        progressWrapper.setAttribute('aria-valuemax', '100');

        const progressBar = document.createElement('div');
        progressBar.className = 'progress-bar progress-bar-striped progress-bar-animated';
        progressBar.style.width = '0%';
        progressBar.setAttribute('data-photo-upload-progress', '');
        progressWrapper.appendChild(progressBar);

        container.appendChild(header);
        container.appendChild(progressWrapper);
        return container;
    }

    function uploadFile(file, options) {
        return new Promise((resolve, reject) => {
            const xhr = new XMLHttpRequest();
            const url = `${form.action}?handler=Batch`;
            xhr.open('POST', url);
            xhr.setRequestHeader('X-Requested-With', 'XMLHttpRequest');

            xhr.upload.addEventListener('progress', (event) => {
                if (event.lengthComputable && typeof options.onProgress === 'function') {
                    const percent = event.total > 0 ? Math.floor((event.loaded / event.total) * 100) : 0;
                    options.onProgress(percent);
                }
            });

            xhr.addEventListener('load', () => {
                try {
                    const json = JSON.parse(xhr.responseText || '{}');
                    if (xhr.status >= 200 && xhr.status < 300) {
                        resolve(json);
                    } else {
                        reject(json);
                    }
                } catch (err) {
                    reject(err);
                }
            });

            xhr.addEventListener('error', () => reject(new Error('Upload failed.')));
            xhr.addEventListener('abort', () => reject(new Error('Upload cancelled.')));

            const payload = new FormData();
            const antiForgery = form.querySelector('input[name="__RequestVerificationToken"]');
            if (antiForgery) {
                payload.append(antiForgery.name, antiForgery.value);
            }
            if (projectIdField) {
                payload.append('Input.ProjectId', projectIdField.value);
            }
            if (rowVersionField) {
                payload.append('Input.RowVersion', rowVersionField.value);
            }
            if (setCoverField) {
                payload.append('Input.SetAsCover', setCoverField.checked ? 'true' : 'false');
            }
            if (linkTotField) {
                payload.append('Input.LinkToTot', linkTotField.checked ? 'true' : 'false');
            }
            if (captionField && !captionField.disabled) {
                payload.append('Input.Caption', captionField.value || '');
            }
            payload.append('files', file, file.name);

            xhr.send(payload);
        });
    }

    async function handleBatchSubmit(event) {
        if (!input) {
            return;
        }
        const files = input.files ? Array.from(input.files).filter((file) => file && file.size > 0) : [];
        if (files.length <= 1) {
            return;
        }

        event.preventDefault();

        if (!validateFiles(files)) {
            return;
        }

        const submitButton = form.querySelector('button[type="submit"]');
        const cancelButton = form.querySelector('a.btn-link');
        submitButton?.setAttribute('disabled', 'disabled');
        cancelButton?.setAttribute('aria-disabled', 'true');

        if (list) {
            list.innerHTML = '';
            list.hidden = false;
        }

        updateStatus('Uploading photos…');
        let allSuccessful = true;
        for (const file of files) {
            const item = createProgressItem(file);
            const state = item.querySelector('[data-photo-upload-state]');
            const progress = item.querySelector('[data-photo-upload-progress]');
            list?.appendChild(item);
            if (state) {
                state.textContent = 'Uploading…';
            }

            try {
                const response = await uploadFile(file, {
                    onProgress(percent) {
                        if (progress) {
                            progress.style.width = `${percent}%`;
                        }
                    }
                });
                if (progress) {
                    progress.style.width = '100%';
                    progress.classList.remove('progress-bar-animated');
                }
                if (state) {
                    state.textContent = response.success === false ? 'Failed' : 'Uploaded';
                    state.classList.toggle('text-danger', response.success === false);
                }
                if (response?.rowVersion && rowVersionField) {
                    rowVersionField.value = response.rowVersion;
                }
                if (response?.success === false) {
                    allSuccessful = false;
                }
            } catch (err) {
                allSuccessful = false;
                if (progress) {
                    progress.classList.remove('progress-bar-animated');
                    progress.classList.add('bg-danger');
                    progress.style.width = '100%';
                }
                if (state) {
                    state.textContent = err?.message || 'Failed';
                    state.classList.add('text-danger');
                }
            }
        }

        submitButton?.removeAttribute('disabled');
        cancelButton?.removeAttribute('aria-disabled');
        input.value = '';
        refreshSelection();

        if (allSuccessful) {
            updateStatus('All photos uploaded. Refresh the gallery to see them.');
        } else {
            updateStatus('Some photos could not be uploaded. Check the list above for details.', true);
        }
    }

    if (dropZone) {
        dropZone.addEventListener('click', handleBrowse);
        dropZone.addEventListener('keydown', handleKeydown);
        dropZone.addEventListener('dragover', (event) => {
            event.preventDefault();
            setDropZoneActive(true);
        });
        dropZone.addEventListener('dragleave', (event) => {
            if (event.target === dropZone) {
                setDropZoneActive(false);
            }
        });
        dropZone.addEventListener('drop', handleDrop);
    }

    input?.addEventListener('change', refreshSelection);
    form.addEventListener('submit', handleBatchSubmit);

    refreshSelection();
})();
