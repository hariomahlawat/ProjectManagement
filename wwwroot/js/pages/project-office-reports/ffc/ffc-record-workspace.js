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

    const escapeText = value => String(value ?? "");

    const initMilestones = () => {
        root.querySelectorAll("[data-ffc-milestone-editor]").forEach(editor => {
            const radios = [...editor.querySelectorAll('input[type="radio"]')];
            if (!radios.length) return;

            const sync = () => {
                const completed = radios.find(radio => radio.checked)?.value === "true";
                editor.classList.toggle("is-pending", !completed);
                editor.querySelectorAll("[data-ffc-milestone-date] input")
                    .forEach(control => {
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
            this.build();
            this.bind();
            this.syncFromSelect();
        }

        build() {
            this.select.classList.add("ffc-search-select__native");
            const wrapper = document.createElement("div");
            wrapper.className = "ffc-search-select";
            const control = document.createElement("div");
            control.className = "ffc-search-select__control";
            this.input = document.createElement("input");
            this.input.type = "search";
            this.input.className = "form-control ffc-search-select__input";
            this.input.placeholder = this.select.dataset.placeholder || "Search or select";
            this.input.autocomplete = "off";
            this.input.setAttribute("role", "combobox");
            this.input.setAttribute("aria-autocomplete", "list");
            this.input.setAttribute("aria-expanded", "false");
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

            if (this.select.classList.contains("input-validation-error")) {
                this.input.classList.add("input-validation-error", "is-invalid");
            }
        }

        bind() {
            this.input.addEventListener("focus", () => this.open());
            this.input.addEventListener("click", () => this.open());
            this.input.addEventListener("input", () => this.render(this.input.value));
            this.toggle.addEventListener("click", () => this.list.hidden ? this.open() : this.close());
            this.input.addEventListener("keydown", event => this.onKeydown(event));
            this.select.addEventListener("change", () => this.syncFromSelect());
            document.addEventListener("pointerdown", event => {
                if (!this.wrapper.contains(event.target)) this.close();
            });
        }

        open() {
            this.render(this.input.value);
            this.list.hidden = false;
            this.input.setAttribute("aria-expanded", "true");
        }

        close() {
            this.list.hidden = true;
            this.activeIndex = -1;
            this.input.setAttribute("aria-expanded", "false");
        }

        render(query) {
            const term = normalise(query);
            this.visible = this.options
                .filter(option => !term || option.search.includes(term))
                .slice(0, 80);
            this.activeIndex = -1;
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
                button.type = "button";
                button.className = "ffc-search-select__option";
                button.setAttribute("role", "option");
                button.innerHTML = "";
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
            this.input.value = option.label;
            this.select.dispatchEvent(new Event("change", { bubbles: true }));
            this.close();
        }

        syncFromSelect() {
            const option = this.options.find(item => item.value === this.select.value);
            this.input.value = option?.label || "";
        }

        onKeydown(event) {
            if (event.key === "Escape") {
                this.close();
                this.syncFromSelect();
                return;
            }

            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();
                if (this.list.hidden) this.open();
                if (!this.visible.length) return;
                const delta = event.key === "ArrowDown" ? 1 : -1;
                this.activeIndex = Math.max(0, Math.min(this.visible.length - 1, this.activeIndex + delta));
                [...this.list.querySelectorAll("button")].forEach((button, index) => {
                    button.classList.toggle("is-active", index === this.activeIndex);
                    if (index === this.activeIndex) button.scrollIntoView({ block: "nearest" });
                });
                return;
            }

            if (event.key === "Enter" && this.activeIndex >= 0) {
                event.preventDefault();
                this.selectOption(this.visible[this.activeIndex]);
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

    const parseProjectOptions = () => {
        const node = document.getElementById("ffc-project-options");
        if (!node) return [];
        try {
            return JSON.parse(node.textContent || "[]").map(project => ({
                ...project,
                available: project.available !== false,
                search: normalise(`${project.name} ${project.lifecycle} ${project.secondary}`)
            }));
        } catch {
            return [];
        }
    };

    const projectForm = root.querySelector("[data-ffc-project-form]");
    const projectEditor = document.getElementById("ffcProjectEditor");
    const projectOptions = parseProjectOptions();

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

    const setProjectSelection = (project, { populateName = true, clearProgressOnChange = true } = {}) => {
        if (!projectElements) return;
        const previousName = projectElements.search.value;
        const previousProjectId = lastSelectedProjectId;
        const nextProjectId = project ? String(project.id) : "";
        projectElements.linkedValue.value = nextProjectId;
        lastSelectedProjectId = nextProjectId;

        if (clearProgressOnChange && previousProjectId && previousProjectId !== nextProjectId) {
            // Progress is canonical to the linked PRISM Project. Never carry one project's
            // external remark into a newly selected project.
            projectElements.progress.value = "";
        }
        projectElements.search.value = project?.name || "";
        projectElements.selectedMeta.textContent = project?.secondary || "";
        projectElements.clear.hidden = !project;
        projectElements.search.setAttribute("aria-expanded", "false");
        projectElements.results.hidden = true;

        const currentDisplay = projectElements.displayName.value.trim();
        if (project && populateName && (!currentDisplay || currentDisplay === previousName)) {
            projectElements.displayName.value = project.name;
        }

        projectElements.linkedValue.dispatchEvent(new Event("change", { bubbles: true }));
    };

    const renderProjectResults = query => {
        if (!projectElements) return;
        const term = normalise(query);
        projectPickerResults = projectOptions
            .filter(project => project.available || String(project.id) === String(lastSelectedProjectId))
            .filter(project => !term || project.search.includes(term))
            .slice(0, 30);
        projectPickerActiveIndex = -1;
        projectElements.results.replaceChildren();

        if (!projectPickerResults.length) {
            const empty = document.createElement("div");
            empty.className = "ffc-project-picker__empty";
            empty.textContent = "No matching project found.";
            projectElements.results.appendChild(empty);
        } else {
            projectPickerResults.forEach((project, index) => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "ffc-project-picker__option";
                button.setAttribute("role", "option");
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
        projectPickerActiveIndex = -1;
    };

    let activeProjectSource = null;

    const syncProjectSource = ({ userInitiated = false } = {}) => {
        if (!projectForm || !projectElements) return;
        const linked = projectForm.querySelector('[data-ffc-project-source="linked"]')?.checked === true;
        const nextSource = linked ? "linked" : "unlinked";

        if (userInitiated && activeProjectSource && activeProjectSource !== nextSource && projectElements.progress.value.trim()) {
            const confirmed = window.confirm(
                "Changing the project source will clear the current progress text because linked and unlinked items use different progress records. Continue?");
            if (!confirmed) {
                const previousRadio = projectForm.querySelector(`[data-ffc-project-source="${activeProjectSource}"]`);
                if (previousRadio) previousRadio.checked = true;
                return;
            }
            projectElements.progress.value = "";
        }

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
            closeProjectResults();
        }

        activeProjectSource = nextSource;
    };

    const positionRank = value => ({
        Planned: 0,
        DeliveredAwaitingInstallation: 1,
        Installed: 2
    }[value] ?? 0);

    let activePosition = null;

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

    const handlePositionChange = event => {
        if (!projectForm || !projectElements) return;
        const nextPosition = event.currentTarget.value;
        const previousPosition = activePosition || "Planned";
        const willClearRecordedDate = positionRank(nextPosition) < positionRank(previousPosition) &&
            (projectElements.deliveredOn.value || projectElements.installedOn.value);

        if (willClearRecordedDate && !window.confirm(
            "Changing the position will clear delivery or installation dates that no longer apply. Continue?")) {
            const previousRadio = projectForm.querySelector(
                `[data-ffc-position-option][value="${previousPosition}"]`);
            if (previousRadio) previousRadio.checked = true;
            return;
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
        projectForm.dispatchEvent(new CustomEvent("ffc:form-reset"));
    };

    const populateProjectForm = trigger => {
        if (!projectForm || !projectElements) return;
        const data = trigger.dataset;
        projectElements.id.value = data.projectId || "";
        projectElements.version.value = data.projectRowVersion || "";
        projectElements.displayName.value = data.projectName || "";
        projectElements.quantity.value = data.projectQuantity || "1";
        projectElements.progress.value = data.projectProgress || "";
        projectElements.deliveredOn.value = data.projectDeliveredOn || "";
        projectElements.installedOn.value = data.projectInstalledOn || "";

        const linked = data.projectLinked === "true";
        const sourceRadio = projectForm.querySelector(`[data-ffc-project-source="${linked ? "linked" : "unlinked"}"]`);
        if (sourceRadio) sourceRadio.checked = true;
        syncProjectSource();

        if (linked) {
            const project = projectOptions.find(option => String(option.id) === String(data.projectLinkedId));
            lastSelectedProjectId = String(data.projectLinkedId || "");
            if (project) {
                setProjectSelection(project, { populateName: false, clearProgressOnChange: false });
            } else {
                projectElements.linkedValue.value = data.projectLinkedId || "";
                lastSelectedProjectId = projectElements.linkedValue.value;
                projectElements.search.value = data.projectLinkedName || "";
            }
        }

        setPosition(data.projectPosition || "0");
        projectElements.title.textContent = "Edit project";
        projectElements.submit.textContent = "Save changes";
        projectForm.dispatchEvent(new CustomEvent("ffc:form-reset"));
    };

    const showProjectEditor = () => {
        if (!projectEditor || !window.bootstrap?.Offcanvas) return;
        window.bootstrap.Offcanvas.getOrCreateInstance(projectEditor).show();
    };

    const initProjectEditor = () => {
        if (!projectForm || !projectElements) return;

        projectElements.search.addEventListener("focus", () => renderProjectResults(projectElements.search.value));
        projectElements.search.addEventListener("click", () => renderProjectResults(projectElements.search.value));
        projectElements.search.addEventListener("input", () => {
            if (projectElements.linkedValue.value) projectElements.linkedValue.value = "";
            renderProjectResults(projectElements.search.value);
        });
        projectElements.clear.addEventListener("click", () => {
            setProjectSelection(null);
            projectElements.search.focus();
            renderProjectResults("");
        });
        projectElements.search.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                closeProjectResults();
                return;
            }
            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();
                if (projectElements.results.hidden) renderProjectResults(projectElements.search.value);
                if (!projectPickerResults.length) return;
                const delta = event.key === "ArrowDown" ? 1 : -1;
                projectPickerActiveIndex = Math.max(0, Math.min(projectPickerResults.length - 1, projectPickerActiveIndex + delta));
                [...projectElements.results.querySelectorAll("button")].forEach((button, index) => {
                    button.classList.toggle("is-active", index === projectPickerActiveIndex);
                    if (index === projectPickerActiveIndex) button.scrollIntoView({ block: "nearest" });
                });
                return;
            }
            if (event.key === "Enter" && projectPickerActiveIndex >= 0) {
                event.preventDefault();
                setProjectSelection(projectPickerResults[projectPickerActiveIndex]);
            }
        });

        document.addEventListener("pointerdown", event => {
            if (!event.target.closest("[data-ffc-project-picker]")) closeProjectResults();
        });

        projectForm.querySelectorAll("[data-ffc-project-source]").forEach(radio =>
            radio.addEventListener("change", () => syncProjectSource({ userInitiated: true })));
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
                populateProjectForm(button);
                showProjectEditor();
            });
        });

        syncProjectSource();
        syncPositionFields();
        projectElements.clear.hidden = !projectElements.linkedValue.value;
    };

    const formState = new WeakMap();

    const serializeForm = form => {
        const data = new FormData(form);
        const pairs = [];
        for (const [key, value] of data.entries()) {
            if (key === "__RequestVerificationToken") continue;
            if (value instanceof File) {
                pairs.push([key, value.name, value.size, value.lastModified]);
            } else {
                pairs.push([key, String(value)]);
            }
        }
        return JSON.stringify(pairs);
    };

    const markBaseline = form => {
        formState.set(form, {
            baseline: serializeForm(form),
            submitting: false,
            allowClose: false
        });
    };

    const isDirty = form => {
        const state = formState.get(form);
        return Boolean(state && state.baseline !== serializeForm(form));
    };

    const initDirtyForms = () => {
        const forms = [...root.querySelectorAll("[data-ffc-dirty-form]")];
        forms.forEach(form => {
            markBaseline(form);
            form.addEventListener("submit", () => {
                const state = formState.get(form);
                if (state) state.submitting = true;
            });
            form.addEventListener("ffc:form-reset", () => markBaseline(form));
        });

        document.querySelectorAll(".ffc-workspace-drawer").forEach(drawer => {
            const form = drawer.querySelector("[data-ffc-dirty-form]");
            if (!form) return;
            drawer.addEventListener("shown.bs.offcanvas", () => markBaseline(form));
            drawer.addEventListener("hide.bs.offcanvas", event => {
                const state = formState.get(form);
                if (!state || state.submitting || state.allowClose || !isDirty(form)) return;
                if (!window.confirm("Discard unsaved changes?")) {
                    event.preventDefault();
                    return;
                }
                state.allowClose = true;
            });
            drawer.addEventListener("hidden.bs.offcanvas", () => {
                const state = formState.get(form);
                if (state) state.allowClose = false;
            });
        });

        root.querySelectorAll("[data-ffc-cancel-link]").forEach(link => {
            link.addEventListener("click", event => {
                const form = link.closest("form");
                if (form && isDirty(form) && !window.confirm("Discard unsaved changes?")) {
                    event.preventDefault();
                }
            });
        });

        window.addEventListener("beforeunload", event => {
            const dirty = forms.some(form => {
                const state = formState.get(form);
                return state && !state.submitting && isDirty(form);
            });
            if (!dirty) return;
            event.preventDefault();
            event.returnValue = "";
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
        if (!zone || !input || !selection) return;

        const sync = () => {
            const file = input.files?.[0];
            selection.textContent = file ? `${file.name} · ${Math.max(1, Math.round(file.size / 1024))} KB` : "No file selected";
        };

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
            form.addEventListener("submit", event => {
                if (!window.confirm("Restore this FFC record to the active portfolio?")) {
                    event.preventDefault();
                }
            });
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
    initDeleteModals();
    initUpload();
    initRestoreConfirmation();
    autoOpenEditor();
})();
