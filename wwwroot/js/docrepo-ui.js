// Document Repository pages with a [data-library] root: the list page (results, filters, reading
// pane) and the reader (favourite and manage dialog).
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
    let controller = null;
    let selectedId = null;

    // SECTION: Fetch and swap the results region
    // The filters form has no action, so form.action carries the current query; build the URL
    // from the path alone and replace its query.
    function filterUrl(form) {
        const url = new URL(form.getAttribute("action") || window.location.pathname, window.location.origin);
        url.search = currentQuery(form).toString();
        return url.pathname + url.search;
    }

    function currentQuery(form) {
        const params = new URLSearchParams();
        new FormData(form).forEach((value, key) => {
            if (String(value) !== "") params.append(key, String(value));
        });
        // The default order is implied, so shared links stay short.
        const sort = form.elements.namedItem("sort");
        if (sort && sort.value === sort.dataset.default) params.delete("sort");
        return params;
    }

    async function load(url, { push = true, focusName = null } = {}) {
        const target = new URL(url, window.location.origin);
        target.searchParams.delete("partial");
        const request = new URL(target);
        request.searchParams.set("partial", "true");

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
            results.innerHTML = await response.text();
            if (push) window.history.pushState({ library: true }, "", target);
            syncSearchForm(target);
            syncTabs(target);
            foldFilters();
            markSelected();
            if (focusName) results.querySelector(`[name="${focusName}"]`)?.focus();
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

    function syncTabs(url) {
        root.querySelectorAll("[data-library-view]").forEach(link => {
            const next = new URL(url);
            next.searchParams.delete("page");
            next.searchParams.delete("open");
            if (link.dataset.libraryView === "list") next.searchParams.delete("view");
            else next.searchParams.set("view", link.dataset.libraryView);
            link.href = next.pathname + next.search;
        });
    }

    // SECTION: Filters change instantly; the tag box waits for a pause in typing
    let tagTimer = null;
    results?.addEventListener("change", event => {
        const field = event.target.closest("[data-library-filter]");
        if (!field) return;
        const form = field.form;
        load(filterUrl(form), { focusName: field.name });
    });

    results?.addEventListener("input", event => {
        const field = event.target.closest("[data-library-filter-text]");
        if (!field) return;
        window.clearTimeout(tagTimer);
        tagTimer = window.setTimeout(() => {
            const form = field.form;
            load(filterUrl(form), { focusName: field.name });
        }, 450);
    });

    results?.addEventListener("submit", event => {
        const form = event.target.closest("[data-library-filters]");
        if (!form) return;
        event.preventDefault();
        load(filterUrl(form));
    });

    searchForm?.addEventListener("submit", event => {
        event.preventDefault();
        const params = currentQuery(searchForm);
        // Keep the current filters with the new search words.
        const current = new URLSearchParams(window.location.search);
        ["officeCategoryId", "documentCategoryId", "year", "tag", "includeInactive", "sort"].forEach(key => {
            if (current.get(key)) params.set(key, current.get(key));
        });
        if (!params.get("q")) params.delete("sort");
        load(`${window.location.pathname}?${params}`);
    });

    // SECTION: Links inside the region (chips, pages, tags) load in place
    results?.addEventListener("click", event => {
        const link = event.target.closest("a[data-library-link]");
        if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
        event.preventDefault();
        load(link.href);
        if (link.closest(".library-pager")) results.scrollIntoView({ behavior: "smooth", block: "start" });
    });

    if (results) window.addEventListener("popstate", () => load(window.location.href, { push: false }));

    // SECTION: Rows open the reader, or the reading pane on wide screens
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
        pane.innerHTML = `<div class="library-pane__empty"><i class="bi bi-file-earmark-text" aria-hidden="true"></i><p>Choose a document to preview it here.</p><p class="library-pane__keys">Use ↑ and ↓ to move between documents.</p></div>`;
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

    async function openPane(id) {
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
        try {
            const response = await fetch(url, { credentials: "same-origin", signal });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const html = await response.text();
            if (signal.aborted || selectedId !== id) return;
            pane.innerHTML = html;
            pane.hidden = false;
            body.classList.add("has-pane");
            pane.querySelector(".library-preview__title")?.focus({ preventScroll: true });
        } catch (error) {
            if (error?.name === "AbortError" || selectedId !== id) return;
            const item = results.querySelector(`[data-doc-id="${CSS.escape(id)}"]`);
            if (item) window.location.assign(item.dataset.href);
            else { selectedId = null; rememberSelection(null); showPanePlaceholder(); }
        }
    }

    function closePane() {
        if (!pane || pane.hidden) return;
        const id = selectedId;
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
        results.querySelectorAll("[data-library-item]").forEach(item => {
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

    // SECTION: Keyboard: arrows move between documents, Escape closes the pane
    root.addEventListener("keydown", event => {
        if (event.key === "Escape" && pane && !pane.hidden && !document.querySelector(".modal.show")) {
            closePane();
            return;
        }
        if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
        const current = event.target.closest("[data-library-open]");
        if (!current) return;
        const links = [...results.querySelectorAll("[data-library-open]")];
        const index = links.indexOf(current);
        const next = event.key === "Home" ? links[0]
            : event.key === "End" ? links[links.length - 1]
            : links[index + (event.key === "ArrowDown" ? 1 : -1)];
        if (!next) return;
        event.preventDefault();
        next.focus();
        if (wideQuery.matches && pane && !pane.hidden) openPane(next.closest("[data-library-item]").dataset.docId);
    });

    // SECTION: Favourites
    root.addEventListener("click", async event => {
        const button = event.target.closest("[data-library-star]");
        if (!button) return;
        event.preventDefault();
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
        const hasActive = /\(\d+\)/.test(box.querySelector("summary")?.textContent || "");
        box.open = !narrowQuery.matches || box.dataset.userOpened === "true" || false;
        box.addEventListener("toggle", () => { if (narrowQuery.matches) box.dataset.userOpened = String(box.open); }, { once: true });
        box.classList.toggle("has-active", hasActive);
    }

    function fitPlaceholder() {
        const input = searchForm?.querySelector("input[data-short-placeholder]");
        if (!input) return;
        input.dataset.longPlaceholder ||= input.placeholder;
        input.placeholder = narrowQuery.matches ? input.dataset.shortPlaceholder : input.dataset.longPlaceholder;
    }

    foldFilters();
    fitPlaceholder();
    narrowQuery.addEventListener("change", () => { foldFilters(); fitPlaceholder(); });

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
