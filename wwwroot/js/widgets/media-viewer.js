// Full-screen photo and video viewer, shared by the Photos gallery and the project pages
// (Overview, project photos, project videos). One implementation: previous/next, keyboard
// (arrows, Esc, + - 0, i), touch swipe, zoom (buttons, mouse wheel, pinch, double-click) with
// drag to look around, video playback, focus kept inside while open and returned to the
// thumbnail on close. A photo opens from its thumbnail and slides to the next one; the
// neighbouring photos are fetched ahead, and the controls fade while the pointer is still.
//
//   PMMediaViewer.attach(viewerElement, options) -> controller
//     options.items       the items to show (see below); or pass items to open()
//     options.hashPrefix  e.g. '#media=': keep the open item in the address (Photos gallery)
//     options.onRender    (item, index, viewer) after an item is shown (the gallery's info panel)
//     options.onNearEnd   (index, count) when one of the last items is shown, so a gallery that
//                         loads in parts can fetch the next part and call setItems()
//
// An item: { key, kind: 'photo' | 'video', displayUrl, title, alt, context, date,
//            originalUrl, downloadUrl, poster, thumbUrl, thumbFallbackUrl }. When the viewer has a
// [data-viewer-strip] element and items carry thumbUrl, a strip of nearby thumbnails is shown.
//
// Pages that only need the viewer render Pages/Shared/_MediaViewer.cshtml (data-media-viewer-auto;
// a JSON list with data-media-viewer-links="#media=" turns on links to one item, as on Visits)
// and mark thumbnails with data-media-viewer-open="<group>" data-media-viewer-key="<key>". The
// group's items come from <script type="application/json" data-media-viewer-items="<group>">,
// or else from the page's [data-media-viewer-item] elements of that group, in page order (so a
// reordered list is shown in its new order). Without JavaScript the thumbnails stay plain links.
(function (global) {
    'use strict';

    const ZOOM_MIN = 0.5;
    const ZOOM_MAX = 3;
    const ZOOM_STEP = 0.25;
    const SWIPE_MIN_PX = 50;
    const WHEEL_ZOOM_FACTOR = 1.15;
    const IDLE_AFTER_MS = 2800;
    const STRIP_SPAN = 10; // thumbnails shown on each side of the current item
    const NEAR_END = 3;
    const OPEN_CLASS = 'photos-viewer-open';
    const focusableSelector = [
        'a[href]:not([hidden])',
        'button:not([disabled]):not([hidden])',
        'input:not([disabled]):not([hidden])',
        'select:not([disabled]):not([hidden])',
        'textarea:not([disabled]):not([hidden])',
        '[tabindex]:not([tabindex="-1"]):not([hidden])',
        // A video's own controls (play, seek, volume, full screen) are reached with Tab too.
        'video[controls]:not([hidden])'
    ].join(',');

    function attach(viewer, options = {}) {
        if (!viewer) return null;
        const doc = viewer.ownerDocument;
        const win = doc.defaultView;

        let items = Array.isArray(options.items) ? options.items : [];
        const hashPrefix = options.hashPrefix || '';
        const onRender = typeof options.onRender === 'function' ? options.onRender : null;
        const onNearEnd = typeof options.onNearEnd === 'function' ? options.onNearEnd : null;

        const q = (selector) => viewer.querySelector(selector);
        const mediaHost = q('[data-viewer-media]');
        const title = q('[data-viewer-title]');
        const context = q('[data-viewer-context]');
        const position = q('[data-viewer-position]');
        const originalLink = q('[data-viewer-original]');
        const downloadLink = q('[data-viewer-download]');
        const infoButton = q('[data-viewer-info]');
        const infoPanel = q('[data-viewer-info-panel]');
        if (infoPanel) infoPanel.inert = true;
        const previousButton = q('[data-viewer-prev]');
        const nextButton = q('[data-viewer-next]');
        const zoomInButton = q('[data-viewer-zoom-in]');
        const zoomOutButton = q('[data-viewer-zoom-out]');
        const zoomResetButton = q('[data-viewer-zoom-reset]');
        const zoomLabel = q('[data-viewer-zoom-label]');
        const strip = q('[data-viewer-strip]');

        let currentIndex = 0;
        let previousFocus = null;
        let returnHash = '';
        let inertedElements = [];
        let zoom = 1;
        let pan = { x: 0, y: 0 };
        let idleTimer = 0;

        const isOpen = () => !viewer.hidden;
        const isMediaHash = (hash) => Boolean(hashPrefix) && hash.startsWith(hashPrefix);
        const hashFor = (item) => `${hashPrefix}${encodeURIComponent(item.key ?? '')}`;

        function writeHash() {
            if (!hashPrefix || items.length === 0) return;
            const next = hashFor(items[currentIndex]);
            if (win.location.hash !== next) win.history.replaceState(null, '', next);
        }

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
                [...parent.children].forEach((sibling) => {
                    if (sibling === current || sibling.contains(viewer)) return;
                    inertedElements.push({ element: sibling, inert: sibling.inert, ariaHidden: sibling.getAttribute('aria-hidden') });
                    sibling.inert = true;
                    sibling.setAttribute('aria-hidden', 'true');
                });
                current = parent;
                if (current === doc.body) break;
            }
        }

        function setOptionalLink(link, href) {
            if (!link) return;
            link.href = href || '#';
            link.hidden = !href;
        }

        // A zoomed photo can be dragged, but never further than its own edge.
        function applyPan() {
            const image = mediaHost.querySelector('img');
            if (!image || zoom <= 1) {
                pan = { x: 0, y: 0 };
            } else {
                const maxX = (image.offsetWidth * (zoom - 1)) / 2;
                const maxY = (image.offsetHeight * (zoom - 1)) / 2;
                pan = {
                    x: Math.min(maxX, Math.max(-maxX, pan.x)),
                    y: Math.min(maxY, Math.max(-maxY, pan.y))
                };
            }
            mediaHost.style.setProperty('--viewer-pan-x', `${pan.x}px`);
            mediaHost.style.setProperty('--viewer-pan-y', `${pan.y}px`);
        }

        function setZoom(nextZoom) {
            zoom = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, nextZoom));
            mediaHost.style.setProperty('--viewer-zoom', String(zoom));
            mediaHost.classList.toggle('is-zoomed', zoom > 1);
            applyPan();
            if (zoomLabel) zoomLabel.textContent = `${Math.round(zoom * 100)}%`;
            if (zoomOutButton) zoomOutButton.disabled = zoom <= ZOOM_MIN;
            if (zoomInButton) zoomInButton.disabled = zoom >= ZOOM_MAX;
        }

        function render(index) {
            if (items.length === 0) return;
            currentIndex = (index + items.length) % items.length;
            const item = items[currentIndex];

            setZoom(1);
            mediaHost.replaceChildren();
            // A video keeps a scrolling stage: zoomed in, its own controls must stay reachable.
            mediaHost.classList.toggle('has-video', item.kind === 'video');

            if (item.kind === 'video') {
                const video = doc.createElement('video');
                video.src = item.displayUrl || '';
                if (item.poster) video.poster = item.poster;
                video.controls = true;
                video.autoplay = true;
                video.playsInline = true;
                video.preload = 'metadata';
                video.setAttribute('aria-label', item.title || 'Video');
                video.tabIndex = 0; // its controls are reached with Tab in every browser
                mediaHost.append(video);
            } else {
                const image = doc.createElement('img');
                image.src = item.displayUrl || '';
                image.alt = item.alt || item.title || '';
                image.decoding = 'async';
                image.draggable = false;
                image.addEventListener('dblclick', () => setZoom(zoom === 1 ? 2 : 1));
                mediaHost.append(image);
            }

            if (title) title.textContent = item.title || '';
            if (context) context.textContent = [item.context, item.date].filter(Boolean).join(' · ');
            showPosition();
            setOptionalLink(originalLink, item.originalUrl);
            setOptionalLink(downloadLink, item.downloadUrl);
            if (previousButton) previousButton.hidden = items.length < 2;
            if (nextButton) nextButton.hidden = items.length < 2;

            if (onRender) onRender(item, currentIndex, viewer);
            renderStrip();
            preloadNeighbours();
            if (onNearEnd && currentIndex >= items.length - NEAR_END) onNearEnd(currentIndex, items.length);
        }

        function showPosition() {
            if (position) position.textContent = items.length > 1 ? `${currentIndex + 1} of ${items.length}` : '';
        }

        // A strip of the thumbnails around the current item. Only a window of them is built,
        // so a long timeline costs no more than a short one.
        function renderStrip() {
            if (!strip) return;
            const usable = items.length > 1 && items.some((item) => item.thumbUrl);
            strip.hidden = !usable;
            if (!usable) {
                strip.replaceChildren();
                return;
            }

            const from = Math.max(0, currentIndex - STRIP_SPAN);
            const to = Math.min(items.length - 1, currentIndex + STRIP_SPAN);
            const buttons = [];
            for (let index = from; index <= to; index += 1) {
                const item = items[index];
                const button = doc.createElement('button');
                button.type = 'button';
                button.className = index === currentIndex ? 'photos-viewer__thumb is-current' : 'photos-viewer__thumb';
                button.setAttribute('aria-label', `${item.title || 'Item'}, ${index + 1} of ${items.length}`);
                if (index === currentIndex) button.setAttribute('aria-current', 'true');
                if (item.thumbUrl) {
                    const image = doc.createElement('img');
                    image.src = item.thumbUrl;
                    image.alt = '';
                    image.decoding = 'async';
                    image.draggable = false;
                    // A thumbnail that cannot be loaded tries the item's fallback once, then
                    // leaves an empty frame rather than a broken picture.
                    const fallback = item.thumbFallbackUrl;
                    image.addEventListener('error', () => {
                        if (fallback && image.dataset.fallbackTried !== 'true' && image.getAttribute('src') !== fallback) {
                            image.dataset.fallbackTried = 'true';
                            image.src = fallback;
                        } else {
                            image.remove();
                        }
                    });
                    button.append(image);
                }
                button.addEventListener('click', () => {
                    if (index === currentIndex) return;
                    mediaHost.dataset.enter = index > currentIndex ? 'next' : 'previous';
                    render(index);
                    writeHash();
                    strip.querySelector('.is-current')?.focus({ preventScroll: true });
                });
                buttons.push(button);
            }
            strip.replaceChildren(...buttons);
            centreStrip();
        }

        // The strip scrolls sideways when it is wider than the screen; keep the current
        // thumbnail in the middle of what is visible. Called again once the viewer is shown,
        // because nothing can be measured while it is hidden.
        function centreStrip() {
            const current = strip?.querySelector('.is-current');
            if (current) strip.scrollLeft = current.offsetLeft - (strip.clientWidth - current.offsetWidth) / 2;
        }

        // Fetch the photos on either side now, so moving to them shows a picture at once.
        function preloadNeighbours() {
            if (typeof win.Image !== 'function' || items.length < 2) return;
            [1, -1].forEach((delta) => {
                const neighbour = items[(currentIndex + delta + items.length) % items.length];
                if (!neighbour || neighbour.kind === 'video' || !neighbour.displayUrl) return;
                const image = new win.Image();
                image.decoding = 'async';
                image.src = neighbour.displayUrl;
            });
        }

        function step(delta) {
            if (!isOpen() || items.length < 2) return;
            mediaHost.dataset.enter = delta > 0 ? 'next' : 'previous';
            render(currentIndex + delta);
            writeHash();
        }

        // The photo grows out of the thumbnail that was pressed. With nothing to measure
        // (opened from a link, or no layout) it simply fades in.
        function setOpenOrigin(trigger) {
            const from = trigger && typeof trigger.getBoundingClientRect === 'function' ? trigger.getBoundingClientRect() : null;
            const stage = mediaHost.getBoundingClientRect();
            if (!from || !from.width || !stage.width) {
                mediaHost.dataset.enter = 'fade';
                return;
            }
            const scale = Math.min(0.7, Math.max(0.12, from.width / stage.width));
            mediaHost.style.setProperty('--viewer-from-x', `${from.left + from.width / 2 - (stage.left + stage.width / 2)}px`);
            mediaHost.style.setProperty('--viewer-from-y', `${from.top + from.height / 2 - (stage.top + stage.height / 2)}px`);
            mediaHost.style.setProperty('--viewer-from-scale', String(scale));
            mediaHost.dataset.enter = 'open';
        }

        // The header and arrows fade while nothing moves, and return on any pointer or key.
        function wake() {
            viewer.classList.remove('is-idle');
            win.clearTimeout(idleTimer);
            if (!isOpen()) return;
            idleTimer = win.setTimeout(() => {
                if (!isOpen() || viewer.classList.contains('is-info-open') || mediaHost.querySelector('video')) return;
                viewer.classList.add('is-idle');
            }, IDLE_AFTER_MS);
        }

        function open(index, trigger, openOptions = {}) {
            if (Array.isArray(openOptions.items)) items = openOptions.items;
            if (items.length === 0) return;

            if (!isOpen()) {
                previousFocus = trigger || doc.activeElement;
                returnHash = isMediaHash(win.location.hash) ? '' : win.location.hash;
                // Only one thing plays at a time: pause any video on the page behind.
                doc.querySelectorAll('video').forEach((video) => {
                    if (!viewer.contains(video) && typeof video.pause === 'function') {
                        try { video.pause(); } catch { /* not playing */ }
                    }
                });
                setBackgroundInert(true);
            }

            const wasOpen = isOpen();
            render(index);
            viewer.hidden = false;
            viewer.setAttribute('aria-hidden', 'false');
            doc.body.classList.add(OPEN_CLASS);
            if (!wasOpen) setOpenOrigin(trigger);
            centreStrip();
            wake();
            if (!openOptions.fromHash) writeHash();
            q('[data-viewer-close]:not(.photos-viewer__backdrop)')?.focus({ preventScroll: true });
        }

        function setInfoOpen(openNow) {
            if (!infoButton || !infoPanel) return;
            viewer.classList.toggle('is-info-open', openNow);
            infoButton.setAttribute('aria-pressed', String(openNow));
            infoPanel.setAttribute('aria-hidden', String(!openNow));
            infoPanel.inert = !openNow;
        }

        function close() {
            if (!isOpen()) return;
            const video = mediaHost.querySelector('video');
            if (video && typeof video.pause === 'function') {
                try { video.pause(); } catch { /* not playing */ }
            }

            viewer.hidden = true;
            viewer.setAttribute('aria-hidden', 'true');
            win.clearTimeout(idleTimer);
            viewer.classList.remove('is-idle');
            delete mediaHost.dataset.enter;
            setInfoOpen(false);
            mediaHost.replaceChildren();
            setZoom(1);
            doc.body.classList.remove(OPEN_CLASS);
            setBackgroundInert(false);

            if (previousFocus && typeof previousFocus.focus === 'function' && doc.contains(previousFocus)) {
                previousFocus.focus({ preventScroll: true });
            }

            if (isMediaHash(win.location.hash)) {
                win.history.replaceState(null, '', `${win.location.pathname}${win.location.search}${returnHash}`);
            }
        }

        // Opens the item named in the address (#media=key), or closes when it names none.
        function syncWithHash() {
            if (!hashPrefix) return;
            if (!isMediaHash(win.location.hash)) {
                close();
                return;
            }

            let key;
            try {
                key = decodeURIComponent(win.location.hash.slice(hashPrefix.length));
            } catch {
                return;
            }
            const index = items.findIndex((item) => String(item.key) === key);
            if (index >= 0) open(index, null, { fromHash: true });
        }

        viewer.querySelectorAll('[data-viewer-close]').forEach((button) => button.addEventListener('click', close));
        previousButton?.addEventListener('click', () => step(-1));
        nextButton?.addEventListener('click', () => step(1));
        zoomInButton?.addEventListener('click', () => setZoom(zoom + ZOOM_STEP));
        zoomOutButton?.addEventListener('click', () => setZoom(zoom - ZOOM_STEP));
        zoomResetButton?.addEventListener('click', () => setZoom(1));
        infoButton?.addEventListener('click', () => setInfoOpen(!viewer.classList.contains('is-info-open')));

        // Mouse wheel zooms a photo in and out (never below its fitted size).
        mediaHost.addEventListener('wheel', (event) => {
            if (!mediaHost.querySelector('img')) return;
            event.preventDefault();
            const factor = event.deltaY < 0 ? WHEEL_ZOOM_FACTOR : 1 / WHEEL_ZOOM_FACTOR;
            setZoom(Math.max(1, zoom * factor));
        }, { passive: false });

        // One pointer drags a zoomed photo; two pointers pinch to zoom.
        const pointers = new Map();
        let dragOrigin = null;
        let pinch = null;
        const pointerDistance = () => {
            const [first, second] = [...pointers.values()];
            return Math.hypot(first.x - second.x, first.y - second.y);
        };
        mediaHost.addEventListener('pointerdown', (event) => {
            if (!mediaHost.querySelector('img')) return;
            pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
            if (pointers.size === 2) {
                pinch = { distance: pointerDistance() || 1, zoom };
                dragOrigin = null;
            } else if (zoom > 1) {
                dragOrigin = { x: event.clientX - pan.x, y: event.clientY - pan.y };
                mediaHost.classList.add('is-panning');
            }
        });
        mediaHost.addEventListener('pointermove', (event) => {
            if (!pointers.has(event.pointerId)) return;
            pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
            if (pinch && pointers.size === 2) {
                setZoom(Math.max(1, pinch.zoom * (pointerDistance() / pinch.distance)));
            } else if (dragOrigin) {
                pan = { x: event.clientX - dragOrigin.x, y: event.clientY - dragOrigin.y };
                applyPan();
            }
        });
        const endPointer = (event) => {
            const wasPinching = pinch !== null;
            pointers.delete(event.pointerId);
            if (pointers.size < 2) pinch = null;
            if (pointers.size === 0) {
                dragOrigin = null;
                mediaHost.classList.remove('is-panning');
            } else if (wasPinching && pointers.size === 1 && zoom > 1) {
                // A pinch that ends with one finger still down carries on as a drag.
                const [remaining] = [...pointers.values()];
                dragOrigin = { x: remaining.x - pan.x, y: remaining.y - pan.y };
                mediaHost.classList.add('is-panning');
            }
        };
        ['pointerup', 'pointercancel', 'pointerleave'].forEach((name) => mediaHost.addEventListener(name, endPointer));

        ['pointermove', 'pointerdown'].forEach((name) => viewer.addEventListener(name, wake, { passive: true }));

        // Touch: a horizontal swipe moves to the previous or next item (not while zoomed in,
        // when a drag is for looking around the picture).
        let touchStart = null;
        mediaHost.addEventListener('touchstart', (event) => {
            const touch = event.changedTouches?.[0];
            touchStart = touch && event.touches.length === 1 ? { x: touch.clientX, y: touch.clientY } : null;
        }, { passive: true });
        mediaHost.addEventListener('touchend', (event) => {
            const touch = event.changedTouches?.[0];
            if (!touchStart || !touch || zoom !== 1) {
                touchStart = null;
                return;
            }
            const dx = touch.clientX - touchStart.x;
            const dy = touch.clientY - touchStart.y;
            touchStart = null;
            if (Math.abs(dx) >= SWIPE_MIN_PX && Math.abs(dx) > Math.abs(dy) * 1.5) step(dx < 0 ? 1 : -1);
        }, { passive: true });

        doc.addEventListener('keydown', (event) => {
            if (!isOpen()) return;
            wake();

            if (event.key === 'Tab') {
                const focusable = [...viewer.querySelectorAll(focusableSelector)]
                    .filter((element) => !element.hidden && element.getClientRects().length > 0);
                if (focusable.length === 0) {
                    event.preventDefault();
                    return;
                }
                const first = focusable[0];
                const last = focusable[focusable.length - 1];
                if (event.shiftKey && doc.activeElement === first) {
                    event.preventDefault();
                    last.focus();
                } else if (!event.shiftKey && doc.activeElement === last) {
                    event.preventDefault();
                    first.focus();
                }
                return;
            }

            // Keys typed into a field (the gallery's caption editor) are not viewer commands.
            const typing = event.target instanceof win.HTMLElement
                && event.target.closest('input, textarea, select, [contenteditable="true"]');
            if (typing && event.key !== 'Escape') return;

            if (event.key === 'Escape') { event.preventDefault(); close(); }
            else if (event.key === 'ArrowLeft') { event.preventDefault(); step(-1); }
            else if (event.key === 'ArrowRight') { event.preventDefault(); step(1); }
            else if (event.key === '+' || event.key === '=') { event.preventDefault(); setZoom(zoom + ZOOM_STEP); }
            else if (event.key === '-') { event.preventDefault(); setZoom(zoom - ZOOM_STEP); }
            else if (event.key === '0') { event.preventDefault(); setZoom(1); }
            else if (infoButton && event.key.toLowerCase() === 'i') { event.preventDefault(); infoButton.click(); }
        });

        return {
            open,
            close,
            step,
            syncWithHash,
            // Items added at the end (a gallery loading its next part) keep the open item.
            setItems(next) {
                items = Array.isArray(next) ? next : [];
                if (isOpen() && items.length > 0) {
                    currentIndex = Math.min(currentIndex, items.length - 1);
                    showPosition();
                    renderStrip();
                }
            },
            get index() { return currentIndex; },
            get isOpen() { return isOpen(); },
        };
    }

    function itemFromElement(element) {
        const data = element.dataset;
        return {
            key: data.mediaViewerKey || '',
            kind: data.kind === 'video' ? 'video' : 'photo',
            displayUrl: data.displayUrl || element.getAttribute('href') || '',
            title: data.title || '',
            alt: data.alt || '',
            context: data.context || '',
            date: data.date || '',
            originalUrl: data.originalUrl || '',
            downloadUrl: data.downloadUrl || '',
            poster: data.poster || '',
        };
    }

    // The items of a group: the JSON list if the page has one, else its elements in page order.
    function readGroup(doc, group) {
        const lists = [...doc.querySelectorAll('script[type="application/json"][data-media-viewer-items]')]
            .filter((script) => script.dataset.mediaViewerItems === group);
        if (lists.length > 0) {
            try {
                const parsed = JSON.parse(lists[0].textContent || '[]');
                return Array.isArray(parsed) ? parsed.filter((item) => item && item.displayUrl) : [];
            } catch {
                return [];
            }
        }

        return [...doc.querySelectorAll('[data-media-viewer-item]')]
            .filter((element) => element.dataset.mediaViewerGroup === group)
            .map(itemFromElement)
            .filter((item) => item.displayUrl);
    }

    function autoWire(doc) {
        const viewer = doc.querySelector('[data-media-viewer-auto]');
        if (!viewer || viewer.dataset.mediaViewerWired === 'true') return null;
        viewer.dataset.mediaViewerWired = 'true';
        // Optional links to one item: a JSON items list marked data-media-viewer-links="#media="
        // keeps the open item of that group in the address and opens it again from a shared link.
        const linkList = doc.querySelector('script[type="application/json"][data-media-viewer-items][data-media-viewer-links]');
        const hashPrefix = linkList?.dataset.mediaViewerLinks || '';
        const linkGroup = linkList?.dataset.mediaViewerItems || '';
        const linked = Boolean(hashPrefix && linkGroup);
        const controller = attach(viewer, linked ? { hashPrefix, items: readGroup(doc, linkGroup) } : {});

        doc.addEventListener('click', (event) => {
            // A modified click (new tab, new window) keeps the link's own behaviour.
            if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
            const trigger = event.target instanceof doc.defaultView.Element
                ? event.target.closest('[data-media-viewer-open]')
                : null;
            if (!trigger) return;

            const items = readGroup(doc, trigger.dataset.mediaViewerOpen);
            const index = items.findIndex((item) => String(item.key) === trigger.dataset.mediaViewerKey);
            if (index < 0) return;
            event.preventDefault();
            controller.open(index, trigger, { items });
        });

        if (linked) {
            const sync = () => {
                controller.setItems(readGroup(doc, linkGroup));
                controller.syncWithHash();
            };
            doc.defaultView.addEventListener('hashchange', sync);
            sync();
        }

        return controller;
    }

    const api = { attach, readGroup, autoWire };
    global.PMMediaViewer = api;

    const doc = global.document;
    if (doc) {
        const start = () => { api.page = autoWire(doc); };
        if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', start, { once: true });
        else start();
    }
})(window);
