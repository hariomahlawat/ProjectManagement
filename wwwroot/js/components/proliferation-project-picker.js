"use strict";

(() => {
    class ProliferationProjectPicker {
        constructor(options) {
            if (!options?.input || !options?.suggestions) {
                throw new Error("Project picker requires an input and suggestions container.");
            }

            this.input = options.input;
            this.hiddenInput = options.hiddenInput || null;
            this.suggestions = options.suggestions;
            this.endpoint = options.endpoint || "/api/proliferation/projects";
            this.getExtraParams = typeof options.getExtraParams === "function" ? options.getExtraParams : () => ({});
            this.onSelected = typeof options.onSelected === "function" ? options.onSelected : () => {};
            this.onCleared = typeof options.onCleared === "function" ? options.onCleared : () => {};
            this.onSearchStateChanged = typeof options.onSearchStateChanged === "function" ? options.onSearchStateChanged : () => {};
            this.minLength = Number.isInteger(options.minLength)
                ? options.minLength
                : (Number.isInteger(options.minimumLength) ? options.minimumLength : 2);
            this.selected = null;
            this.activeIndex = -1;
            this.results = [];
            this.controller = null;
            this.requestSequence = 0;
            this.timer = null;
            this.disposed = false;
            this.clearButton = options.clearButton || null;
            this.statusElement = options.statusElement || null;
            this.noResultsText = options.noResultsText || "No matching project found. Search by project name, acronym or code.";
            this.selectPrompt = options.selectPrompt || "Select a project from the suggestions.";
            this.searchDelay = Number.isInteger(options.searchDelay) ? options.searchDelay : 180;
            this.take = Number.isInteger(options.take) ? Math.max(5, Math.min(30, options.take)) : 20;
            this.showRecents = options.showRecents === true;
            this.recentLimit = Number.isInteger(options.recentLimit) ? Math.max(1, Math.min(8, options.recentLimit)) : 5;
            this.recentStorageKey = options.recentStorageKey || "prism.proliferation.recent-projects";
            this.emptyPrompt = options.emptyPrompt || "Type a project name, acronym or code.";

            this.input.setAttribute("role", "combobox");
            this.input.setAttribute("aria-autocomplete", "list");
            this.input.setAttribute("aria-expanded", "false");
            this.input.setAttribute("autocomplete", "off");
            if (this.suggestions.id) {
                this.input.setAttribute("aria-controls", this.suggestions.id);
            }
            this.suggestions.setAttribute("role", "listbox");
            this.suggestions.tabIndex = -1;

            this.bind();
            this.updateClearButton();
        }

        bind() {
            this.handleInput = () => {
                const text = this.input.value.trim();
                this.updateClearButton();
                if (this.selected && text !== this.selected.display) {
                    this.clearSelection({ preserveText: true, notify: true });
                }

                this.controller?.abort();
                this.requestSequence += 1;
                this.setBusy(false);
                this.close();
                window.clearTimeout(this.timer);

                if (!text) {
                    this.setStatus("");
                    if (this.showRecents && document.activeElement === this.input) {
                        this.renderRecents();
                    }
                    return;
                }

                if (text.length < this.minLength) {
                    this.setStatus(`Enter at least ${this.minLength} ${this.minLength === 1 ? "character" : "characters"}.`);
                    return;
                }

                this.timer = window.setTimeout(() => this.search(text), this.searchDelay);
            };

            this.handleFocus = () => {
                if (this.input.disabled || this.input.readOnly || this.selected) return;
                const text = this.input.value.trim();
                if (!text) {
                    this.renderRecents();
                } else if (text.length >= this.minLength && this.results.length > 0) {
                    this.renderResults(this.results, "Search results");
                }
            };

            this.handleClick = () => {
                if (this.input.disabled || this.input.readOnly || this.selected) return;
                if (!this.input.value.trim()) this.renderRecents();
            };

            this.handleKeydown = event => {
                if (event.key === "Escape") {
                    this.close();
                    return;
                }

                if (event.key === "Tab") {
                    this.close();
                    return;
                }

                if (event.altKey && event.key === "ArrowDown") {
                    event.preventDefault();
                    if (this.input.value.trim().length >= this.minLength && this.results.length > 0) {
                        this.renderResults(this.results, "Search results");
                    } else {
                        this.renderRecents();
                    }
                    return;
                }

                const optionCount = this.results.length;
                if ((event.key === "ArrowDown" || event.key === "ArrowUp") && optionCount > 0) {
                    event.preventDefault();
                    const direction = event.key === "ArrowDown" ? 1 : -1;
                    this.activeIndex = (this.activeIndex + direction + optionCount) % optionCount;
                    this.updateActiveOption();
                    return;
                }

                if (event.key === "Home" && optionCount > 0 && this.isOpen()) {
                    event.preventDefault();
                    this.activeIndex = 0;
                    this.updateActiveOption();
                    return;
                }

                if (event.key === "End" && optionCount > 0 && this.isOpen()) {
                    event.preventDefault();
                    this.activeIndex = optionCount - 1;
                    this.updateActiveOption();
                    return;
                }

                if (event.key === "Enter") {
                    if (this.activeIndex >= 0 && this.results[this.activeIndex]) {
                        event.preventDefault();
                        this.select(this.results[this.activeIndex]);
                        return;
                    }
                    if (!this.selected && this.results.length === 1) {
                        event.preventDefault();
                        this.select(this.results[0]);
                    }
                }
            };

            this.handleDocumentPointer = event => {
                if (event.target === this.input || this.suggestions.contains(event.target) || this.clearButton?.contains(event.target)) return;
                this.close();
            };

            this.input.addEventListener("input", this.handleInput);
            this.input.addEventListener("focus", this.handleFocus);
            this.input.addEventListener("click", this.handleClick);
            this.input.addEventListener("keydown", this.handleKeydown);
            document.addEventListener("pointerdown", this.handleDocumentPointer);
            this.clearButton?.addEventListener("click", () => {
                this.clearSelection({ preserveText: false, notify: true });
                this.input.focus();
                if (this.showRecents) this.renderRecents();
            });
        }

        async search(query) {
            if (this.disposed) return;
            this.controller?.abort();
            this.controller = new AbortController();
            const sequence = ++this.requestSequence;
            this.setBusy(true);
            this.setStatus("Searching projects…");

            const params = new URLSearchParams({ q: query, take: String(this.take) });
            const extra = this.getExtraParams() || {};
            Object.entries(extra).forEach(([key, value]) => {
                if (value !== undefined && value !== null && String(value).trim() !== "") {
                    params.set(key, String(value));
                }
            });

            try {
                const response = await fetch(`${this.endpoint}?${params}`, {
                    headers: { Accept: "application/json" },
                    signal: this.controller.signal,
                    credentials: "same-origin"
                });
                if (!response.ok) throw new Error(`Unable to search projects (${response.status}).`);
                const rows = await response.json();
                if (sequence !== this.requestSequence || this.disposed) return;

                this.results = this.rankResults(Array.isArray(rows) ? rows : [], query);
                this.activeIndex = this.results.length === 1 ? 0 : -1;
                this.renderResults(this.results, "Search results");
                this.setStatus(this.results.length === 0
                    ? this.noResultsText
                    : `${this.results.length} matching ${this.results.length === 1 ? "project" : "projects"}.`);
            } catch (error) {
                if (error.name === "AbortError") return;
                this.results = [];
                this.activeIndex = -1;
                this.renderMessage("Project search is temporarily unavailable.", "text-danger");
                this.setStatus("Project search is temporarily unavailable.");
            } finally {
                if (sequence === this.requestSequence && !this.disposed) this.setBusy(false);
            }
        }

        rankResults(rows, query) {
            const normalizedQuery = this.normalize(query);
            return rows
                .map(row => this.normalizeProject(row))
                .filter(Boolean)
                .map((project, index) => ({ project, index, score: this.getMatchScore(project, normalizedQuery) }))
                .sort((left, right) => left.score - right.score || left.index - right.index)
                .map(result => result.project);
        }

        getMatchScore(project, query) {
            if (!query) return 0;
            const name = this.normalize(project.name);
            const code = this.normalize(project.code);
            const display = this.normalize(project.display);
            if (code === query) return 0;
            if (name === query) return 1;
            if (code.startsWith(query)) return 2;
            if (name.startsWith(query)) return 3;
            if (code.includes(query)) return 4;
            if (name.includes(query)) return 5;
            if (display.includes(query)) return 6;
            return 20;
        }

        normalize(value) {
            return String(value || "")
                .toLocaleLowerCase()
                .normalize("NFKD")
                .replace(/[^a-z0-9]+/g, "");
        }

        normalizeProject(project) {
            const id = Number(project?.id);
            if (!Number.isInteger(id) || id <= 0) return null;
            const name = String(project.name || project.display || "Project").trim();
            const code = String(project.code || "").trim();
            const technicalCategory = String(project.technicalCategory || project.category || "").trim();
            const display = String(project.display || (code ? `${name} (${code})` : name)).trim();
            return { ...project, id, name, code, technicalCategory, display };
        }

        renderResults(projects, heading = "") {
            this.suggestions.replaceChildren();
            if (!projects || projects.length === 0) {
                this.renderMessage(this.noResultsText);
                return;
            }

            if (heading) this.appendHeading(heading);
            projects.forEach((project, index) => this.appendProject(project, index));
            this.open();
            this.updateActiveOption();
        }

        renderRecents() {
            if (!this.showRecents) {
                this.renderMessage(this.emptyPrompt);
                return;
            }

            const recents = this.readRecents();
            this.results = recents;
            this.activeIndex = -1;
            this.suggestions.replaceChildren();

            if (recents.length === 0) {
                this.renderMessage(this.emptyPrompt);
                return;
            }

            this.appendHeading("Recently used");
            recents.forEach((project, index) => this.appendProject(project, index));
            this.open();
        }

        appendHeading(text) {
            const heading = document.createElement("div");
            heading.className = "pf-suggestions__heading";
            heading.setAttribute("role", "presentation");
            heading.textContent = text;
            this.suggestions.appendChild(heading);
        }

        appendProject(project, index) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "pf-suggestion";
            button.setAttribute("role", "option");
            button.id = `${this.suggestions.id || "pf-project-options"}-option-${project.id}-${index}`;
            button.dataset.index = String(index);
            button.setAttribute("aria-selected", index === this.activeIndex ? "true" : "false");

            const name = document.createElement("strong");
            name.textContent = project.name || project.display || "Project";
            button.appendChild(name);

            const secondaryParts = [project.code, project.technicalCategory].filter(Boolean);
            if (secondaryParts.length > 0) {
                const secondary = document.createElement("small");
                secondary.textContent = secondaryParts.join(" · ");
                button.appendChild(secondary);
            }

            button.addEventListener("pointerdown", event => event.preventDefault());
            button.addEventListener("click", () => this.select(project));
            button.addEventListener("mousemove", () => {
                this.activeIndex = index;
                this.updateActiveOption();
            });
            this.suggestions.appendChild(button);
        }

        renderMessage(message, className = "text-muted") {
            this.results = [];
            this.activeIndex = -1;
            this.suggestions.replaceChildren();
            const text = document.createElement("div");
            text.className = `pf-suggestions__message ${className}`;
            text.textContent = message;
            this.suggestions.appendChild(text);
            this.open();
        }

        updateActiveOption() {
            const options = [...this.suggestions.querySelectorAll('[role="option"]')];
            options.forEach((option, index) => {
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

        select(project, options = {}) {
            const normalized = this.normalizeProject(project);
            if (!normalized) return;
            this.selected = normalized;
            this.input.value = normalized.display;
            if (this.hiddenInput) this.hiddenInput.value = String(normalized.id);
            this.input.classList.remove("is-invalid");
            this.input.removeAttribute("aria-invalid");
            this.input.dataset.projectSelected = "true";
            this.close();
            this.updateClearButton();
            this.setStatus(`Selected ${normalized.display}.`);
            if (options.remember !== false) this.rememberProject(normalized);
            if (options.notify !== false) this.onSelected(this.selected);
            this.input.dispatchEvent(new CustomEvent("proliferation:project-selected", { bubbles: true, detail: this.selected }));
        }

        clearSelection(options = {}) {
            const hadSelection = Boolean(this.selected || this.hiddenInput?.value);
            this.selected = null;
            this.results = [];
            this.activeIndex = -1;
            if (!options.preserveText) this.input.value = "";
            if (this.hiddenInput) this.hiddenInput.value = "";
            delete this.input.dataset.projectSelected;
            this.input.removeAttribute("aria-activedescendant");
            this.close();
            this.updateClearButton();
            if (!options.preserveText) this.setStatus("");
            if (hadSelection && options.notify !== false) this.onCleared();
            if (hadSelection) this.input.dispatchEvent(new CustomEvent("proliferation:project-cleared", { bubbles: true }));
        }

        setSelection(project, options = {}) {
            this.select(project, options);
        }

        clear(options = {}) {
            this.clearSelection({ preserveText: false, ...options });
        }

        getSelection() {
            return this.selected ? { ...this.selected } : null;
        }

        requireSelection() {
            if (this.selected || this.hiddenInput?.value) return true;
            this.input.classList.add("is-invalid");
            this.input.setAttribute("aria-invalid", "true");
            this.setStatus(this.selectPrompt);
            this.input.focus();
            return false;
        }

        open() {
            this.suggestions.classList.remove("d-none");
            this.input.setAttribute("aria-expanded", "true");
        }

        isOpen() {
            return this.input.getAttribute("aria-expanded") === "true";
        }

        close() {
            this.suggestions.classList.add("d-none");
            this.suggestions.replaceChildren();
            this.input.setAttribute("aria-expanded", "false");
            this.input.removeAttribute("aria-activedescendant");
        }

        updateClearButton() {
            if (!this.clearButton) return;
            const visible = Boolean(this.input.value || this.selected);
            this.clearButton.classList.toggle("d-none", !visible);
        }

        setBusy(value) {
            this.input.setAttribute("aria-busy", value ? "true" : "false");
            this.onSearchStateChanged(Boolean(value));
        }

        setStatus(message) {
            if (this.statusElement) this.statusElement.textContent = message || "";
        }

        readRecents() {
            try {
                const raw = window.localStorage?.getItem(this.recentStorageKey);
                const parsed = raw ? JSON.parse(raw) : [];
                if (!Array.isArray(parsed)) return [];
                return parsed
                    .map(project => this.normalizeProject(project))
                    .filter(Boolean)
                    .slice(0, this.recentLimit);
            } catch {
                return [];
            }
        }

        rememberProject(project) {
            if (!this.showRecents) return;
            try {
                const existing = this.readRecents().filter(item => item.id !== project.id);
                const next = [project, ...existing]
                    .slice(0, this.recentLimit)
                    .map(item => ({
                        id: item.id,
                        name: item.name,
                        code: item.code || "",
                        technicalCategory: item.technicalCategory || "",
                        display: item.display
                    }));
                window.localStorage?.setItem(this.recentStorageKey, JSON.stringify(next));
            } catch {
                // Recent projects are a convenience only; storage failures must never block entry.
            }
        }

        async initializeById(id, options = {}) {
            const projectId = Number(id);
            if (!Number.isInteger(projectId) || projectId <= 0) return null;
            this.controller?.abort();
            this.controller = new AbortController();
            const sequence = ++this.requestSequence;
            this.setBusy(true);

            const params = new URLSearchParams();
            const extra = this.getExtraParams() || {};
            Object.entries(extra).forEach(([key, value]) => {
                if (value !== undefined && value !== null && String(value).trim() !== "") {
                    params.set(key, String(value));
                }
            });
            const suffix = params.toString() ? `?${params.toString()}` : "";

            try {
                const response = await fetch(`${this.endpoint}/${projectId}${suffix}`, {
                    headers: { Accept: "application/json" },
                    signal: this.controller.signal,
                    credentials: "same-origin"
                });
                if (sequence !== this.requestSequence || this.disposed) return null;
                if (!response.ok) return null;
                const project = await response.json();
                this.select(project, {
                    notify: options.notify !== false,
                    remember: options.remember === true
                });
                return project;
            } catch (error) {
                if (error.name !== "AbortError") this.setStatus("Unable to load the selected project.");
                return null;
            } finally {
                if (sequence === this.requestSequence && !this.disposed) this.setBusy(false);
            }
        }

        destroy() {
            this.dispose();
        }

        dispose() {
            this.disposed = true;
            this.controller?.abort();
            window.clearTimeout(this.timer);
            this.input.removeEventListener("input", this.handleInput);
            this.input.removeEventListener("focus", this.handleFocus);
            this.input.removeEventListener("click", this.handleClick);
            this.input.removeEventListener("keydown", this.handleKeydown);
            document.removeEventListener("pointerdown", this.handleDocumentPointer);
        }
    }

    window.ProliferationProjectPicker = ProliferationProjectPicker;
})();
