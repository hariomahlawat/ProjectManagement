(() => {
    'use strict';

    const root = document.querySelector('[data-photos-library]');
    const viewer = document.querySelector('[data-photos-viewer]');
    if (!root || !viewer) return;

    const tiles = Array.from(root.querySelectorAll('[data-media-item]'));
    if (tiles.length === 0) return;

    const mediaHost = viewer.querySelector('[data-viewer-media]');
    const title = viewer.querySelector('[data-viewer-title]');
    const context = viewer.querySelector('[data-viewer-context]');
    const originalLink = viewer.querySelector('[data-viewer-original]');
    const downloadLink = viewer.querySelector('[data-viewer-download]');
    const infoButton = viewer.querySelector('[data-viewer-info]');
    const infoPanel = viewer.querySelector('[data-viewer-info-panel]');
    const previousButton = viewer.querySelector('[data-viewer-prev]');
    const nextButton = viewer.querySelector('[data-viewer-next]');
    let currentIndex = 0;
    let previousFocus = null;

    const value = (tile, name) => tile.dataset[name] || '';

    function setOptionalLink(link, href) {
        if (!link) return;
        link.href = href || '#';
        link.hidden = !href;
    }

    function render(index) {
        currentIndex = (index + tiles.length) % tiles.length;
        const tile = tiles[currentIndex];
        const kind = value(tile, 'kind');
        const displayUrl = value(tile, 'displayUrl');

        mediaHost.replaceChildren();
        if (kind === 'video') {
            const video = document.createElement('video');
            video.src = displayUrl;
            video.controls = true;
            video.autoplay = true;
            video.playsInline = true;
            video.preload = 'metadata';
            mediaHost.append(video);
        } else {
            const image = document.createElement('img');
            image.src = displayUrl;
            image.alt = value(tile, 'title');
            image.decoding = 'async';
            mediaHost.append(image);
        }

        title.textContent = value(tile, 'title');
        context.textContent = `${value(tile, 'context')} · ${value(tile, 'date')}`;
        setOptionalLink(originalLink, value(tile, 'originalUrl'));
        setOptionalLink(downloadLink, value(tile, 'downloadUrl') || value(tile, 'originalUrl'));

        const sourceLink = viewer.querySelector('[data-info-source-link]');
        setOptionalLink(sourceLink, value(tile, 'sourceUrl'));
        viewer.querySelector('[data-info-context]').textContent = value(tile, 'context');
        viewer.querySelector('[data-info-date]').textContent = value(tile, 'date');
        viewer.querySelector('[data-info-source]').textContent = `${value(tile, 'sourceLabel')} · ${value(tile, 'subtitle')}`;
        viewer.querySelector('[data-info-caption]').textContent = value(tile, 'caption');

        const width = value(tile, 'width');
        const height = value(tile, 'height');
        const dimensionsRow = viewer.querySelector('[data-info-dimensions-row]');
        dimensionsRow.hidden = !width || !height;
        viewer.querySelector('[data-info-dimensions]').textContent = width && height ? `${width} × ${height} px` : '';

        const duration = value(tile, 'duration');
        const durationRow = viewer.querySelector('[data-info-duration-row]');
        durationRow.hidden = !duration;
        viewer.querySelector('[data-info-duration]').textContent = duration;

        previousButton.hidden = tiles.length < 2;
        nextButton.hidden = tiles.length < 2;
    }

    function open(index, trigger) {
        previousFocus = trigger || document.activeElement;
        render(index);
        viewer.hidden = false;
        viewer.setAttribute('aria-hidden', 'false');
        document.body.classList.add('photos-viewer-open');
        viewer.querySelector('[data-viewer-close]').focus({ preventScroll: true });
    }

    function close() {
        const video = mediaHost.querySelector('video');
        if (video) video.pause();
        viewer.hidden = true;
        viewer.setAttribute('aria-hidden', 'true');
        viewer.classList.remove('is-info-open');
        infoButton.setAttribute('aria-pressed', 'false');
        infoPanel.setAttribute('aria-hidden', 'true');
        mediaHost.replaceChildren();
        document.body.classList.remove('photos-viewer-open');
        if (previousFocus instanceof HTMLElement) previousFocus.focus({ preventScroll: true });
    }

    tiles.forEach((tile, index) => tile.addEventListener('click', () => open(index, tile)));
    viewer.querySelectorAll('[data-viewer-close]').forEach(button => button.addEventListener('click', close));
    previousButton.addEventListener('click', () => render(currentIndex - 1));
    nextButton.addEventListener('click', () => render(currentIndex + 1));
    infoButton.addEventListener('click', () => {
        const openNow = viewer.classList.toggle('is-info-open');
        infoButton.setAttribute('aria-pressed', String(openNow));
        infoPanel.setAttribute('aria-hidden', String(!openNow));
    });

    document.addEventListener('keydown', event => {
        if (viewer.hidden) return;
        if (event.key === 'Escape') close();
        if (event.key === 'ArrowLeft') render(currentIndex - 1);
        if (event.key === 'ArrowRight') render(currentIndex + 1);
        if (event.key.toLowerCase() === 'i') infoButton.click();
    });
})();
