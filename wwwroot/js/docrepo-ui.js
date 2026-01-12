// SECTION: Document repository row navigation
(() => {
    const interactiveSelector = "a, button, input, select, textarea, .dropdown, .dropdown-menu, [data-no-row-nav]";
    const navigableSelector = ".docrepo-row, .docrepo-card";

    // SECTION: Click navigation
    document.addEventListener("click", (event) => {
        const target = event.target;
        const navigable = target.closest(navigableSelector);
        if (!navigable) {
            return;
        }

        if (target.closest(interactiveSelector)) {
            return;
        }

        const href = navigable.dataset.href;
        if (!href) {
            return;
        }

        window.location.href = href;
    });

    // SECTION: Keyboard navigation
    document.addEventListener("keydown", (event) => {
        const target = event.target;
        const row = target.closest(".docrepo-row");
        if (!row) {
            return;
        }

        if (target !== row && target.closest(interactiveSelector)) {
            return;
        }

        if (event.key === "Enter") {
            const href = row.dataset.href;
            if (href) {
                window.location.href = href;
            }
        }

        if (event.key !== "ArrowDown" && event.key !== "ArrowUp") {
            return;
        }

        const rowContainer = row.closest("tbody") ?? row.parentElement;
        if (!rowContainer) {
            return;
        }

        const rows = Array.from(rowContainer.querySelectorAll(".docrepo-row"));
        const index = rows.indexOf(row);
        if (index < 0) {
            return;
        }

        const nextIndex = event.key === "ArrowDown" ? index + 1 : index - 1;
        const nextRow = rows[nextIndex];
        if (nextRow) {
            event.preventDefault();
            nextRow.focus();
        }
    });
})();

// SECTION: Document repository facets + collapse
(() => {
    const storageKey = "docrepo.filters.collapsed";
    const filtersEl = document.getElementById("docrepoFilters");

    if (!filtersEl) {
        return;
    }

    const toggleButton = document.getElementById("docrepoFiltersToggle");
    const expandButton = document.getElementById("docrepoFiltersExpandBtn");

    const setCollapsed = (collapsed) => {
        filtersEl.classList.toggle("docrepo-filters--collapsed", collapsed);

        if (toggleButton) {
            toggleButton.title = collapsed ? "Expand filters" : "Collapse filters";
            toggleButton.setAttribute("aria-label", toggleButton.title);
            const icon = toggleButton.querySelector("i");
            if (icon) {
                icon.classList.toggle("bi-chevron-right", collapsed);
                icon.classList.toggle("bi-chevron-left", !collapsed);
            }
        }

        try {
            localStorage.setItem(storageKey, collapsed ? "true" : "false");
        } catch (error) {
            // Ignore storage failures.
        }
    };

    // SECTION: Collapse initialization
    try {
        const collapsed = localStorage.getItem(storageKey) === "true";
        setCollapsed(collapsed);
    } catch (error) {
        setCollapsed(false);
    }

    if (toggleButton) {
        toggleButton.addEventListener("click", () => {
            const collapsed = !filtersEl.classList.contains("docrepo-filters--collapsed");
            setCollapsed(collapsed);
        });
    }

    if (expandButton) {
        expandButton.addEventListener("click", () => setCollapsed(false));
    }

    // SECTION: Single-select facets
    const facetGroups = Array.from(document.querySelectorAll("[data-facet-single]"));
    facetGroups.forEach((group) => {
        const hiddenSelector = group.getAttribute("data-target-hidden");
        const hiddenInput = hiddenSelector ? document.querySelector(hiddenSelector) : null;

        const syncHidden = () => {
            if (!hiddenInput) {
                return;
            }

            const checked = group.querySelector("input.docrepo-check__input:checked");
            hiddenInput.value = checked ? checked.getAttribute("data-value") || "" : "";
        };

        group.addEventListener("change", (event) => {
            const target = event.target;
            if (!(target instanceof HTMLInputElement) || !target.classList.contains("docrepo-check__input")) {
                return;
            }

            const allInputs = Array.from(group.querySelectorAll("input.docrepo-check__input"));

            if (target.checked) {
                allInputs.forEach((input) => {
                    if (input !== target) {
                        input.checked = false;
                    }
                });
            } else {
                const allOption = allInputs.find((input) => (input.getAttribute("data-value") || "") === "");
                if (allOption) {
                    allOption.checked = true;
                }
            }

            const customYearInput = document.querySelector("[data-custom-year]");
            if (group.getAttribute("data-facet-single") === "year" && customYearInput) {
                if (target.checked) {
                    customYearInput.value = "";
                }
            }

            syncHidden();
        });

        syncHidden();
    });

    // SECTION: Custom year precedence
    const customYearInput = document.querySelector("[data-custom-year]");
    const yearGroup = document.querySelector('[data-facet-single="year"]');
    const yearHidden = document.getElementById("yearHidden");

    if (customYearInput && yearGroup && yearHidden) {
        const clearYearChecks = () => {
            const yearChecks = Array.from(yearGroup.querySelectorAll("input.docrepo-check__input"));
            yearChecks.forEach((input) => {
                input.checked = false;
            });
        };

        const syncYearHidden = () => {
            const value = customYearInput.value.trim();
            if (value.length > 0) {
                yearHidden.value = value;
                clearYearChecks();
            } else {
                let checked = yearGroup.querySelector("input.docrepo-check__input:checked");
                if (!checked) {
                    const allOption = yearGroup.querySelector('input.docrepo-check__input[data-value=""]');
                    if (allOption) {
                        allOption.checked = true;
                        checked = allOption;
                    }
                }
                yearHidden.value = checked ? checked.getAttribute("data-value") || "" : "";
            }
        };

        customYearInput.addEventListener("input", syncYearHidden);
        syncYearHidden();
    }
})();

// SECTION: Document repository apply button dirty tracking
function initDirtyTracking() {
    const form = document.querySelector("#docrepoFacetsForm");
    if (!form) {
        return;
    }

    const applyBtn = document.querySelector("#docrepoApplyBtn");
    if (!applyBtn) {
        return;
    }

    const officeHidden = document.querySelector("#officeCategoryIdHidden");
    const typeHidden = document.querySelector("#documentCategoryIdHidden");
    const yearHidden = document.querySelector("#yearHidden");
    const tagInput = form.querySelector('input[name="tag"]');
    const inactiveChk = form.querySelector('input[name="includeInactive"]');

    const initial = {
        office: officeHidden ? (officeHidden.value || "") : "",
        type: typeHidden ? (typeHidden.value || "") : "",
        year: yearHidden ? (yearHidden.value || "") : "",
        tag: tagInput ? (tagInput.value || "").trim() : "",
        inactive: inactiveChk ? (inactiveChk.checked ? "1" : "0") : "0"
    };

    function currentState() {
        return {
            office: officeHidden ? (officeHidden.value || "") : "",
            type: typeHidden ? (typeHidden.value || "") : "",
            year: yearHidden ? (yearHidden.value || "") : "",
            tag: tagInput ? (tagInput.value || "").trim() : "",
            inactive: inactiveChk ? (inactiveChk.checked ? "1" : "0") : "0"
        };
    }

    function isDirty() {
        const current = currentState();
        return (
            current.office !== initial.office ||
            current.type !== initial.type ||
            current.year !== initial.year ||
            current.tag !== initial.tag ||
            current.inactive !== initial.inactive
        );
    }

    function updateApplyState() {
        const dirty = isDirty();
        applyBtn.disabled = !dirty;
        applyBtn.classList.toggle("disabled", !dirty);
    }

    form.addEventListener("change", updateApplyState);
    form.addEventListener("input", updateApplyState);

    updateApplyState();
}

// SECTION: Document repository scope sync
function syncScopeUiFromLocation() {
    const url = new URL(window.location.href);
    const scope = (url.searchParams.get("scope") || "").toLowerCase();

    // SECTION: Hidden scope inputs
    document.querySelectorAll('input[name="scope"]').forEach((input) => {
        input.value = scope;
    });

    // SECTION: Rail active state
    const rail = document.querySelector(".docrepo-rail");
    if (!rail) {
        return;
    }

    rail.querySelectorAll("a.docrepo-rail__item").forEach((link) => {
        const linkScope = (link.getAttribute("data-scope") || "").toLowerCase();
        link.classList.toggle("is-active", linkScope === scope);
    });
}

// SECTION: Document repository scope navigation
function initScopeNavigation() {
    const rail = document.querySelector(".docrepo-rail");
    if (!rail) {
        return;
    }

    rail.addEventListener("click", (event) => {
        const link = event.target.closest("a.docrepo-rail__item");
        if (!link) {
            return;
        }

        if (event.button !== 0) {
            return;
        }

        if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
            return;
        }

        const href = link.getAttribute("href");
        if (!href) {
            return;
        }

        event.preventDefault();

        const targetUrl = new URL(href, window.location.origin);
        const scope = (targetUrl.searchParams.get("scope") || "").toLowerCase();

        // SECTION: Immediate UI feedback
        rail.querySelectorAll("a.docrepo-rail__item").forEach((item) => item.classList.remove("is-active"));
        link.classList.add("is-active");

        // SECTION: Keep forms aligned with scope
        document.querySelectorAll('input[name="scope"]').forEach((input) => {
            input.value = scope;
        });

        fetchAndSwapResults(targetUrl.toString());
    });
}

// SECTION: Document repository partial results fetch
async function fetchAndSwapResults(targetUrl, options = {}) {
    const results = document.querySelector("#docrepoResults");
    if (!results) {
        window.location.assign(targetUrl);
        return;
    }

    const updateHistory = options.updateHistory !== false;
    const indicator = document.querySelector("#docrepoUpdating");

    const setUpdating = (isUpdating) => {
        if (indicator) {
            indicator.classList.toggle("d-none", !isUpdating);
        }
        results.classList.toggle("docrepo-results--loading", isUpdating);
    };

    const requestUrl = new URL(targetUrl, window.location.origin);
    requestUrl.searchParams.set("partial", "true");

    setUpdating(true);

    try {
        const response = await fetch(requestUrl.toString(), {
            method: "GET",
            headers: { "X-Requested-With": "DocRepoPartial" }
        });

        if (!response.ok) {
            window.location.assign(targetUrl);
            return;
        }

        const html = await response.text();
        results.innerHTML = html;

        if (updateHistory) {
            const cleanUrl = new URL(targetUrl, window.location.origin);
            cleanUrl.searchParams.delete("partial");
            history.pushState({}, "", cleanUrl.toString());
        }

        syncScopeUiFromLocation();
    } catch (error) {
        window.location.assign(targetUrl);
    } finally {
        setUpdating(false);
    }
}

// SECTION: Document repository auto-apply filters
function initAutoApplyFilters() {
    const form = document.querySelector("#docrepoFacetsForm");
    if (!form) {
        return;
    }

    const enabled = form.getAttribute("data-autoapply") === "true";
    if (!enabled) {
        return;
    }

    const officeHidden = document.querySelector("#officeCategoryIdHidden");
    const typeHidden = document.querySelector("#documentCategoryIdHidden");
    const yearHidden = document.querySelector("#yearHidden");
    const tagInput = form.querySelector('input[name="tag"]');
    const inactiveChk = form.querySelector('input[name="includeInactive"]');
    const customYear = document.querySelector("[data-custom-year]");
    const viewHidden = form.querySelector('input[name="view"]');
    const pageSizeHidden = form.querySelector('input[name="pageSize"]');

    let tagTimer = null;
    let yearTimer = null;

    // SECTION: URL builder
    const buildUrlAndGo = () => {
        const url = new URL(window.location.href);

        // Always reset page
        url.searchParams.set("page", "1");

        if (viewHidden && viewHidden.value) {
            url.searchParams.set("view", viewHidden.value);
        }

        if (pageSizeHidden && pageSizeHidden.value) {
            url.searchParams.set("pageSize", pageSizeHidden.value);
        }

        const office = officeHidden ? (officeHidden.value || "").trim() : "";
        const type = typeHidden ? (typeHidden.value || "").trim() : "";
        const year = yearHidden ? (yearHidden.value || "").trim() : "";
        const tag = tagInput ? (tagInput.value || "").trim() : "";
        const inactive = inactiveChk ? inactiveChk.checked : false;

        const setOrDelete = (key, value) => {
            if (value) {
                url.searchParams.set(key, value);
            } else {
                url.searchParams.delete(key);
            }
        };

        setOrDelete("officeCategoryId", office);
        setOrDelete("documentCategoryId", type);
        setOrDelete("year", year);
        setOrDelete("tag", tag);

        if (inactive) {
            url.searchParams.set("includeInactive", "true");
        } else {
            url.searchParams.delete("includeInactive");
        }

        fetchAndSwapResults(url.toString());
    };

    const autoApplyImmediate = () => {
        buildUrlAndGo();
    };

    // SECTION: Form submit interception
    form.addEventListener("submit", (event) => {
        event.preventDefault();
        autoApplyImmediate();
    });

    // SECTION: Facet change handling
    form.addEventListener("change", (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        if (target.getAttribute("name") === "includeInactive") {
            autoApplyImmediate();
            return;
        }

        if (target.classList.contains("docrepo-check__input")) {
            const isYearFacet = target.closest('[data-facet-single="year"]');
            if (isYearFacet && customYear && customYear.value.trim().length > 0) {
                return;
            }

            autoApplyImmediate();
        }
    });

    // SECTION: Tag debounce
    if (tagInput) {
        tagInput.addEventListener("input", () => {
            if (tagTimer) {
                clearTimeout(tagTimer);
            }

            tagTimer = setTimeout(() => {
                const value = (tagInput.value || "").trim();
                if (value.length === 0 || value.length >= 2) {
                    autoApplyImmediate();
                }
            }, 500);
        });

        tagInput.addEventListener("keydown", (event) => {
            if (event.key === "Enter") {
                event.preventDefault();
                autoApplyImmediate();
            }
        });
    }

    // SECTION: Custom year validation
    if (customYear) {
        const errorMessage = document.querySelector("#docrepoYearError");

        const setYearError = (text) => {
            if (!errorMessage) {
                return;
            }

            errorMessage.textContent = text;
            errorMessage.classList.toggle("d-none", !text);
        };

        customYear.addEventListener("input", () => {
            if (yearTimer) {
                clearTimeout(yearTimer);
            }

            yearTimer = setTimeout(() => {
                const value = (customYear.value || "").trim();

                if (value.length === 0) {
                    setYearError("");
                    autoApplyImmediate();
                    return;
                }

                const parsed = Number(value);
                const valid = Number.isInteger(parsed) && value.length === 4 && parsed >= 1900 && parsed <= 2100;

                if (!valid) {
                    setYearError("Invalid year. Enter a 4-digit year between 1900 and 2100.");
                    return;
                }

                setYearError("");
                if (yearHidden) {
                    yearHidden.value = value;
                }
                autoApplyImmediate();
            }, 600);
        });
    }

    // SECTION: Pagination links
    const resultsContainer = document.querySelector("#docrepoResults");
    if (resultsContainer) {
        resultsContainer.addEventListener("click", (event) => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            const link = target.closest("a");
            if (!link || !link.closest(".pagination")) {
                return;
            }

            const href = link.getAttribute("href");
            if (!href) {
                return;
            }

            event.preventDefault();
            fetchAndSwapResults(new URL(href, window.location.origin).toString());
        });
    }
}

// SECTION: Document repository view preference
function initViewPreference() {
    const toggle = document.querySelector(".docrepo-view-toggle");
    if (!toggle) {
        return;
    }

    const url = new URL(window.location.href);
    const hasView = url.searchParams.has("view");

    toggle.addEventListener("click", (event) => {
        const link = event.target.closest("a");
        if (!link) {
            return;
        }

        const href = link.getAttribute("href");
        if (!href) {
            return;
        }

        try {
            const parsed = new URL(href, window.location.origin);
            const view = (parsed.searchParams.get("view") || "").toLowerCase();
            if (view === "list" || view === "cards") {
                localStorage.setItem("docrepo.viewMode", view);
            }
        } catch (error) {
            // Ignore invalid URLs or storage failures.
        }
    });

    if (!hasView) {
        try {
            const pref = (localStorage.getItem("docrepo.viewMode") || "").toLowerCase();
            if (pref === "list" || pref === "cards") {
                url.searchParams.set("view", pref);
                window.location.replace(url.toString());
            }
        } catch (error) {
            // Ignore storage failures.
        }
    }
}

// SECTION: Document repository favourite toggles
function initFavouriteToggles() {
    const results = document.querySelector("#docrepoResults");
    if (!results) {
        return;
    }

    const toggleUrl = results.getAttribute("data-toggle-url");
    if (!toggleUrl) {
        return;
    }

    const tokenInput = document.querySelector("#docrepoFavouriteToken input[name=\"__RequestVerificationToken\"]");
    const alertEl = document.getElementById("docrepoFavouriteAlert");
    let alertTimer = null;

    const showAlert = () => {
        if (!alertEl) {
            return;
        }

        alertEl.classList.remove("d-none");
        if (alertTimer) {
            clearTimeout(alertTimer);
        }
        alertTimer = setTimeout(() => {
            alertEl.classList.add("d-none");
        }, 4000);
    };

    const setButtonState = (button, isFavourite) => {
        const nextValue = isFavourite ? "true" : "false";
        const title = isFavourite ? "Remove from favourites" : "Add to favourites";

        button.setAttribute("data-fav", nextValue);
        button.setAttribute("title", title);
        button.setAttribute("aria-label", title);

        const icon = button.querySelector("i");
        if (icon) {
            icon.classList.toggle("bi-star-fill", isFavourite);
            icon.classList.toggle("bi-star", !isFavourite);
        }
    };

    const removeFavouriteItem = (button) => {
        const row = button.closest(".docrepo-row");
        if (row) {
            row.remove();
            return;
        }

        const col = button.closest(".col");
        if (col) {
            col.remove();
            return;
        }

        const card = button.closest(".docrepo-card");
        if (card) {
            card.remove();
        }
    };

    document.addEventListener("click", async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const button = target.closest(".docrepo-star");
        if (!button) {
            return;
        }

        event.preventDefault();

        const documentId = button.getAttribute("data-docid");
        if (!documentId || !tokenInput) {
            showAlert();
            return;
        }

        const wasFavourite = button.getAttribute("data-fav") === "true";
        setButtonState(button, !wasFavourite);

        try {
            // SECTION: Toggle endpoint with query-safe id
            const endpoint = new URL(toggleUrl, window.location.origin);
            endpoint.searchParams.set("id", documentId);

            const response = await fetch(endpoint.toString(), {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken": tokenInput.value
                }
            });

            if (!response.ok) {
                throw new Error("Favourite toggle failed.");
            }

            const data = await response.json();
            const isFavourite = !!data.isFavourite;
            setButtonState(button, isFavourite);

            const isFavouritesScope = (new URL(window.location.href)).searchParams.get("scope")?.toLowerCase() === "favourites";
            if (isFavouritesScope && !isFavourite) {
                removeFavouriteItem(button);
            }
        } catch (error) {
            setButtonState(button, wasFavourite);
            showAlert();
        }
    });
}

// SECTION: Document repository DOM initialization
document.addEventListener("DOMContentLoaded", () => {
    syncScopeUiFromLocation();
    initScopeNavigation();
    initDirtyTracking();
    initViewPreference();
    initAutoApplyFilters();
    initFavouriteToggles();

    // SECTION: Back/forward navigation
    window.addEventListener("popstate", async () => {
        await fetchAndSwapResults(window.location.href, { updateHistory: false });
        syncScopeUiFromLocation();
    });
});
