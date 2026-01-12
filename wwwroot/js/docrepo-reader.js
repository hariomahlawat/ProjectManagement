// SECTION: Document reader interactions
(() => {
    const ready = (handler) => {
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", handler);
        } else {
            handler();
        }
    };

    ready(() => {
        // SECTION: Details panel toggle
        const detailsPanel = document.querySelector("[data-docreader-details]");
        const detailsBackdrop = document.querySelector("[data-docreader-details-backdrop]");
        const toggleButtons = document.querySelectorAll("[data-docreader-details-toggle]");
        const closeButton = document.querySelector("[data-docreader-details-close]");

        const setExpandedState = (isExpanded) => {
            toggleButtons.forEach((button) => {
                button.setAttribute("aria-expanded", isExpanded ? "true" : "false");
            });
        };

        const openDetails = () => {
            if (!detailsPanel || !detailsBackdrop) {
                return;
            }

            detailsPanel.classList.add("is-open");
            detailsBackdrop.classList.add("is-visible");
            detailsPanel.setAttribute("aria-hidden", "false");
            setExpandedState(true);
        };

        const closeDetails = () => {
            if (!detailsPanel || !detailsBackdrop) {
                return;
            }

            detailsPanel.classList.remove("is-open");
            detailsBackdrop.classList.remove("is-visible");
            detailsPanel.setAttribute("aria-hidden", "true");
            setExpandedState(false);
        };

        toggleButtons.forEach((button) => {
            button.addEventListener("click", () => {
                if (detailsPanel?.classList.contains("is-open")) {
                    closeDetails();
                } else {
                    openDetails();
                }
            });
        });

        closeButton?.addEventListener("click", closeDetails);
        detailsBackdrop?.addEventListener("click", closeDetails);
        document.addEventListener("keydown", (event) => {
            if (event.key === "Escape") {
                if (detailsPanel?.classList.contains("is-open")) {
                    event.preventDefault();
                    closeDetails();
                }
            }
        });

        // SECTION: Favourite toggle
        const favouriteButton = document.querySelector("[data-docrepo-favourite]");
        const favouriteToken = document.querySelector("#docrepoFavouriteToken input[name=\"__RequestVerificationToken\"]");

        const favouriteIndicator = document.querySelector("[data-docreader-favourite-indicator]");

        const setFavouriteState = (isFavourite) => {
            if (!favouriteButton) {
                return;
            }

            const nextValue = isFavourite ? "true" : "false";
            const title = isFavourite ? "Remove from favourites" : "Add to favourites";

            favouriteButton.setAttribute("data-fav", nextValue);
            favouriteButton.setAttribute("title", title);
            favouriteButton.setAttribute("aria-label", title);

            const icon = favouriteButton.querySelector("i");
            if (icon) {
                icon.classList.toggle("bi-star-fill", isFavourite);
                icon.classList.toggle("bi-star", !isFavourite);
            }

            if (favouriteIndicator) {
                favouriteIndicator.setAttribute("data-fav", nextValue);
                favouriteIndicator.classList.toggle("docreader-favourite-indicator--active", isFavourite);
                favouriteIndicator.classList.toggle("docreader-favourite-indicator--inactive", !isFavourite);

                const indicatorIcon = favouriteIndicator.querySelector("i");
                if (indicatorIcon) {
                    indicatorIcon.classList.toggle("bi-star-fill", isFavourite);
                    indicatorIcon.classList.toggle("bi-star", !isFavourite);
                }

                const indicatorLabel = favouriteIndicator.querySelector("span");
                if (indicatorLabel) {
                    indicatorLabel.textContent = isFavourite ? "Yes" : "No";
                }
            }
        };

        favouriteButton?.addEventListener("click", async () => {
            if (!favouriteButton) {
                return;
            }

            const documentId = favouriteButton.getAttribute("data-docid");
            const toggleUrl = favouriteButton.getAttribute("data-toggle-url");
            if (!documentId || !toggleUrl || !favouriteToken) {
                return;
            }

            const wasFavourite = favouriteButton.getAttribute("data-fav") === "true";
            setFavouriteState(!wasFavourite);

            try {
                // SECTION: Toggle endpoint with query-safe id
                const endpoint = new URL(toggleUrl, window.location.origin);
                endpoint.searchParams.set("id", documentId);

                const response = await fetch(endpoint.toString(), {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",
                        "RequestVerificationToken": favouriteToken.value
                    }
                });

                if (!response.ok) {
                    throw new Error("Favourite toggle failed.");
                }

                const data = await response.json();
                setFavouriteState(!!data.isFavourite);
            } catch (error) {
                setFavouriteState(wasFavourite);
            }
        });

        // SECTION: Viewer loading state
        const viewer = document.querySelector("[data-docreader-viewer]");
        const iframe = document.querySelector("[data-docreader-frame]");
        const warning = document.querySelector("[data-docreader-warning]");

        if (!viewer || !iframe) {
            return;
        }

        let timeoutHandle = null;
        const showWarning = () => {
            warning?.classList.remove("d-none");
        };

        const markLoaded = () => {
            viewer.classList.add("is-loaded");
            if (timeoutHandle) {
                clearTimeout(timeoutHandle);
            }
        };

        iframe.addEventListener("load", markLoaded, { once: true });
        timeoutHandle = window.setTimeout(showWarning, 8000);
    });
})();
