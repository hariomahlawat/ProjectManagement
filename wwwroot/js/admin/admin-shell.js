(() => {
    "use strict";

    const roots = document.querySelectorAll(".admin-sidebar, .admin-sidebar-offcanvas__body");
    const offcanvasElement = document.getElementById("adminSidebarOffcanvas");
    const storageKey = "prism.admin.sidebar.groups.v3";

    const readState = () => {
        try {
            const parsed = JSON.parse(window.sessionStorage.getItem(storageKey) ?? "{}");
            return parsed && typeof parsed === "object" ? parsed : {};
        } catch {
            return {};
        }
    };

    const writeState = state => {
        try {
            window.sessionStorage.setItem(storageKey, JSON.stringify(state));
        } catch {
            // Session storage may be unavailable. Native details behaviour remains functional.
        }
    };

    const normalise = value => (value ?? "").trim().toLocaleLowerCase();

    // Every word typed must appear somewhere in the label (name, search words, description), so
    // "chat files" finds "Chat file storage" and "restore project" finds "Restore deleted items".
    const matchesQuery = (label, query) => {
        if (query.length === 0) return true;
        const haystack = normalise(label);
        return query.split(/\s+/).every(word => haystack.includes(word));
    };

    // A closed Bootstrap drawer keeps its layout box but is visibility:hidden, so that is checked too.
    const isVisible = element =>
        element instanceof HTMLElement
        && !element.hidden
        && !element.closest("[hidden]")
        && !element.closest(".offcanvas:not(.show):not(.showing)")
        && element.getClientRects().length > 0
        && window.getComputedStyle(element).visibility !== "hidden";

    const isEditable = target =>
        target instanceof Element
        && Boolean(target.closest("input, textarea, select, [contenteditable=''], [contenteditable='true']"));

    const pageWord = count => (count === 1 ? "1 page" : `${count} pages`);

    roots.forEach(root => {
        const nav = root.querySelector("[data-admin-sidebar-nav]");
        const search = root.querySelector("[data-admin-sidebar-search]");
        const clear = root.querySelector("[data-admin-sidebar-search-clear]");
        const noResults = root.querySelector("[data-admin-sidebar-no-results]");
        const groups = Array.from(root.querySelectorAll("[data-admin-sidebar-group]"));
        const searchOnly = root.querySelector("[data-admin-sidebar-search-only]");
        const searchOnlyLinks = Array.from(root.querySelectorAll("[data-admin-sidebar-search-only-link]"));
        const status = root.querySelector("[data-admin-sidebar-search-status]");
        const savedState = readState();

        groups.forEach(group => {
            const key = group.dataset.groupKey;
            const isActive = group.dataset.activeGroup === "true";
            if (isActive) {
                group.open = true;
            } else if (key && Object.prototype.hasOwnProperty.call(savedState, key)) {
                group.open = Boolean(savedState[key]);
            }

            group.addEventListener("toggle", () => {
                if (!key || group.dataset.activeGroup === "true") {
                    return;
                }
                const state = readState();
                state[key] = group.open;
                writeState(state);
            });
        });

        const activeLink = nav?.querySelector(".admin-sidebar__item.is-active");
        if (activeLink instanceof HTMLElement && nav instanceof HTMLElement) {
            window.requestAnimationFrame(() => {
                const navRect = nav.getBoundingClientRect();
                const linkRect = activeLink.getBoundingClientRect();
                const isOutside = linkRect.top < navRect.top || linkRect.bottom > navRect.bottom;
                if (isOutside) {
                    nav.scrollTop += linkRect.top - navRect.top - Math.max(0, (nav.clientHeight - linkRect.height) / 2);
                }
            });
        }

        const applySearch = () => {
            const query = normalise(search?.value);
            let visibleCount = 0;

            groups.forEach(group => {
                const links = Array.from(group.querySelectorAll("[data-admin-sidebar-label]"));
                let groupMatches = 0;
                links.forEach(link => {
                    const matches = matchesQuery(link.dataset.adminSidebarLabel, query);
                    link.hidden = !matches;
                    if (matches) {
                        groupMatches += 1;
                        visibleCount += 1;
                    }
                });
                group.hidden = groupMatches === 0;
                if (query.length > 0 && groupMatches > 0) {
                    group.open = true;
                }
            });

            // Pages that open from a hub appear only while searching.
            let extraMatches = 0;
            searchOnlyLinks.forEach(link => {
                const matches = query.length > 0 && matchesQuery(link.dataset.adminSidebarLabel, query);
                link.hidden = !matches;
                if (matches) {
                    extraMatches += 1;
                }
            });
            if (searchOnly) {
                searchOnly.hidden = extraMatches === 0;
            }
            visibleCount += extraMatches;

            if (clear) {
                clear.hidden = query.length === 0;
            }
            if (noResults) {
                noResults.hidden = visibleCount > 0;
            }
            if (status) {
                status.textContent = query.length === 0 ? "" : visibleCount === 0 ? "Nothing matches." : `${pageWord(visibleCount)} match.`;
            }
        };

        // The links a keyboard user moves through: whatever the filter left visible.
        const visibleLinks = () => Array.from(nav?.querySelectorAll("a.admin-sidebar__item") ?? [])
            .filter(link => !link.hidden && !link.closest("[hidden]") && !link.closest("details:not([open])"));

        const clearSearch = () => {
            if (!search) return;
            search.value = "";
            applySearch();
        };

        search?.addEventListener("input", applySearch);
        search?.addEventListener("keydown", event => {
            if (event.key === "ArrowDown") {
                const first = visibleLinks()[0];
                if (first) {
                    event.preventDefault();
                    first.focus();
                }
            } else if (event.key === "Enter") {
                // Enter opens the first match, the way a search box is expected to behave.
                const first = search.value.trim().length > 0 ? visibleLinks()[0] : null;
                if (first) {
                    event.preventDefault();
                    first.click();
                }
            } else if (event.key === "Escape" && search.value.length > 0) {
                event.preventDefault();
                clearSearch();
            }
        });

        nav?.addEventListener("keydown", event => {
            if (event.key !== "ArrowDown" && event.key !== "ArrowUp" && event.key !== "Escape") return;
            const links = visibleLinks();
            const index = links.indexOf(event.target);
            if (index < 0) return;
            event.preventDefault();
            if (event.key === "Escape" || (event.key === "ArrowUp" && index === 0)) {
                search?.focus();
            } else {
                links[event.key === "ArrowDown" ? Math.min(index + 1, links.length - 1) : index - 1]?.focus();
            }
        });

        clear?.addEventListener("click", () => {
            if (!search) return;
            clearSearch();
            search.focus();
        });
    });

    // "/" from anywhere on an Administration page goes to "Find a page or setting" (Ctrl+K still
    // opens the app-wide search). Capture phase, so it runs before the app-wide "/" handler.
    document.addEventListener("keydown", event => {
        if (event.key !== "/" || event.defaultPrevented || event.isComposing
            || event.ctrlKey || event.metaKey || event.altKey || isEditable(event.target)
            || document.querySelector(".modal.show")) {
            return;
        }
        const search = Array.from(document.querySelectorAll("[data-admin-sidebar-search]")).find(isVisible);
        if (!search) return;
        event.preventDefault();
        search.focus();
        search.select();
    }, true);

    // "Every admin page": the same matching narrows the full list.
    document.querySelectorAll("[data-admin-index]").forEach(index => {
        const search = index.querySelector("[data-admin-index-search]");
        const count = index.querySelector("[data-admin-index-count]");
        const empty = index.querySelector("[data-admin-index-empty]");
        const groups = Array.from(index.querySelectorAll("[data-admin-index-group]"));
        const total = Number(count?.dataset.total ?? 0);

        const apply = () => {
            const query = normalise(search?.value);
            let shown = 0;
            groups.forEach(group => {
                let inGroup = 0;
                group.querySelectorAll("[data-admin-index-item]").forEach(item => {
                    const matches = matchesQuery(item.dataset.adminIndexLabel, query);
                    item.hidden = !matches;
                    if (matches) inGroup += 1;
                });
                group.hidden = inGroup === 0;
                shown += inGroup;
            });
            if (empty) empty.hidden = shown > 0;
            if (count) count.textContent = query.length === 0 ? pageWord(total) : `${shown} of ${pageWord(total)}`;
        };

        search?.addEventListener("input", apply);
        search?.addEventListener("keydown", event => {
            if (event.key === "Escape" && search.value.length > 0) {
                event.preventDefault();
                search.value = "";
                apply();
            } else if (event.key === "Enter") {
                const first = index.querySelector("[data-admin-index-item]:not([hidden]) a");
                if (first && search.value.trim().length > 0) {
                    event.preventDefault();
                    first.click();
                }
            }
        });
    });

    if (offcanvasElement && window.bootstrap?.Offcanvas) {
        offcanvasElement.querySelectorAll("[data-admin-sidebar-link]").forEach(link => {
            link.addEventListener("click", () => {
                window.bootstrap.Offcanvas.getInstance(offcanvasElement)?.hide();
            });
        });
    }

    document.querySelectorAll("[data-admin-controlled-submit]").forEach(form => {
        form.addEventListener("submit", () => {
            form.querySelectorAll("button[type='submit']").forEach(button => {
                button.disabled = true;
                button.setAttribute("aria-busy", "true");
            });
        });
    });
})();
