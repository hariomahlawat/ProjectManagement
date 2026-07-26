(() => {
    "use strict";

    const root = document.querySelector("[data-arpp-workspace]");
    const form = root?.querySelector("[data-arpp-form]");
    const body = root?.querySelector("[data-arpp-entry-body]");
    const template = root?.querySelector("[data-arpp-row-template]");
    if (!root || !form || !body || !template) return;

    let dirty = false;
    let searchController = null;
    let searchTimer = null;

    const markDirty = () => {
        dirty = true;
        const state = root.querySelector("[data-arpp-save-state]");
        if (state) state.textContent = "Unsaved changes";
    };

    const formatFinancialYear = (value) => {
        const year = Number(value);
        if (!Number.isInteger(year) || year < 2000 || year > 9998) return "Enter a valid start year";
        return `${year}-${String((year + 1) % 100).padStart(2, "0")}`;
    };

    const updateFinancialYearDisplay = () => {
        const input = root.querySelector("[data-arpp-financial-year]");
        const display = root.querySelector("[data-arpp-financial-year-display]");
        if (input && display) display.textContent = formatFinancialYear(input.value);
    };

    const updateIssueSequence = () => {
        const kind = root.querySelector("[data-arpp-issue-kind]");
        const sequence = root.querySelector("[data-arpp-issue-sequence]");
        if (!kind || !sequence) return;
        if (kind.value === "1") {
            sequence.value = "0";
            sequence.min = "0";
            sequence.readOnly = true;
        } else {
            sequence.readOnly = false;
            sequence.min = "1";
            if (Number(sequence.value) <= 0) sequence.value = "1";
        }
    };

    const rows = () => Array.from(body.querySelectorAll("[data-arpp-entry-row]"));

    const replaceIndex = (value, index) => value
        .replace(/Input\.Entries\[\d+\]/g, `Input.Entries[${index}]`)
        .replace(/Input_Entries_\d+__/g, `Input_Entries_${index}__`);

    const reindexRows = () => {
        rows().forEach((row, index) => {
            row.querySelectorAll("[name]").forEach(element => {
                element.name = replaceIndex(element.name, index);
            });
            row.querySelectorAll("[id]").forEach(element => {
                element.id = replaceIndex(element.id, index);
            });
            row.querySelectorAll("label[for]").forEach(element => {
                element.htmlFor = replaceIndex(element.htmlFor, index);
            });
            row.querySelectorAll("[data-valmsg-for]").forEach(element => {
                element.dataset.valmsgFor = replaceIndex(element.dataset.valmsgFor, index);
            });
            const number = row.querySelector("[data-arpp-row-number]");
            if (number) number.textContent = String(index + 1);
        });

        const count = root.querySelector("[data-arpp-row-count]");
        if (count) count.textContent = String(rows().length);
        refreshUnobtrusiveValidation();
    };

    const refreshUnobtrusiveValidation = () => {
        if (!window.jQuery?.validator?.unobtrusive) return;
        const jqueryForm = window.jQuery(form);
        jqueryForm.removeData("validator");
        jqueryForm.removeData("unobtrusiveValidation");
        window.jQuery.validator.unobtrusive.parse(form);
    };

    const addRow = (values = {}) => {
        const index = rows().length;
        const wrapper = document.createElement("tbody");
        wrapper.innerHTML = template.innerHTML
            .replaceAll("__INDEX__", String(index))
            .replaceAll("__ROW__", String(index + 1));
        const row = wrapper.firstElementChild;
        body.appendChild(row);
        initialiseRow(row);
        setRowValues(row, values);
        reindexRows();
        markDirty();
        return row;
    };

    const getField = (row, suffix) => row.querySelector(`[name$=".${suffix}"]`);

    const setRowValues = (row, values) => {
        const mapping = {
            SerialNumber: values.serialNumber,
            ProjectReference: values.projectReference,
            Category: values.category,
            IpaCost: values.ipaCost,
            Cfa: values.cfa,
            Fund: values.fund,
            DfpdsSchedule: values.dfpdsSchedule
        };
        Object.entries(mapping).forEach(([suffix, value]) => {
            const input = getField(row, suffix);
            if (input && value !== undefined && value !== null) input.value = value;
        });
    };

    const clearProject = (picker, clearSearch = false) => {
        const id = picker.querySelector("[data-arpp-project-id]");
        const search = picker.querySelector("[data-arpp-project-search]");
        const metaInput = picker.querySelector("[data-arpp-project-meta-input]");
        const meta = picker.querySelector("[data-arpp-project-meta]");
        const clear = picker.querySelector("[data-arpp-project-clear]");
        if (id) id.value = "";
        if (clearSearch && search) search.value = "";
        if (search) delete search.dataset.selectedName;
        if (metaInput) metaInput.value = "";
        if (meta) meta.textContent = "";
        clear?.classList.add("d-none");
    };

    const closeResults = (picker) => {
        const results = picker.querySelector("[data-arpp-project-results]");
        if (results) {
            results.classList.add("d-none");
            results.replaceChildren();
        }
    };

    const selectProject = (picker, project) => {
        const id = picker.querySelector("[data-arpp-project-id]");
        const search = picker.querySelector("[data-arpp-project-search]");
        const metaInput = picker.querySelector("[data-arpp-project-meta-input]");
        const meta = picker.querySelector("[data-arpp-project-meta]");
        const clear = picker.querySelector("[data-arpp-project-clear]");
        const projectMeta = [project.caseFileNumber, project.statusLabel].filter(Boolean).join(" · ");

        if (id) id.value = String(project.id);
        if (search) {
            search.value = project.name;
            search.dataset.selectedName = project.name;
        }
        if (metaInput) metaInput.value = projectMeta;
        if (meta) meta.textContent = projectMeta;
        clear?.classList.remove("d-none");
        closeResults(picker);
        markDirty();
    };

    const renderProjectResults = (picker, projects) => {
        const results = picker.querySelector("[data-arpp-project-results]");
        if (!results) return;
        results.replaceChildren();

        if (!projects.length) {
            const empty = document.createElement("div");
            empty.className = "arpp-project-picker__empty";
            empty.textContent = "No matching PRISM projects";
            results.appendChild(empty);
            results.classList.remove("d-none");
            return;
        }

        projects.forEach(project => {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "arpp-project-picker__result";
            button.setAttribute("role", "option");

            const title = document.createElement("strong");
            title.textContent = project.name;
            const meta = document.createElement("span");
            meta.textContent = [project.caseFileNumber, project.statusLabel].filter(Boolean).join(" · ");
            button.append(title, meta);
            button.addEventListener("click", () => selectProject(picker, project));
            results.appendChild(button);
        });
        results.classList.remove("d-none");
    };

    const searchProjects = async (picker, query) => {
        searchController?.abort();
        searchController = new AbortController();
        try {
            const response = await fetch(`/api/arpp/projects?q=${encodeURIComponent(query)}&take=25`, {
                headers: { Accept: "application/json" },
                signal: searchController.signal
            });
            if (!response.ok) throw new Error(`Project lookup failed (${response.status}).`);
            const payload = await response.json();
            renderProjectResults(picker, Array.isArray(payload.items) ? payload.items : []);
        } catch (error) {
            if (error.name === "AbortError") return;
            renderProjectResults(picker, []);
        }
    };

    const initialiseProjectPicker = (picker) => {
        const search = picker.querySelector("[data-arpp-project-search]");
        const id = picker.querySelector("[data-arpp-project-id]");
        const clear = picker.querySelector("[data-arpp-project-clear]");
        if (!search || !id) return;

        if (id.value && search.value) search.dataset.selectedName = search.value;

        search.addEventListener("input", () => {
            if (id.value && search.value !== search.dataset.selectedName) clearProject(picker, false);
            window.clearTimeout(searchTimer);
            const query = search.value.trim();
            if (query.length < 2) {
                closeResults(picker);
                return;
            }
            searchTimer = window.setTimeout(() => searchProjects(picker, query), 180);
            markDirty();
        });

        search.addEventListener("focus", () => {
            const query = search.value.trim();
            if (!id.value && query.length >= 2) searchProjects(picker, query);
        });

        clear?.addEventListener("click", () => {
            clearProject(picker, true);
            closeResults(picker);
            search.focus();
            markDirty();
        });
    };

    const initialiseRow = (row) => {
        row.querySelector("[data-arpp-remove-row]")?.addEventListener("click", () => {
            const currentRows = rows();
            if (currentRows.length === 1) {
                row.querySelectorAll("input, textarea, select").forEach(element => {
                    if (element.type === "hidden" || element.tagName !== "SELECT") element.value = "";
                    else element.selectedIndex = 0;
                });
                const category = getField(row, "Category");
                if (category) category.value = "1";
                const picker = row.querySelector("[data-arpp-project-picker]");
                if (picker) clearProject(picker, true);
            } else {
                row.remove();
                reindexRows();
            }
            markDirty();
        });

        row.querySelector("[data-arpp-copy-previous]")?.addEventListener("click", () => {
            const currentRows = rows();
            const index = currentRows.indexOf(row);
            if (index <= 0) return;
            const previous = currentRows[index - 1];
            ["Cfa", "Fund", "DfpdsSchedule"].forEach(suffix => {
                const source = getField(previous, suffix);
                const target = getField(row, suffix);
                if (source && target) target.value = source.value;
            });
            markDirty();
        });

        const picker = row.querySelector("[data-arpp-project-picker]");
        if (picker) initialiseProjectPicker(picker);
    };

    const isBlankRow = (row) => ["SerialNumber", "ProjectReference", "IpaCost", "Cfa", "Fund", "DfpdsSchedule"]
        .every(suffix => !(getField(row, suffix)?.value || "").trim());

    const categoryValue = (value) => {
        const normalized = String(value || "").trim().toLowerCase().replace(/[^a-z]/g, "");
        if (normalized === "new") return "1";
        if (normalized === "cl" || normalized === "committedliability") return "2";
        if (normalized === "cf" || normalized === "carryforward") return "3";
        if (normalized === "delisted") return "4";
        return null;
    };

    const parsePastedRows = (text) => {
        const lines = text.split(/\r?\n/).map(line => line.trimEnd()).filter(line => line.trim().length > 0);
        return lines.map((line, index) => {
            const columns = line.split("\t");
            if (columns.length < 7) throw new Error(`Row ${index + 1} has ${columns.length} columns; seven are required.`);
            const category = categoryValue(columns[2]);
            if (!category) throw new Error(`Row ${index + 1} has an unrecognised category: ${columns[2] || "(blank)"}.`);
            const cost = columns[3].replace(/[₹,$\s]/g, "");
            if (cost === "" || Number.isNaN(Number(cost)) || Number(cost) < 0) {
                throw new Error(`Row ${index + 1} has an invalid IPA cost.`);
            }
            return {
                serialNumber: columns[0].trim(),
                projectReference: columns[1].trim(),
                category,
                ipaCost: cost,
                cfa: columns[4].trim(),
                fund: columns[5].trim(),
                dfpdsSchedule: columns[6].trim()
            };
        });
    };

    root.querySelector("[data-arpp-add-row]")?.addEventListener("click", () => {
        const previous = rows().at(-1);
        const row = addRow(previous ? {
            cfa: getField(previous, "Cfa")?.value || "",
            fund: getField(previous, "Fund")?.value || "",
            dfpdsSchedule: getField(previous, "DfpdsSchedule")?.value || "",
            category: "1"
        } : { category: "1" });
        getField(row, "SerialNumber")?.focus();
    });

    const pasteText = document.querySelector("[data-arpp-paste-text]");
    const pasteError = document.querySelector("[data-arpp-paste-error]");
    document.querySelector("[data-arpp-apply-paste]")?.addEventListener("click", () => {
        try {
            const parsed = parsePastedRows(pasteText?.value || "");
            if (!parsed.length) throw new Error("Paste at least one Excel row.");
            pasteError?.classList.add("d-none");
            if (rows().length === 1 && isBlankRow(rows()[0])) rows()[0].remove();
            parsed.forEach(values => addRow(values));
            reindexRows();
            pasteText.value = "";
            const modal = bootstrap.Modal.getInstance(document.getElementById("arppPasteModal"));
            modal?.hide();
        } catch (error) {
            if (pasteError) {
                pasteError.textContent = error.message || "The pasted rows could not be read.";
                pasteError.classList.remove("d-none");
            }
        }
    });

    root.querySelectorAll("[data-arpp-entry-row]").forEach(initialiseRow);
    root.querySelector("[data-arpp-financial-year]")?.addEventListener("input", () => {
        updateFinancialYearDisplay();
        markDirty();
    });
    root.querySelector("[data-arpp-issue-kind]")?.addEventListener("change", () => {
        updateIssueSequence();
        markDirty();
    });

    form.addEventListener("input", markDirty);
    form.addEventListener("change", markDirty);
    form.addEventListener("submit", () => {
        dirty = false;
        const button = root.querySelector("[data-arpp-save-button]");
        if (button) {
            button.disabled = true;
            button.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Saving…';
        }
    });

    document.addEventListener("click", event => {
        root.querySelectorAll("[data-arpp-project-picker]").forEach(picker => {
            if (!picker.contains(event.target)) closeResults(picker);
        });
    });

    window.addEventListener("beforeunload", event => {
        if (!dirty) return;
        event.preventDefault();
        event.returnValue = "";
    });

    updateFinancialYearDisplay();
    updateIssueSequence();
    reindexRows();
})();
