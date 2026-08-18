(() => {
    'use strict';

    const root = document.querySelector('[data-photos-library]');
    const viewer = document.querySelector('[data-photos-viewer]');
    if (!root || !viewer) return;

    const tiles = Array.from(root.querySelectorAll('[data-media-item]'));

    // Keep an open Photos page current without making catalogue discovery depend on
    // navigation. The background worker discovers PRISM-owned media; this lightweight
    // check only refreshes the UI after the rendered result set changes.
    const autoRefreshUrl = root.dataset.autoRefreshUrl;
    const updateBanner = root.querySelector('[data-library-update-banner]');
    const refreshButton = root.querySelector('[data-library-refresh]');
    let currentLibraryVersion = root.dataset.libraryVersion || '';
    let updateCheckInProgress = false;

    const showUpdateAvailable = () => {
        if (!updateBanner) return;
        updateBanner.hidden = false;
        updateBanner.classList.add('is-visible');
    };

    refreshButton?.addEventListener('click', () => window.location.reload());

    const checkForLibraryUpdates = async () => {
        if (!autoRefreshUrl || updateCheckInProgress || document.hidden || !updateBanner?.hidden) return;
        updateCheckInProgress = true;
        try {
            const response = await fetch(autoRefreshUrl, {
                headers: {
                    'Accept': 'application/json',
                    'X-Requested-With': 'PhotosCataloguePoll'
                },
                cache: 'no-store',
                credentials: 'same-origin'
            });
            if (!response.ok) return;

            const payload = await response.json();
            const nextVersion = typeof payload?.revision === 'string'
                ? payload.revision
                : '';

            if (nextVersion && nextVersion !== currentLibraryVersion) {
                currentLibraryVersion = nextVersion;
                showUpdateAvailable();
            }
        } catch {
            // Enhancement only. A failed update check must not affect normal browsing.
        } finally {
            updateCheckInProgress = false;
        }
    };

    window.setInterval(checkForLibraryUpdates, 15000);
    document.addEventListener('visibilitychange', () => {
        if (!document.hidden) void checkForLibraryUpdates();
    });

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
    if (infoPanel) infoPanel.inert = true;
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
        const caption = value(tile, 'caption');
        viewer.querySelector('[data-info-caption]').textContent = caption;
        const captionRow = viewer.querySelector('[data-info-caption-row]');
        if (captionRow) captionRow.hidden = !caption;

        const albumsHost = viewer.querySelector('[data-info-albums]');
        const albumsRow = viewer.querySelector('[data-info-albums-row]');
        let albums = [];
        try {
            const parsed = JSON.parse(value(tile, 'albums') || '[]');
            albums = Array.isArray(parsed) ? parsed.filter(album => album && album.name && album.url) : [];
        } catch {
            albums = [];
        }
        if (albumsHost) {
            albumsHost.replaceChildren();
            albums.forEach(album => {
                const link = document.createElement('a');
                link.href = album.url;
                link.textContent = album.name;
                albumsHost.append(link);
            });
        }
        if (albumsRow) albumsRow.hidden = albums.length === 0;

        const filename = value(tile, 'filename');
        const filenameRow = viewer.querySelector('[data-info-filename-row]');
        if (filenameRow) filenameRow.hidden = !filename;
        const filenameHost = viewer.querySelector('[data-info-filename]');
        if (filenameHost) filenameHost.textContent = filename;

        const fileSize = value(tile, 'fileSize');
        const fileSizeRow = viewer.querySelector('[data-info-file-size-row]');
        if (fileSizeRow) fileSizeRow.hidden = !fileSize;
        const fileSizeHost = viewer.querySelector('[data-info-file-size]');
        if (fileSizeHost) fileSizeHost.textContent = fileSize;

        const editCaptionButton = viewer.querySelector('[data-info-edit-caption]');
        if (editCaptionButton) {
            const assetId = value(tile, 'assetId');
            editCaptionButton.hidden = !assetId;
            editCaptionButton.dataset.assetId = assetId;
            editCaptionButton.dataset.caption = value(tile, 'editorialCaption');
            editCaptionButton.dataset.token = value(tile, 'editorialToken');
        }

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
        infoPanel.inert = true;
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

    if (root.dataset.organizeAlbum !== 'true') {
        tiles.forEach((tile, index) => tile.addEventListener('click', () => open(index, tile)));
    }
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
        infoPanel.inert = !openNow;
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
        const sparseLastRow = root.dataset.sparseLastRow === 'true';
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
                // A lone panorama should remain easy to inspect without taking over the
                // complete desktop viewport. Multi-item rows continue to use the full width.
                width = Math.min(containerWidth, 680, window.innerWidth * 0.55);
                height = Math.min(300, width / ratio);
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

            const lastRowCeiling = sparseLastRow ? target * 1.18 : target;
            if (isLast && height > lastRowCeiling) height = lastRowCeiling;
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

    if (root.dataset.organizeAlbum !== 'true') {
        window.addEventListener('hashchange', syncViewerWithHash);
        syncViewerWithHash();
    } else if (isMediaHash(window.location.hash)) {
        history.replaceState(null, '', `${window.location.pathname}${window.location.search}`);
    }

})();

// Media-first selection mode. Normal clicks continue to open media unless the user
// explicitly enters Select mode. Selection state is local to the current rendered page.
(() => {
    'use strict';

    const library = document.querySelector('[data-photos-library]');
    const toggle = document.querySelector('[data-photos-select-toggle]');
    if (!library || !toggle) return;

    const tiles = Array.from(library.querySelectorAll('[data-media-item]'));
    const actionBar = library.querySelector('[data-photos-selection-bar]');
    const countLabel = actionBar?.querySelector('[data-selection-count]');
    const noteLabel = actionBar?.querySelector('[data-selection-note]');
    const clearButton = actionBar?.querySelector('[data-selection-clear]');
    const selectAllButton = actionBar?.querySelector('[data-selection-select-all]');
    const downloadForm = actionBar?.querySelector('[data-selection-download-form]');
    const downloadButton = actionBar?.querySelector('[data-selection-download]');
    const reviewPeopleLink = actionBar?.querySelector('[data-selection-review-people]');
    const addAlbumButton = actionBar?.querySelector('[data-selection-add-album]');
    const albumForm = document.querySelector('[data-selection-album-form]');
    const removeAlbumForm = actionBar?.querySelector('[data-selection-remove-album-form]');
    const removeAlbumButton = actionBar?.querySelector('[data-selection-remove-album]');
    const coverForm = actionBar?.querySelector('[data-selection-cover-form]');
    const coverButton = actionBar?.querySelector('[data-selection-set-cover]');
    const label = toggle.querySelector('span');

    let selecting = false;
    let lastSelectedIndex = -1;
    let suppressNextClick = false;
    const selected = new Set();

    const keyFor = (tile, index) => tile.dataset.mediaKey || String(index);
    const assetIdFor = tile => {
        const value = Number.parseInt(tile.dataset.assetId || '', 10);
        return Number.isSafeInteger(value) && value > 0 ? value : null;
    };

    const selectedTiles = () => tiles.filter((tile, index) => selected.has(keyFor(tile, index)));

    const setTileSelected = (tile, index, value) => {
        const key = keyFor(tile, index);
        if (value) selected.add(key);
        else selected.delete(key);
        tile.classList.toggle('is-selected', value);
        if (selecting) tile.setAttribute('aria-pressed', String(value));
    };

    const rebuildActionTargets = () => {
        if (!actionBar) return;
        const chosen = selectedTiles();
        const assetIds = chosen.map(assetIdFor).filter(id => id !== null);
        const coverAssetIds = chosen
            .filter(tile => tile.dataset.kind === 'photo')
            .map(assetIdFor)
            .filter(id => id !== null);

        const syncAssetInputs = (form, name = 'assetIds') => {
            if (!form) return;
            form.querySelectorAll(`input[name="${name}"]`).forEach(input => input.remove());
            assetIds.forEach(assetId => {
                const input = document.createElement('input');
                input.type = 'hidden';
                input.name = name;
                input.value = String(assetId);
                form.appendChild(input);
            });
        };

        if (downloadForm) {
            downloadForm.querySelectorAll('input[name="assetIds"]').forEach(input => input.remove());
            assetIds.forEach(assetId => {
                const input = document.createElement('input');
                input.type = 'hidden';
                input.name = 'assetIds';
                input.value = String(assetId);
                downloadForm.appendChild(input);
            });
        }

        if (downloadButton) downloadButton.disabled = assetIds.length === 0;
        syncAssetInputs(albumForm);
        syncAssetInputs(removeAlbumForm);
        if (addAlbumButton) addAlbumButton.disabled = assetIds.length === 0;
        if (removeAlbumButton) removeAlbumButton.disabled = assetIds.length === 0;

        const canSetCover = chosen.length === 1 && assetIds.length === 1 && coverAssetIds.length === 1;
        if (coverForm) {
            coverForm.querySelectorAll('input[name="assetId"]').forEach(input => input.remove());
            if (canSetCover) {
                const input = document.createElement('input');
                input.type = 'hidden';
                input.name = 'assetId';
                input.value = String(coverAssetIds[0]);
                coverForm.appendChild(input);
            }
        }
        if (coverButton) {
            coverButton.disabled = !canSetCover;
            coverButton.title = canSetCover
                ? 'Use selected photo as album cover'
                : 'Select exactly one photo';
        }

        if (reviewPeopleLink) {
            const base = reviewPeopleLink.dataset.baseUrl || reviewPeopleLink.getAttribute('href') || '';
            if (assetIds.length > 0 && base) {
                const url = new URL(base, window.location.origin);
                url.searchParams.delete('AssetIds');
                assetIds.forEach(assetId => url.searchParams.append('AssetIds', String(assetId)));
                reviewPeopleLink.href = `${url.pathname}${url.search}${url.hash}`;
                reviewPeopleLink.classList.remove('disabled');
                reviewPeopleLink.setAttribute('aria-disabled', 'false');
            } else {
                reviewPeopleLink.classList.add('disabled');
                reviewPeopleLink.setAttribute('aria-disabled', 'true');
            }
        }

        if (countLabel) countLabel.textContent = `${chosen.length} selected`;
        if (noteLabel) {
            const unsupported = chosen.length - assetIds.length;
            noteLabel.textContent = unsupported > 0
                ? `${unsupported} item${unsupported === 1 ? '' : 's'} still awaiting catalogue actions`
                : '';
        }
    };

    const render = () => {
        library.classList.toggle('is-selecting', selecting);
        toggle.setAttribute('aria-pressed', String(selecting));
        if (label) label.textContent = selecting ? 'Cancel' : 'Select';

        tiles.forEach((tile, index) => {
            if (selecting) tile.setAttribute('aria-pressed', String(selected.has(keyFor(tile, index))));
            else tile.removeAttribute('aria-pressed');
        });

        if (actionBar) actionBar.hidden = !selecting;
        rebuildActionTargets();
    };

    const clearSelection = () => {
        selected.clear();
        tiles.forEach(tile => tile.classList.remove('is-selected'));
        lastSelectedIndex = -1;
        rebuildActionTargets();
    };

    const leaveSelectionMode = () => {
        selecting = false;
        clearSelection();
        render();
    };

    toggle.addEventListener('click', () => {
        selecting = !selecting;
        if (!selecting) clearSelection();
        render();
    });

    clearButton?.addEventListener('click', clearSelection);
    selectAllButton?.addEventListener('click', () => {
        tiles.forEach((tile, index) => {
            if (!tile.disabled && !tile.classList.contains('photos-tile--unavailable')) {
                setTileSelected(tile, index, true);
            }
        });
        lastSelectedIndex = tiles.length - 1;
        rebuildActionTargets();
    });

    reviewPeopleLink?.addEventListener('click', event => {
        if (reviewPeopleLink.getAttribute('aria-disabled') === 'true') event.preventDefault();
    });

    library.addEventListener('click', event => {
        if (!selecting) return;
        const tile = event.target.closest('[data-media-item]');
        if (!tile) return;

        event.preventDefault();
        event.stopImmediatePropagation();
        if (suppressNextClick) {
            suppressNextClick = false;
            return;
        }

        const index = tiles.indexOf(tile);
        if (index < 0) return;

        if (event.shiftKey && lastSelectedIndex >= 0) {
            const from = Math.min(lastSelectedIndex, index);
            const to = Math.max(lastSelectedIndex, index);
            for (let position = from; position <= to; position += 1) {
                if (!tiles[position].disabled) setTileSelected(tiles[position], position, true);
            }
        } else {
            setTileSelected(tile, index, !selected.has(keyFor(tile, index)));
        }

        lastSelectedIndex = index;
        rebuildActionTargets();
    }, true);

    // Desktop lasso selection. It is deliberately available only in explicit Select mode
    // and only for a primary mouse pointer, so touch scrolling and normal gallery browsing
    // keep their native behaviour.
    let dragStart = null;
    let dragGrid = null;
    let lasso = null;

    const removeLasso = () => {
        lasso?.remove();
        lasso = null;
        dragStart = null;
        dragGrid = null;
    };

    const intersect = (a, b) => !(a.right < b.left || a.left > b.right || a.bottom < b.top || a.top > b.bottom);

    library.querySelectorAll('.photos-grid').forEach(grid => {
        grid.addEventListener('pointerdown', event => {
            if (!selecting || event.pointerType !== 'mouse' || event.button !== 0) return;
            dragStart = { x: event.clientX, y: event.clientY };
            dragGrid = grid;
        });
    });

    document.addEventListener('pointermove', event => {
        if (!selecting || !dragStart || !dragGrid) return;
        const dx = event.clientX - dragStart.x;
        const dy = event.clientY - dragStart.y;
        if (!lasso && Math.hypot(dx, dy) < 7) return;

        if (!lasso) {
            lasso = document.createElement('div');
            lasso.className = 'photos-selection-lasso';
            document.body.appendChild(lasso);
        }

        event.preventDefault();
        const left = Math.min(dragStart.x, event.clientX);
        const top = Math.min(dragStart.y, event.clientY);
        const right = Math.max(dragStart.x, event.clientX);
        const bottom = Math.max(dragStart.y, event.clientY);
        Object.assign(lasso.style, {
            left: `${left}px`,
            top: `${top}px`,
            width: `${right - left}px`,
            height: `${bottom - top}px`
        });

        const lassoRect = { left, top, right, bottom };
        tiles.forEach((tile, index) => {
            if (tile.closest('.photos-grid') !== dragGrid || tile.disabled) return;
            if (intersect(lassoRect, tile.getBoundingClientRect())) setTileSelected(tile, index, true);
        });
        rebuildActionTargets();
    }, { passive: false });

    document.addEventListener('pointerup', () => {
        if (lasso) suppressNextClick = true;
        removeLasso();
    });
    document.addEventListener('pointercancel', removeLasso);
    document.addEventListener('keydown', event => {
        if (selecting && event.key === 'Escape') leaveSelectionMode();
    });

    render();
})();

// Organisation-wide album forms: keep the create/add interaction compact and explicit.
(() => {
    'use strict';

    const form = document.querySelector('[data-selection-album-form]');
    if (form) {
        const choices = Array.from(form.querySelectorAll('[data-album-choice]'));
        const existingFields = form.querySelector('[data-album-existing-fields]');
        const newFields = form.querySelector('[data-album-new-fields]');
        const existingSelect = existingFields?.querySelector('select[name="albumId"]');
        const newName = newFields?.querySelector('input[name="newAlbumName"]');

        const sync = () => {
            const mode = choices.find(choice => choice.checked)?.value || (choices.length === 0 ? 'new' : 'existing');
            const isNew = mode === 'new';
            if (existingFields) existingFields.hidden = isNew;
            if (newFields) newFields.hidden = !isNew;
            if (existingSelect) existingSelect.disabled = isNew;
            if (newName) {
                newName.disabled = !isNew;
                newName.required = isNew;
            }
        };

        choices.forEach(choice => choice.addEventListener('change', sync));
        sync();
    }

    const captionButton = document.querySelector('[data-info-edit-caption]');
    const captionModal = document.getElementById('editCaptionModal');
    const captionForm = captionModal?.querySelector('[data-caption-form]');
    captionButton?.addEventListener('click', () => {
        if (!captionModal || !captionForm) return;
        const assetInput = captionForm.querySelector('[data-caption-asset-id]');
        const tokenInput = captionForm.querySelector('[data-caption-token]');
        const captionInput = captionForm.querySelector('[data-caption-value]');
        if (assetInput) assetInput.value = captionButton.dataset.assetId || '';
        if (tokenInput) tokenInput.value = captionButton.dataset.token || '';
        if (captionInput) captionInput.value = captionButton.dataset.caption || '';

        // Close the full-screen viewer first so its background-inert contract is restored
        // before Bootstrap moves focus into the editorial modal.
        document.querySelector('[data-photos-viewer] [data-viewer-close]')?.click();
        window.setTimeout(() => {
            if (window.bootstrap?.Modal) window.bootstrap.Modal.getOrCreateInstance(captionModal).show();
        }, 0);
    });
})();

// Album ordering is intentionally confined to explicit Organise mode. The browser sends
// only the ordered asset IDs; the server revalidates album ownership and membership.
(() => {
    'use strict';

    const grid = document.querySelector('[data-album-sortable="true"]');
    const form = document.querySelector('[data-album-reorder-form]');
    const status = document.querySelector('[data-album-organize-status]');
    if (!grid || !form) return;

    let dragging = null;
    let saveTimer = null;
    let saving = false;
    let queued = false;

    const tiles = () => Array.from(grid.querySelectorAll('[data-media-item][data-asset-id]'));
    const setStatus = (message, state = '') => {
        if (!status) return;
        status.textContent = message;
        status.dataset.state = state;
    };

    const save = async () => {
        if (saving) {
            queued = true;
            return;
        }
        const orderedAssetIds = tiles()
            .map(tile => Number.parseInt(tile.dataset.assetId || '', 10))
            .filter(value => Number.isSafeInteger(value) && value > 0);
        if (orderedAssetIds.length === 0) return;

        saving = true;
        queued = false;
        setStatus('Saving album order…', 'saving');
        try {
            const payload = new FormData(form);
            payload.append('albumId', form.dataset.albumId || '');
            orderedAssetIds.forEach(id => payload.append('orderedAssetIds', String(id)));
            const response = await fetch(form.action, {
                method: 'POST',
                body: payload,
                credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' }
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success) {
                setStatus(result?.message || 'Album order could not be saved. Reload before trying again.', 'error');
                return;
            }
            setStatus('Album order saved.', 'saved');
        } catch {
            setStatus('Album order could not be saved. Check the connection and retry.', 'error');
        } finally {
            saving = false;
            if (queued) void save();
        }
    };

    const queueSave = () => {
        window.clearTimeout(saveTimer);
        saveTimer = window.setTimeout(() => void save(), 300);
    };

    grid.addEventListener('dragstart', event => {
        const tile = event.target.closest('[data-media-item]');
        if (!tile || !tile.dataset.assetId) return;
        dragging = tile;
        tile.classList.add('is-dragging');
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', tile.dataset.assetId);
    });

    grid.addEventListener('dragover', event => {
        if (!dragging) return;
        const target = event.target.closest('[data-media-item]');
        if (!target || target === dragging) return;
        event.preventDefault();
        event.dataTransfer.dropEffect = 'move';
        const rect = target.getBoundingClientRect();
        const before = event.clientY < rect.top + rect.height / 2
            || (Math.abs(event.clientY - (rect.top + rect.height / 2)) < rect.height * .25
                && event.clientX < rect.left + rect.width / 2);
        grid.insertBefore(dragging, before ? target : target.nextSibling);
    });

    grid.addEventListener('drop', event => {
        if (!dragging) return;
        event.preventDefault();
        queueSave();
    });

    grid.addEventListener('dragend', () => {
        if (!dragging) return;
        dragging.classList.remove('is-dragging');
        dragging = null;
        queueSave();
    });
})();
