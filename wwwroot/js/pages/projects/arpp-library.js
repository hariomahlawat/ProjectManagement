(() => {
    "use strict";

    const root = document.querySelector("[data-arpp-library]");
    if (!root) return;

    const readerStart = root.querySelector("[data-arpp-library-reader-start]");
    const rail = root.querySelector("[data-arpp-library-rail]");
    const readerScrollKey = "prism:projects:arpp:reader-focus";
    const yearStatePrefix = "prism:projects:arpp:year:";

    const cssPixels = (name, fallback) => {
        const raw = getComputedStyle(document.documentElement).getPropertyValue(name);
        const parsed = Number.parseFloat(raw);
        return Number.isFinite(parsed) ? parsed : fallback;
    };

    const stickyOffset = () =>
        cssPixels("--pm-topbar-height", 64) +
        cssPixels("--pm-module-subnav-height", 54) +
        16;

    const scrollWithOffset = (element, { focus = false } = {}) => {
        if (!(element instanceof HTMLElement)) return;

        const top = Math.max(
            0,
            window.scrollY + element.getBoundingClientRect().top - stickyOffset()
        );

        window.scrollTo({ top, behavior: "auto" });

        if (focus) {
            window.requestAnimationFrame(() => element.focus({ preventScroll: true }));
        }
    };

    const markReaderNavigation = () => {
        try {
            sessionStorage.setItem(readerScrollKey, "1");
        } catch {
            // Storage is an enhancement only; navigation must remain functional.
        }
    };

    root.querySelectorAll("[data-arpp-library-reader-link]").forEach(link => {
        link.addEventListener("click", markReaderNavigation);
    });

    root.querySelector("[data-arpp-library-search-form]")
        ?.addEventListener("submit", markReaderNavigation);

    const restoreReaderPosition = () => {
        let shouldFocusReader = false;
        try {
            shouldFocusReader = sessionStorage.getItem(readerScrollKey) === "1";
            if (shouldFocusReader) {
                sessionStorage.removeItem(readerScrollKey);
            }
        } catch {
            shouldFocusReader = false;
        }

        const hashTarget = window.location.hash
            ? document.getElementById(decodeURIComponent(window.location.hash.slice(1)))
            : null;

        if (hashTarget instanceof HTMLElement) {
            window.requestAnimationFrame(() => scrollWithOffset(hashTarget));
            return;
        }

        if (shouldFocusReader && readerStart instanceof HTMLElement) {
            window.requestAnimationFrame(() => scrollWithOffset(readerStart, { focus: true }));
        }
    };

    const configureYearGroups = () => {
        const queryValue = new URLSearchParams(window.location.search).get("Query") ?? "";
        const hasQuery = queryValue.trim().length > 0;

        root.querySelectorAll("[data-arpp-library-year]").forEach(group => {
            if (!(group instanceof HTMLDetailsElement)) return;

            const year = group.dataset.arppLibraryYear;
            const isActive = group.dataset.arppLibraryYearActive === "true";
            const storageKey = `${yearStatePrefix}${year ?? "unknown"}`;

            if (!hasQuery && !isActive) {
                try {
                    const saved = localStorage.getItem(storageKey);
                    if (saved === "open") group.open = true;
                    if (saved === "closed") group.open = false;
                } catch {
                    // The server-provided default remains authoritative.
                }
            }

            if (isActive || hasQuery) {
                group.open = true;
            }

            group.addEventListener("toggle", () => {
                if (hasQuery || isActive) return;

                try {
                    localStorage.setItem(storageKey, group.open ? "open" : "closed");
                } catch {
                    // Persistence is optional.
                }
            });
        });
    };

    const keepActiveDocumentVisible = () => {
        if (!(rail instanceof HTMLElement)) return;

        const active = rail.querySelector(".project-arpp-nav__item.is-active");
        if (!(active instanceof HTMLElement)) return;

        window.requestAnimationFrame(() => {
            const railRect = rail.getBoundingClientRect();
            const activeRect = active.getBoundingClientRect();

            if (activeRect.top < railRect.top || activeRect.bottom > railRect.bottom) {
                const nextTop = active.offsetTop - Math.max(16, (rail.clientHeight - active.offsetHeight) / 2);
                rail.scrollTo({ top: Math.max(0, nextTop), behavior: "auto" });
            }
        });
    };

    configureYearGroups();
    keepActiveDocumentVisible();
    restoreReaderPosition();

    const toggle = root.querySelector("[data-arpp-library-rail-toggle]");
    const closeButton = root.querySelector("[data-arpp-library-rail-close]");
    const backdrop = root.querySelector("[data-arpp-library-rail-backdrop]");

    if (!toggle || !rail || !backdrop) return;

    const desktopQuery = window.matchMedia("(min-width: 992px)");
    let previouslyFocused = null;

    const focusableSelector = [
        "a[href]",
        "button:not([disabled])",
        "input:not([disabled])",
        "select:not([disabled])",
        "textarea:not([disabled])",
        "[tabindex]:not([tabindex='-1'])"
    ].join(",");

    const setRailAccessibility = isOpen => {
        if (desktopQuery.matches) {
            rail.removeAttribute("aria-hidden");
            rail.inert = false;
            return;
        }

        rail.setAttribute("aria-hidden", isOpen ? "false" : "true");
        rail.inert = !isOpen;
    };

    const closeRail = ({ restoreFocus = true } = {}) => {
        rail.classList.remove("is-open");
        backdrop.classList.remove("is-open");
        document.body.classList.remove("arpp-library-rail-open");
        toggle.setAttribute("aria-expanded", "false");
        setRailAccessibility(false);

        if (restoreFocus && previouslyFocused instanceof HTMLElement) {
            previouslyFocused.focus({ preventScroll: true });
        }

        previouslyFocused = null;
    };

    const openRail = () => {
        previouslyFocused = document.activeElement instanceof HTMLElement
            ? document.activeElement
            : toggle;

        rail.classList.add("is-open");
        backdrop.classList.add("is-open");
        document.body.classList.add("arpp-library-rail-open");
        toggle.setAttribute("aria-expanded", "true");
        setRailAccessibility(true);

        const firstTarget = rail.querySelector("input[type='search']") ??
            rail.querySelector(focusableSelector);
        window.setTimeout(() => firstTarget?.focus({ preventScroll: true }), 0);
    };

    toggle.addEventListener("click", () => {
        if (rail.classList.contains("is-open")) {
            closeRail();
        } else {
            openRail();
        }
    });

    closeButton?.addEventListener("click", () => closeRail());
    backdrop.addEventListener("click", () => closeRail());

    rail.addEventListener("click", event => {
        if (event.target.closest("a") && !desktopQuery.matches) {
            closeRail({ restoreFocus: false });
        }
    });

    document.addEventListener("keydown", event => {
        if (!rail.classList.contains("is-open")) return;

        if (event.key === "Escape") {
            event.preventDefault();
            closeRail();
            return;
        }

        if (event.key !== "Tab" || desktopQuery.matches) return;

        const focusable = Array.from(rail.querySelectorAll(focusableSelector))
            .filter(element => element instanceof HTMLElement && !element.hidden);
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
    });

    desktopQuery.addEventListener("change", event => {
        if (event.matches) {
            closeRail({ restoreFocus: false });
            setRailAccessibility(false);
        } else {
            setRailAccessibility(rail.classList.contains("is-open"));
        }
    });

    setRailAccessibility(false);
})();
