(() => {
    "use strict";

    const root = document.querySelector("[data-arpp-library]");
    if (!root) return;

    const toggle = root.querySelector("[data-arpp-library-rail-toggle]");
    const rail = root.querySelector("[data-arpp-library-rail]");
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
