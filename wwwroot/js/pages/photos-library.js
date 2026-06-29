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
    let returnHash = '';
    let inertedElements = [];
    let zoom = 1;

    const mediaHashPrefix = '#media=';
    const mediaKey = tile => tile.dataset.mediaKey || tile.dataset.assetId || String(tiles.indexOf(tile) + 1);
    const hashFor = tile => `${mediaHashPrefix}${encodeURIComponent(mediaKey(tile))}`;
    const isMediaHash = hash => hash.startsWith(mediaHashPrefix);
    const focusableSelector = [
        'a[href]:not([hidden])',
        'button:not([disabled]):not([hidden])',
        'input:not([disabled]):not([hidden])',
        'select:not([disabled]):not([hidden])',
        'textarea:not([disabled]):not([hidden])',
        '[tabindex]:not([tabindex="-1"]):not([hidden])'
    ].join(',');

    const value = (tile, name) => tile.dataset[name] || '';

    function setBackgroundInert(enabled) {
        if (!enabled) {
            inertedElements.forEach(({ element, inert, ariaHidden }) => {
                element.inert = inert;
                if (ariaHidden === null) element.removeAttribute('aria-hidden');
                else element.setAttribute('aria-hidden', ariaHidden);
            });
            inertedElements = [];
            return;
        }

        if (inertedElements.length > 0) return;
        let current = viewer;
        while (current?.parentElement) {
            const parent = current.parentElement;
            [...parent.children].forEach(sibling => {
                if (sibling === current || sibling.contains(viewer)) return;
                inertedElements.push({
                    element: sibling,
                    inert: sibling.inert,
                    ariaHidden: sibling.getAttribute('aria-hidden')
                });
                sibling.inert = true;
                sibling.setAttribute('aria-hidden', 'true');
            });
            current = parent;
            if (current === document.body) break;
        }
    }

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
        viewer.querySelector('[data-info-classification]').textContent = value(tile, 'classification') || 'Not classified';
        viewer.querySelector('[data-info-caption]').textContent = value(tile, 'caption');

        const peopleHost = viewer.querySelector('[data-info-people]');
        const peopleRow = viewer.querySelector('[data-info-people-row]');
        let people = [];
        try {
            const parsed = JSON.parse(value(tile, 'people') || '[]');
            people = Array.isArray(parsed)
                ? parsed.filter(person => person && person.name && person.url)
                : [];
        } catch {
            people = [];
        }
        peopleHost.replaceChildren();
        people.forEach(person => {
            const link = document.createElement('a');
            link.href = person.url;
            link.textContent = person.name;
            peopleHost.append(link);
        });
        peopleRow.hidden = people.length === 0;

        const unidentified = Number.parseInt(value(tile, 'unidentified'), 10) || 0;
        const unidentifiedRow = viewer.querySelector('[data-info-unidentified-row]');
        unidentifiedRow.hidden = unidentified === 0;
        viewer.querySelector('[data-info-unidentified]').textContent = unidentified === 1
            ? '1 face awaiting review'
            : `${unidentified} faces awaiting review`;

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

    function open(index, trigger, fromHash = false) {
        if (viewer.hidden) {
            previousFocus = trigger || document.activeElement;
            returnHash = isMediaHash(window.location.hash) ? '' : window.location.hash;
            setBackgroundInert(true);
        }
        render(index);
        viewer.hidden = false;
        viewer.setAttribute('aria-hidden', 'false');
        document.body.classList.add('photos-viewer-open');
        if (!fromHash) {
            const nextHash = hashFor(tiles[currentIndex]);
            if (window.location.hash !== nextHash) history.replaceState(null, '', nextHash);
        }
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
        setBackgroundInert(false);

        if (previousFocus instanceof HTMLElement && document.contains(previousFocus)) {
            previousFocus.focus({ preventScroll: true });
        }

        if (isMediaHash(window.location.hash)) {
            const destination = `${window.location.pathname}${window.location.search}${returnHash}`;
            history.replaceState(null, '', destination);
        }
    }

    tiles.forEach((tile, index) => tile.addEventListener('click', () => open(index, tile)));
    viewer.querySelectorAll('[data-viewer-close]').forEach(button => button.addEventListener('click', close));
    previousButton.addEventListener('click', () => {
        render(currentIndex - 1);
        history.replaceState(null, '', hashFor(tiles[currentIndex]));
    });
    nextButton.addEventListener('click', () => {
        render(currentIndex + 1);
        history.replaceState(null, '', hashFor(tiles[currentIndex]));
    });
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

        if (event.key === 'Tab') {
            const focusable = [...viewer.querySelectorAll(focusableSelector)]
                .filter(element => !element.hidden && element.getClientRects().length > 0);
            if (focusable.length === 0) {
                event.preventDefault();
                return;
            }
            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
            return;
        }

        if (event.key === 'Escape') { event.preventDefault(); close(); }
        if (event.key === 'ArrowLeft') { event.preventDefault(); render(currentIndex - 1); history.replaceState(null, '', hashFor(tiles[currentIndex])); }
        if (event.key === 'ArrowRight') { event.preventDefault(); render(currentIndex + 1); history.replaceState(null, '', hashFor(tiles[currentIndex])); }
        if (event.key === '+' || event.key === '=') { event.preventDefault(); setZoom(zoom + 0.25); }
        if (event.key === '-') { event.preventDefault(); setZoom(zoom - 0.25); }
        if (event.key === '0') { event.preventDefault(); setZoom(1); }
        if (event.key.toLowerCase() === 'i') { event.preventDefault(); infoButton.click(); }
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
                height = Math.min(410, Math.max(300, window.innerHeight * 0.44));
                width = height * ratio;
            } else if (ratio > 1.2) {
                width = Math.min(containerWidth, 780);
                height = Math.min(350, width / ratio);
            } else {
                height = Math.min(390, Math.max(285, window.innerHeight * 0.40));
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

    const syncViewerWithHash = () => {
        if (!isMediaHash(window.location.hash)) {
            if (!viewer.hidden) close();
            return;
        }

        let key;
        try {
            key = decodeURIComponent(window.location.hash.slice(mediaHashPrefix.length));
        } catch {
            return;
        }
        const index = tiles.findIndex(tile => mediaKey(tile) === key);
        if (index >= 0) open(index, tiles[index], true);
    };

    window.addEventListener('hashchange', syncViewerWithHash);
    syncViewerWithHash();

})();
