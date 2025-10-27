(function () {
    const bootstrapModal = window.bootstrap?.Modal;
    if (typeof bootstrapModal !== 'function') {
        return;
    }

    const galleries = document.querySelectorAll('[data-video-gallery]');
    if (!galleries.length) {
        return;
    }

    function setupGallery(gallery) {
        const modalId = gallery.getAttribute('data-video-modal-id');
        const modalElement = modalId ? document.getElementById(modalId) : null;
        if (!modalElement) {
            return;
        }

        const modal = new bootstrapModal(modalElement, { backdrop: 'static' });
        const player = modalElement.querySelector('[data-video-player]');
        const titleElement = modalElement.querySelector('[data-video-modal-title]');
        const downloadLink = modalElement.querySelector('[data-video-download]');

        function resetPlayer() {
            if (!player) {
                return;
            }
            player.pause();
            player.removeAttribute('src');
            player.load();
            if (player.firstElementChild && player.firstElementChild.tagName === 'SOURCE') {
                player.firstElementChild.remove();
            }
        }

        function openVideo({ src, title, poster }) {
            if (!player) {
                return;
            }

            resetPlayer();

            if (titleElement) {
                titleElement.textContent = title || 'Video playback';
            }

            if (poster) {
                player.setAttribute('poster', poster);
            } else {
                player.removeAttribute('poster');
            }

            const source = document.createElement('source');
            source.src = src;
            player.appendChild(source);
            player.load();
            player.play().catch(() => {
                /* autoplay blocked */
            });

            if (downloadLink) {
                downloadLink.href = src;
            }

            modal.show();
        }

        function handleGalleryClick(event) {
            const trigger = event.target.closest('[data-video-trigger]');
            if (!trigger) {
                return;
            }
            event.preventDefault();
            const src = trigger.getAttribute('data-video-src');
            if (!src) {
                return;
            }
            openVideo({
                src,
                title: trigger.getAttribute('data-video-title') || '',
                poster: trigger.getAttribute('data-video-poster') || ''
            });
        }

        gallery.addEventListener('click', handleGalleryClick);
        modalElement.addEventListener('hidden.bs.modal', resetPlayer);
    }

    galleries.forEach(setupGallery);
})();
