(() => {
    "use strict";

    const desktopSidebar = document.querySelector(".admin-sidebar");
    const offcanvasElement = document.getElementById("adminSidebarOffcanvas");

    if (!desktopSidebar && !offcanvasElement) {
        return;
    }

    const storageKey = "prism.admin.sidebar.scrollTop";

    const readStoredScrollPosition = () => {
        try {
            const value = Number.parseInt(window.sessionStorage.getItem(storageKey) ?? "", 10);
            return Number.isFinite(value) && value >= 0 ? value : null;
        } catch {
            return null;
        }
    };

    const storeScrollPosition = value => {
        try {
            window.sessionStorage.setItem(storageKey, String(value));
        } catch {
            // Storage can be unavailable in restricted browser contexts. Navigation remains functional.
        }
    };

    if (desktopSidebar) {
        const nav = desktopSidebar.querySelector(".admin-sidebar__nav");
        if (nav) {
            const savedPosition = readStoredScrollPosition();
            if (savedPosition !== null) {
                nav.scrollTop = savedPosition;
            }

            nav.addEventListener("scroll", () => {
                storeScrollPosition(nav.scrollTop);
            }, { passive: true });

            const activeLink = nav.querySelector(".admin-sidebar__item.is-active");
            if (activeLink instanceof HTMLElement) {
                const navBounds = nav.getBoundingClientRect();
                const activeBounds = activeLink.getBoundingClientRect();
                const isOutsideViewport = activeBounds.top < navBounds.top
                    || activeBounds.bottom > navBounds.bottom;

                if (isOutsideViewport) {
                    activeLink.scrollIntoView({ block: "center" });
                }
            }
        }
    }

    if (offcanvasElement && window.bootstrap?.Offcanvas) {
        offcanvasElement.querySelectorAll("[data-admin-sidebar-link]").forEach(link => {
            link.addEventListener("click", () => {
                window.bootstrap.Offcanvas.getInstance(offcanvasElement)?.hide();
            });
        });
    }
})();
