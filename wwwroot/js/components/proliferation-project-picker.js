"use strict";

(() => {
    const DEFAULT_RECENT_KEY = "prism-proliferation-recent-projects";
    const cache = new Map();

    const normalise = value => String(value ?? "")
        .normalize("NFKD")
        .replace(/[\u0300-\u036f]/g, "")
        .toUpperCase()
        .replace(/[^A-Z0-9]/g, "");

    const projectDisplay = project => String(project?.name || project?.display || "").trim();
    const projectSecondary = project => {
        if (project?.secondaryDisplay) return String(project.secondaryDisplay).trim();
        return [project?.code, project?.technicalCategory || project?.projectCategory]
            .map(value => String(value ?? "").trim())
            .filter(Boolean)
            .join(" · ");
    };

    const searchRank = (project, query) => {
        const term = normalise(query);
        if (!term) return 0;
        const acronym = normalise(project?.acronym);
        const code = normalise(project?.code);
        const name = normalise(project?.name || project?.display);
        if (acronym && acronym === term) return 0;
        if (code && code === term) return 1;
        if (name.startsWith(term)) return 2;
        if (acronym && acronym.startsWith(term)) return 3;
        if (code && code.includes(term)) return 4;
        if (name.includes(term)) return 5;
        return Number.MAX_SAFE_INTEGER;
    };

    class ProliferationProjectPicker {
        constructor(options) {
            if (!options?.input || !options?.suggestions) {
                throw new Error("Project picker requires an input and suggestions container.");
            }

            this.input = options.input;
            this.hiddenInput = options.hiddenInput || options.valueInput || null;
            this.suggestions = options.suggestions;
            this.clearButton = options.clearButton || null;
            this.statusElement = options.statusElement || null;
            this.selectedMeta = options.selectedMeta || this.input.closest("[data-project-search-picker]")?.querySelector("[data-project-selected-meta]") || null;
            this.endpoint = options.endpoint || "/api/proliferation/projects";
            this.getExtraParams = typeof options.getExtraParams === "function" ? options.getExtraParams : () => ({});
            this.onSelected = typeof options.onSelected === "function" ? options.onSelected : () => {};
            this.onCleared = typeof options.onCleared === "function" ? options.onCleared : () => {};
            this.onSearchStateChanged = typeof options.onSearchStateChanged === "function" ? options.onSearchStateChanged : () => {};
            this.minimumLength = Number.isInteger(options.minimumLength) ? options.minimumLength : (Number.isInteger(options.minLength) ? options.minLength : 0);
            this.maxVisible = Number.isInteger(options.maxVisible) ? Math.max(1, options.maxVisible) : 8;
            this.preloadTake = Number.isInteger(options.preloadTake) ? Math.max(25, options.preloadTake) : 200;
            this.searchDelay = Number.isInteger(options.searchDelay) ? options.searchDelay : 100;
            this.showRecent = options.showRecent !== false && this.input.closest("[data-project-search-picker]")?.dataset?.showRecent !== "false";
            this.recentStorageKey = options.recentStorageKey || this.input.closest("[data-project-search-picker]")?.dataset?.recentStorageKey || DEFAULT_RECENT_KEY;
            this.noResultsText = options.noResultsText || "No matching project found. Search by project name, acronym or code.";
            this.selectPrompt = options.selectPrompt || "Select a project from the search results.";

            this.selected = null;
            this.previousSelection = null;
            this.results = [];
            this.activeIndex = -1;
            this.totalMatches = 0;
            this.committedText = "";
            this.controller = null;
            this.requestSequence = 0;
            this.timer = null;
            this.disposed = false;
            this.isOpen = false;
            this.isDisabled = false;

            if (!this.suggestions.id) {
                this.suggestions.id = `${this.input.id || "project"}-suggestions`;
            }
            this.input.setAttribute("role", "combobox");
            this.input.setAttribute("aria-autocomplete", "list");
            this.input.setAttribute("aria-haspopup", "listbox");
            this.input.setAttribute("aria-expanded", "false");
            this.input.setAttribute("aria-controls", this.suggestions.id);
            this.suggestions.setAttribute("role", "listbox");
            this.suggestions.classList.add("pf-project-picker__panel");
            this.suggestions.tabIndex = -1;

            this.bind();
            this.updateClearButton();
        }

        bind() {
            this.handleInput = () => {
                if (this.isDisabled) return;
                const text = this.input.value;
                if (this.selected && text !== this.committedText) {
                    this.previousSelection = this.selected;
                    this.clearSelection({ preserveText: true, notify: false, dispatch: true, preservePrevious: true });
                }
                this.updateClearButton();
                window.clearTimeout(this.timer);
                this.timer = window.setTimeout(() => this.showForQuery(text), this.searchDelay);
            };

            this.handleFocus = () => {
                if (!this.isDisabled) this.showForQuery(this.input.value);
            };

            this.handleClick = () => {
                if (!this.isDisabled && !this.isOpen) this.showForQuery(this.input.value);
            };

            this.handleKeydown = event => {
                if (this.isDisabled) return;

                if (event.altKey && event.key === "ArrowDown") {
                    event.preventDefault();
                    this.showForQuery(this.input.value);
                    return;
                }

                if (event.key === "Escape") {
                    if (this.isOpen) {
                        event.preventDefault();
                        if (!this.selected && this.previousSelection) {
                            const previous = this.previousSelection;
                            this.previousSelection = null;
                            this.select(previous, { notify: false, dispatch: true, focus: false });
                        } else if (this.selected) {
                            this.input.value = this.committedText;
                        }
                        this.close();
                    }
                    return;
                }

                if ((event.key === "ArrowDown" || event.key === "ArrowUp")) {
                    event.preventDefault();
                    if (!this.isOpen) {
                        this.showForQuery(this.input.value).then(() => this.moveActive(event.key === "ArrowDown" ? 1 : -1));
                    } else {
                        this.moveActive(event.key === "ArrowDown" ? 1 : -1);
                    }
                    return;
                }

                if (event.key === "Enter") {
                    if (!this.isOpen) {
                        event.preventDefault();
                        this.showForQuery(this.input.value);
                        return;
                    }
                    const project = this.results[this.activeIndex];
                    if (project) {
                        event.preventDefault();
                        this.select(project);
                    }
                    return;
                }

                if (event.key === "Tab" && this.isOpen) {
                    const project = this.results[this.activeIndex];
                    if (project) this.select(project, { focus: false });
                    else this.close();
                }
            };

            this.handleDocumentPointer = event => {
                const root = this.input.closest("[data-project-search-picker]") || this.input.parentElement;
                if (root?.contains(event.target) || this.suggestions.contains(event.target) || this.clearButton?.contains(event.target)) return;
                this.close();
            };

            this.handleClear = () => {
                this.previousSelection = null;
                this.clearSelection({ preserveText: false, notify: true, dispatch: true });
                this.input.focus();
                this.showForQuery("");
            };

            this.input.addEventListener("input", this.handleInput);
            this.input.addEventListener("focus", this.handleFocus);
            this.input.addEventListener("click", this.handleClick);
            this.input.addEventListener("keydown", this.handleKeydown);
            this.clearButton?.addEventListener("click", this.handleClear);
            document.addEventListener("pointerdown", this.handleDocumentPointer);
        }

        cacheKey() {
            const params = this.getExtraParams() || {};
            const stable = Object.entries(params)
                .filter(([, value]) => value !== undefined && value !== null && String(value).trim() !== "")
                .sort(([a], [b]) => a.localeCompare(b));
            return `${this.endpoint}|${JSON.stringify(stable)}`;
        }

        buildUrl(query = "", take = this.preloadTake) {
            const params = new URLSearchParams();
            if (query) params.set("q", query);
            params.set("take", String(take));
            const extra = this.getExtraParams() || {};
            Object.entries(extra).forEach(([key, value]) => {
                if (value !== undefined && value !== null && String(value).trim() !== "") params.set(key, String(value));
            });
            return `${this.endpoint}?${params.toString()}`;
        }

        async ensureIndex() {
            const key = this.cacheKey();
            const cached = cache.get(key);
            if (cached?.items) return cached;
            if (cached?.promise) return cached.promise;

            const promise = this.fetchProjects("", this.preloadTake)
                .then(payload => {
                    cache.set(key, payload);
                    return payload;
                })
                .catch(error => {
                    cache.delete(key);
                    throw error;
                });
            cache.set(key, { promise });
            return promise;
        }

        async fetchProjects(query, take) {
            this.controller?.abort();
            this.controller = new AbortController();
            const sequence = ++this.requestSequence;
            this.setBusy(true);
            try {
                const response = await fetch(this.buildUrl(query, take), {
                    headers: { Accept: "application/json" },
                    credentials: "same-origin",
                    signal: this.controller.signal
                });
                if (!response.ok) throw new Error(`Unable to search projects (${response.status}).`);
                const payload = await response.json();
                if (sequence !== this.requestSequence || this.disposed) return { items: [], total: 0 };
                if (Array.isArray(payload)) return { items: payload, total: payload.length };
                const items = Array.isArray(payload?.items) ? payload.items : [];
                return {
                    items,
                    total: Number.isFinite(Number(payload?.total)) ? Number(payload.total) : items.length,
                    eligibilityDescription: payload?.eligibilityDescription || ""
                };
            } finally {
                if (sequence === this.requestSequence && !this.disposed) this.setBusy(false);
            }
        }

        async showForQuery(rawQuery) {
            if (this.disposed || this.isDisabled) return;
            const query = String(rawQuery ?? "").trim();
            if (query.length < this.minimumLength) {
                this.renderMessage(`Enter at least ${this.minimumLength} characters.`);
                return;
            }

            try {
                this.setStatus(query ? "Searching projects…" : "Loading eligible projects…");
                const index = await this.ensureIndex();
                if (this.disposed) return;
                let ranked = index.items
                    .map(project => ({ project, rank: searchRank(project, query) }))
                    .filter(item => item.rank < Number.MAX_SAFE_INTEGER)
                    .sort((a, b) => a.rank - b.rank || projectDisplay(a.project).localeCompare(projectDisplay(b.project), undefined, { sensitivity: "base" }))
                    .map(item => item.project);

                // If the cached index is truncated, preserve server-side scalability.
                if (query && index.total > index.items.length) {
                    const remote = await this.fetchProjects(query, this.preloadTake);
                    ranked = remote.items;
                    this.totalMatches = remote.total;
                } else {
                    this.totalMatches = ranked.length;
                }

                this.results = ranked;
                this.activeIndex = this.findSelectedIndex();
                if (this.activeIndex < 0 && this.results.length === 1) this.activeIndex = 0;
                this.render(query, index.items);
                this.setStatus(this.totalMatches === 0
                    ? this.noResultsText
                    : `${this.totalMatches} matching ${this.totalMatches === 1 ? "project" : "projects"}.`);
            } catch (error) {
                if (error?.name === "AbortError") return;
                this.results = [];
                this.activeIndex = -1;
                this.renderMessage("Project search is temporarily unavailable.", "text-danger");
                this.setStatus("Project search is temporarily unavailable.");
            }
        }

        findSelectedIndex() {
            const selectedId = Number(this.selected?.id || this.hiddenInput?.value || 0);
            if (!selectedId) return -1;
            return this.results.findIndex(project => Number(project?.id) === selectedId);
        }

        readRecentIds() {
            if (!this.showRecent) return [];
            try {
                const value = JSON.parse(window.localStorage.getItem(this.recentStorageKey) || "[]");
                return Array.isArray(value) ? value.map(Number).filter(Number.isInteger).slice(0, 6) : [];
            } catch {
                return [];
            }
        }

        remember(project) {
            if (!this.showRecent || !project?.id) return;
            try {
                const ids = [Number(project.id), ...this.readRecentIds().filter(id => id !== Number(project.id))].slice(0, 6);
                window.localStorage.setItem(this.recentStorageKey, JSON.stringify(ids));
            } catch {
                // Browser storage may be blocked; selection must still work.
            }
        }

        render(query, allProjects) {
            this.suggestions.replaceChildren();
            if (this.results.length === 0) {
                this.renderMessage(this.noResultsText);
                return;
            }

            const fragment = document.createDocumentFragment();
            if (!query && this.showRecent) {
                const recentIds = this.readRecentIds();
                const recent = recentIds
                    .map(id => allProjects.find(project => Number(project.id) === id))
                    .filter(Boolean);
                if (recent.length) {
                    fragment.appendChild(this.createSectionHeader("Recently used"));
                    recent.forEach(project => fragment.appendChild(this.createOption(project, query, this.results.indexOf(project))));
                    fragment.appendChild(this.createSectionHeader(`All projects · ${this.totalMatches}`));
                } else {
                    fragment.appendChild(this.createSectionHeader(`All projects · ${this.totalMatches}`));
                }
            } else {
                fragment.appendChild(this.createSectionHeader(`Search results · ${this.totalMatches}`));
            }

            const recentSet = !query ? new Set(this.readRecentIds()) : new Set();
            this.results.forEach((project, index) => {
                if (!query && recentSet.has(Number(project.id))) return;
                fragment.appendChild(this.createOption(project, query, index));
            });

            this.suggestions.appendChild(fragment);
            this.open();
            this.updateActiveOption();
        }

        createSectionHeader(text) {
            const header = document.createElement("div");
            header.className = "pf-project-picker__section";
            header.setAttribute("role", "presentation");
            header.textContent = text;
            return header;
        }

        createOption(project, query, index) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "pf-suggestion pf-project-picker__option";
            button.setAttribute("role", "option");
            button.id = `${this.suggestions.id}-option-${project.id}`;
            button.dataset.index = String(index);
            button.setAttribute("aria-selected", index === this.activeIndex ? "true" : "false");

            const primary = document.createElement("strong");
            primary.className = "pf-project-picker__primary";
            this.appendHighlighted(primary, projectDisplay(project), query);
            button.appendChild(primary);

            const secondaryText = projectSecondary(project);
            if (secondaryText) {
                const secondary = document.createElement("small");
                secondary.className = "pf-project-picker__secondary";
                this.appendHighlighted(secondary, secondaryText, query);
                button.appendChild(secondary);
            }

            button.addEventListener("pointerdown", event => event.preventDefault());
            button.addEventListener("click", () => this.select(project));
            button.addEventListener("mousemove", () => {
                this.activeIndex = index;
                this.updateActiveOption();
            });
            return button;
        }

        appendHighlighted(host, text, query) {
            const source = String(text ?? "");
            const raw = String(query ?? "").trim();
            if (!raw) {
                host.textContent = source;
                return;
            }
            const escaped = raw.replace(/[.*+?^${}()|[\]\\]/g, "\\$&").replace(/[\s\-_/]+/g, "[\\s\\-_/]*");
            let expression;
            try { expression = new RegExp(`(${escaped})`, "ig"); } catch { host.textContent = source; return; }
            const parts = source.split(expression);
            parts.forEach(part => {
                if (!part) return;
                if (expression.test(part)) {
                    expression.lastIndex = 0;
                    const mark = document.createElement("mark");
                    mark.textContent = part;
                    host.appendChild(mark);
                } else {
                    expression.lastIndex = 0;
                    host.appendChild(document.createTextNode(part));
                }
            });
        }

        renderMessage(message, className = "text-muted") {
            this.suggestions.replaceChildren();
            const text = document.createElement("div");
            text.className = `pf-project-picker__message ${className}`;
            text.textContent = message;
            this.suggestions.appendChild(text);
            this.open();
        }

        moveActive(direction) {
            if (!this.results.length) return;
            if (this.activeIndex < 0) this.activeIndex = direction > 0 ? 0 : this.results.length - 1;
            else this.activeIndex = (this.activeIndex + direction + this.results.length) % this.results.length;
            this.updateActiveOption();
        }

        updateActiveOption() {
            const options = [...this.suggestions.querySelectorAll('[role="option"]')];
            options.forEach(option => {
                const index = Number(option.dataset.index);
                const active = index === this.activeIndex;
                option.classList.toggle("active", active);
                option.setAttribute("aria-selected", active ? "true" : "false");
                if (active) {
                    this.input.setAttribute("aria-activedescendant", option.id);
                    option.scrollIntoView({ block: "nearest" });
                }
            });
            if (this.activeIndex < 0) this.input.removeAttribute("aria-activedescendant");
        }

        syncBackingValue(value, dispatch = true) {
            if (!this.hiddenInput) return;
            this.hiddenInput.value = value ? String(value) : "";
            if (dispatch) this.hiddenInput.dispatchEvent(new Event("change", { bubbles: true }));
        }

        select(project, options = {}) {
            if (!project?.id) return;
            const display = projectDisplay(project);
            this.selected = { ...project, display };
            this.previousSelection = null;
            this.committedText = display;
            this.input.value = display;
            this.input.dataset.projectSelected = "true";
            this.input.classList.remove("is-invalid");
            this.input.removeAttribute("aria-invalid");
            this.syncBackingValue(project.id, options.dispatch !== false);
            this.remember(project);
            this.updateSelectedMeta();
            this.close();
            this.updateClearButton();
            this.setStatus(`Selected ${display}.`);
            if (options.notify !== false) this.onSelected(this.selected);
            this.input.dispatchEvent(new Event("change", { bubbles: true }));
            this.input.dispatchEvent(new CustomEvent("proliferation:project-selected", { bubbles: true, detail: this.selected }));
            if (options.focus === true) this.input.focus();
        }

        clearSelection(options = {}) {
            const hadSelection = Boolean(this.selected || this.hiddenInput?.value);
            this.selected = null;
            if (!options.preservePrevious) this.previousSelection = null;
            this.committedText = "";
            this.results = [];
            this.activeIndex = -1;
            if (!options.preserveText) this.input.value = "";
            delete this.input.dataset.projectSelected;
            this.input.removeAttribute("aria-activedescendant");
            this.syncBackingValue("", options.dispatch !== false);
            this.updateSelectedMeta();
            this.close();
            this.updateClearButton();
            if (!options.preserveText) this.setStatus("");
            if (hadSelection && options.notify !== false) this.onCleared();
            if (hadSelection) {
                this.input.dispatchEvent(new Event("change", { bubbles: true }));
                this.input.dispatchEvent(new CustomEvent("proliferation:project-cleared", { bubbles: true }));
            }
        }

        updateSelectedMeta() {
            if (!this.selectedMeta) return;
            const name = this.selectedMeta.querySelector("[data-project-selected-name]");
            const secondary = this.selectedMeta.querySelector("[data-project-selected-secondary]");
            if (!this.selected) {
                this.selectedMeta.classList.add("d-none");
                if (name) name.textContent = "";
                if (secondary) secondary.textContent = "";
                return;
            }
            if (name) name.textContent = projectDisplay(this.selected);
            const secondaryText = projectSecondary(this.selected);
            if (secondary) {
                secondary.textContent = secondaryText;
                secondary.classList.toggle("d-none", !secondaryText);
            }
            this.selectedMeta.classList.toggle("d-none", !secondaryText);
        }

        setSelection(project, options = {}) { this.select(project, options); }
        clear(options = {}) { this.clearSelection({ preserveText: false, ...options }); }
        getSelected() { return this.selected; }
        destroy() { this.dispose(); }

        requireSelection() {
            if (this.selected || Number(this.hiddenInput?.value) > 0) return true;
            this.input.classList.add("is-invalid");
            this.input.setAttribute("aria-invalid", "true");
            this.setStatus(this.selectPrompt);
            this.input.focus();
            return false;
        }

        open() {
            if (this.isDisabled) return;
            this.suggestions.classList.remove("d-none");
            this.positionPanel();
            this.input.setAttribute("aria-expanded", "true");
            this.isOpen = true;
        }

        positionPanel() {
            const rect = this.input.getBoundingClientRect();
            const below = window.innerHeight - rect.bottom;
            const above = rect.top;
            const openAbove = below < 250 && above > below;
            this.suggestions.classList.toggle("pf-project-picker__panel--above", openAbove);
            const available = Math.max(180, Math.floor((openAbove ? above : below) - 20));
            this.suggestions.style.setProperty("--pf-project-picker-available-height", `${available}px`);
        }

        close() {
            this.suggestions.classList.add("d-none");
            this.input.setAttribute("aria-expanded", "false");
            this.input.removeAttribute("aria-activedescendant");
            this.isOpen = false;
        }

        updateClearButton() {
            if (!this.clearButton) return;
            const visible = Boolean(this.input.value || this.selected);
            this.clearButton.classList.toggle("d-none", !visible);
            this.clearButton.disabled = this.isDisabled;
        }

        setBusy(value) {
            this.input.setAttribute("aria-busy", value ? "true" : "false");
            const root = this.input.closest("[data-project-search-picker]");
            root?.querySelector(".pf-project-picker__spinner")?.classList.toggle("d-none", !value);
            this.onSearchStateChanged(Boolean(value));
        }

        setStatus(message) {
            if (this.statusElement) this.statusElement.textContent = message || "";
        }

        setDisabled(value) {
            this.isDisabled = Boolean(value);
            this.input.disabled = this.isDisabled;
            if (this.clearButton) this.clearButton.disabled = this.isDisabled;
            if (this.isDisabled) this.close();
        }

        async initializeById(id, options = {}) {
            const projectId = Number(id);
            if (!Number.isInteger(projectId) || projectId <= 0) {
                this.clear({ notify: options.notify !== false, dispatch: options.dispatch !== false });
                return null;
            }

            const cached = cache.get(this.cacheKey());
            const cachedProject = cached?.items?.find(project => Number(project.id) === projectId);
            if (cachedProject) {
                this.select(cachedProject, options);
                return cachedProject;
            }

            this.controller?.abort();
            this.controller = new AbortController();
            const sequence = ++this.requestSequence;
            this.setBusy(true);
            const params = new URLSearchParams();
            const extra = this.getExtraParams() || {};
            Object.entries(extra).forEach(([key, value]) => {
                if (value !== undefined && value !== null && String(value).trim() !== "") params.set(key, String(value));
            });
            const suffix = params.toString() ? `?${params}` : "";

            try {
                const response = await fetch(`${this.endpoint}/${projectId}${suffix}`, {
                    headers: { Accept: "application/json" },
                    credentials: "same-origin",
                    signal: this.controller.signal
                });
                if (sequence !== this.requestSequence || this.disposed || !response.ok) return null;
                const project = await response.json();
                this.select(project, options);
                return project;
            } catch (error) {
                if (error?.name !== "AbortError") this.setStatus("Unable to load the selected project.");
                return null;
            } finally {
                if (sequence === this.requestSequence && !this.disposed) this.setBusy(false);
            }
        }

        dispose() {
            this.disposed = true;
            this.controller?.abort();
            window.clearTimeout(this.timer);
            this.input.removeEventListener("input", this.handleInput);
            this.input.removeEventListener("focus", this.handleFocus);
            this.input.removeEventListener("click", this.handleClick);
            this.input.removeEventListener("keydown", this.handleKeydown);
            this.clearButton?.removeEventListener("click", this.handleClear);
            document.removeEventListener("pointerdown", this.handleDocumentPointer);
        }
    }

    window.ProliferationProjectPicker = ProliferationProjectPicker;
})();
