(() => {
    'use strict';

    const root = document.querySelector('[data-photos-library]');
    const viewer = document.querySelector('[data-photos-viewer]');
    if (!root || !viewer) return;

    const tiles = Array.from(root.querySelectorAll('[data-media-item]'));
    const filterForm = document.querySelector('[data-photos-filter-form]');
    const filterSubmit = document.querySelector('[data-photos-filter-submit]');

    if (filterForm && filterSubmit) {
        const serialize = () => new URLSearchParams(new FormData(filterForm)).toString();
        const initial = serialize();

        const syncFilterSubmit = () => {
            filterSubmit.disabled = serialize() === initial;
        };

        filterSubmit.disabled = true;
        filterForm.addEventListener('change', syncFilterSubmit);
        filterForm.addEventListener('input', syncFilterSubmit);
        filterForm.addEventListener('reset', () => window.setTimeout(syncFilterSubmit, 0));

        const filterCanvas = document.getElementById('photosFilters');
        filterCanvas?.addEventListener('shown.bs.offcanvas', syncFilterSubmit);
    }

    if (tiles.length === 0) return;

    const mediaHost = viewer.querySelector('[data-viewer-media]');
    const title = viewer.querySelector('[data-viewer-title]');
    const context = viewer.querySelector('[data-viewer-context]');
    const position = viewer.querySelector('[data-viewer-position]');
    const originalLink = viewer.querySelector('[data-viewer-original]');
    const downloadLink = viewer.querySelector('[data-viewer-download]');
    const infoButton = viewer.querySelector('[data-viewer-info]');
    const infoPanel = viewer.querySelector('[data-viewer-info-panel]');
    const previousButton = viewer.querySelector('[data-viewer-prev]');
    const nextButton = viewer.querySelector('[data-viewer-next]');
    const zoomInButton = viewer.querySelector('[data-viewer-zoom-in]');
    const zoomOutButton = viewer.querySelector('[data-viewer-zoom-out]');
    const zoomResetButton = viewer.querySelector('[data-viewer-zoom-reset]');
    const zoomLabel = viewer.querySelector('[data-viewer-zoom-label]');

    let currentIndex = 0;
    let previousFocus = null;
    let zoom = 1;

    const value = (tile, name) => tile.dataset[name] || '';

    function setOptionalLink(link, href) {
        if (!link) return;
        link.href = href || '#';
        link.hidden = !href;
    }

    function setZoom(nextZoom) {
        zoom = Math.min(3, Math.max(0.5, nextZoom));
        mediaHost.style.setProperty('--viewer-zoom', String(zoom));
        if (zoomLabel) zoomLabel.textContent = `${Math.round(zoom * 100)}%`;
        if (zoomOutButton) zoomOutButton.disabled = zoom <= 0.5;
        if (zoomInButton) zoomInButton.disabled = zoom >= 3;
    }

    function render(index) {
        currentIndex = (index + tiles.length) % tiles.length;
        const tile = tiles[currentIndex];
        const kind = value(tile, 'kind');
        const displayUrl = value(tile, 'displayUrl');

        setZoom(1);
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
            image.addEventListener('dblclick', () => setZoom(zoom === 1 ? 2 : 1));
            mediaHost.append(image);
        }

        title.textContent = value(tile, 'title');
        context.textContent = `${value(tile, 'context')} · ${value(tile, 'date')}`;
        position.textContent = `${currentIndex + 1} of ${tiles.length}`;

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
        setZoom(1);
        document.body.classList.remove('photos-viewer-open');

        if (previousFocus instanceof HTMLElement) {
            previousFocus.focus({ preventScroll: true });
        }

        if (window.location.hash.startsWith('#media-')) {
            history.replaceState(null, '', `${window.location.pathname}${window.location.search}`);
        }
    }

    tiles.forEach((tile, index) => tile.addEventListener('click', () => open(index, tile)));
    viewer.querySelectorAll('[data-viewer-close]').forEach(button => button.addEventListener('click', close));
    previousButton.addEventListener('click', () => render(currentIndex - 1));
    nextButton.addEventListener('click', () => render(currentIndex + 1));
    zoomInButton?.addEventListener('click', () => setZoom(zoom + 0.25));
    zoomOutButton?.addEventListener('click', () => setZoom(zoom - 0.25));
    zoomResetButton?.addEventListener('click', () => setZoom(1));

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
        if (event.key === '+' || event.key === '=') setZoom(zoom + 0.25);
        if (event.key === '-') setZoom(zoom - 0.25);
        if (event.key === '0') setZoom(1);
        if (event.key.toLowerCase() === 'i') infoButton.click();
    });


    const markUnavailable = (image) => {
        const tile = image.closest('[data-media-item]');
        if (!tile) return;
        image.hidden = true;
        tile.classList.add('photos-tile--unavailable');
        tile.setAttribute('aria-label', 'Media unavailable');
        tile.disabled = true;
    };

    document.querySelectorAll('[data-media-image]').forEach((image) => {
        image.addEventListener('error', () => markUnavailable(image), { once: true });
        if (image.complete && image.naturalWidth === 0) {
            markUnavailable(image);
        }
    });


    const parseRatio = (tile) => {
        const raw = Number.parseFloat(tile.dataset.aspectRatio || '');
        if (Number.isFinite(raw) && raw > 0.1 && raw < 10) return raw;
        return 1;
    };

    const gridTargetHeight = () => {
        if (window.matchMedia('(max-width: 575.98px)').matches) return 145;
        if (window.matchMedia('(max-width: 991.98px)').matches) return 180;
        return 215;
    };

    function layoutGrid(grid) {
        const items = Array.from(grid.querySelectorAll('[data-media-item]:not(.photos-tile--unavailable)'));
        if (items.length === 0 || grid.clientWidth < 120) return;

        const gap = 5;
        const containerWidth = grid.clientWidth;
        const target = gridTargetHeight();
        const minHeight = Math.max(120, target * 0.72);
        const maxHeight = target * 1.26;

        if (items.length === 1) {
            const tile = items[0];
            const ratio = parseRatio(tile);
            let height;
            let width;

            if (ratio < 0.86) {
                height = Math.min(500, Math.max(350, window.innerHeight * 0.52));
                width = height * ratio;
            } else if (ratio > 1.2) {
                width = Math.min(containerWidth, 920);
                height = Math.min(430, width / ratio);
            } else {
                height = Math.min(460, Math.max(320, window.innerHeight * 0.46));
                width = height * ratio;
            }

            if (width > containerWidth) {
                width = containerWidth;
                height = width / ratio;
            }

            tile.style.width = `${Math.round(width)}px`;
            tile.style.height = `${Math.round(height)}px`;
            grid.classList.add('is-layout-ready');
            return;
        }

        let row = [];
        let ratioSum = 0;
        const rows = [];

        items.forEach((tile, index) => {
            const ratio = parseRatio(tile);
            row.push({ tile, ratio });
            ratioSum += ratio;

            const rowWidthAtTarget = ratioSum * target + gap * (row.length - 1);
            const isLast = index === items.length - 1;

            if (rowWidthAtTarget >= containerWidth || isLast) {
                rows.push({ entries: row, ratioSum, isLast });
                row = [];
                ratioSum = 0;
            }
        });

        rows.forEach(({ entries, ratioSum: sum, isLast }) => {
            const available = containerWidth - gap * (entries.length - 1);
            let height = available / sum;

            if (isLast && height > target) height = target;
            height = Math.max(minHeight, Math.min(maxHeight, height));

            entries.forEach(({ tile, ratio }) => {
                tile.style.width = `${Math.max(96, Math.round(height * ratio))}px`;
                tile.style.height = `${Math.round(height)}px`;
            });
        });

        grid.classList.add('is-layout-ready');
    }

    const grids = Array.from(root.querySelectorAll('.photos-grid'));
    const layoutAll = () => grids.forEach(layoutGrid);
    let layoutFrame = 0;
    const queueLayout = () => {
        cancelAnimationFrame(layoutFrame);
        layoutFrame = requestAnimationFrame(layoutAll);
    };

    if ('ResizeObserver' in window) {
        const observer = new ResizeObserver(queueLayout);
        grids.forEach(grid => observer.observe(grid));
    } else {
        window.addEventListener('resize', queueLayout, { passive: true });
    }

    document.querySelectorAll('[data-media-image]').forEach((image) => {
        const markLoaded = () => {
            image.closest('[data-media-item]')?.classList.add('is-loaded');
            queueLayout();
        };
        image.addEventListener('load', markLoaded, { once: true });
        if (image.complete && image.naturalWidth > 0) markLoaded();
    });

    layoutAll();

    const openFromHash = () => {
        const match = /^#media-(\d+)$/.exec(window.location.hash);
        if (!match) return;
        const index = Number.parseInt(match[1], 10) - 1;
        if (index >= 0 && index < tiles.length) open(index, tiles[index]);
    };

    tiles.forEach((tile, index) => {
        tile.addEventListener('click', () => {
            const nextHash = `#media-${index + 1}`;
            if (window.location.hash !== nextHash) history.replaceState(null, '', nextHash);
        });
    });

    window.addEventListener('hashchange', openFromHash);
    openFromHash();

})();
