(() => {
    "use strict";

    const states = new WeakMap();
    let activePicker = null;

    const overlay = document.createElement("div");
    overlay.className = "arpp-project-picker-overlay d-none";
    overlay.setAttribute("role", "listbox");
    overlay.id = "arpp-project-picker-overlay";
    document.body.appendChild(overlay);

    const rootUrl = () => document.querySelector("[data-arpp-project-lookup-url]")?.dataset.arppProjectLookupUrl
        || `${document.baseURI.replace(/\/$/, "")}/api/arpp/projects`;

    const stateFor = picker => {
        let state = states.get(picker);
        if (!state) {
            state = { timer: null, controller: null, projects: [], activeIndex: -1 };
            states.set(picker, state);
        }
        return state;
    };

    const elements = picker => ({
        id: picker.querySelector("[data-arpp-project-id]"),
        search: picker.querySelector("[data-arpp-project-search]"),
        metaInput: picker.querySelector("[data-arpp-project-meta-input]"),
        meta: picker.querySelector("[data-arpp-project-meta]"),
        clear: picker.querySelector("[data-arpp-project-clear]")
    });

    const positionOverlay = () => {
        if (!activePicker || overlay.classList.contains("d-none")) return;
        const search = elements(activePicker).search;
        if (!search) return;
        const rect = search.getBoundingClientRect();
        const availableBelow = window.innerHeight - rect.bottom - 12;
        const availableAbove = rect.top - 12;
        const openAbove = availableBelow < 220 && availableAbove > availableBelow;
        const maxHeight = Math.max(140, Math.min(320, openAbove ? availableAbove : availableBelow));

        overlay.style.position = "fixed";
        overlay.style.left = `${Math.max(8, rect.left)}px`;
        overlay.style.width = `${Math.max(280, rect.width)}px`;
        overlay.style.maxWidth = `${Math.max(280, window.innerWidth - Math.max(8, rect.left) - 8)}px`;
        overlay.style.maxHeight = `${maxHeight}px`;
        overlay.style.top = openAbove ? "auto" : `${rect.bottom + 4}px`;
        overlay.style.bottom = openAbove ? `${window.innerHeight - rect.top + 4}px` : "auto";
    };

    const close = picker => {
        if (picker && activePicker !== picker) return;
        activePicker = null;
        overlay.classList.add("d-none");
        overlay.replaceChildren();
        overlay.removeAttribute("aria-label");
        document.querySelectorAll("[data-arpp-project-search][aria-expanded='true']")
            .forEach(input => input.setAttribute("aria-expanded", "false"));
    };

    const setActive = (picker, index) => {
        const state = stateFor(picker);
        const buttons = Array.from(overlay.querySelectorAll("[role='option']"));
        if (!buttons.length) {
            state.activeIndex = -1;
            return;
        }
        state.activeIndex = Math.max(0, Math.min(index, buttons.length - 1));
        buttons.forEach((button, buttonIndex) => {
            const active = buttonIndex === state.activeIndex;
            button.classList.toggle("is-active", active);
            button.setAttribute("aria-selected", active ? "true" : "false");
            if (active) {
                elements(picker).search?.setAttribute("aria-activedescendant", button.id);
                button.scrollIntoView({ block: "nearest" });
            }
        });
    };

    const clearProject = (picker, clearSearch = false, notify = true) => {
        const { id, search, metaInput, meta, clear } = elements(picker);
        if (id) id.value = "";
        if (clearSearch && search) search.value = "";
        if (search) {
            delete search.dataset.selectedName;
            search.removeAttribute("aria-activedescendant");
        }
        if (metaInput) metaInput.value = "";
        if (meta) meta.textContent = "";
        clear?.classList.add("d-none");
        if (notify) picker.dispatchEvent(new CustomEvent("arpp:project-cleared", { bubbles: true }));
    };

    const selectProject = (picker, project) => {
        const { id, search, metaInput, meta, clear } = elements(picker);
        const projectMeta = [project.caseFileNumber, project.statusLabel].filter(Boolean).join(" · ");
        if (id) {
            id.value = String(project.id);
            id.dispatchEvent(new Event("change", { bubbles: true }));
        }
        if (search) {
            search.value = project.name;
            search.dataset.selectedName = project.name;
            search.removeAttribute("aria-activedescendant");
        }
        if (metaInput) metaInput.value = projectMeta;
        if (meta) meta.textContent = projectMeta;
        clear?.classList.remove("d-none");
        close(picker);
        picker.dispatchEvent(new CustomEvent("arpp:project-selected", {
            bubbles: true,
            detail: { project }
        }));
    };

    const render = (picker, projects) => {
        activePicker = picker;
        const state = stateFor(picker);
        state.projects = projects;
        state.activeIndex = -1;
        overlay.replaceChildren();
        overlay.setAttribute("aria-label", "Matching PRISM projects");

        if (!projects.length) {
            const empty = document.createElement("div");
            empty.className = "arpp-project-picker__empty";
            empty.textContent = "No matching PRISM projects";
            overlay.appendChild(empty);
        } else {
            projects.forEach((project, index) => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "arpp-project-picker__result";
                button.setAttribute("role", "option");
                button.setAttribute("aria-selected", "false");
                button.id = `arpp-project-option-${project.id}-${index}`;

                const title = document.createElement("strong");
                title.textContent = project.name;
                const meta = document.createElement("span");
                meta.textContent = [project.caseFileNumber, project.statusLabel].filter(Boolean).join(" · ");
                button.append(title, meta);
                button.addEventListener("mousedown", event => event.preventDefault());
                button.addEventListener("click", () => selectProject(picker, project));
                overlay.appendChild(button);
            });
        }

        const search = elements(picker).search;
        search?.setAttribute("aria-expanded", "true");
        overlay.classList.remove("d-none");
        positionOverlay();
    };

    const searchProjects = async (picker, query) => {
        const state = stateFor(picker);
        state.controller?.abort();
        state.controller = new AbortController();
        const endpoint = picker.dataset.arppProjectLookupUrl || rootUrl();
        const separator = endpoint.includes("?") ? "&" : "?";
        try {
            const response = await fetch(`${endpoint}${separator}q=${encodeURIComponent(query)}&take=25`, {
                headers: { Accept: "application/json" },
                signal: state.controller.signal
            });
            if (!response.ok) throw new Error(`Project lookup failed (${response.status}).`);
            const payload = await response.json();
            render(picker, Array.isArray(payload.items) ? payload.items : []);
        } catch (error) {
            if (error.name !== "AbortError") render(picker, []);
        }
    };

    const initialise = picker => {
        if (!picker || picker.dataset.arppProjectPickerInitialised === "true") return;
        picker.dataset.arppProjectPickerInitialised = "true";
        const { id, search, clear } = elements(picker);
        if (!id || !search) return;

        search.setAttribute("role", "combobox");
        search.setAttribute("aria-autocomplete", "list");
        search.setAttribute("aria-controls", overlay.id);
        search.setAttribute("aria-expanded", "false");
        if (id.value && search.value) {
            search.dataset.selectedName = search.value;
            clear?.classList.remove("d-none");
        }

        search.addEventListener("input", () => {
            if (id.value && search.value !== search.dataset.selectedName) clearProject(picker, false);
            const state = stateFor(picker);
            window.clearTimeout(state.timer);
            const query = search.value.trim();
            if (query.length < 2) {
                close(picker);
                return;
            }
            state.timer = window.setTimeout(() => searchProjects(picker, query), 180);
        });

        search.addEventListener("focus", () => {
            const query = search.value.trim();
            if (!id.value && query.length >= 2) searchProjects(picker, query);
        });

        search.addEventListener("keydown", event => {
            const state = stateFor(picker);
            if (event.key === "ArrowDown") {
                event.preventDefault();
                if (activePicker !== picker || overlay.classList.contains("d-none")) {
                    const query = search.value.trim();
                    if (query.length >= 2) searchProjects(picker, query);
                } else {
                    setActive(picker, state.activeIndex + 1);
                }
            } else if (event.key === "ArrowUp") {
                event.preventDefault();
                if (activePicker === picker) setActive(picker, state.activeIndex <= 0 ? state.projects.length - 1 : state.activeIndex - 1);
            } else if (event.key === "Enter" && activePicker === picker && state.activeIndex >= 0) {
                event.preventDefault();
                selectProject(picker, state.projects[state.activeIndex]);
            } else if (event.key === "Escape") {
                close(picker);
            }
        });

        clear?.addEventListener("click", () => {
            clearProject(picker, true);
            close(picker);
            search.focus();
        });
    };

    document.querySelectorAll("[data-arpp-project-picker]").forEach(initialise);
    document.addEventListener("click", event => {
        if (!activePicker) return;
        if (!activePicker.contains(event.target) && !overlay.contains(event.target)) close(activePicker);
    });
    window.addEventListener("resize", positionOverlay);
    window.addEventListener("scroll", positionOverlay, true);

    window.PrismArppProjectPicker = { initialise, close, selectProject, clearProject };
})();
