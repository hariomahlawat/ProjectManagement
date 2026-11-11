(() => {
    'use strict';

    const root = document.querySelector('[data-photos-library]');
    const viewer = document.querySelector('[data-photos-viewer]');
    if (!root || !viewer) return;

    let tiles = Array.from(root.querySelectorAll('[data-media-item]'));

    // The slim bar sticks just below the application's top bar, whatever its height.
    const topbar = document.querySelector('.pm-topbar');
    if (topbar && root.querySelector('[data-photos-bar]')) {
        const syncBarOffset = () => root.style.setProperty('--photos-bar-top', `${topbar.offsetHeight}px`);
        syncBarOffset();
        if ('ResizeObserver' in window) new ResizeObserver(syncBarOffset).observe(topbar);
    }

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
    const buildItems = () => tiles.map(tile => ({
        key: mediaKey(tile),
        kind: value(tile, 'kind') === 'video' ? 'video' : 'photo',
        displayUrl: value(tile, 'displayUrl'),
        title: value(tile, 'title'),
        context: value(tile, 'context'),
        date: value(tile, 'date'),
        originalUrl: value(tile, 'originalUrl'),
        downloadUrl: value(tile, 'downloadUrl') || value(tile, 'originalUrl'),
        // Read when the strip is drawn, so a tile that fell back to its larger thumbnail
        // gives the strip the picture that actually loaded.
        get thumbUrl() { return tile.querySelector('[data-media-image]')?.getAttribute('src') || ''; },
        get thumbFallbackUrl() { return tile.querySelector('[data-media-image]')?.dataset.fallbackSrc || ''; },
        tile
    }));
    let items = buildItems();

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

        // One line for the file: name, size and pixel size, whichever are known.
        const width = value(tile, 'width');
        const height = value(tile, 'height');
        const fileFacts = [
            value(tile, 'filename'),
            value(tile, 'fileSize'),
            width && height ? `${width} × ${height} px` : ''
        ].filter(Boolean);
        setRow('[data-info-file-row]', fileFacts.length > 0);
        setText('[data-info-file]', fileFacts.join(' · '));

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

        const duration = value(tile, 'duration');
        setRow('[data-info-duration-row]', Boolean(duration));
        setText('[data-info-duration]', duration);
    }

    const mediaViewer = window.PMMediaViewer.attach(viewer, {
        items,
        hashPrefix: '#media=',
        onRender: renderInfo,
        // Near the end of what is loaded, fetch the next part so the viewer can carry on.
        onNearEnd: () => { void loadNextPart(); }
    });

    // One listener for every tile, including tiles added later as the user scrolls.
    if (root.dataset.organizeAlbum !== 'true') {
        root.addEventListener('click', (event) => {
            const tile = event.target?.closest?.('[data-media-item]') ?? null;
            if (!tile || tile.matches(':disabled')) return;
            const index = tiles.indexOf(tile);
            if (index >= 0) mediaViewer.open(index, tile);
        });
    }

    const markUnavailable = (image) => {
        const tile = image.closest('[data-media-item]');
        if (!tile) return;
        image.hidden = true;
        tile.classList.add('photos-tile--unavailable');
        tile.setAttribute('aria-label', 'Media unavailable');
        tile.disabled = true;
    };

    // Tiles ask for a small picture. A library that does not have that size falls back once
    // to the larger thumbnail before the tile is shown as unavailable.
    const handleImageFailure = (image) => {
        const fallback = image.dataset.fallbackSrc;
        if (fallback && image.dataset.fallbackTried !== 'true') {
            image.dataset.fallbackTried = 'true';
            image.src = fallback;
            return;
        }
        markUnavailable(image);
    };

    const watchImageFailure = (image) => {
        image.addEventListener('error', () => handleImageFailure(image));
        if (image.complete && image.naturalWidth === 0) {
            handleImageFailure(image);
        }
    };
    document.querySelectorAll('[data-media-image]').forEach(watchImageFailure);


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

    const layoutAll = () => root.querySelectorAll('.photos-grid').forEach(layoutGrid);
    let layoutFrame = 0;
    const queueLayout = () => {
        cancelAnimationFrame(layoutFrame);
        layoutFrame = requestAnimationFrame(layoutAll);
    };

    // Every grid is as wide as the library, so watching the library covers grids added later.
    if ('ResizeObserver' in window) {
        new ResizeObserver(queueLayout).observe(root);
    } else {
        window.addEventListener('resize', queueLayout, { passive: true });
    }

    const watchImageLoad = (image) => {
        const markLoaded = () => {
            image.closest('[data-media-item]')?.classList.add('is-loaded');
        };
        image.addEventListener('load', markLoaded);
        if (image.complete && image.naturalWidth > 0) markLoaded();
    };
    document.querySelectorAll('[data-media-image]').forEach(watchImageLoad);

    layoutAll();

    // Continuous scroll: when the user nears the end, the next part of the timeline is fetched
    // from the same page (same filters, same access rules) and added below. Without script,
    // or if a part fails to load, the Previous / Next links still work.
    const timeline = root.querySelector('[data-photos-timeline]');
    const more = root.querySelector('[data-photos-more]');
    const pagination = root.querySelector('[data-photos-pagination]');
    let nextPartUrl = timeline?.dataset.nextPartUrl || '';
    let loadingPart = false;
    let moreObserver = null;

    const itemLabel = (count) => `${count} item${count === 1 ? '' : 's'}`;

    function appendTimelinePart(html) {
        const holder = document.createElement('template');
        holder.innerHTML = html;
        const added = [];

        Array.from(holder.content.children).forEach((node) => {
            if (node.classList.contains('photos-month')) {
                if (!node.id || !document.getElementById(node.id)) timeline.append(node);
                return;
            }
            if (!node.classList.contains('photos-day')) return;

            // A day split across two parts continues in the day already on the page.
            const headingId = node.getAttribute('aria-labelledby');
            const existingHeading = headingId ? document.getElementById(headingId) : null;
            const existingDay = existingHeading?.closest('.photos-day');
            const existingGrid = existingDay?.querySelector('.photos-grid');
            if (existingGrid) {
                const incoming = Array.from(node.querySelectorAll('[data-media-item]'));
                incoming.forEach(tile => existingGrid.append(tile));
                added.push(...incoming);
                const count = existingDay.querySelector('[data-day-count]');
                if (count) {
                    const total = existingGrid.querySelectorAll('[data-media-item]').length;
                    count.dataset.dayCount = String(total);
                    count.textContent = itemLabel(total);
                }
                return;
            }

            timeline.append(node);
            added.push(...node.querySelectorAll('[data-media-item]'));
        });

        if (added.length === 0) return;
        added.forEach((tile) => {
            const image = tile.querySelector('[data-media-image]');
            if (image) {
                watchImageFailure(image);
                watchImageLoad(image);
            }
        });
        tiles = Array.from(root.querySelectorAll('[data-media-item]'));
        items = buildItems();
        mediaViewer.setItems(items);
        layoutAll();
        root.dispatchEvent(new CustomEvent('photos:tiles-changed'));
    }

    // The page links come back when a part cannot be loaded. By then earlier parts may have
    // been added, so Next must open the part that failed, not the page after the first one.
    function showPaginationFallback() {
        if (!pagination) return;
        const nextLink = pagination.querySelector('[data-photos-next-page]');
        if (nextLink && nextPartUrl) {
            try {
                const url = new URL(nextPartUrl, window.location.href);
                url.searchParams.delete('handler');
                nextLink.setAttribute('href', `${url.pathname}${url.search}`);
                // The timeline now spans several pages, so "Page 1" would mislead.
                pagination.querySelector('[data-photos-page-label]')?.setAttribute('hidden', '');
            } catch {
                // Keep the link the page was rendered with.
            }
        }
        pagination.hidden = false;
    }

    async function loadNextPart() {
        if (!timeline || !nextPartUrl || loadingPart) return;
        loadingPart = true;
        more?.querySelector('[data-photos-more-loading]')?.removeAttribute('hidden');
        more?.querySelector('[data-photos-more-error]')?.setAttribute('hidden', '');
        try {
            const response = await fetch(nextPartUrl, {
                headers: { 'X-Requested-With': 'PhotosTimelinePart' },
                cache: 'no-store',
                credentials: 'same-origin'
            });
            // A real part always carries the next-part header, even when empty. A redirect (an
            // expired sign-in lands on the login page) or any other page does not, and must not
            // be taken for "no more pictures".
            const nextHeader = response.headers.get('X-Photos-Next-Part');
            if (!response.ok || response.redirected || nextHeader === null) {
                throw new Error(`Timeline part failed: ${response.status}`);
            }
            const html = await response.text();
            nextPartUrl = nextHeader;
            appendTimelinePart(html);
            if (!nextPartUrl && more) {
                more.hidden = true;
            } else if (moreObserver && more) {
                // An observer reports changes only. Watching again asks "still near the end?",
                // so a short part on a tall screen is followed by the next one.
                moreObserver.unobserve(more);
                moreObserver.observe(more);
            }
        } catch {
            more?.querySelector('[data-photos-more-loading]')?.setAttribute('hidden', '');
            more?.querySelector('[data-photos-more-error]')?.removeAttribute('hidden');
            showPaginationFallback();
        } finally {
            loadingPart = false;
        }
    }

    if (timeline && more && nextPartUrl && 'IntersectionObserver' in window) {
        more.hidden = false;
        if (pagination) pagination.hidden = true;
        moreObserver = new IntersectionObserver((entries) => {
            if (entries.some(entry => entry.isIntersecting)) void loadNextPart();
        }, { rootMargin: '1200px 0px' });
        moreObserver.observe(more);
        more.querySelector('[data-photos-more-retry]')?.addEventListener('click', () => void loadNextPart());
    }

    // The month list on the right edge. A month already on the page is scrolled to; any other
    // month is opened by its own link, which starts the timeline at that month.
    const scrubber = root.querySelector('[data-photos-scrubber]');
    if (scrubber && timeline) {
        const barHeight = () => (root.querySelector('[data-photos-bar]')?.getBoundingClientRect().bottom || 0) + 12;
        scrubber.addEventListener('click', (event) => {
            const link = event.target?.closest?.('a[data-month]') ?? null;
            const heading = link ? document.getElementById(`month-${link.dataset.month}`) : null;
            if (!heading) return;
            event.preventDefault();
            window.scrollTo({ top: heading.getBoundingClientRect().top + window.scrollY - barHeight(), behavior: 'smooth' });
        });

        let scrubberFrame = 0;
        const markCurrentMonth = () => {
            const limit = barHeight() + 40;
            let current = '';
            timeline.querySelectorAll('.photos-month').forEach((heading) => {
                if (!current || heading.getBoundingClientRect().top < limit) current = heading.id.replace('month-', '');
            });
            scrubber.querySelectorAll('a[data-month]').forEach((link) => {
                const isCurrent = link.dataset.month === current;
                link.classList.toggle('is-current', isCurrent);
                if (isCurrent) link.setAttribute('aria-current', 'true');
                else link.removeAttribute('aria-current');
            });
        };
        window.addEventListener('scroll', () => {
            cancelAnimationFrame(scrubberFrame);
            scrubberFrame = requestAnimationFrame(markCurrentMonth);
        }, { passive: true });
        root.addEventListener('photos:tiles-changed', markCurrentMonth);
        markCurrentMonth();
    }

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

    let tiles = Array.from(library.querySelectorAll('[data-media-item]'));
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
    const cancelButton = actionBar?.querySelector('[data-selection-cancel]');

    let selecting = autoStart;
    // Pressing Select keeps the mode on with nothing chosen. A mode started by ticking a
    // picture ends by itself when the last tick is removed.
    let startedByButton = autoStart;
    let lastSelectedIndex = -1;
    let suppressNextClick = false;
    const selected = new Set();

    const keyFor = (tile, index) => tile.dataset.mediaKey || String(index);
    const assetIdFor = tile => {
        const value = Number.parseInt(tile.dataset.assetId || '', 10);
        return Number.isSafeInteger(value) && value > 0 ? value : null;
    };

    const selectedTiles = () => tiles.filter((tile, index) => selected.has(keyFor(tile, index)));
    const isSelectable = tile => !tile.disabled && !tile.classList.contains('photos-tile--unavailable');
    const selectableIn = scope => (scope ? Array.from(scope.querySelectorAll('[data-media-item]')).filter(isSelectable) : []);

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

        library.querySelectorAll('[data-day-select]').forEach(button => {
            const dayTiles = selectableIn(button.closest('.photos-day'));
            const allChosen = dayTiles.length > 0 && dayTiles.every(tile => tile.classList.contains('is-selected'));
            button.classList.toggle('is-selected', allChosen);
            button.setAttribute('aria-pressed', String(allChosen));
        });

        if (countLabel) countLabel.textContent = chosen.length === 0 ? 'Choose pictures' : `${chosen.length} selected`;
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
        startedByButton = false;
        clearSelection();
        render();
    };

    const enterSelectionMode = () => {
        if (selecting) return;
        selecting = true;
        render();
    };

    toggle.addEventListener('click', () => {
        if (selecting && targetedCuration) {
            window.location.assign(cancelUrl);
            return;
        }
        selecting = !selecting;
        startedByButton = selecting;
        if (!selecting) clearSelection();
        render();
    });

    cancelButton?.addEventListener('click', leaveSelectionMode);

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
        // The tick beside a day heading takes, or releases, the whole day.
        const dayButton = event.target?.closest?.('[data-day-select]') ?? null;
        if (dayButton) {
            const dayTiles = selectableIn(dayButton.closest('.photos-day'));
            const release = dayTiles.length > 0 && dayTiles.every(tile => tile.classList.contains('is-selected'));
            enterSelectionMode();
            dayTiles.forEach(tile => setTileSelected(tile, tiles.indexOf(tile), !release));
            rebuildActionTargets();
            if (selected.size === 0 && !startedByButton && !targetedCuration) leaveSelectionMode();
            return;
        }

        const tile = event.target?.closest?.('[data-media-item]') ?? null;
        if (!tile) return;
        // Outside the mode, only the tick on a picture selects; the picture itself opens.
        const onTick = Boolean(event.target.closest('.photos-tile__select'));
        if (!selecting && (!onTick || !isSelectable(tile))) return;
        enterSelectionMode();

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
        if (selected.size === 0 && !startedByButton && !targetedCuration) leaveSelectionMode();
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

    library.addEventListener('pointerdown', event => {
        if (!selecting || event.pointerType !== 'mouse' || event.button !== 0) return;
        const grid = event.target?.closest?.('.photos-grid') ?? null;
        if (!grid) return;
        dragStart = { x: event.clientX, y: event.clientY };
        dragGrid = grid;
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

    // Tiles added while scrolling join the selection mode that is already active.
    library.addEventListener('photos:tiles-changed', () => {
        tiles = Array.from(library.querySelectorAll('[data-media-item]'));
        render();
    });

    render();
})();
