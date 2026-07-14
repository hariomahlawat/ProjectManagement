(() => {
    "use strict";

    const roots = document.querySelectorAll(".admin-sidebar, .admin-sidebar-offcanvas__body");
    const offcanvasElement = document.getElementById("adminSidebarOffcanvas");
    const storageKey = "prism.admin.sidebar.groups.v2";

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

    roots.forEach(root => {
        const nav = root.querySelector("[data-admin-sidebar-nav]");
        const search = root.querySelector("[data-admin-sidebar-search]");
        const clear = root.querySelector("[data-admin-sidebar-search-clear]");
        const noResults = root.querySelector("[data-admin-sidebar-no-results]");
        const groups = Array.from(root.querySelectorAll("[data-admin-sidebar-group]"));
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
                    const matches = query.length === 0 || normalise(link.dataset.adminSidebarLabel).includes(query);
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

            if (clear) {
                clear.hidden = query.length === 0;
            }
            if (noResults) {
                noResults.hidden = visibleCount > 0;
            }
        };

        search?.addEventListener("input", applySearch);
        clear?.addEventListener("click", () => {
            if (!search) return;
            search.value = "";
            applySearch();
            search.focus();
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
