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

    // The full-screen viewer is the shared one (js/widgets/media-viewer.js); the gallery adds
    // its information panel (collection, people, albums, caption, file details).
    const value = (tile, name) => tile.dataset[name] || '';
    const mediaKey = tile => tile.dataset.mediaKey || tile.dataset.assetId || String(tiles.indexOf(tile) + 1);
    const items = tiles.map(tile => ({
        key: mediaKey(tile),
        kind: value(tile, 'kind') === 'video' ? 'video' : 'photo',
        displayUrl: value(tile, 'displayUrl'),
        title: value(tile, 'title'),
        context: value(tile, 'context'),
        date: value(tile, 'date'),
        originalUrl: value(tile, 'originalUrl'),
        downloadUrl: value(tile, 'downloadUrl') || value(tile, 'originalUrl'),
        tile
    }));

    const parseLinks = (raw) => {
        try {
            const parsed = JSON.parse(raw || '[]');
            return Array.isArray(parsed) ? parsed.filter(entry => entry && entry.name && entry.url) : [];
        } catch {
            return [];
        }
    };

    const fillLinks = (host, row, links) => {
        if (host) {
            host.replaceChildren();
            links.forEach(entry => {
                const link = document.createElement('a');
                link.href = entry.url;
                link.textContent = entry.name;
                host.append(link);
            });
        }
        if (row) row.hidden = links.length === 0;
    };

    const setText = (selector, text) => {
        const host = viewer.querySelector(selector);
        if (host) host.textContent = text;
    };

    const setRow = (selector, visible) => {
        const row = viewer.querySelector(selector);
        if (row) row.hidden = !visible;
    };

    function renderInfo(item) {
        const tile = item.tile;
        const sourceLink = viewer.querySelector('[data-info-source-link]');
        if (sourceLink) {
            sourceLink.href = value(tile, 'sourceUrl') || '#';
            sourceLink.hidden = !value(tile, 'sourceUrl');
        }
        setText('[data-info-context]', value(tile, 'context'));
        setText('[data-info-date]', value(tile, 'date'));
        setText('[data-info-source]', `${value(tile, 'sourceLabel')} · ${value(tile, 'subtitle')}`);
        setText('[data-info-classification]', value(tile, 'classification') || 'Not classified');

        const caption = value(tile, 'caption');
        setText('[data-info-caption]', caption);
        setRow('[data-info-caption-row]', Boolean(caption));

        fillLinks(viewer.querySelector('[data-info-albums]'), viewer.querySelector('[data-info-albums-row]'), parseLinks(value(tile, 'albums')));
        fillLinks(viewer.querySelector('[data-info-people]'), viewer.querySelector('[data-info-people-row]'), parseLinks(value(tile, 'people')));

        const filename = value(tile, 'filename');
        setRow('[data-info-filename-row]', Boolean(filename));
        setText('[data-info-filename]', filename);

        const fileSize = value(tile, 'fileSize');
        setRow('[data-info-file-size-row]', Boolean(fileSize));
        setText('[data-info-file-size]', fileSize);

        const editCaptionButton = viewer.querySelector('[data-info-edit-caption]');
        if (editCaptionButton) {
            const assetId = value(tile, 'assetId');
            editCaptionButton.hidden = !assetId;
            editCaptionButton.dataset.assetId = assetId;
            editCaptionButton.dataset.caption = value(tile, 'editorialCaption');
            editCaptionButton.dataset.token = value(tile, 'editorialToken');
        }

        const unidentified = Number.parseInt(value(tile, 'unidentified'), 10) || 0;
        setRow('[data-info-unidentified-row]', unidentified > 0);
        setText('[data-info-unidentified]', unidentified === 1 ? '1 face awaiting review' : `${unidentified} faces awaiting review`);

        const width = value(tile, 'width');
        const height = value(tile, 'height');
        setRow('[data-info-dimensions-row]', Boolean(width && height));
        setText('[data-info-dimensions]', width && height ? `${width} × ${height} px` : '');

        const duration = value(tile, 'duration');
        setRow('[data-info-duration-row]', Boolean(duration));
        setText('[data-info-duration]', duration);
    }

    const mediaViewer = window.PMMediaViewer.attach(viewer, {
        items,
        hashPrefix: '#media=',
        onRender: renderInfo
    });

    if (root.dataset.organizeAlbum !== 'true') {
        tiles.forEach((tile, index) => tile.addEventListener('click', () => mediaViewer.open(index, tile)));
    }

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

    if (root.dataset.organizeAlbum !== 'true') {
        window.addEventListener('hashchange', () => mediaViewer.syncWithHash());
        mediaViewer.syncWithHash();
    } else if (window.location.hash.startsWith('#media=')) {
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

    const autoStart = library.dataset.selectionAutoStart === 'true';
    const cancelUrl = library.dataset.selectionCancelUrl || '';
    const targetedCuration = autoStart && cancelUrl.length > 0;

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

    let selecting = autoStart;
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
        if (targetedCuration) {
            window.location.assign(cancelUrl);
            return;
        }
        selecting = false;
        clearSelection();
        render();
    };

    toggle.addEventListener('click', () => {
        if (selecting && targetedCuration) {
            window.location.assign(cancelUrl);
            return;
        }
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
