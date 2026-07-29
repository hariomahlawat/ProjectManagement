(() => {
    "use strict";

    const root = document.querySelector("[data-arpp-workspace]");
    const form = root?.querySelector("[data-arpp-form]");
    const body = root?.querySelector("[data-arpp-entry-body]");
    const template = root?.querySelector("[data-arpp-row-template]");
    if (!root || !form || !body || !template) return;

    let dirty = false;
    let pasteRows = [];

    const rows = () => Array.from(body.querySelectorAll("[data-arpp-entry-row]"));
    const getField = (row, suffix) => row.querySelector(`[name$=".${suffix}"]`);
    const isDelisted = row => getField(row, "Category")?.value === "4";

    const syncIssuedIdentifiers = row => {
        const delisted = isDelisted(row);
        row.querySelectorAll("[data-arpp-issued-identifier]").forEach(input => {
            if (delisted) input.value = "";
            input.readOnly = delisted;
            input.required = !delisted;
            input.setAttribute("aria-readonly", delisted ? "true" : "false");
            input.setAttribute("aria-required", delisted ? "false" : "true");
            input.placeholder = delisted
                ? "Not applicable"
                : input.dataset.arppIdentifierKind === "ppp"
                    ? "PPP No. as issued"
                    : "Serial No.";
            input.classList.toggle("arpp-issued-identifier--not-applicable", delisted);
        });
    };

    const markDirty = () => {
        dirty = true;
        const state = root.querySelector("[data-arpp-save-state]");
        const actions = root.querySelector("[data-arpp-sticky-actions]");
        const saveButton = root.querySelector("[data-arpp-save-button]");
        if (state) state.textContent = "Unsaved changes";
        if (saveButton) saveButton.disabled = false;
        actions?.classList.add("arpp-editor__actions--dirty");
    };

    const markClean = () => {
        dirty = false;
        const state = root.querySelector("[data-arpp-save-state]");
        const actions = root.querySelector("[data-arpp-sticky-actions]");
        const saveButton = root.querySelector("[data-arpp-save-button]");
        if (state) state.textContent = "All changes saved.";
        if (saveButton) saveButton.disabled = true;
        actions?.classList.remove("arpp-editor__actions--dirty");
    };

    const updateIssueSequence = () => {
        const kind = root.querySelector("[data-arpp-issue-kind]");
        const sequence = root.querySelector("[data-arpp-issue-sequence]");
        const help = root.querySelector("[data-arpp-sequence-help]");
        const sequenceField = root.querySelector("[data-arpp-addendum-number-field]");
        if (!kind || !sequence) return;
        if (kind.value === "1") {
            sequence.value = "0";
            sequence.min = "0";
            sequence.readOnly = true;
            sequenceField?.classList.add("d-none");
            if (help) help.textContent = "Original ARPP; internal sequence 0 is stored automatically.";
        } else {
            sequence.readOnly = false;
            sequence.min = "1";
            sequenceField?.classList.remove("d-none");
            if (Number(sequence.value) <= 0) sequence.value = "1";
            if (help) help.textContent = "Enter the addendum number shown in the issued document.";
        }
    };

    const replaceIndex = (value, index) => value
        .replace(/Input\.Entries\[\d+\]/g, `Input.Entries[${index}]`)
        .replace(/Input_Entries_\d+__/g, `Input_Entries_${index}__`);

    const refreshUnobtrusiveValidation = () => {
        if (!window.jQuery?.validator?.unobtrusive) return;
        const jqueryForm = window.jQuery(form);
        jqueryForm.removeData("validator");
        jqueryForm.removeData("unobtrusiveValidation");
        window.jQuery.validator.unobtrusive.parse(form);
    };

    const updateEmptyState = () => {
        const count = rows().length;
        root.querySelector("[data-arpp-empty-rows]")?.classList.toggle("d-none", count > 0);
        root.querySelector("[data-arpp-table-wrap]")?.classList.toggle("d-none", count === 0);
        const countElement = root.querySelector("[data-arpp-row-count]");
        const labelElement = root.querySelector("[data-arpp-row-count-label]");
        if (countElement) countElement.textContent = String(count);
        if (labelElement) labelElement.textContent = count === 1 ? "row" : "rows";
    };

    const refreshRowWarnings = () => {
        const serialCounts = new Map();
        const pppCounts = new Map();
        rows().forEach(row => {
            if (isDelisted(row)) return;
            const serial = (getField(row, "SerialNumber")?.value || "").trim().toLowerCase();
            const pppNumber = (getField(row, "PppNumber")?.value || "").trim().toLowerCase();
            if (serial) serialCounts.set(serial, (serialCounts.get(serial) || 0) + 1);
            if (pppNumber) pppCounts.set(pppNumber, (pppCounts.get(pppNumber) || 0) + 1);
        });

        rows().forEach(row => {
            syncIssuedIdentifiers(row);
            const messages = [];
            const delisted = isDelisted(row);
            const serial = (getField(row, "SerialNumber")?.value || "").trim().toLowerCase();
            const pppNumber = (getField(row, "PppNumber")?.value || "").trim().toLowerCase();
            const projectId = Number(getField(row, "ProjectId")?.value || 0);
            const projectReference = (getField(row, "ProjectReference")?.value || "").trim();
            const costValue = getField(row, "IpaCost")?.value || "";
            const cost = parseMoney(costValue);

            if (!delisted && !serial) messages.push("Serial No. is required for approved rows.");
            if (!delisted && !pppNumber) messages.push("PPP No. is required for approved rows.");
            if (serial && serialCounts.get(serial) > 1) messages.push("Duplicate serial number — verify against the issued document.");
            if (pppNumber && pppCounts.get(pppNumber) > 1) messages.push("Duplicate PPP number — verify against the issued document.");
            if (projectReference && !projectId) messages.push("PRISM linkage pending; this may be reconciled later.");
            const unresolvedReferences = Array.from(row.querySelectorAll("[data-arpp-reference-select]"))
                .some(select => select.value === "-1");
            if (unresolvedReferences) messages.push("CFA, Fund or DFPDS mapping is pending; Admin must add or map the value before verification.");
            if (cost === 0) messages.push("IPA cost is zero; confirm the issued value.");

            const warning = row.querySelector("[data-arpp-row-warning]");
            if (!warning) return;
            warning.textContent = messages.join(" ");
            warning.classList.toggle("d-none", messages.length === 0);
        });
    };

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
        updateEmptyState();
        refreshRowWarnings();
        refreshUnobtrusiveValidation();
    };

    const parseMoney = value => {
        const normalized = String(value ?? "")
            .replace(/[₹,$\s]/g, "")
            .replace(/,/g, "")
            .trim();
        if (!normalized.length) return null;
        const amount = Number(normalized);
        return Number.isFinite(amount) && amount >= 0 ? amount : null;
    };

    const compactMoney = amount => {
        if (amount >= 10_000_000) return `₹${(amount / 10_000_000).toLocaleString("en-IN", { maximumFractionDigits: 2 })} Cr`;
        if (amount >= 100_000) return `₹${(amount / 100_000).toLocaleString("en-IN", { maximumFractionDigits: 2 })} Lakh`;
        return `₹${amount.toLocaleString("en-IN", { maximumFractionDigits: 2 })}`;
    };

    const updateMoneyHelper = input => {
        const helper = input.closest("td")?.querySelector("[data-arpp-money-helper]");
        if (!helper) return;
        const amount = parseMoney(input.value);
        if (amount === null) {
            helper.textContent = "";
            helper.classList.remove("text-danger");
            return;
        }
        helper.classList.remove("text-danger");
        helper.textContent = `₹${amount.toLocaleString("en-IN", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} · ${compactMoney(amount)}`;
    };

    const normaliseMoneyInput = input => {
        const amount = parseMoney(input.value);
        if (amount !== null) input.value = amount.toFixed(2);
        updateMoneyHelper(input);
    };

    const autosizeReference = textarea => {
        textarea.style.height = "auto";
        textarea.style.height = `${Math.min(Math.max(textarea.scrollHeight, 31), 92)}px`;
    };

    const normaliseReferenceValue = value => String(value || "")
        .trim()
        .replace(/\s+/g, " ")
        .toLocaleUpperCase("en-IN");

    const syncReferenceSelect = (select, focusCustom = false, updateSnapshot = false) => {
        const kind = select.dataset.referenceKind;
        const row = select.closest("[data-arpp-entry-row]");
        const snapshot = row?.querySelector(`[data-arpp-reference-snapshot][data-reference-kind="${kind}"]`);
        const pending = select.closest("td")?.querySelector("[data-arpp-reference-pending]");
        if (!snapshot) return;

        if (select.value === "-1") {
            snapshot.type = "text";
            snapshot.required = true;
            pending?.classList.remove("d-none");
            if (focusCustom) snapshot.focus({ preventScroll: true });
            return;
        }

        snapshot.type = "hidden";
        snapshot.required = true;
        pending?.classList.add("d-none");
        const option = select.selectedOptions[0];
        if (updateSnapshot || !snapshot.value.trim()) {
            snapshot.value = option?.dataset.snapshot || "";
        }
    };

    const selectReferenceValue = (row, kind, value, optionId) => {
        const select = row.querySelector(`[data-arpp-reference-select][data-reference-kind="${kind}"]`);
        const snapshot = row.querySelector(`[data-arpp-reference-snapshot][data-reference-kind="${kind}"]`);
        if (!select || !snapshot) return;

        if (Number(optionId) > 0) {
            const requested = Array.from(select.options).find(option => option.value === String(optionId));
            if (requested && requested.dataset.active !== "false" && !requested.disabled) {
                select.value = String(optionId);
                syncReferenceSelect(select, false, true);
                return;
            }
        }

        const normalized = normaliseReferenceValue(value);
        const matching = Array.from(select.options).find(option =>
            option.value && option.value !== "-1" &&
            option.dataset.active !== "false" && !option.disabled &&
            normaliseReferenceValue(option.dataset.snapshot) === normalized);
        if (matching) {
            select.value = matching.value;
            syncReferenceSelect(select, false, true);
            return;
        }

        if (String(value || "").trim()) {
            select.value = "-1";
            snapshot.value = String(value).trim();
            syncReferenceSelect(select, false, true);
        } else {
            select.value = "";
            snapshot.value = "";
            syncReferenceSelect(select, false, true);
        }
    };

    const copyReference = (sourceRow, targetRow, kind) => {
        const sourceSelect = sourceRow.querySelector(`[data-arpp-reference-select][data-reference-kind="${kind}"]`);
        const sourceSnapshot = sourceRow.querySelector(`[data-arpp-reference-snapshot][data-reference-kind="${kind}"]`);
        const selectedOption = sourceSelect?.selectedOptions?.[0];
        if (selectedOption?.dataset.active === "false" || selectedOption?.disabled) return;
        selectReferenceValue(targetRow, kind, sourceSnapshot?.value || "", Number(sourceSelect?.value || 0));
    };

    const setRowValues = (row, values) => {
        const mapping = {
            SerialNumber: values.serialNumber,
            PppNumber: values.pppNumber,
            ProjectReference: values.projectReference,
            Category: values.category,
            IpaCost: values.ipaCost
        };
        Object.entries(mapping).forEach(([suffix, value]) => {
            const input = getField(row, suffix);
            if (input && value !== undefined && value !== null) input.value = value;
        });
        selectReferenceValue(row, "cfa", values.cfa, values.cfaOptionId);
        selectReferenceValue(row, "fund", values.fund, values.fundOptionId);
        selectReferenceValue(row, "dfpds", values.dfpdsSchedule, values.dfpdsScheduleId);

        const reference = getField(row, "ProjectReference");
        if (reference) autosizeReference(reference);
        const money = getField(row, "IpaCost");
        if (money) updateMoneyHelper(money);
        syncIssuedIdentifiers(row);
    };

    const initialiseRow = row => {
        row.querySelector("[data-arpp-remove-row]")?.addEventListener("click", () => {
            row.remove();
            reindexRows();
            markDirty();
        });

        row.querySelector("[data-arpp-copy-previous]")?.addEventListener("click", () => {
            const currentRows = rows();
            const index = currentRows.indexOf(row);
            if (index <= 0) return;
            const previous = currentRows[index - 1];
            ["cfa", "fund", "dfpds"].forEach(kind => copyReference(previous, row, kind));
            refreshRowWarnings();
            markDirty();
        });

        const reference = row.querySelector("[data-arpp-project-reference]");
        if (reference) {
            reference.addEventListener("input", () => autosizeReference(reference));
            autosizeReference(reference);
        }

        const money = row.querySelector("[data-arpp-money]");
        if (money) {
            money.addEventListener("input", () => {
                updateMoneyHelper(money);
                refreshRowWarnings();
            });
            money.addEventListener("blur", () => normaliseMoneyInput(money));
            updateMoneyHelper(money);
        }

        const category = row.querySelector("[data-arpp-category]");
        if (category) {
            category.addEventListener("change", () => {
                syncIssuedIdentifiers(row);
                refreshRowWarnings();
                markDirty();
            });
            syncIssuedIdentifiers(row);
        }

        row.querySelectorAll("[data-arpp-reference-select]").forEach(select => {
            select.addEventListener("change", () => {
                syncReferenceSelect(select, true, true);
                refreshRowWarnings();
                markDirty();
            });
            syncReferenceSelect(select);
        });

        const picker = row.querySelector("[data-arpp-project-picker]");
        if (picker) {
            window.PrismArppProjectPicker?.initialise(picker);
            picker.addEventListener("arpp:project-selected", event => {
                const projectReference = row.querySelector("[data-arpp-project-reference]");
                const hint = row.querySelector("[data-arpp-reference-prefill-hint]");
                if (projectReference && !projectReference.value.trim()) {
                    projectReference.value = event.detail.project.name;
                    hint?.classList.remove("d-none");
                    autosizeReference(projectReference);
                }
                refreshRowWarnings();
                markDirty();
            });
            picker.addEventListener("arpp:project-cleared", () => {
                refreshRowWarnings();
                markDirty();
            });
        }
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
        if (values.project && window.PrismArppProjectPicker) {
            const picker = row.querySelector("[data-arpp-project-picker]");
            if (picker) window.PrismArppProjectPicker.selectProject(picker, values.project);
        }
        markDirty();
        return row;
    };

    const addManualRow = () => {
        const previous = rows().at(-1);
        const row = addRow(previous ? {
            cfa: getField(previous, "Cfa")?.value || "",
            cfaOptionId: Number(getField(previous, "CfaOptionId")?.value || 0),
            fund: getField(previous, "Fund")?.value || "",
            fundOptionId: Number(getField(previous, "FundOptionId")?.value || 0),
            dfpdsSchedule: getField(previous, "DfpdsSchedule")?.value || "",
            dfpdsScheduleId: Number(getField(previous, "DfpdsScheduleId")?.value || 0),
            category: "1"
        } : { category: "1" });
        getField(row, "SerialNumber")?.focus();
    };

    const categoryValue = value => {
        const normalized = String(value || "").trim().toLowerCase().replace(/[^a-z]/g, "");
        if (normalized === "new") return "1";
        if (normalized === "cl" || normalized === "committedliability") return "2";
        if (normalized === "cf" || normalized === "carryforward") return "3";
        if (normalized === "delisted") return "4";
        return null;
    };

    const categoryLabel = value => ({ "1": "New", "2": "CL", "3": "CF", "4": "Delisted" })[value] || "";

    const looksLikeHeader = columns => {
        const joined = columns.slice(0, 5).join(" ").toLowerCase();
        return joined.includes("serial") && joined.includes("ppp") && joined.includes("project") && joined.includes("category");
    };

    const parsePastedRows = text => {
        const rawLines = text.split(/\r?\n/).map(line => line.trimEnd()).filter(line => line.trim().length > 0);
        const lines = rawLines.length && looksLikeHeader(rawLines[0].split("\t")) ? rawLines.slice(1) : rawLines;
        const parsed = lines.map((line, index) => {
            const columns = line.split("\t");
            const errors = [];
            const warnings = [];
            if (columns.length < 8) errors.push(`Only ${columns.length} columns found; eight are required.`);
            const values = [...columns, "", "", "", "", "", "", "", ""].slice(0, 8).map(value => value.trim());
            const category = categoryValue(values[3]);
            const cost = parseMoney(values[4]);
            const delisted = category === "4";
            if (!delisted && !values[0]) errors.push("Serial number is blank.");
            if (!delisted && !values[1]) errors.push("PPP number is blank.");
            if (!values[2]) errors.push("Project reference is blank.");
            if (!category) errors.push(`Category “${values[3] || "blank"}” is not recognised.`);
            if (cost === null) errors.push("IPA cost is invalid.");
            if (!values[5]) errors.push("CFA is blank.");
            if (!values[6]) errors.push("Fund is blank.");
            if (!values[7]) errors.push("DFPDS is blank.");
            return {
                sourceRow: index + 1,
                serialNumber: delisted ? "" : values[0],
                pppNumber: delisted ? "" : values[1],
                projectReference: values[2],
                category,
                ipaCost: cost === null ? values[4] : cost.toFixed(2),
                cfa: values[5],
                fund: values[6],
                dfpdsSchedule: values[7],
                errors,
                warnings,
                project: null,
                suggestions: []
            };
        });

        const serialCounts = new Map();
        const pppCounts = new Map();
        parsed.filter(row => row.category !== "4").forEach(row => {
            const serialKey = row.serialNumber.toLowerCase();
            const pppKey = row.pppNumber.toLowerCase();
            if (serialKey) serialCounts.set(serialKey, (serialCounts.get(serialKey) || 0) + 1);
            if (pppKey) pppCounts.set(pppKey, (pppCounts.get(pppKey) || 0) + 1);
        });
        parsed.forEach(row => {
            if (row.serialNumber && serialCounts.get(row.serialNumber.toLowerCase()) > 1) row.warnings.push("Duplicate serial number in pasted rows.");
            if (row.pppNumber && pppCounts.get(row.pppNumber.toLowerCase()) > 1) row.warnings.push("Duplicate PPP number in pasted rows.");
        });
        return parsed;
    };

    const lookupSuggestions = async row => {
        if (row.errors.length || row.projectReference.length < 2) return;
        const endpoint = root.dataset.arppProjectLookupUrl;
        try {
            const response = await fetch(`${endpoint}?q=${encodeURIComponent(row.projectReference)}&take=3`, { headers: { Accept: "application/json" } });
            if (!response.ok) return;
            const payload = await response.json();
            row.suggestions = Array.isArray(payload.items) ? payload.items : [];
        } catch {
            row.suggestions = [];
        }
    };

    const lookupPasteSuggestions = async candidateRows => {
        let nextIndex = 0;
        const worker = async () => {
            while (nextIndex < candidateRows.length) {
                const currentIndex = nextIndex++;
                await lookupSuggestions(candidateRows[currentIndex]);
            }
        };
        const workerCount = Math.min(5, candidateRows.length);
        await Promise.all(Array.from({ length: workerCount }, worker));
    };

    const renderPastePreview = () => {
        const previewBody = document.querySelector("[data-arpp-paste-preview-body]");
        const applyButton = document.querySelector("[data-arpp-apply-paste]");
        if (!previewBody) return;
        previewBody.replaceChildren();

        pasteRows.forEach((row, index) => {
            const tr = document.createElement("tr");
            if (row.errors.length) tr.classList.add("table-danger");
            else if (row.warnings.length) tr.classList.add("table-warning");

            const cell = value => {
                const td = document.createElement("td");
                td.textContent = value;
                return td;
            };
            tr.append(cell(String(row.sourceRow)), cell(row.serialNumber || "—"), cell(row.pppNumber || "—"), cell(row.projectReference), cell(categoryLabel(row.category)), cell(row.ipaCost));

            const suggestionCell = document.createElement("td");
            if (row.project) {
                const selected = document.createElement("div");
                selected.className = "arpp-paste-project-selected";
                selected.innerHTML = `<strong></strong><small></small>`;
                selected.querySelector("strong").textContent = row.project.name;
                selected.querySelector("small").textContent = [row.project.caseFileNumber, row.project.statusLabel].filter(Boolean).join(" · ");
                const clear = document.createElement("button");
                clear.type = "button";
                clear.className = "btn btn-link btn-sm p-0";
                clear.textContent = "Clear";
                clear.addEventListener("click", () => { row.project = null; renderPastePreview(); });
                suggestionCell.append(selected, clear);
            } else if (row.suggestions.length) {
                const select = document.createElement("select");
                select.className = "form-select form-select-sm";
                select.innerHTML = '<option value="">Do not link now</option>';
                row.suggestions.forEach((project, suggestionIndex) => {
                    const option = document.createElement("option");
                    option.value = String(suggestionIndex);
                    option.textContent = `${project.name}${project.caseFileNumber ? ` · ${project.caseFileNumber}` : ""}`;
                    select.appendChild(option);
                });
                select.addEventListener("change", () => {
                    row.project = select.value === "" ? null : row.suggestions[Number(select.value)];
                    renderPastePreview();
                });
                suggestionCell.appendChild(select);
            } else {
                suggestionCell.textContent = "Link later";
                suggestionCell.className = "text-body-secondary";
            }
            tr.appendChild(suggestionCell);

            const reviewCell = document.createElement("td");
            const messages = [...row.errors, ...row.warnings];
            if (!messages.length) {
                reviewCell.innerHTML = '<span class="text-success"><span class="bi bi-check-circle" aria-hidden="true"></span> Ready</span>';
            } else {
                const list = document.createElement("ul");
                list.className = "mb-0 ps-3 small";
                messages.forEach(message => {
                    const li = document.createElement("li");
                    li.textContent = message;
                    list.appendChild(li);
                });
                reviewCell.appendChild(list);
            }
            tr.appendChild(reviewCell);
            previewBody.appendChild(tr);
        });

        if (applyButton) applyButton.disabled = pasteRows.some(row => row.errors.length > 0);
    };

    const pasteEntry = document.querySelector("[data-arpp-paste-entry]");
    const pastePreview = document.querySelector("[data-arpp-paste-preview]");
    const pasteText = document.querySelector("[data-arpp-paste-text]");
    const pasteError = document.querySelector("[data-arpp-paste-error]");
    const previewButton = document.querySelector("[data-arpp-preview-paste]");
    const applyButton = document.querySelector("[data-arpp-apply-paste]");

    previewButton?.addEventListener("click", async () => {
        pasteRows = parsePastedRows(pasteText?.value || "");
        if (!pasteRows.length) {
            if (pasteError) {
                pasteError.textContent = "Paste at least one Excel row.";
                pasteError.classList.remove("d-none");
            }
            return;
        }
        pasteError?.classList.add("d-none");
        previewButton.disabled = true;
        previewButton.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Checking…';
        await lookupPasteSuggestions(pasteRows);
        renderPastePreview();
        pasteEntry?.classList.add("d-none");
        pastePreview?.classList.remove("d-none");
        previewButton.classList.add("d-none");
        applyButton?.classList.remove("d-none");
        previewButton.disabled = false;
        previewButton.textContent = "Preview rows";
    });

    document.querySelector("[data-arpp-paste-back]")?.addEventListener("click", () => {
        pastePreview?.classList.add("d-none");
        pasteEntry?.classList.remove("d-none");
        applyButton?.classList.add("d-none");
        previewButton?.classList.remove("d-none");
    });

    applyButton?.addEventListener("click", () => {
        if (!pasteRows.length || pasteRows.some(row => row.errors.length)) return;
        pasteRows.forEach(values => addRow(values));
        pasteRows = [];
        if (pasteText) pasteText.value = "";
        pastePreview?.classList.add("d-none");
        pasteEntry?.classList.remove("d-none");
        applyButton.classList.add("d-none");
        previewButton?.classList.remove("d-none");
        bootstrap.Modal.getInstance(document.getElementById("arppPasteModal"))?.hide();
    });

    root.querySelector("[data-arpp-apply-common]")?.addEventListener("click", () => {
        const currentRows = rows();
        const source = currentRows.find(row =>
            ["cfa", "fund", "dfpds"].every(kind => {
                const snapshot = row.querySelector(`[data-arpp-reference-snapshot][data-reference-kind="${kind}"]`);
                return Boolean(snapshot?.value.trim());
            }));
        if (!source) return;

        currentRows.forEach(row => {
            if (row === source) return;
            ["cfa", "fund", "dfpds"].forEach(kind => {
                const snapshot = row.querySelector(`[data-arpp-reference-snapshot][data-reference-kind="${kind}"]`);
                if (!snapshot?.value.trim()) copyReference(source, row, kind);
            });
        });
        refreshRowWarnings();
        markDirty();
    });

    root.querySelector("[data-arpp-add-row]")?.addEventListener("click", addManualRow);
    root.querySelector("[data-arpp-add-first-row]")?.addEventListener("click", addManualRow);
    root.querySelectorAll("[data-arpp-entry-row]").forEach(initialiseRow);

    root.querySelector("[data-arpp-issue-kind]")?.addEventListener("change", () => {
        updateIssueSequence();
        markDirty();
    });

    form.addEventListener("input", () => {
        refreshRowWarnings();
        markDirty();
    });
    form.addEventListener("change", () => {
        refreshRowWarnings();
        markDirty();
    });
    form.addEventListener("submit", () => {
        root.querySelectorAll("[data-arpp-money]").forEach(normaliseMoneyInput);
        dirty = false;
        const button = root.querySelector("[data-arpp-save-button]");
        if (button) {
            button.disabled = true;
            button.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Saving…';
        }
    });

    window.addEventListener("beforeunload", event => {
        if (!dirty) return;
        event.preventDefault();
        event.returnValue = "";
    });

    const entryGuidance = root.querySelector("[data-arpp-entry-guidance]");
    if (entryGuidance) {
        const storageKey = "prism.arpp.entryGuidance.open";
        try {
            if (window.localStorage.getItem(storageKey) === "false") {
                entryGuidance.removeAttribute("open");
            }
        } catch {
            // Storage can be unavailable in hardened or private browser contexts.
        }

        entryGuidance.addEventListener("toggle", () => {
            try {
                window.localStorage.setItem(storageKey, entryGuidance.open ? "true" : "false");
            } catch {
                // Guidance remains fully functional without persistence.
            }
        });
    }

    updateIssueSequence();
    reindexRows();
    markClean();
})();
