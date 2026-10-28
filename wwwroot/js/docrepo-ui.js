// Document Repository pages with a [data-library] root: the library (shelves, filters, results,
// reading pane), the reader and the other repository pages (quick search, favourites, the
// action dialog).
// Filters apply in place: the results region is fetched with ?partial=true and swapped, and the
// address bar keeps the same query so reloads and shared links match the screen. Every link and
// form also works without this script.
(() => {
    "use strict";

    const root = document.querySelector("[data-library]");
    if (!root) return;

    const results = root.querySelector("#libraryResults");
    const pane = root.querySelector("#libraryPane");
    const body = root.querySelector(".library-body");
    const searchForm = root.querySelector("[data-library-search]");
    const token = root.querySelector('#libraryToken input[name="__RequestVerificationToken"]')?.value || "";
    // From 1600px the reading pane keeps its own column, so opening a document never reflows the list.
    const wideQuery = window.matchMedia("(min-width: 1600px)");
    const narrowQuery = window.matchMedia("(max-width: 767.98px)");
    const filterKeys = ["officeCategoryId", "documentCategoryId", "year", "tag", "from", "includeInactive", "sort"];
    let controller = null;
    let textTimer = null;
    let selectedId = null;

    // SECTION: Fetch and swap the results region
    async function load(url, { push = true, focusName = null } = {}) {
        if (!results) { window.location.assign(url); return; }
        let target = new URL(url, window.location.origin);
        target.searchParams.delete("partial");
        const request = new URL(target);
        request.searchParams.set("partial", "true");

        // A newer navigation replaces any text filter still waiting for a pause in typing.
        window.clearTimeout(textTimer);
        controller?.abort();
        controller = new AbortController();
        results.setAttribute("aria-busy", "true");
        try {
            const response = await fetch(request, {
                headers: { "X-Requested-With": "DocRepoPartial" },
                credentials: "same-origin",
                signal: controller.signal
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            // Shortcuts typed in the search box (type:, office:…) come back as a redirect to the
            // URL with real filters; that is the address to show.
            if (response.redirected) {
                target = new URL(response.url);
                target.searchParams.delete("partial");
            }
            results.innerHTML = await response.text();
            // The open preview stays open, so the new history entry records it too.
            if (selectedId && wideQuery.matches && !target.searchParams.has("open")) target.searchParams.set("open", selectedId);
            if (push) window.history.pushState({ library: true }, "", target);
            else if (response.redirected) window.history.replaceState({ library: true }, "", target);
            syncSearchForm(target);
            syncHero(target);
            enhanceRegion();
            markSelected();
            if (focusName) {
                const field = results.querySelector(`[name="${focusName}"]`);
                field?.focus();
                if (field && typeof field.setSelectionRange === "function") field.setSelectionRange(field.value.length, field.value.length);
            }
        } catch (error) {
            if (error.name === "AbortError") return;
            window.location.assign(target);
        } finally {
            results.setAttribute("aria-busy", "false");
        }
    }

    function syncSearchForm(url) {
        if (!searchForm) return;
        const input = searchForm.querySelector('input[name="q"]');
        if (input && document.activeElement !== input) input.value = url.searchParams.get("q") || "";
        const view = searchForm.querySelector('input[name="view"]');
        if (view) view.value = url.searchParams.get("view") || "";
    }

    // The front page's large hero folds to a search bar once anything narrows the list.
    function syncHero(url) {
        const hero = root.querySelector(".library-hero");
        if (!hero) return;
        const home = !results.querySelector(".library-shelves") ? false : true;
        hero.classList.toggle("is-home", home);
        hero.classList.toggle("is-compact", !home);
        if (!home && url.searchParams.get("q")) hero.classList.add("is-compact");
    }

    function withParams(changes) {
        const url = new URL(window.location.href);
        url.searchParams.delete("page");
        url.searchParams.delete("partial");
        Object.entries(changes).forEach(([key, value]) => {
            if (value === null || value === undefined || value === "") url.searchParams.delete(key);
            else url.searchParams.set(key, value);
        });
        return url.pathname + url.search;
    }

    // SECTION: Tag and sender filters wait for a pause in typing
    results?.addEventListener("input", event => {
        const field = event.target.closest("[data-library-filter-text]");
        if (!field) return;
        window.clearTimeout(textTimer);
        textTimer = window.setTimeout(() => load(withParams({ [field.name]: field.value.trim() }), { focusName: field.name }), 500);
    });

    results?.addEventListener("submit", event => {
        const form = event.target.closest("[data-library-textfilter]");
        if (!form) return;
        event.preventDefault();
        const changes = {};
        form.querySelectorAll("[data-library-filter-text]").forEach(field => { changes[field.name] = field.value.trim(); });
        load(withParams(changes));
    });

    searchForm?.addEventListener("submit", event => {
        if (!results) return; // On other pages the search form simply opens the library.
        event.preventDefault();
        const params = new URLSearchParams();
        new FormData(searchForm).forEach((value, key) => { if (String(value) !== "") params.append(key, String(value)); });
        // Keep the current filters with the new search words.
        const current = new URLSearchParams(window.location.search);
        filterKeys.forEach(key => { if (current.get(key)) params.set(key, current.get(key)); });
        if (!params.get("q")) params.delete("sort");
        closeQuick();
        load(`${window.location.pathname}?${params}`);
    });

    // SECTION: Links inside the region (facets, chips, sort, layouts, pages, tags) load in place
    results?.addEventListener("click", event => {
        const link = event.target.closest("a[data-library-link]");
        if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
        event.preventDefault();
        const toTop = link.closest(".library-pager, .library-browse, .library-shelf__head");
        load(link.href);
        if (toTop) root.querySelector("#libraryBrowse, #libraryResults")?.scrollIntoView({ behavior: "smooth", block: "start" });
    });

    root.querySelector(".library-hero")?.addEventListener("click", event => {
        const link = event.target.closest("a[data-library-link]");
        if (!link || !results || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
        event.preventDefault();
        load(link.href);
    });

    // Back and Forward restore the results and the preview that the URL names.
    if (results) window.addEventListener("popstate", async () => {
        await load(window.location.href, { push: false });
        syncPane(new URL(window.location.href));
    });

    // SECTION: Facet lists show six options, then "Show all"
    results?.addEventListener("click", event => {
        const more = event.target.closest("[data-facet-more]");
        if (!more) return;
        const facet = more.closest(".library-facet");
        const open = !facet.classList.contains("is-expanded");
        facet.classList.toggle("is-expanded", open);
        more.setAttribute("aria-expanded", open ? "true" : "false");
        more.dataset.label ||= more.textContent;
        more.textContent = open ? "Show fewer" : more.dataset.label;
    });

    // SECTION: Covers: a first-page image when there is one, the type-coloured cover otherwise
    document.addEventListener("error", event => {
        const image = event.target;
        if (image instanceof HTMLImageElement && image.matches("[data-cover]")) image.classList.add("is-missing");
    }, true);

    // SECTION: Shelves scroll sideways; the arrows move one screen of covers
    root.addEventListener("click", event => {
        const button = event.target.closest("[data-shelf-scroll]");
        if (!button) return;
        const row = button.closest(".library-shelf")?.querySelector("[data-shelf-row]");
        if (!row) return;
        row.scrollBy({ left: Math.sign(Number(button.dataset.shelfScroll)) * row.clientWidth * 0.85, behavior: "smooth" });
    });

    function updateShelfArrows() {
        root.querySelectorAll(".library-shelf").forEach(shelf => {
            const row = shelf.querySelector("[data-shelf-row]");
            if (!row) return;
            const update = () => {
                shelf.classList.toggle("can-back", row.scrollLeft > 4);
                shelf.classList.toggle("can-forward", row.scrollLeft + row.clientWidth < row.scrollWidth - 4);
            };
            if (!row.dataset.watched) {
                row.addEventListener("scroll", update, { passive: true });
                row.dataset.watched = "true";
            }
            update();
        });
    }

    // "Continue reading" comes from this browser's memory of the documents opened here.
    async function fillContinueReading() {
        const shelf = results?.querySelector("[data-shelf-continue]");
        if (!shelf || !root.dataset.cardsUrl) return;
        let recent = [];
        try { recent = JSON.parse(window.localStorage.getItem(`prism.docrepo.recent.${root.dataset.userKey}`) || "[]"); } catch { recent = []; }
        const ids = (Array.isArray(recent) ? recent : []).map(entry => entry?.id).filter(Boolean).slice(0, 12);
        if (!ids.length) return;
        try {
            const url = new URL(root.dataset.cardsUrl, window.location.origin);
            url.searchParams.set("ids", ids.join(","));
            const response = await fetch(url, { credentials: "same-origin" });
            if (!response.ok) return;
            const html = (await response.text()).trim();
            if (!html) return;
            const row = shelf.querySelector("[data-shelf-row]");
            row.innerHTML = html;
            row.setAttribute("data-library-items", "");
            // The page last read shows on each cover.
            row.querySelectorAll("[data-library-item]").forEach(item => {
                const entry = recent.find(candidate => candidate?.id === item.dataset.docId);
                if (entry?.page > 1) {
                    const badge = document.createElement("span");
                    badge.className = "library-cover__page";
                    badge.textContent = `Page ${entry.page}`;
                    item.querySelector(".library-cover__art")?.append(badge);
                    const link = new URL(item.dataset.href, window.location.origin);
                    link.searchParams.set("page", String(entry.page));
                    item.dataset.href = link.pathname + link.search;
                }
            });
            shelf.hidden = false;
            updateShelfArrows();
            markSelected();
        } catch {
            // The shelf simply stays hidden.
        }
    }

    // SECTION: Rows and covers open the reader, or the reading pane on wide screens
    const interactive = "a, button, input, select, textarea, label, .dropdown-menu";

    results?.addEventListener("click", event => {
        const item = event.target.closest("[data-library-item]");
        if (!item || event.target.closest(interactive)) return;
        if (window.getSelection()?.toString()) return;
        if (wideQuery.matches) openPane(item.dataset.docId);
        else window.location.assign(item.dataset.href);
    });

    results?.addEventListener("click", event => {
        const open = event.target.closest("a[data-library-open]");
        if (!open || !wideQuery.matches || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
        event.preventDefault();
        openPane(open.closest("[data-library-item]").dataset.docId);
    });

    function showPanePlaceholder() {
        if (!pane || !wideQuery.matches || !results) return;
        destroyPaneViewer();
        pane.innerHTML = `<div class="library-pane__empty"><i class="bi bi-hand-index" aria-hidden="true"></i><p>Choose a document to read it here.</p><p class="library-pane__keys"><kbd>↑</kbd> <kbd>↓</kbd> move between documents · <kbd>Esc</kbd> closes</p></div>`;
        pane.hidden = false;
        body.classList.add("has-pane");
    }

    function rememberSelection(id) {
        const url = new URL(window.location.href);
        if (id) url.searchParams.set("open", id);
        else url.searchParams.delete("open");
        window.history.replaceState(window.history.state, "", url);
    }

    let previewRequest = null;
    let paneViewer = null;

    function destroyPaneViewer() {
        try { paneViewer?.destroy(); } catch { /* already gone */ }
        paneViewer = null;
    }

    async function openPane(id, { focusPreview = true } = {}) {
        if (!pane || !id) return;
        // Only the latest selection may fill the pane; a slower earlier response is dropped.
        previewRequest?.abort();
        previewRequest = new AbortController();
        const signal = previewRequest.signal;
        selectedId = id;
        markSelected();
        rememberSelection(id);
        const url = new URL(root.dataset.previewUrl, window.location.origin);
        url.searchParams.set("id", id);
        url.searchParams.set("returnUrl", window.location.pathname + window.location.search);
        const query = new URLSearchParams(window.location.search).get("q");
        if (query) url.searchParams.set("q", query);
        try {
            const response = await fetch(url, { credentials: "same-origin", signal });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const html = await response.text();
            if (signal.aborted || selectedId !== id) return;
            destroyPaneViewer();
            pane.innerHTML = html;
            pane.hidden = false;
            body.classList.add("has-pane");
            // Keyboard browsing keeps focus on the row link so the next arrow press still moves.
            if (focusPreview) pane.querySelector(".library-preview__title")?.focus({ preventScroll: true });
            mountPaneViewer(id, query);
        } catch (error) {
            if (error?.name === "AbortError" || selectedId !== id) return;
            const item = results.querySelector(`[data-doc-id="${CSS.escape(id)}"]`);
            if (item) window.location.assign(item.dataset.href);
            else { selectedId = null; rememberSelection(null); showPanePlaceholder(); }
        }
    }

    function mountPaneViewer(id, query) {
        const host = pane.querySelector("[data-preview-viewer]");
        if (!host || !root.dataset.viewerModule) return;
        import(root.dataset.viewerModule)
            .then(module => {
                if (selectedId !== id || !host.isConnected) return null;
                return module.mountViewer(host, {
                    url: host.dataset.pdfUrl,
                    libraryBase: root.dataset.pdfjsBase,
                    documentId: id,
                    userKey: root.dataset.userKey,
                    query: query || "",
                    compact: true
                });
            })
            .then(viewer => {
                if (!viewer) return;
                if (selectedId !== id || !host.isConnected) viewer.destroy();
                else paneViewer = viewer;
            })
            .catch(() => host.classList.add("is-failed"));
    }

    function syncPane(url) {
        if (!pane || !wideQuery.matches) return;
        const open = url.searchParams.get("open");
        if (open && open !== selectedId) openPane(open, { focusPreview: false });
        else if (!open && selectedId) {
            previewRequest?.abort();
            selectedId = null;
            markSelected();
            showPanePlaceholder();
        }
    }

    function closePane() {
        if (!pane || pane.hidden) return;
        const id = selectedId;
        destroyPaneViewer();
        pane.hidden = true;
        pane.innerHTML = "";
        body.classList.remove("has-pane");
        selectedId = null;
        markSelected();
        rememberSelection(null);
        showPanePlaceholder();
        results.querySelector(`[data-doc-id="${CSS.escape(id || "")}"] [data-library-open]`)?.focus();
    }

    function markSelected() {
        results?.querySelectorAll("[data-library-item]").forEach(item => {
            item.classList.toggle("is-selected", item.dataset.docId === selectedId);
        });
    }

    pane?.addEventListener("click", event => {
        if (event.target.closest("[data-library-pane-close]")) closePane();
    });

    wideQuery.addEventListener("change", event => {
        if (event.matches) showPanePlaceholder();
        else { closePane(); if (pane) { pane.hidden = true; body.classList.remove("has-pane"); } }
    });

    // A shared or reloaded link with ?open=<id> opens that document again.
    if (results && wideQuery.matches) {
        const open = new URLSearchParams(window.location.search).get("open");
        if (open) openPane(open);
        else showPanePlaceholder();
    }

    // SECTION: Keyboard: arrows move between documents, Escape closes the pane, "/" searches
    root.addEventListener("keydown", event => {
        if (!results) return;
        if (event.key === "Escape" && pane && !pane.hidden && selectedId && !document.querySelector(".modal.show")) {
            closePane();
            return;
        }
        const keys = ["ArrowDown", "ArrowUp", "ArrowLeft", "ArrowRight", "Home", "End"];
        if (!keys.includes(event.key)) return;
        // From a document link, or from inside the reading pane (a click focuses its heading),
        // moving on from the selected document. Form fields, the PDF and menus keep their keys.
        const inPane = pane && !pane.hidden && pane.contains(event.target)
            && !event.target.closest("input, textarea, select, .dropdown-menu, [data-viewer-stage]");
        const current = event.target.closest("[data-library-open]")
            ?? (inPane && selectedId ? results.querySelector(`[data-doc-id="${CSS.escape(selectedId)}"] [data-library-open]`) : null);
        if (!current) return;
        // Left and right only move along covers (a shelf or the Covers layout).
        const sideways = event.key === "ArrowLeft" || event.key === "ArrowRight";
        if (sideways && !current.closest(".library-cover")) return;
        const group = current.closest("[data-library-items]") || results;
        const links = [...group.querySelectorAll("[data-library-open]")];
        const index = links.indexOf(current);
        const forward = event.key === "ArrowDown" || event.key === "ArrowRight";
        const next = event.key === "Home" ? links[0]
            : event.key === "End" ? links[links.length - 1]
            : links[index + (forward ? 1 : -1)];
        if (!next) return;
        event.preventDefault();
        next.focus();
        next.closest("[data-library-item]")?.scrollIntoView({ block: "nearest", inline: "nearest" });
        if (wideQuery.matches && pane && !pane.hidden && selectedId) openPane(next.closest("[data-library-item]").dataset.docId, { focusPreview: false });
    });

    // SECTION: Quick search ("/" outside a field; Ctrl+K stays PRISM's own search)
    const quickElement = document.getElementById("libraryQuick");
    const quickInput = quickElement?.querySelector("#libraryQuickInput");
    const quickList = quickElement?.querySelector("#libraryQuickList");
    const quickHint = quickElement?.querySelector("[data-quick-hint]");
    const quickNone = quickElement?.querySelector("[data-quick-none]");
    const quickAll = quickElement?.querySelector("[data-quick-all]");
    let quickRequest = null;
    let quickTimer = null;
    let quickActive = -1;

    function openQuick(seed = "") {
        if (!quickElement || !window.bootstrap?.Modal || !root.dataset.quickUrl) {
            root.querySelector("#librarySearch")?.focus();
            return;
        }
        const modal = window.bootstrap.Modal.getOrCreateInstance(quickElement);
        quickElement.addEventListener("shown.bs.modal", () => {
            quickInput.focus();
            quickInput.select();
        }, { once: true });
        if (seed) quickInput.value = seed;
        modal.show();
        if (quickInput.value.trim()) runQuick();
    }

    function closeQuick() {
        if (!quickElement || !window.bootstrap?.Modal) return;
        window.bootstrap.Modal.getInstance(quickElement)?.hide();
    }

    document.addEventListener("keydown", event => {
        if (!quickElement) return;
        const inField = event.target.closest?.("input, textarea, select, [contenteditable='true']");
        if (event.key === "/" && !inField && !event.ctrlKey && !event.metaKey && !event.altKey && !document.querySelector(".modal.show")) {
            event.preventDefault();
            openQuick();
        }
    });

    root.addEventListener("click", event => {
        if (!event.target.closest("[data-quick-open]")) return;
        event.preventDefault();
        openQuick(root.querySelector("#librarySearch")?.value || "");
    });

    quickInput?.addEventListener("input", () => {
        window.clearTimeout(quickTimer);
        quickTimer = window.setTimeout(runQuick, 140);
    });

    quickInput?.addEventListener("keydown", event => {
        const options = [...quickList.querySelectorAll("[role='option']")];
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            if (!options.length) return;
            quickActive = (quickActive + (event.key === "ArrowDown" ? 1 : -1) + options.length) % options.length;
            markQuick(options);
        } else if (event.key === "Enter") {
            event.preventDefault();
            const chosen = options[quickActive];
            if (chosen) window.location.assign(chosen.dataset.href);
            else searchEverything();
        }
    });

    function markQuick(options) {
        options.forEach((option, index) => {
            const on = index === quickActive;
            option.classList.toggle("is-active", on);
            option.setAttribute("aria-selected", on ? "true" : "false");
            if (on) {
                quickInput.setAttribute("aria-activedescendant", option.id);
                option.scrollIntoView({ block: "nearest" });
            }
        });
        if (quickActive < 0) quickInput.removeAttribute("aria-activedescendant");
    }

    function searchEverything() {
        const text = quickInput.value.trim();
        const target = new URL(root.dataset.resultsUrl || root.dataset.quickUrl.split("?")[0], window.location.origin);
        target.search = "";
        if (text) target.searchParams.set("q", text);
        if (results) {
            closeQuick();
            const input = root.querySelector("#librarySearch");
            if (input) input.value = text;
            load(target.pathname + target.search);
        } else {
            window.location.assign(target.pathname + target.search);
        }
    }

    async function runQuick() {
        const text = quickInput.value.trim();
        quickRequest?.abort();
        quickActive = -1;
        if (!text) {
            quickList.replaceChildren();
            quickHint.hidden = false;
            quickNone.hidden = true;
            quickAll.hidden = true;
            quickInput.setAttribute("aria-expanded", "false");
            return;
        }
        quickRequest = new AbortController();
        const url = new URL(root.dataset.quickUrl, window.location.origin);
        url.searchParams.set("q", text);
        try {
            const response = await fetch(url, { credentials: "same-origin", signal: quickRequest.signal });
            if (!response.ok) return;
            const data = await response.json();
            if (quickInput.value.trim() !== text) return;
            renderQuick(data, text);
        } catch {
            // A superseded or failed lookup leaves the last results in place.
        }
    }

    function renderQuick(data, text) {
        quickHint.hidden = true;
        const items = Array.isArray(data.items) ? data.items : [];
        quickNone.hidden = items.length > 0;
        quickAll.hidden = false;
        quickAll.href = data.allUrl || "#";
        quickAll.firstChild.textContent = data.total > 0
            ? `See all ${data.more ? "100+" : data.total} results `
            : "Search the text of every PDF ";
        quickInput.setAttribute("aria-expanded", items.length ? "true" : "false");
        const words = text.toLowerCase().split(/\s+/).filter(word => word.length > 1 && !word.includes(":"));
        quickList.replaceChildren(...items.map((item, index) => {
            const option = document.createElement("li");
            option.id = `libraryQuickOption${index}`;
            option.setAttribute("role", "option");
            option.setAttribute("aria-selected", "false");
            option.className = `library-quick__item library-hue-${item.hue}`;
            option.dataset.href = item.url;

            const thumb = document.createElement("span");
            thumb.className = "library-thumb";
            thumb.setAttribute("aria-hidden", "true");
            const image = document.createElement("img");
            image.alt = "";
            image.loading = "lazy";
            image.dataset.cover = "";
            image.src = item.coverUrl;
            const icon = document.createElement("i");
            icon.className = "bi bi-file-earmark-text";
            thumb.append(image, icon);

            const textBox = document.createElement("span");
            textBox.className = "library-quick__text";
            const title = document.createElement("strong");
            title.append(...highlight(item.subject, words));
            const meta = document.createElement("span");
            meta.textContent = item.meta;
            textBox.append(title, meta);

            option.append(thumb, textBox);
            if (item.aots) {
                const pill = document.createElement("span");
                pill.className = "library-pill library-pill--aots";
                pill.textContent = "AOTS";
                option.append(pill);
            }
            option.addEventListener("click", () => window.location.assign(item.url));
            option.addEventListener("mousemove", () => {
                const options = [...quickList.querySelectorAll("[role='option']")];
                const position = options.indexOf(option);
                if (position !== quickActive) { quickActive = position; markQuick(options); }
            });
            return option;
        }));
    }

    // Wraps the typed words in <mark>, building text nodes only (titles are never parsed as HTML).
    function highlight(text, words) {
        if (!words.length) return [document.createTextNode(text)];
        const pattern = new RegExp(`(${words.map(word => word.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("|")})`, "gi");
        return text.split(pattern).filter(Boolean).map(part => {
            if (words.includes(part.toLowerCase())) {
                const mark = document.createElement("mark");
                mark.textContent = part;
                return mark;
            }
            return document.createTextNode(part);
        });
    }

    quickAll?.addEventListener("click", event => {
        event.preventDefault();
        searchEverything();
    });

    quickElement?.addEventListener("hidden.bs.modal", () => {
        quickRequest?.abort();
        quickActive = -1;
    });

    // SECTION: Favourites
    root.addEventListener("click", async event => {
        const button = event.target.closest("[data-library-star]");
        if (!button) return;
        event.preventDefault();
        event.stopPropagation();
        const id = button.dataset.docId;
        const wasOn = button.getAttribute("aria-pressed") === "true";
        setStar(id, !wasOn);
        try {
            const url = new URL(root.dataset.favouriteUrl, window.location.origin);
            url.searchParams.set("id", id);
            const response = await fetch(url, {
                method: "POST",
                credentials: "same-origin",
                headers: { "X-CSRF-TOKEN": token }
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const data = await response.json();
            setStar(id, Boolean(data.isFavourite));
            updateFavouriteCount(Boolean(data.isFavourite) === wasOn ? 0 : (data.isFavourite ? 1 : -1));
            // In Favourites, an unstarred document leaves the list; reloading also fixes the
            // count, the paging and the empty state.
            if (results && wasOn && !data.isFavourite && new URLSearchParams(window.location.search).get("scope") === "favourites") {
                load(window.location.href, { push: false });
            }
        } catch {
            setStar(id, wasOn);
            toast("The favourite could not be saved. Try again.", "danger");
        }
    });

    function setStar(id, on) {
        root.querySelectorAll(`[data-library-star][data-doc-id="${CSS.escape(id)}"]`).forEach(button => {
            button.setAttribute("aria-pressed", on ? "true" : "false");
            button.classList.toggle("is-on", on);
            const icon = button.querySelector("i");
            icon?.classList.toggle("bi-star-fill", on);
            icon?.classList.toggle("bi-star", !on);
        });
    }

    function updateFavouriteCount(delta) {
        if (!delta) return;
        root.querySelectorAll('[data-library-scope="favourites"] .library-tabs__count').forEach(count => {
            count.textContent = String(Math.max(0, (parseInt(count.textContent, 10) || 0) + delta));
        });
    }

    // SECTION: Deactivate, activate, read again and request deletion (confirmed in a dialog)
    const dialogElement = document.getElementById("libraryConfirm");
    const dialogForm = dialogElement?.querySelector("[data-library-dialog]");
    const actions = {
        "deactivate": {
            handler: "Deactivate",
            title: "Deactivate this document?",
            body: "It disappears from the library for everyone except the people who manage it. You can activate it again at any time.",
            confirm: "Deactivate",
            style: "btn-outline-danger",
            focusCancel: true
        },
        "activate": {
            handler: "Activate",
            title: "Activate this document again?",
            body: "Everyone will be able to find and open it again.",
            confirm: "Activate",
            style: "btn-primary"
        },
        "retry-ocr": {
            handler: "RetryOcr",
            title: "Read this PDF's text again?",
            body: "The document goes back into the reading queue. Its contents become searchable once reading succeeds.",
            confirm: "Read again",
            style: "btn-primary"
        },
        "request-delete": {
            handler: "RequestDelete",
            title: "Request deletion",
            body: "An approver decides in the Decision Centre. Until then the document stays in the library.",
            confirm: "Send request",
            style: "btn-danger",
            reason: true
        }
    };

    root.addEventListener("click", event => {
        const trigger = event.target.closest("[data-library-action]");
        if (!trigger || !dialogForm || !window.bootstrap?.Modal) return;
        const action = actions[trigger.dataset.libraryAction];
        if (!action) return;
        event.preventDefault();

        // The link is the document's Manage page (/Manage/{id}); post the chosen handler to it.
        const url = new URL(trigger.getAttribute("href"), window.location.origin);
        url.searchParams.set("handler", action.handler);
        url.searchParams.set("returnUrl", trigger.dataset.returnUrl || window.location.pathname + window.location.search);
        dialogForm.action = url.pathname + url.search;
        dialogForm.querySelector("[data-dialog-title]").textContent = action.title;
        dialogForm.querySelector("[data-dialog-subject]").textContent = trigger.dataset.docSubject || "";
        dialogForm.querySelector("[data-dialog-body]").textContent = action.body;
        const confirm = dialogForm.querySelector("[data-dialog-confirm]");
        confirm.textContent = action.confirm;
        confirm.className = `btn ${action.style}`;
        const reason = dialogForm.querySelector("[data-dialog-reason]");
        const textarea = reason.querySelector("textarea");
        reason.hidden = !action.reason;
        textarea.required = Boolean(action.reason);
        textarea.value = "";

        // Focus returns to the ⋮ button that opened the menu (the menu item itself is hidden by then).
        const opener = trigger.closest(".dropdown")?.querySelector('[data-bs-toggle="dropdown"]') || trigger;
        const cancel = dialogForm.querySelector('[data-bs-dismiss="modal"].btn');
        const modal = window.bootstrap.Modal.getOrCreateInstance(dialogElement);
        dialogElement.addEventListener("shown.bs.modal", () => (action.reason ? textarea : action.focusCancel ? cancel : confirm)?.focus(), { once: true });
        dialogElement.addEventListener("hidden.bs.modal", () => opener?.focus(), { once: true });
        modal.show();
    });

    // SECTION: Phones: filters fold behind "Filters", and the search box gets a shorter hint
    function foldFilters() {
        const box = results?.querySelector("[data-library-filterbox]");
        if (!box) return;
        box.open = !narrowQuery.matches || box.dataset.userOpened === "true";
        box.addEventListener("toggle", () => { if (narrowQuery.matches) box.dataset.userOpened = String(box.open); }, { once: true });
    }

    function fitPlaceholder() {
        const input = searchForm?.querySelector("input[data-short-placeholder]");
        if (!input) return;
        input.dataset.longPlaceholder ||= input.placeholder;
        input.placeholder = narrowQuery.matches ? input.dataset.shortPlaceholder : input.dataset.longPlaceholder;
    }

    function enhanceRegion() {
        foldFilters();
        updateShelfArrows();
        fillContinueReading();
    }

    enhanceRegion();
    fitPlaceholder();
    narrowQuery.addEventListener("change", () => { foldFilters(); fitPlaceholder(); });
    window.addEventListener("resize", updateShelfArrows, { passive: true });

    // SECTION: Messages
    function toast(message, tone = "info") {
        if (typeof window.showToast === "function") {
            window.showToast(message, tone);
            return;
        }
        const live = document.createElement("div");
        live.className = `alert alert-${tone} position-fixed bottom-0 end-0 m-3 shadow`;
        live.setAttribute("role", "status");
        live.textContent = message;
        document.body.appendChild(live);
        window.setTimeout(() => live.remove(), 4000);
    }
})();
