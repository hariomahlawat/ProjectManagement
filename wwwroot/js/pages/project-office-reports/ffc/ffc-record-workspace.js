"use strict";

(() => {
    const root = document.querySelector("[data-ffc-workspace], .ffc-record-create, [data-ffc-archived-records]");
    if (!root) return;

    const normalise = value => String(value ?? "")
        .normalize("NFKD")
        .replace(/[\u0300-\u036f]/g, "")
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, " ")
        .trim();

    const confirmAction = options => window.PrismConfirm?.show
        ? window.PrismConfirm.show(options)
        : Promise.resolve(false);

    const initMilestones = () => {
        root.querySelectorAll("[data-ffc-milestone-editor]").forEach(editor => {
            const radios = [...editor.querySelectorAll('input[type="radio"]')];
            if (!radios.length) return;

            const sync = () => {
                const completed = radios.find(radio => radio.checked)?.value === "true";
                editor.classList.toggle("is-pending", !completed);
                editor.querySelectorAll("[data-ffc-milestone-date] input").forEach(control => {
                    control.disabled = !completed;
                });
            };

            radios.forEach(radio => radio.addEventListener("change", sync));
            sync();
        });
    };

    class SearchSelect {
        constructor(select) {
            this.select = select;
            this.options = [...select.options]
                .filter(option => option.value)
                .map(option => ({
                    value: option.value,
                    label: option.textContent.trim(),
                    secondary: option.dataset.secondary || "",
                    search: normalise(`${option.textContent} ${option.dataset.secondary || ""}`)
                }));
            this.activeIndex = -1;
            this.visible = [];
            this.committedValue = select.value;
            this.hasShownValidation = select.classList.contains("input-validation-error");
            this.build();
            this.bind();
            this.syncFromSelect();
        }

        build() {
            this.select.classList.add("ffc-search-select__native");
            this.select.tabIndex = -1;
            this.select.setAttribute("aria-hidden", "true");

            const wrapper = document.createElement("div");
            wrapper.className = "ffc-search-select";
            const control = document.createElement("div");
            control.className = "ffc-search-select__control";

            this.input = document.createElement("input");
            this.input.id = `${this.select.id || "ffc-search-select"}-search`;
            this.input.type = "search";
            this.input.className = "form-control ffc-search-select__input";
            this.input.placeholder = this.select.dataset.placeholder || "Search or select";
            this.input.autocomplete = "off";
            this.input.spellcheck = false;
            this.input.required = true;
            this.input.setAttribute("role", "combobox");
            this.input.setAttribute("aria-autocomplete", "list");
            this.input.setAttribute("aria-expanded", "false");

            const label = [...document.querySelectorAll("label[for]")]
                .find(candidate => candidate.htmlFor === this.select.id);
            if (label) label.htmlFor = this.input.id;

            this.toggle = document.createElement("button");
            this.toggle.type = "button";
            this.toggle.className = "ffc-search-select__toggle";
            this.toggle.setAttribute("aria-label", "Open options");
            this.toggle.innerHTML = '<span class="bi bi-chevron-down" aria-hidden="true"></span>';

            this.list = document.createElement("div");
            this.list.className = "ffc-search-select__list";
            this.list.hidden = true;
            this.list.setAttribute("role", "listbox");
            this.list.id = `${this.select.id || "ffc-search-select"}-options`;
            this.input.setAttribute("aria-controls", this.list.id);

            control.append(this.input, this.toggle);
            wrapper.append(control, this.list);
            this.select.insertAdjacentElement("afterend", wrapper);
            wrapper.appendChild(this.select);
            this.wrapper = wrapper;

            this.syncValidationState();
        }

        bind() {
            this.input.addEventListener("focus", () => this.open());
            this.input.addEventListener("click", () => this.open());
            this.input.addEventListener("input", () => {
                const selected = this.getSelectedOption();
                if (!selected || normalise(this.input.value) !== normalise(selected.label)) {
                    if (this.select.value) {
                        this.select.value = "";
                    }
                }
                this.hasShownValidation = false;
                this.syncValidationState();
                this.render(this.input.value);
            });
            this.input.addEventListener("blur", () => {
                queueMicrotask(() => {
                    if (this.wrapper.contains(document.activeElement)) return;
                    this.hasShownValidation = Boolean(this.input.value && !this.select.value);
                    this.syncValidationState();
                });
            });
            this.input.addEventListener("invalid", () => {
                this.hasShownValidation = true;
                this.syncValidationState();
            });
            this.toggle.addEventListener("click", () => this.list.hidden ? this.open() : this.close());
            this.input.addEventListener("keydown", event => this.onKeydown(event));
            this.select.addEventListener("change", () => {
                this.committedValue = this.select.value;
                this.syncFromSelect();
                this.syncValidationState();
            });
            document.addEventListener("pointerdown", event => {
                if (!this.wrapper.contains(event.target)) this.close();
            });
        }

        getSelectedOption() {
            return this.options.find(item => item.value === this.select.value) || null;
        }

        open() {
            this.render(this.input.value);
            this.list.hidden = false;
            this.input.setAttribute("aria-expanded", "true");
        }

        close() {
            this.list.hidden = true;
            this.activeIndex = -1;
            this.input.removeAttribute("aria-activedescendant");
            this.input.setAttribute("aria-expanded", "false");
        }

        render(query) {
            const term = normalise(query);
            this.visible = this.options
                .filter(option => !term || option.search.includes(term))
                .slice(0, 80);
            this.activeIndex = -1;
            this.input.removeAttribute("aria-activedescendant");
            this.list.replaceChildren();

            if (!this.visible.length) {
                const empty = document.createElement("div");
                empty.className = "ffc-project-picker__empty";
                empty.textContent = "No matching country found.";
                this.list.appendChild(empty);
                this.openWithoutRender();
                return;
            }

            this.visible.forEach((option, index) => {
                const button = document.createElement("button");
                button.id = `${this.list.id}-${index}`;
                button.type = "button";
                button.className = "ffc-search-select__option";
                button.setAttribute("role", "option");
                button.setAttribute("aria-selected", "false");
                const strong = document.createElement("strong");
                strong.textContent = option.label;
                button.appendChild(strong);
                if (option.secondary) {
                    const small = document.createElement("small");
                    small.textContent = option.secondary;
                    button.appendChild(small);
                }
                button.addEventListener("pointerdown", event => event.preventDefault());
                button.addEventListener("click", () => this.selectOption(option));
                this.list.appendChild(button);
            });
            this.openWithoutRender();
        }

        openWithoutRender() {
            this.list.hidden = false;
            this.input.setAttribute("aria-expanded", "true");
        }

        selectOption(option) {
            this.select.value = option.value;
            this.committedValue = option.value;
            this.input.value = option.label;
            this.hasShownValidation = false;
            this.select.dispatchEvent(new Event("change", { bubbles: true }));
            this.close();
        }

        syncFromSelect() {
            const option = this.getSelectedOption();
            if (option) this.input.value = option.label;
        }

        syncValidationState() {
            const valid = Boolean(this.select.value);
            this.input.setCustomValidity(valid ? "" : "Select a country from the list.");
            const showInvalid = !valid && this.hasShownValidation;
            this.input.classList.toggle("is-invalid", showInvalid);
            this.input.classList.toggle("input-validation-error", showInvalid);
            this.input.setAttribute("aria-invalid", showInvalid ? "true" : "false");
        }

        setActiveIndex(index) {
            this.activeIndex = index;
            [...this.list.querySelectorAll('[role="option"]')].forEach((button, optionIndex) => {
                const active = optionIndex === index;
                button.classList.toggle("is-active", active);
                button.setAttribute("aria-selected", active ? "true" : "false");
                if (active) {
                    this.input.setAttribute("aria-activedescendant", button.id);
                    button.scrollIntoView({ block: "nearest" });
                }
            });
        }

        selectExactMatch() {
            const term = normalise(this.input.value);
            if (!term) return false;
            const matches = this.options.filter(option => normalise(option.label) === term);
            if (matches.length !== 1) return false;
            this.selectOption(matches[0]);
            return true;
        }

        onKeydown(event) {
            if (event.key === "Escape") {
                event.preventDefault();
                this.close();
                if (!this.select.value && this.committedValue) {
                    this.select.value = this.committedValue;
                }
                this.syncFromSelect();
                this.syncValidationState();
                return;
            }

            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();
                if (this.list.hidden) this.open();
                if (!this.visible.length) return;
                const delta = event.key === "ArrowDown" ? 1 : -1;
                const next = this.activeIndex < 0
                    ? (delta > 0 ? 0 : this.visible.length - 1)
                    : Math.max(0, Math.min(this.visible.length - 1, this.activeIndex + delta));
                this.setActiveIndex(next);
                return;
            }

            if (event.key === "Enter") {
                if (this.activeIndex >= 0) {
                    event.preventDefault();
                    this.selectOption(this.visible[this.activeIndex]);
                    return;
                }
                if (!this.select.value) {
                    event.preventDefault();
                    if (!this.selectExactMatch()) {
                        this.hasShownValidation = true;
                        this.syncValidationState();
                        this.open();
                    }
                }
            }
        }
    }

    const initSearchSelects = () => {
        root.querySelectorAll("select[data-ffc-search-select]").forEach(select => {
            if (select.dataset.enhanced === "true") return;
            select.dataset.enhanced = "true";
            new SearchSelect(select);
        });
    };

    const parseJsonNode = id => {
        const node = document.getElementById(id);
        if (!node) return [];
        try {
            return JSON.parse(node.textContent || "[]");
        } catch {
            return [];
        }
    };

    const projectOptions = parseJsonNode("ffc-project-options").map(project => ({
        ...project,
        available: project.available !== false,
        search: normalise(`${project.name} ${project.lifecycle} ${project.secondary}`)
    }));
    const projectEditorData = new Map(
        parseJsonNode("ffc-project-editor-data").map(project => [String(project.id), project]));

    const projectForm = root.querySelector("[data-ffc-project-form]");
    const projectEditor = document.getElementById("ffcProjectEditor");
    const projectElements = projectForm ? {
        id: projectForm.querySelector("[data-ffc-project-id-input]"),
        version: projectForm.querySelector("[data-ffc-project-version-input]"),
        linkedValue: projectForm.querySelector("[data-ffc-project-value]"),
        search: projectForm.querySelector("[data-ffc-project-search]"),
        results: projectForm.querySelector("[data-ffc-project-results]"),
        clear: projectForm.querySelector("[data-ffc-project-clear]"),
        selectedMeta: projectForm.querySelector("[data-ffc-project-selected-meta]"),
        displayName: projectForm.querySelector("[data-ffc-project-name-input]"),
        quantity: projectForm.querySelector("[data-ffc-project-quantity]"),
        progress: projectForm.querySelector("[data-ffc-project-progress]"),
        linkedSection: projectForm.querySelector("[data-ffc-linked-project-section]"),
        nameLabel: projectForm.querySelector("[data-ffc-project-name-label]"),
        nameHelp: projectForm.querySelector("[data-ffc-project-name-help]"),
        progressHelp: projectForm.querySelector("[data-ffc-progress-help]"),
        linkedValidation: projectForm.querySelector('[data-valmsg-for="ProjectInput.LinkedProjectId"]'),
        deliveredField: projectForm.querySelector("[data-ffc-delivery-date-field]"),
        installedField: projectForm.querySelector("[data-ffc-installation-date-field]"),
        deliveredOn: projectForm.querySelector('[name="ProjectInput.DeliveredOn"]'),
        installedOn: projectForm.querySelector('[name="ProjectInput.InstalledOn"]'),
        title: document.querySelector("[data-ffc-project-editor-title]"),
        submit: projectForm.querySelector("[data-ffc-project-submit]")
    } : null;

    let projectPickerActiveIndex = -1;
    let projectPickerResults = [];
    let lastSelectedProjectId = projectElements?.linkedValue.value || "";
    let activeProjectSource = null;
    let activePosition = null;
    let sourceChangePending = false;
    let positionChangePending = false;

    const setProjectSelectionValidity = show => {
        if (!projectForm || !projectElements) return true;
        const linked = projectForm.querySelector('[data-ffc-project-source="linked"]')?.checked === true;
        const selected = projectOptions.find(project =>
            String(project.id) === String(projectElements.linkedValue.value));
        const valid = !linked || Boolean(
            selected && normalise(projectElements.search.value) === normalise(selected.name));
        projectElements.search.required = linked;
        projectElements.search.setCustomValidity(valid ? "" : "Select a PRISM project from the list.");
        projectElements.search.classList.toggle("is-invalid", !valid && show);
        projectElements.search.setAttribute("aria-invalid", !valid && show ? "true" : "false");
        return valid;
    };

    const setProjectSelection = (project, { populateName = true, clearProgressOnChange = true } = {}) => {
        if (!projectElements) return;
        const previousName = projectElements.search.value;
        const previousProjectId = lastSelectedProjectId;
        const nextProjectId = project ? String(project.id) : "";
        projectElements.linkedValue.value = nextProjectId;

        if (clearProgressOnChange && previousProjectId && previousProjectId !== nextProjectId) {
            projectElements.progress.value = "";
        }

        lastSelectedProjectId = nextProjectId;
        projectElements.search.value = project?.name || "";
        projectElements.selectedMeta.textContent = project?.secondary || "";
        projectElements.clear.hidden = !project;
        projectElements.search.setAttribute("aria-expanded", "false");
        projectElements.results.hidden = true;
        projectElements.search.removeAttribute("aria-activedescendant");

        const currentDisplay = projectElements.displayName.value.trim();
        if (project && populateName && (!currentDisplay || currentDisplay === previousName)) {
            projectElements.displayName.value = project.name;
        }

        projectElements.linkedValue.dispatchEvent(new Event("change", { bubbles: true }));
        setProjectSelectionValidity(false);
    };

    const renderProjectResults = query => {
        if (!projectElements) return;
        const term = normalise(query);
        projectPickerResults = projectOptions
            .filter(project => project.available || String(project.id) === String(lastSelectedProjectId))
            .filter(project => !term || project.search.includes(term))
            .slice(0, 30);
        projectPickerActiveIndex = -1;
        projectElements.search.removeAttribute("aria-activedescendant");
        projectElements.results.replaceChildren();

        if (!projectPickerResults.length) {
            const empty = document.createElement("div");
            empty.className = "ffc-project-picker__empty";
            empty.textContent = "No matching project found.";
            projectElements.results.appendChild(empty);
        } else {
            projectPickerResults.forEach((project, index) => {
                const button = document.createElement("button");
                button.id = `ffc-project-picker-option-${index}`;
                button.type = "button";
                button.className = "ffc-project-picker__option";
                button.setAttribute("role", "option");
                button.setAttribute("aria-selected", "false");
                const strong = document.createElement("strong");
                strong.textContent = project.name;
                const small = document.createElement("small");
                small.textContent = project.secondary || project.lifecycle || "";
                button.append(strong, small);
                button.addEventListener("pointerdown", event => event.preventDefault());
                button.addEventListener("click", () => setProjectSelection(project));
                projectElements.results.appendChild(button);
            });
        }

        projectElements.results.hidden = false;
        projectElements.search.setAttribute("aria-expanded", "true");
    };

    const closeProjectResults = () => {
        if (!projectElements) return;
        projectElements.results.hidden = true;
        projectElements.search.setAttribute("aria-expanded", "false");
        projectElements.search.removeAttribute("aria-activedescendant");
        projectPickerActiveIndex = -1;
    };

    const setProjectActiveIndex = index => {
        if (!projectElements) return;
        projectPickerActiveIndex = index;
        [...projectElements.results.querySelectorAll('[role="option"]')].forEach((button, optionIndex) => {
            const active = optionIndex === index;
            button.classList.toggle("is-active", active);
            button.setAttribute("aria-selected", active ? "true" : "false");
            if (active) {
                projectElements.search.setAttribute("aria-activedescendant", button.id);
                button.scrollIntoView({ block: "nearest" });
            }
        });
    };

    const syncProjectSource = () => {
        if (!projectForm || !projectElements) return;
        const linked = projectForm.querySelector('[data-ffc-project-source="linked"]')?.checked === true;
        const nextSource = linked ? "linked" : "unlinked";

        projectElements.linkedSection.hidden = !linked;
        projectElements.linkedValue.disabled = !linked;
        projectElements.search.disabled = !linked;
        projectElements.nameLabel.textContent = linked ? "FFC display title" : "FFC item name";
        projectElements.nameHelp.textContent = linked
            ? "Defaults to the linked Project name and may be adjusted for the FFC-specific quantity or scope."
            : "Enter a clear name for this FFC item. It will not be linked to a PRISM Project.";
        projectElements.progressHelp.textContent = linked
            ? "This is the same canonical external remark shown in the Project module and Detailed Table."
            : "This progress is stored only against the unlinked FFC item.";

        if (!linked) {
            projectElements.linkedValue.value = "";
            lastSelectedProjectId = "";
            projectElements.search.value = "";
            projectElements.selectedMeta.textContent = "";
            projectElements.clear.hidden = true;
            closeProjectResults();
        }

        activeProjectSource = nextSource;
        setProjectSelectionValidity(false);
    };

    const handleSourceChange = async event => {
        if (!projectForm || !projectElements || sourceChangePending) return;
        const nextSource = event.currentTarget.dataset.ffcProjectSource;
        const previousSource = activeProjectSource || nextSource;
        if (previousSource === nextSource) {
            syncProjectSource();
            return;
        }

        if (projectElements.progress.value.trim()) {
            sourceChangePending = true;
            const sourceRadios = [...projectForm.querySelectorAll("[data-ffc-project-source]")];
            sourceRadios.forEach(radio => { radio.disabled = true; });
            const confirmed = await confirmAction({
                title: "Change project source?",
                message: "Linked and unlinked items store progress in different records. Changing the source will clear the current progress text.",
                confirmText: "Change source",
                cancelText: "Keep current source",
                tone: "warning"
            });
            sourceRadios.forEach(radio => { radio.disabled = false; });
            sourceChangePending = false;
            if (!confirmed) {
                const previousRadio = projectForm.querySelector(`[data-ffc-project-source="${previousSource}"]`);
                if (previousRadio) previousRadio.checked = true;
                return;
            }
            projectElements.progress.value = "";
        }

        syncProjectSource();
    };

    const positionRank = value => ({
        Planned: 0,
        DeliveredAwaitingInstallation: 1,
        Installed: 2
    }[value] ?? 0);

    const syncPositionFields = () => {
        if (!projectForm || !projectElements) return;
        const value = projectForm.querySelector("[data-ffc-position-option]:checked")?.value || "Planned";
        const delivered = value !== "Planned";
        const installed = value === "Installed";
        projectElements.deliveredField.hidden = !delivered;
        projectElements.installedField.hidden = !installed;
        projectElements.deliveredOn.disabled = !delivered;
        projectElements.installedOn.disabled = !installed;
        if (!delivered) projectElements.deliveredOn.value = "";
        if (!installed) projectElements.installedOn.value = "";
        activePosition = value;
    };

    const handlePositionChange = async event => {
        if (!projectForm || !projectElements || positionChangePending) return;
        const nextPosition = event.currentTarget.value;
        const previousPosition = activePosition || "Planned";
        const willClearRecordedDate = positionRank(nextPosition) < positionRank(previousPosition) &&
            Boolean(projectElements.deliveredOn.value || projectElements.installedOn.value);

        if (willClearRecordedDate) {
            positionChangePending = true;
            const positionRadios = [...projectForm.querySelectorAll("[data-ffc-position-option]")];
            positionRadios.forEach(radio => { radio.disabled = true; });
            const confirmed = await confirmAction({
                title: "Change project position?",
                message: "Delivery or installation dates that no longer apply will be cleared.",
                confirmText: "Change position",
                cancelText: "Keep current position",
                tone: "warning"
            });
            positionRadios.forEach(radio => { radio.disabled = false; });
            positionChangePending = false;
            if (!confirmed) {
                const previousRadio = projectForm.querySelector(
                    `[data-ffc-position-option][value="${previousPosition}"]`);
                if (previousRadio) previousRadio.checked = true;
                return;
            }
        }

        syncPositionFields();
    };

    const setPosition = numericValue => {
        if (!projectForm) return;
        const mapping = {
            "0": "Planned",
            "1": "DeliveredAwaitingInstallation",
            "2": "Installed"
        };
        const value = mapping[String(numericValue)] || "Planned";
        const radio = projectForm.querySelector(`[data-ffc-position-option][value="${value}"]`);
        if (radio) radio.checked = true;
        syncPositionFields();
    };

    const resetProjectForm = () => {
        if (!projectForm || !projectElements) return;
        projectForm.reset();
        projectElements.id.value = "";
        projectElements.version.value = "";
        projectElements.linkedValue.value = "";
        lastSelectedProjectId = "";
        projectElements.search.value = "";
        projectElements.selectedMeta.textContent = "";
        projectElements.clear.hidden = true;
        projectElements.displayName.value = "";
        projectElements.quantity.value = "1";
        projectElements.progress.value = "";
        projectElements.deliveredOn.value = "";
        projectElements.installedOn.value = "";
        const linkedRadio = projectForm.querySelector('[data-ffc-project-source="linked"]');
        if (linkedRadio) linkedRadio.checked = true;
        setPosition("0");
        syncProjectSource();
        projectElements.title.textContent = "Add project";
        projectElements.submit.textContent = "Add project";
        projectElements.submit.dataset.ffcSubmittingText = "Adding…";
        projectForm.dispatchEvent(new CustomEvent("ffc:form-reset"));
    };

    const populateProjectForm = projectId => {
        if (!projectForm || !projectElements) return false;
        const data = projectEditorData.get(String(projectId));
        if (!data) return false;

        projectElements.id.value = data.id || "";
        projectElements.version.value = data.rowVersion || "";
        projectElements.displayName.value = data.name || "";
        projectElements.quantity.value = data.quantity || "1";
        projectElements.progress.value = data.progress || "";
        projectElements.deliveredOn.value = data.deliveredOn || "";
        projectElements.installedOn.value = data.installedOn || "";

        const linked = data.linked === true;
        const sourceRadio = projectForm.querySelector(`[data-ffc-project-source="${linked ? "linked" : "unlinked"}"]`);
        if (sourceRadio) sourceRadio.checked = true;
        syncProjectSource();

        if (linked) {
            const project = projectOptions.find(option => String(option.id) === String(data.linkedProjectId));
            lastSelectedProjectId = String(data.linkedProjectId || "");
            if (project) {
                setProjectSelection(project, { populateName: false, clearProgressOnChange: false });
            } else {
                projectElements.linkedValue.value = data.linkedProjectId || "";
                projectElements.search.value = data.linkedProjectName || "";
                projectElements.clear.hidden = !projectElements.linkedValue.value;
                setProjectSelectionValidity(false);
            }
        }

        setPosition(data.position || "0");
        projectElements.title.textContent = "Edit project";
        projectElements.submit.textContent = "Save changes";
        projectElements.submit.dataset.ffcSubmittingText = "Saving…";
        projectForm.dispatchEvent(new CustomEvent("ffc:form-reset"));
        return true;
    };

    const showProjectEditor = () => {
        if (!projectEditor || !window.bootstrap?.Offcanvas) return;
        window.bootstrap.Offcanvas.getOrCreateInstance(projectEditor).show();
    };

    const selectExactProject = () => {
        if (!projectElements) return false;
        const term = normalise(projectElements.search.value);
        if (!term) return false;
        const matches = projectOptions.filter(project => normalise(project.name) === term &&
            (project.available || String(project.id) === String(lastSelectedProjectId)));
        if (matches.length !== 1) return false;
        setProjectSelection(matches[0]);
        return true;
    };

    const initProjectEditor = () => {
        if (!projectForm || !projectElements) return;

        projectElements.search.addEventListener("focus", () => renderProjectResults(projectElements.search.value));
        projectElements.search.addEventListener("click", () => renderProjectResults(projectElements.search.value));
        projectElements.search.addEventListener("input", () => {
            const selected = projectOptions.find(project => String(project.id) === String(projectElements.linkedValue.value));
            if (!selected || normalise(projectElements.search.value) !== normalise(selected.name)) {
                if (projectElements.linkedValue.value) {
                    projectElements.linkedValue.value = "";
                    projectElements.linkedValue.dispatchEvent(new Event("change", { bubbles: true }));
                }
                projectElements.selectedMeta.textContent = "";
            }
            projectElements.clear.hidden = !projectElements.search.value;
            setProjectSelectionValidity(false);
            renderProjectResults(projectElements.search.value);
        });
        projectElements.search.addEventListener("blur", () => {
            queueMicrotask(() => {
                if (projectElements.search.closest("[data-ffc-project-picker]")?.contains(document.activeElement)) return;
                setProjectSelectionValidity(Boolean(projectElements.search.value));
            });
        });
        projectElements.search.addEventListener("invalid", () => setProjectSelectionValidity(true));
        projectElements.clear.addEventListener("click", () => {
            setProjectSelection(null);
            projectElements.search.focus();
            renderProjectResults("");
        });
        projectElements.search.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                event.preventDefault();
                closeProjectResults();
                if (!projectElements.linkedValue.value && lastSelectedProjectId) {
                    const previous = projectOptions.find(project =>
                        String(project.id) === String(lastSelectedProjectId));
                    if (previous) {
                        setProjectSelection(previous, {
                            populateName: false,
                            clearProgressOnChange: false
                        });
                    }
                }
                return;
            }
            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();
                if (projectElements.results.hidden) renderProjectResults(projectElements.search.value);
                if (!projectPickerResults.length) return;
                const delta = event.key === "ArrowDown" ? 1 : -1;
                const next = projectPickerActiveIndex < 0
                    ? (delta > 0 ? 0 : projectPickerResults.length - 1)
                    : Math.max(0, Math.min(projectPickerResults.length - 1, projectPickerActiveIndex + delta));
                setProjectActiveIndex(next);
                return;
            }
            if (event.key === "Enter") {
                if (projectPickerActiveIndex >= 0) {
                    event.preventDefault();
                    setProjectSelection(projectPickerResults[projectPickerActiveIndex]);
                    return;
                }
                if (!projectElements.linkedValue.value) {
                    event.preventDefault();
                    if (!selectExactProject()) {
                        setProjectSelectionValidity(true);
                        renderProjectResults(projectElements.search.value);
                    }
                }
            }
        });

        document.addEventListener("pointerdown", event => {
            if (!event.target.closest("[data-ffc-project-picker]")) closeProjectResults();
        });

        projectForm.querySelectorAll("[data-ffc-project-source]").forEach(radio =>
            radio.addEventListener("change", handleSourceChange));
        projectForm.querySelectorAll("[data-ffc-position-option]").forEach(radio =>
            radio.addEventListener("change", handlePositionChange));

        root.querySelectorAll("[data-ffc-new-project]").forEach(button => {
            button.addEventListener("click", () => {
                resetProjectForm();
                showProjectEditor();
            });
        });

        root.querySelectorAll("[data-ffc-edit-project]").forEach(button => {
            button.addEventListener("click", () => {
                if (populateProjectForm(button.dataset.projectId)) showProjectEditor();
            });
        });

        syncProjectSource();
        syncPositionFields();
        projectElements.clear.hidden = !projectElements.linkedValue.value;
        setProjectSelectionValidity(false);
    };

    const formState = new WeakMap();

    const serializeForm = form => {
        const data = new FormData(form);
        const pairs = [];
        for (const [key, value] of data.entries()) {
            if (key === "__RequestVerificationToken") continue;
            if (value instanceof File) {
                if (!value.name && value.size === 0) continue;
                pairs.push([key, value.name, value.size, value.lastModified]);
            } else {
                pairs.push([key, String(value)]);
            }
        }
        return JSON.stringify(pairs);
    };

    const hasValidationErrors = form => Boolean(
        form.querySelector(".input-validation-error, .validation-summary-errors, .field-validation-error:not(:empty)"));

    const captureControlState = form => [...form.elements].map((control, index) => ({
        index,
        type: String(control.type || control.tagName).toLowerCase(),
        value: control.value,
        checked: "checked" in control ? control.checked : null,
        selectedValues: control instanceof HTMLSelectElement
            ? [...control.options].filter(option => option.selected).map(option => option.value)
            : null
    }));

    const refreshRestoredFormUi = form => {
        form.querySelectorAll("select[data-ffc-search-select]").forEach(select => {
            select.dispatchEvent(new Event("change", { bubbles: true }));
        });

        form.querySelectorAll("[data-ffc-milestone-editor]").forEach(editor => {
            editor.querySelector('input[type="radio"]:checked')
                ?.dispatchEvent(new Event("change", { bubbles: true }));
        });

        if (form === projectForm && projectElements) {
            const source = form.querySelector("[data-ffc-project-source]:checked")?.dataset.ffcProjectSource || "linked";
            activeProjectSource = source;
            const position = form.querySelector("[data-ffc-position-option]:checked")?.value || "Planned";
            activePosition = position;
            syncProjectSource();
            syncPositionFields();

            const selected = projectOptions.find(project =>
                String(project.id) === String(projectElements.linkedValue.value));
            lastSelectedProjectId = projectElements.linkedValue.value || "";
            projectElements.search.value = selected?.name || "";
            projectElements.selectedMeta.textContent = selected?.secondary || "";
            projectElements.clear.hidden = !selected;
            closeProjectResults();
            setProjectSelectionValidity(false);
        }

        const uploadInput = form.querySelector("[data-ffc-upload-input]");
        if (uploadInput) uploadInput.dispatchEvent(new Event("change", { bubbles: true }));
    };

    const restoreBaseline = form => {
        const state = formState.get(form);
        if (!state?.controls) return;

        state.controls.forEach(snapshot => {
            const control = form.elements[snapshot.index];
            if (!control) return;
            if (snapshot.type === "file") {
                control.value = "";
                return;
            }
            if (snapshot.selectedValues && control instanceof HTMLSelectElement) {
                const selected = new Set(snapshot.selectedValues);
                [...control.options].forEach(option => {
                    option.selected = selected.has(option.value);
                });
                return;
            }
            if (snapshot.checked !== null && "checked" in control) {
                control.checked = snapshot.checked;
            }
            control.value = snapshot.value;
        });

        refreshRestoredFormUi(form);
        state.forceDirty = false;
        state.baseline = serializeForm(form);
        state.controls = captureControlState(form);
    };

    const markBaseline = (form, { preserveValidationErrors = true } = {}) => {
        formState.set(form, {
            baseline: serializeForm(form),
            controls: captureControlState(form),
            submitting: false,
            allowClose: false,
            confirmPending: false,
            forceDirty: preserveValidationErrors && hasValidationErrors(form)
        });
    };

    const isDirty = form => {
        const state = formState.get(form);
        return Boolean(state && (state.forceDirty || state.baseline !== serializeForm(form)));
    };

    const isActiveForm = form => {
        const drawer = form.closest(".offcanvas");
        return !drawer || drawer.classList.contains("show");
    };

    const initDirtyForms = () => {
        const forms = [...root.querySelectorAll("[data-ffc-dirty-form]")];
        forms.forEach(form => {
            markBaseline(form);
            form.addEventListener("ffc:form-reset", () => markBaseline(form, { preserveValidationErrors: false }));
        });

        document.querySelectorAll(".ffc-workspace-drawer").forEach(drawer => {
            const form = drawer.querySelector("[data-ffc-dirty-form]");
            if (!form) return;

            drawer.addEventListener("shown.bs.offcanvas", () => {
                const current = formState.get(form);
                if (!current?.forceDirty) markBaseline(form, { preserveValidationErrors: false });
            });

            drawer.addEventListener("hide.bs.offcanvas", event => {
                const state = formState.get(form);
                if (!state || state.submitting || state.allowClose || !isDirty(form)) return;
                event.preventDefault();
                if (state.confirmPending) return;
                state.confirmPending = true;

                confirmAction({
                    title: "Discard unsaved changes?",
                    message: "Changes made in this editor have not been saved.",
                    confirmText: "Discard changes",
                    cancelText: "Continue editing",
                    tone: "danger"
                }).then(confirmed => {
                    state.confirmPending = false;
                    if (!confirmed) return;
                    restoreBaseline(form);
                    state.allowClose = true;
                    window.bootstrap?.Offcanvas.getOrCreateInstance(drawer).hide();
                });
            });

            drawer.addEventListener("hidden.bs.offcanvas", () => {
                const state = formState.get(form);
                if (!state) return;
                state.allowClose = false;
                state.confirmPending = false;
                state.forceDirty = false;
            });
        });

        root.querySelectorAll("[data-ffc-cancel-link]").forEach(link => {
            link.addEventListener("click", async event => {
                const form = link.closest("form");
                if (!form || !isDirty(form)) return;
                event.preventDefault();
                const confirmed = await confirmAction({
                    title: "Discard unsaved changes?",
                    message: "The new FFC record has not been created and your entered information will be lost.",
                    confirmText: "Discard changes",
                    cancelText: "Continue editing",
                    tone: "danger"
                });
                if (confirmed) window.location.assign(link.href);
            });
        });

        window.addEventListener("beforeunload", event => {
            const dirty = forms.some(form => {
                const state = formState.get(form);
                return isActiveForm(form) && state && !state.submitting && isDirty(form);
            });
            if (!dirty) return;
            event.preventDefault();
            event.returnValue = "";
        });
    };

    const formIsValid = form => {
        if (!form.checkValidity()) return false;
        const jq = window.jQuery;
        if (jq?.validator && typeof jq(form).valid === "function" && !jq(form).valid()) return false;
        return true;
    };

    const setSubmitButtonBusy = (button, busy) => {
        if (!button) return;
        if (busy) {
            if (!button.dataset.ffcOriginalHtml) button.dataset.ffcOriginalHtml = button.innerHTML;
            const text = button.dataset.ffcSubmittingText || "Working…";
            button.innerHTML = `<span class="spinner-border spinner-border-sm" aria-hidden="true"></span><span>${text}</span>`;
            button.disabled = true;
            button.setAttribute("aria-busy", "true");
            return;
        }
        if (button.dataset.ffcOriginalHtml) button.innerHTML = button.dataset.ffcOriginalHtml;
        button.disabled = false;
        button.removeAttribute("aria-busy");
    };

    const initSubmissionController = () => {
        const forms = [...root.querySelectorAll('form[method="post"]')];
        forms.forEach(form => {
            form.addEventListener("submit", event => {
                if (event.defaultPrevented) return;
                if (form.dataset.ffcSubmitLocked === "true") {
                    event.preventDefault();
                    return;
                }
                if (!formIsValid(form)) return;

                form.dataset.ffcSubmitLocked = "true";
                form.setAttribute("aria-busy", "true");
                const state = formState.get(form);
                if (state) state.submitting = true;

                const submitter = event.submitter || form.querySelector('button[type="submit"], input[type="submit"]');
                setSubmitButtonBusy(submitter, true);
                form.querySelectorAll('button[type="submit"], input[type="submit"]').forEach(button => {
                    if (button !== submitter) button.disabled = true;
                });
            });
        });

        window.addEventListener("pageshow", event => {
            if (!event.persisted) return;
            forms.forEach(form => {
                delete form.dataset.ffcSubmitLocked;
                form.removeAttribute("aria-busy");
                form.querySelectorAll('button[type="submit"], input[type="submit"]').forEach(button => {
                    setSubmitButtonBusy(button, false);
                });
                const state = formState.get(form);
                if (state) state.submitting = false;
            });
        });
    };

    const initDeleteModals = () => {
        const projectModalElement = document.getElementById("ffcDeleteProjectModal");
        root.querySelectorAll("[data-ffc-delete-project]").forEach(button => {
            button.addEventListener("click", () => {
                if (!projectModalElement || !window.bootstrap?.Modal) return;
                const linked = button.dataset.projectLinkedName;
                projectModalElement.querySelector("[data-ffc-delete-project-name]").textContent = button.dataset.projectName || "Project";
                projectModalElement.querySelector("[data-ffc-delete-project-detail]").textContent = linked
                    ? `Quantity ${button.dataset.projectQuantity || "1"}. The FFC link will be removed; the underlying PRISM Project will not be deleted.`
                    : `Quantity ${button.dataset.projectQuantity || "1"}. The unlinked FFC item will be removed from this record.`;
                projectModalElement.querySelector("[data-ffc-delete-project-id]").value = button.dataset.projectId || "";
                projectModalElement.querySelector("[data-ffc-delete-project-version]").value = button.dataset.projectRowVersion || "";
                window.bootstrap.Modal.getOrCreateInstance(projectModalElement).show();
            });
        });

        const attachmentModalElement = document.getElementById("ffcDeleteAttachmentModal");
        root.querySelectorAll("[data-ffc-delete-attachment]").forEach(button => {
            button.addEventListener("click", () => {
                if (!attachmentModalElement || !window.bootstrap?.Modal) return;
                attachmentModalElement.querySelector("[data-ffc-delete-attachment-label]").textContent = button.dataset.attachmentLabel || "this attachment";
                attachmentModalElement.querySelector("[data-ffc-delete-attachment-id]").value = button.dataset.attachmentId || "";
                window.bootstrap.Modal.getOrCreateInstance(attachmentModalElement).show();
            });
        });
    };

    const initUpload = () => {
        const zone = root.querySelector("[data-ffc-upload-zone]");
        const input = root.querySelector("[data-ffc-upload-input]");
        const selection = root.querySelector("[data-ffc-upload-selection]");
        const submit = root.querySelector("[data-ffc-upload-submit]");
        const clientError = root.querySelector("[data-ffc-upload-client-error]");
        if (!zone || !input || !selection) return;

        const maxSize = Number(input.dataset.maxFileSize || 0);
        const allowedTypes = new Set(["application/pdf", "image/jpeg", "image/png", "image/webp"]);

        const sync = () => {
            const file = input.files?.[0];
            let error = "";
            if (!file) {
                error = "Select a file to upload.";
            } else if (maxSize > 0 && file.size > maxSize) {
                error = "The selected file exceeds the maximum permitted size.";
            } else if (file.type && !allowedTypes.has(file.type)) {
                error = "Select a PDF, JPEG, PNG or WEBP file.";
            }

            input.setCustomValidity(error);
            const showError = Boolean(file && error);
            zone.classList.toggle("is-invalid", showError);
            if (clientError) {
                clientError.textContent = showError ? error : "";
                clientError.hidden = !showError;
            }
            selection.textContent = file
                ? `${file.name} · ${Math.max(1, Math.round(file.size / 1024))} KB`
                : "No file selected";
            selection.title = file?.name || "";
            if (submit) submit.disabled = !file || Boolean(error);
        };

        input.required = true;
        input.addEventListener("invalid", () => {
            zone.classList.add("is-invalid");
            if (clientError) {
                clientError.textContent = input.validationMessage || "Select a file to upload.";
                clientError.hidden = false;
            }
        });
        input.addEventListener("change", sync);
        ["dragenter", "dragover"].forEach(type => zone.addEventListener(type, event => {
            event.preventDefault();
            zone.classList.add("is-dragover");
        }));
        ["dragleave", "drop"].forEach(type => zone.addEventListener(type, event => {
            event.preventDefault();
            zone.classList.remove("is-dragover");
        }));
        zone.addEventListener("drop", event => {
            const files = event.dataTransfer?.files;
            if (!files?.length) return;
            const transfer = new DataTransfer();
            transfer.items.add(files[0]);
            input.files = transfer.files;
            input.dispatchEvent(new Event("change", { bubbles: true }));
        });
        sync();
    };

    const initRestoreConfirmation = () => {
        root.querySelectorAll("[data-ffc-restore-record]").forEach(form => {
            form.addEventListener("submit", async event => {
                if (form.dataset.ffcRestoreConfirmed === "true") {
                    delete form.dataset.ffcRestoreConfirmed;
                    return;
                }
                event.preventDefault();
                if (form.dataset.ffcRestoreConfirmPending === "true") return;
                form.dataset.ffcRestoreConfirmPending = "true";
                const submitter = event.submitter;
                const confirmed = await confirmAction({
                    title: "Restore this FFC record?",
                    message: "The country-year record, its projects and attachments will return to the active FFC portfolio.",
                    confirmText: "Restore record",
                    cancelText: "Keep archived",
                    tone: "primary"
                });
                delete form.dataset.ffcRestoreConfirmPending;
                if (!confirmed) return;
                form.dataset.ffcRestoreConfirmed = "true";
                if (submitter) form.requestSubmit(submitter);
                else form.requestSubmit();
            }, true);
        });
    };

    const autoOpenEditor = () => {
        const workspaceRoot = document.querySelector("[data-ffc-workspace]");
        if (!workspaceRoot || !window.bootstrap?.Offcanvas) return;
        const editor = String(workspaceRoot.dataset.openEditor || "").toLowerCase();
        const target = editor === "record"
            ? document.getElementById("ffcRecordEditor")
            : editor === "project"
                ? document.getElementById("ffcProjectEditor")
                : editor === "attachment"
                    ? document.getElementById("ffcAttachmentEditor")
                    : null;
        if (target) window.bootstrap.Offcanvas.getOrCreateInstance(target).show();
    };

    initMilestones();
    initSearchSelects();
    initProjectEditor();
    initDirtyForms();
    initRestoreConfirmation();
    initSubmissionController();
    initDeleteModals();
    initUpload();
    autoOpenEditor();
})();
