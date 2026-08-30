(() => {
    'use strict';

    function isVideoFile(file) {
        const type = (file.type || '').toLowerCase();
        if (type.startsWith('video/')) return true;
        const name = (file.name || '').toLowerCase();
        return name.endsWith('.mp4') || name.endsWith('.mov') || name.endsWith('.webm');
    }

    function formatMegabytes(bytes) {
        return Math.round(bytes / (1024 * 1024));
    }

    function initUploadValidation() {
        document.querySelectorAll('[data-activity-upload-input]').forEach(input => {
            const form = input.closest('form');
            const errorHost = form?.querySelector('[data-activity-upload-error]');
            const maxFiles = Number.parseInt(input.dataset.maxFiles || '0', 10);
            const standardMax = Number.parseInt(input.dataset.standardMax || '0', 10);
            const videoMax = Number.parseInt(input.dataset.videoMax || '0', 10);
            const batchMax = Number.parseInt(input.dataset.batchMax || '0', 10);

            const validate = () => {
                const files = Array.from(input.files || []);
                let message = '';

                if (maxFiles > 0 && files.length > maxFiles) {
                    message = `Select no more than ${maxFiles} additional file${maxFiles === 1 ? '' : 's'}.`;
                } else {
                    const oversized = files.find(file => file.size > (isVideoFile(file) ? videoMax : standardMax));
                    if (oversized) {
                        const limit = isVideoFile(oversized) ? videoMax : standardMax;
                        message = `${oversized.name} exceeds the ${formatMegabytes(limit)} MB limit for this file type.`;
                    } else if (batchMax > 0 && files.reduce((total, file) => total + file.size, 0) > batchMax) {
                        message = `The selected files exceed the ${formatMegabytes(batchMax)} MB upload batch limit. Upload large videos separately.`;
                    }
                }

                if (errorHost) {
                    errorHost.textContent = message;
                    errorHost.hidden = !message;
                }

                input.setCustomValidity(message);
                return !message;
            };

            input.addEventListener('change', validate);
            form?.addEventListener('submit', event => {
                if (!validate()) {
                    event.preventDefault();
                    input.reportValidity();
                }
            });
        });
    }

    function initPhotoViewer() {
        const gallery = document.querySelector('[data-activity-photo-gallery]');
        const viewer = document.querySelector('[data-activity-photo-viewer]');
        if (!gallery || !viewer) return;

        const bootstrapModal = window.bootstrap?.Modal;
        if (typeof bootstrapModal !== 'function') {
            // Progressive enhancement: photo anchors continue to open their preview URL.
            return;
        }

        const items = Array.from(gallery.querySelectorAll('[data-activity-photo-item]'));
        if (items.length === 0) return;

        const image = viewer.querySelector('[data-activity-viewer-image]');
        const counter = viewer.querySelector('[data-activity-viewer-counter]');
        const fileLabel = viewer.querySelector('[data-activity-viewer-file]');
        const previous = viewer.querySelector('[data-activity-viewer-previous]');
        const next = viewer.querySelector('[data-activity-viewer-next]');
        const loading = viewer.querySelector('[data-activity-viewer-loading]');
        const error = viewer.querySelector('[data-activity-viewer-error]');
        const stage = viewer.querySelector('[data-activity-viewer-stage]');
        const modal = bootstrapModal.getOrCreateInstance(viewer, { backdrop: true, keyboard: true, focus: true });

        let activeIndex = 0;
        let activeTrigger = null;
        let touchStartX = null;
        let renderToken = 0;

        const readItem = index => {
            const element = items[index];
            return {
                element,
                previewUrl: element.dataset.previewUrl || element.getAttribute('href') || '',
                originalUrl: element.dataset.originalUrl || '',
                fileName: element.dataset.fileName || `Photo ${index + 1}`
            };
        };

        const setBusy = busy => {
            if (loading) loading.hidden = !busy;
            if (error) error.hidden = true;
            if (image) image.classList.toggle('is-ready', !busy && Boolean(image.getAttribute('src')));
        };

        const preload = index => {
            if (index < 0 || index >= items.length) return;
            const item = readItem(index);
            if (!item.previewUrl) return;
            const preloadImage = new Image();
            preloadImage.src = item.previewUrl;
        };

        const render = index => {
            if (!image) return;
            activeIndex = Math.max(0, Math.min(index, items.length - 1));
            const item = readItem(activeIndex);
            const token = ++renderToken;

            setBusy(true);
            image.classList.remove('is-ready');
            image.alt = `Activity photo ${activeIndex + 1} of ${items.length}: ${item.fileName}`;

            if (counter) counter.textContent = `${activeIndex + 1} of ${items.length}`;
            if (fileLabel) fileLabel.textContent = item.fileName;
            if (previous) previous.disabled = activeIndex === 0;
            if (next) next.disabled = activeIndex === items.length - 1;

            const handleLoad = () => {
                if (token !== renderToken) return;
                if (loading) loading.hidden = true;
                if (error) error.hidden = true;
                image.classList.add('is-ready');
                preload(activeIndex - 1);
                preload(activeIndex + 1);
            };

            const handleError = () => {
                if (token !== renderToken) return;
                if (loading) loading.hidden = true;
                image.classList.remove('is-ready');
                if (error) error.hidden = false;
            };

            image.onload = handleLoad;
            image.onerror = handleError;
            image.src = item.previewUrl;
        };

        const show = (index, trigger) => {
            activeTrigger = trigger;
            render(index);
            modal.show();
        };

        items.forEach((item, index) => {
            item.addEventListener('click', event => {
                event.preventDefault();
                show(index, item);
            });
        });

        previous?.addEventListener('click', () => render(activeIndex - 1));
        next?.addEventListener('click', () => render(activeIndex + 1));

        viewer.addEventListener('keydown', event => {
            if (event.key === 'ArrowLeft' && activeIndex > 0) {
                event.preventDefault();
                render(activeIndex - 1);
            } else if (event.key === 'ArrowRight' && activeIndex < items.length - 1) {
                event.preventDefault();
                render(activeIndex + 1);
            }
        });

        stage?.addEventListener('touchstart', event => {
            touchStartX = event.changedTouches?.[0]?.clientX ?? null;
        }, { passive: true });

        stage?.addEventListener('touchend', event => {
            if (touchStartX === null) return;
            const endX = event.changedTouches?.[0]?.clientX ?? touchStartX;
            const delta = endX - touchStartX;
            touchStartX = null;
            if (Math.abs(delta) < 45) return;
            if (delta > 0 && activeIndex > 0) render(activeIndex - 1);
            if (delta < 0 && activeIndex < items.length - 1) render(activeIndex + 1);
        }, { passive: true });

        viewer.addEventListener('hidden.bs.modal', () => {
            renderToken++;
            if (image) {
                image.onload = null;
                image.onerror = null;
                image.removeAttribute('src');
                image.classList.remove('is-ready');
            }
            if (loading) loading.hidden = false;
            if (error) error.hidden = true;
            activeTrigger?.focus({ preventScroll: true });
            activeTrigger = null;
        });
    }

    document.addEventListener('DOMContentLoaded', () => {
        initPhotoViewer();
        initUploadValidation();
    });
})();
