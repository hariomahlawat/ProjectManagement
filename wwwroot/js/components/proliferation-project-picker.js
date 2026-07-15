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
            this.minLength = Number.isInteger(options.minLength) ? options.minLength : (Number.isInteger(options.minimumLength) ? options.minimumLength : 2);
            this.selected = null;
            this.activeIndex = -1;
            this.results = [];
            this.controller = null;
            this.requestSequence = 0;
            this.timer = null;
            this.disposed = false;
            this.clearButton = options.clearButton || null;
            this.statusElement = options.statusElement || null;
            this.noResultsText = options.noResultsText || "No matching projects found.";
            this.selectPrompt = options.selectPrompt || "Select a project from the suggestions.";
            this.searchDelay = Number.isInteger(options.searchDelay) ? options.searchDelay : 220;
            this.input.setAttribute("role", "combobox");
            this.input.setAttribute("aria-autocomplete", "list");
            this.input.setAttribute("aria-expanded", "false");
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
                if (text.length < this.minLength) {
                    this.close();
                    this.setStatus(text ? `Enter at least ${this.minLength} characters.` : "");
                    return;
                }
                this.timer = window.setTimeout(() => this.search(text), this.searchDelay);
            };

            this.handleKeydown = event => {
                if (event.key === "Escape") {
                    this.close();
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
                if (event.target === this.input || this.suggestions.contains(event.target)) return;
                this.close();
            };

            this.input.addEventListener("input", this.handleInput);
            this.input.addEventListener("keydown", this.handleKeydown);
            document.addEventListener("pointerdown", this.handleDocumentPointer);
            this.clearButton?.addEventListener("click", () => {
                this.clearSelection({ preserveText: false, notify: true });
                this.input.focus();
            });
        }

        async search(query) {
            if (this.disposed) return;
            this.controller?.abort();
            this.controller = new AbortController();
            const sequence = ++this.requestSequence;
            this.setBusy(true);
            this.setStatus("Searching projects…");

            const params = new URLSearchParams({ q: query });
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
                this.results = Array.isArray(rows) ? rows : [];
                this.activeIndex = this.results.length === 1 ? 0 : -1;
                this.render();
                this.setStatus(this.results.length === 0 ? this.noResultsText : `${this.results.length} matching ${this.results.length === 1 ? "project" : "projects"}.`);
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

        render() {
            this.suggestions.replaceChildren();
            if (this.results.length === 0) {
                this.renderMessage(this.noResultsText);
                return;
            }

            this.results.forEach((project, index) => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "pf-suggestion";
                button.setAttribute("role", "option");
                button.id = `${this.suggestions.id || "pf-project-options"}-option-${project.id}`;
                button.dataset.index = String(index);
                button.setAttribute("aria-selected", index === this.activeIndex ? "true" : "false");

                const name = document.createElement("strong");
                name.textContent = project.name || project.display || "Project";
                button.appendChild(name);
                if (project.code) {
                    const code = document.createElement("small");
                    code.textContent = project.code;
                    button.appendChild(code);
                }

                button.addEventListener("pointerdown", event => event.preventDefault());
                button.addEventListener("click", () => this.select(project));
                button.addEventListener("mousemove", () => {
                    this.activeIndex = index;
                    this.updateActiveOption();
                });
                this.suggestions.appendChild(button);
            });

            this.suggestions.classList.remove("d-none");
            this.input.setAttribute("aria-expanded", "true");
            this.updateActiveOption();
        }

        renderMessage(message, className = "text-muted") {
            this.suggestions.replaceChildren();
            const text = document.createElement("div");
            text.className = `px-3 py-2 small ${className}`;
            text.textContent = message;
            this.suggestions.appendChild(text);
            this.suggestions.classList.remove("d-none");
            this.input.setAttribute("aria-expanded", "true");
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
            if (!project) return;
            const display = project.display || (project.code ? `${project.name} (${project.code})` : project.name);
            this.selected = { ...project, display };
            this.input.value = display || "";
            if (this.hiddenInput) this.hiddenInput.value = String(project.id || "");
            this.input.classList.remove("is-invalid");
            this.input.dataset.projectSelected = "true";
            this.close();
            this.updateClearButton();
            this.setStatus(`Selected ${display}.`);
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

        destroy() {
            this.dispose();
        }

        requireSelection() {
            if (this.selected || this.hiddenInput?.value) return true;
            this.input.classList.add("is-invalid");
            this.setStatus(this.selectPrompt);
            this.input.focus();
            return false;
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
                this.select(project, { notify: options.notify !== false });
                return project;
            } catch (error) {
                if (error.name !== "AbortError") this.setStatus("Unable to load the selected project.");
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
            this.input.removeEventListener("keydown", this.handleKeydown);
            document.removeEventListener("pointerdown", this.handleDocumentPointer);
        }
    }

    window.ProliferationProjectPicker = ProliferationProjectPicker;
})();
