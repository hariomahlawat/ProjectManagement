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
        // The page's own box first (Home's finder, Every admin page's filter), then the sidebar's.
        const search = Array.from(document.querySelectorAll("[data-admin-home-search]")).find(isVisible)
            ?? Array.from(document.querySelectorAll("[data-admin-index-search]")).find(isVisible)
            ?? Array.from(document.querySelectorAll("[data-admin-sidebar-search]")).find(isVisible);
        if (!search) return;
        event.preventDefault();
        search.focus();
        search.select();
    }, true);

    // Home: "Find a page or setting" shows the best matches first. A word in a page's name counts
    // most, then its search words, then its description; every word must match somewhere.
    const scoreEntry = (entry, words) => {
        let total = 0;
        for (const word of words) {
            const score = (entry.dataset.title ?? "").includes(word) ? 3
                : (entry.dataset.keywords ?? "").includes(word) ? 2
                : (entry.dataset.label ?? "").includes(word) ? 1
                : 0;
            if (score === 0) return 0;
            total += score;
        }
        return total;
    };

    document.querySelectorAll("[data-admin-home-find]").forEach(find => {
        const input = find.querySelector("[data-admin-home-search]");
        const results = find.querySelector("[data-admin-home-find-results]");
        const empty = find.querySelector("[data-admin-home-find-empty]");
        const status = find.querySelector("[data-admin-home-find-status]");
        const entries = Array.from(find.querySelectorAll("[data-admin-home-find-entry]"));
        const maximum = 7;
        if (!input || !results) return;

        const shownLinks = () => Array.from(results.querySelectorAll("[data-admin-home-find-entry]:not([hidden]) a"));

        const apply = () => {
            const query = normalise(input.value);
            const words = query.length === 0 ? [] : query.split(/\s+/);
            const ranked = words.length === 0 ? [] : entries
                .map((entry, order) => ({ entry, order, score: scoreEntry(entry, words) }))
                .filter(item => item.score > 0)
                .sort((a, b) => b.score - a.score || a.order - b.order)
                .slice(0, maximum);

            entries.forEach(entry => { entry.hidden = true; });
            ranked.forEach(item => {
                item.entry.hidden = false;
                results.appendChild(item.entry);
            });

            results.hidden = ranked.length === 0;
            input.setAttribute("aria-expanded", String(ranked.length > 0));
            if (empty) empty.hidden = words.length === 0 || ranked.length > 0;
            if (status) {
                status.textContent = words.length === 0 ? "" : ranked.length === 0 ? "Nothing matches." : `${pageWord(ranked.length)} found.`;
            }
        };

        const reset = () => {
            input.value = "";
            apply();
            input.focus();
        };

        input.addEventListener("input", apply);
        input.addEventListener("keydown", event => {
            const links = shownLinks();
            if (event.key === "ArrowDown" && links.length > 0) {
                event.preventDefault();
                links[0].focus();
            } else if (event.key === "Enter" && links.length > 0) {
                event.preventDefault();
                links[0].click();
            } else if (event.key === "Escape" && input.value.length > 0) {
                event.preventDefault();
                reset();
            }
        });
        results.addEventListener("keydown", event => {
            const links = shownLinks();
            const index = links.indexOf(event.target);
            if (index < 0) return;
            if (event.key === "ArrowDown") {
                event.preventDefault();
                links[Math.min(index + 1, links.length - 1)].focus();
            } else if (event.key === "ArrowUp") {
                event.preventDefault();
                (index === 0 ? input : links[index - 1]).focus();
            } else if (event.key === "Escape") {
                event.preventDefault();
                reset();
            }
        });
        find.querySelectorAll("[data-admin-home-try]").forEach(button => {
            button.addEventListener("click", () => {
                input.value = button.dataset.adminHomeTry ?? "";
                apply();
                input.focus();
            });
        });
        input.setAttribute("aria-expanded", "false");
    });

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

    // Restore deleted items on a phone: the tab strip scrolls sideways, so bring the open tab into
    // view. Only the strip moves; "nearest" leaves the page where it is (docs/ux/admin-review-3-fixes.md, PR 8).
    document.querySelectorAll("[data-admin-restore-tabs]").forEach(strip => {
        if (strip.scrollWidth <= strip.clientWidth) return;
        const active = strip.querySelector("[aria-current='page']");
        if (active && typeof active.scrollIntoView === "function") {
            active.scrollIntoView({ inline: "center", block: "nearest" });
        }
    });

    document.querySelectorAll("[data-admin-controlled-submit]").forEach(form => {
        form.addEventListener("submit", () => {
            form.querySelectorAll("button[type='submit']").forEach(button => {
                button.disabled = true;
                button.setAttribute("aria-busy", "true");
            });
        });
    });
})();
