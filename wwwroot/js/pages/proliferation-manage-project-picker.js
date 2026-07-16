"use strict";

(() => {
    const root = document.querySelector('[data-manage-project-picker]');
    const nativeSelect = document.getElementById("pf-project");
    const input = document.getElementById("pf-project-search");
    const suggestions = document.getElementById("pf-project-search-suggestions");
    const clearButton = document.getElementById("pf-project-search-clear");
    const statusElement = document.getElementById("pf-project-search-status");

    if (!root || !nativeSelect || !input || !suggestions || !window.ProliferationProjectPicker) {
        return;
    }

    const nativeValueDescriptor = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value");
    let internalWrite = false;
    let pendingSync = 0;

    const readNativeValue = () => nativeValueDescriptor?.get
        ? nativeValueDescriptor.get.call(nativeSelect)
        : nativeSelect.getAttribute("value") || "";

    const writeNativeValue = value => {
        internalWrite = true;
        try {
            if (nativeValueDescriptor?.set) {
                nativeValueDescriptor.set.call(nativeSelect, String(value ?? ""));
            } else {
                nativeSelect.setAttribute("value", String(value ?? ""));
            }
        } finally {
            internalWrite = false;
        }
    };

    const projectFromOption = option => {
        if (!option?.value) return null;
        const display = (option.dataset.display || option.textContent || "").trim();
        return {
            id: Number(option.value),
            name: (option.dataset.name || display).trim(),
            code: (option.dataset.code || "").trim(),
            technicalCategory: (option.dataset.technicalCategory || "").trim(),
            display
        };
    };

    const mirrorNativeState = () => {
        const disabled = Boolean(nativeSelect.disabled);
        input.disabled = disabled;
        input.setAttribute("aria-disabled", disabled ? "true" : "false");
        if (clearButton) clearButton.disabled = disabled;

        const invalid = nativeSelect.getAttribute("aria-invalid") === "true";
        input.classList.toggle("is-invalid", invalid);
        if (invalid) input.setAttribute("aria-invalid", "true");
        else input.removeAttribute("aria-invalid");
    };

    const dispatchNativeChange = () => {
        nativeSelect.dispatchEvent(new Event("input", { bubbles: true }));
        nativeSelect.dispatchEvent(new Event("change", { bubbles: true }));
    };

    const picker = new window.ProliferationProjectPicker({
        input,
        hiddenInput: nativeSelect,
        suggestions,
        clearButton,
        statusElement,
        endpoint: "/api/proliferation/project-picker",
        minLength: 1,
        take: 20,
        showRecents: true,
        recentLimit: 5,
        recentStorageKey: "prism.proliferation.manage.recent-projects",
        emptyPrompt: "Type a project name, acronym or code.",
        noResultsText: "No matching completed project found. Search by project name, acronym or code.",
        selectPrompt: "Select a completed project from the suggestions.",
        onSelected: project => {
            writeNativeValue(project.id);
            dispatchNativeChange();
            mirrorNativeState();
            window.requestAnimationFrame(() => document.getElementById("pf-source")?.focus());
        },
        onCleared: () => {
            writeNativeValue("");
            dispatchNativeChange();
            mirrorNativeState();
        }
    });

    const syncFromNative = async ({ enrich = true } = {}) => {
        window.clearTimeout(pendingSync);
        const id = Number(readNativeValue());
        if (!Number.isInteger(id) || id <= 0) {
            picker.clear({ notify: false });
            mirrorNativeState();
            return;
        }

        if (Number(picker.selected?.id) === id) {
            mirrorNativeState();
            return;
        }

        const option = Array.from(nativeSelect.options).find(candidate => Number(candidate.value) === id);
        const fallback = projectFromOption(option);
        if (fallback) {
            picker.setSelection(fallback, { notify: false, remember: false });
        }

        if (enrich) {
            await picker.initializeById(id, { notify: false, remember: false });
        }
        mirrorNativeState();
    };

    if (nativeValueDescriptor?.get && nativeValueDescriptor?.set) {
        Object.defineProperty(nativeSelect, "value", {
            configurable: true,
            enumerable: nativeValueDescriptor.enumerable,
            get() {
                return nativeValueDescriptor.get.call(this);
            },
            set(value) {
                nativeValueDescriptor.set.call(this, String(value ?? ""));
                if (!internalWrite) {
                    pendingSync = window.setTimeout(() => syncFromNative(), 0);
                }
            }
        });
    }

    Object.defineProperty(nativeSelect, "focus", {
        configurable: true,
        value(options) {
            input.focus(options);
        }
    });

    nativeSelect.addEventListener("change", () => {
        if (!internalWrite) syncFromNative();
    });

    input.addEventListener("blur", () => {
        if (!picker.selected && input.value.trim()) {
            input.classList.add("is-invalid");
            input.setAttribute("aria-invalid", "true");
            statusElement.textContent = "Select a project from the suggestions or clear the search.";
        }
    });

    const form = nativeSelect.closest("form");
    form?.addEventListener("reset", () => {
        window.requestAnimationFrame(() => syncFromNative({ enrich: false }));
    });

    const observer = new MutationObserver(mirrorNativeState);
    observer.observe(nativeSelect, {
        attributes: true,
        attributeFilter: ["disabled", "aria-invalid", "required"]
    });

    window.addEventListener("pagehide", () => {
        observer.disconnect();
        picker.destroy();
    }, { once: true });

    window.ProliferationManageProjectPicker = picker;
    syncFromNative();
})();
