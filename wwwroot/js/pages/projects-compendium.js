(() => {
    "use strict";

    const form = document.querySelector("[data-compendium-builder]");
    if (!(form instanceof HTMLFormElement)) return;

    const parseJson = (node, fallback) => {
        try { return node?.textContent ? JSON.parse(node.textContent) : fallback; }
        catch { return fallback; }
    };
    const projects = parseJson(form.querySelector("[data-compendium-projects]"), []);
    const projectById = new Map(projects.map(p => [Number(p.projectId), p]));
    const presetSeed = parseJson(form.querySelector("[data-compendium-presets]"), []);
    const presets = new Map(presetSeed.map(p => [Number(p.id), { ...p, id: Number(p.id) }]));
    const activeSeed = parseJson(form.querySelector("[data-compendium-active-preset]"), {});
    const canManage = Boolean(activeSeed?.canManage);

    const selectedInput = form.querySelector("[data-selected-project-ids]");
    const activeIdInput = form.querySelector("[data-active-preset-id]");
    const activeVersionInput = form.querySelector("[data-active-preset-row-version]");
    let activePresetId = Number(activeIdInput?.value || activeSeed?.id || 0) || null;
    let activeRowVersion = String(activeVersionInput?.value || activeSeed?.rowVersion || "");
    let orderedIds = String(selectedInput?.value || "").split(",").map(Number).filter(id => id > 0 && projectById.has(id));
    orderedIds = [...new Set(orderedIds)];
    let activeReviewId = orderedIds[0] ?? null;
    let preflightTimer = null;
    let preflightController = null;
    let preflightPending = false;
    let preflightRevision = 0;
    let lastPreflight = null;
    let baselineSnapshot = null;
    let pendingLoadPresetId = null;
    let saveMode = "create";

    const $ = selector => form.querySelector(selector);
    const rows = [...form.querySelectorAll("[data-project-row]")];
    const rowById = new Map(rows.map(row => [Number(row.dataset.id), row]));
    const search = $("[data-filter-search]");
    const lifecycle = $("[data-filter-lifecycle]");
    const category = $("[data-filter-category]");
    const technical = $("[data-filter-technical]");
    const proliferation = $("[data-filter-proliferation]");
    const selectedOnly = $("[data-filter-selected]");
    const matchingCount = $("[data-compendium-matching]");
    const selectedCount = $("[data-compendium-selected-count]");
    const selectMatching = $("[data-select-matching]");
    const clearSelection = $("[data-clear-selection]");
    const orderList = $("[data-order-list]");
    const railCount = $("[data-rail-count]");
    const reviewEmpty = $("[data-review-empty]");
    const reviewCard = $("[data-review-card]");
    const reviewOrdinal = $("[data-review-ordinal]");
    const reviewName = $("[data-review-name]");
    const reviewMeta = $("[data-review-meta]");
    const reviewFacts = $("[data-review-facts]");
    const reviewOpen = $("[data-review-open-project]");
    const reviewNext = $("[data-review-next]");
    const readySelected = $("[data-ready-selected]");
    const readyBlockers = $("[data-ready-blockers]");
    const readyWarnings = $("[data-ready-warnings]");
    const readyInfo = $("[data-ready-info]");
    const readyCategories = $("[data-ready-categories]");
    const readyFindings = $("[data-ready-findings]");
    const preflightSpinner = $("[data-preflight-spinner]");
    const preview = $("[data-preview]");
    const generate = $("[data-generate]");
    const outputStatus = $("[data-output-status]");

    const presetSelect = document.querySelector("[data-compendium-preset-select]");
    const presetLoad = document.querySelector("[data-compendium-preset-load]");
    const presetDirty = document.querySelector("[data-compendium-preset-dirty]");
    const presetMeta = document.querySelector("[data-compendium-preset-meta]");
    const saveAsNew = document.querySelector("[data-compendium-save-as-new]");
    const saveChanges = document.querySelector("[data-compendium-save-changes]");
    const renameButton = document.querySelector("[data-compendium-rename]");
    const duplicateButton = document.querySelector("[data-compendium-duplicate]");
    const deleteButton = document.querySelector("[data-compendium-delete]");

    const bootstrapModal = id => {
        const node = document.getElementById(id);
        return node && window.bootstrap?.Modal ? window.bootstrap.Modal.getOrCreateInstance(node) : null;
    };
    const discardModal = bootstrapModal("compendiumDiscardModal");
    const saveModal = bootstrapModal("compendiumSaveModal");
    const renameModal = bootstrapModal("compendiumRenameModal");
    const deleteModal = bootstrapModal("compendiumDeleteModal");
    const saveName = document.querySelector("[data-save-name]");
    const saveDescription = document.querySelector("[data-save-description]");
    const saveMessage = document.querySelector("[data-save-message]");
    const renameName = document.querySelector("[data-rename-name]");

    const normalize = value => String(value ?? "").trim().toLowerCase();
    const isSelected = id => orderedIds.includes(Number(id));
    const syncHidden = () => {
        if (selectedInput) selectedInput.value = orderedIds.join(",");
        if (activeIdInput) activeIdInput.value = activePresetId ? String(activePresetId) : "";
        if (activeVersionInput) activeVersionInput.value = activeRowVersion || "";
    };

    const visibleRows = () => rows.filter(row => !row.hidden);
    const applyFilters = () => {
        const term = normalize(search?.value);
        const life = normalize(lifecycle?.value);
        const cat = normalize(category?.value);
        const tech = normalize(technical?.value);
        const prol = normalize(proliferation?.value);
        const only = Boolean(selectedOnly?.checked);
        let count = 0;
        rows.forEach(row => {
            const id = Number(row.dataset.id);
            const visible = (!term || normalize(row.dataset.name).includes(term))
                && (!life || normalize(row.dataset.lifecycle) === life)
                && (!cat || normalize(row.dataset.category) === cat)
                && (!tech || normalize(row.dataset.technical) === tech)
                && (!prol || normalize(row.dataset.proliferation) === prol)
                && (!only || isSelected(id));
            row.hidden = !visible;
            if (visible) count++;
        });
        if (matchingCount) matchingCount.textContent = String(count);
        if (selectMatching) {
            const selectable = visibleRows().filter(row => !isSelected(Number(row.dataset.id))).length;
            selectMatching.disabled = selectable === 0;
            selectMatching.textContent = selectable > 100 ? "Select first 100 matching" : selectable === 1 ? "Select 1 matching" : `Select ${selectable} matching`;
        }
    };

    const updateCheckboxes = () => rows.forEach(row => {
        const box = row.querySelector("[data-project-checkbox]");
        if (box instanceof HTMLInputElement) box.checked = isSelected(Number(row.dataset.id));
        row.classList.toggle("is-selected", isSelected(Number(row.dataset.id)));
    });

    const escapeHtml = value => String(value ?? "").replace(/[&<>'"]/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;","'":"&#39;",'"':"&quot;"})[c]);
    const renderOrder = () => {
        if (selectedCount) selectedCount.textContent = String(orderedIds.length);
        if (railCount) railCount.textContent = String(orderedIds.length);
        if (!orderList) return;
        if (orderedIds.length === 0) {
            orderList.innerHTML = '<div class="compendium-order-empty"><i class="bi bi-journal"></i><span>Select projects from the portfolio.</span></div>';
            return;
        }
        orderList.innerHTML = orderedIds.map((id, index) => {
            const p = projectById.get(id);
            if (!p) return "";
            return `<div class="compendium-order-item" data-order-id="${id}" draggable="true">
                <span class="compendium-order-handle" aria-label="Drag to reorder"><i class="bi bi-grip-vertical"></i></span>
                <div class="compendium-order-copy"><strong>${escapeHtml(p.projectName)}</strong><small>${escapeHtml(p.technicalCategory || "Technical category not recorded")} · ${escapeHtml(p.lifecycle)}</small></div>
                <div class="compendium-order-actions"><button type="button" data-move-up title="Move up" ${index === 0 ? "disabled" : ""}><i class="bi bi-chevron-up"></i></button><button type="button" data-move-down title="Move down" ${index === orderedIds.length-1 ? "disabled" : ""}><i class="bi bi-chevron-down"></i></button><button type="button" data-remove title="Remove"><i class="bi bi-x-lg"></i></button></div>
            </div>`;
        }).join("");
    };

    const renderReview = () => {
        if (activeReviewId == null || !isSelected(activeReviewId)) activeReviewId = orderedIds[0] ?? null;
        const p = activeReviewId ? projectById.get(activeReviewId) : null;
        if (!p) { if (reviewEmpty) reviewEmpty.hidden = false; if (reviewCard) reviewCard.hidden = true; if (reviewNext) reviewNext.disabled = true; return; }
        if (reviewEmpty) reviewEmpty.hidden = true; if (reviewCard) reviewCard.hidden = false; if (reviewNext) reviewNext.disabled = orderedIds.length < 2;
        const ordinal = orderedIds.indexOf(activeReviewId) + 1;
        if (reviewOrdinal) reviewOrdinal.textContent = `PROJECT ${ordinal} OF ${orderedIds.length}`;
        if (reviewName) reviewName.textContent = p.projectName;
        if (reviewMeta) reviewMeta.textContent = `${p.lifecycle} · ${p.technicalCategory || "Technical category not recorded"}`;
        if (reviewFacts) reviewFacts.innerHTML = `
            <div><span>Description</span><strong>${p.hasDescription ? "Recorded" : "Missing"}</strong></div>
            <div><span>Arm / Service</span><strong>${p.hasArmService ? "Recorded" : "Missing"}</strong></div>
            <div><span>Proliferation</span><strong>${p.isAvailableForProliferation ? "Available" : "Not marked available"}</strong></div>
            <div><span>Photographs</span><strong>${p.photoCount}</strong></div>`;
        if (reviewOpen) reviewOpen.href = `/Projects/Overview?id=${p.projectId}`;
    };

    const captureSnapshot = () => JSON.stringify({
        title: form.elements["Input.Title"]?.value?.trim() || "",
        subtitle: form.elements["Input.Subtitle"]?.value?.trim() || "",
        edition: form.elements["Input.Edition"]?.value?.trim() || "",
        marking: form.elements["Input.HandlingMarking"]?.value?.trim() || "",
        projectIds: orderedIds
    });
    const renderDirty = () => {
        const dirty = baselineSnapshot != null && captureSnapshot() !== baselineSnapshot;
        if (presetDirty) { presetDirty.hidden = !activePresetId || !dirty; presetDirty.textContent = canManage ? "Modified" : "Modified locally"; }
        if (saveChanges) saveChanges.disabled = !activePresetId || !dirty;
        if (renameButton) renameButton.disabled = !activePresetId;
        if (duplicateButton) duplicateButton.disabled = !activePresetId;
        if (deleteButton) deleteButton.disabled = !activePresetId;
        return dirty;
    };
    const markClean = () => { baselineSnapshot = captureSnapshot(); renderDirty(); };

    const updateOutput = (preflight = lastPreflight) => {
        const selected = Number(preflight?.selected ?? orderedIds.length);
        const blockers = Number(preflight?.blockers ?? 0);
        const warnings = Number(preflight?.warnings ?? 0);
        const isCurrent = preflight != null && !preflightPending;
        const canGenerate = isCurrent && Boolean(preflight.canGenerate) && blockers === 0;

        if (preview) preview.disabled = !canGenerate;
        if (generate) generate.disabled = !canGenerate;
        if (!outputStatus) return;

        if (!selected) {
            outputStatus.innerHTML = '<i class="bi bi-journal"></i><div><strong>Select projects</strong><span>Choose at least one project to begin publication preflight.</span></div>';
        } else if (preflightPending || preflight == null) {
            outputStatus.innerHTML = '<i class="bi bi-arrow-repeat"></i><div><strong>Checking publication</strong><span>PRISM is validating the current selection and settings.</span></div>';
        } else if (blockers) {
            outputStatus.innerHTML = `<i class="bi bi-exclamation-octagon"></i><div><strong>Resolve ${blockers} blocker${blockers === 1 ? "" : "s"}</strong><span>Preview and download remain unavailable.</span></div>`;
        } else if (warnings) {
            outputStatus.innerHTML = `<i class="bi bi-exclamation-triangle"></i><div><strong>Ready with warnings</strong><span>${warnings} warning${warnings === 1 ? "" : "s"} remain for editorial review.</span></div>`;
        } else {
            outputStatus.innerHTML = '<i class="bi bi-check-circle"></i><div><strong>Ready</strong><span>Selected Compendium passed current publication preflight.</span></div>';
        }
    };

    const renderPreflight = result => {
        lastPreflight = result;
        if (readySelected) readySelected.textContent = String(result.selected ?? 0);
        if (readyBlockers) readyBlockers.textContent = String(result.blockers ?? 0);
        if (readyWarnings) readyWarnings.textContent = String(result.warnings ?? 0);
        if (readyInfo) readyInfo.textContent = String(result.info ?? 0);
        if (readyCategories) readyCategories.textContent = String(result.categories ?? 0);
        if (readyFindings) {
            const findings = Array.isArray(result.findings) ? result.findings : [];
            readyFindings.innerHTML = findings.length
                ? findings.slice(0, 12).map(finding => `<div class="compendium-finding is-${escapeHtml(finding.severity)}"><strong>${escapeHtml(finding.projectName || "Publication")}</strong><span>${escapeHtml(finding.message)}</span></div>`).join("")
                : '<div class="compendium-finding is-information"><span>No current publication findings.</span></div>';
        }
    };

    const invalidatePreflight = () => {
        preflightRevision += 1;
        preflightController?.abort();
        preflightController = null;
        lastPreflight = null;
        preflightPending = orderedIds.length > 0;
        updateOutput();
    };

    const runPreflight = async revision => {
        if (revision !== preflightRevision) return;
        syncHidden();
        const controller = new AbortController();
        preflightController = controller;
        preflightPending = true;
        preflightSpinner?.classList.remove("d-none");
        updateOutput();

        try {
            const response = await fetch(form.dataset.preflightUrl, {
                method: "POST",
                body: new FormData(form),
                signal: controller.signal,
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            if (!response.ok) {
                throw new Error("Publication preflight could not be completed.");
            }
            const result = await response.json();
            if (revision !== preflightRevision) return;
            renderPreflight(result);
        } catch (error) {
            if (revision !== preflightRevision || error?.name === "AbortError") return;
            const failure = {
                selected: orderedIds.length,
                blockers: 1,
                warnings: 0,
                info: 0,
                categories: 0,
                canGenerate: false,
                findings: [{
                    severity: "blocker",
                    code: "preflightFailed",
                    message: error.message || "Publication preflight failed.",
                    projectId: null,
                    projectName: null
                }]
            };
            renderPreflight(failure);
        } finally {
            if (revision === preflightRevision) {
                preflightPending = false;
                preflightController = null;
                preflightSpinner?.classList.add("d-none");
                updateOutput();
            }
        }
    };

    const schedulePreflight = () => {
        window.clearTimeout(preflightTimer);
        invalidatePreflight();
        const revision = preflightRevision;
        if (orderedIds.length === 0) {
            preflightPending = false;
            if (readySelected) readySelected.textContent = "0";
            if (readyBlockers) readyBlockers.textContent = "1";
            if (readyWarnings) readyWarnings.textContent = "0";
            if (readyInfo) readyInfo.textContent = "0";
            if (readyCategories) readyCategories.textContent = "0";
            if (readyFindings) readyFindings.innerHTML = '<div class="compendium-finding is-blocker">Select at least one project to begin publication preflight.</div>';
            updateOutput();
            return;
        }
        preflightTimer = window.setTimeout(() => runPreflight(revision), 260);
    };

    const changed = () => { syncHidden(); updateCheckboxes(); renderOrder(); renderReview(); applyFilters(); renderDirty(); schedulePreflight(); };
    rows.forEach(row => row.querySelector("[data-project-checkbox]")?.addEventListener("change", event => {
        const id = Number(row.dataset.id);
        if (event.currentTarget.checked) { if (!isSelected(id)) orderedIds.push(id); activeReviewId ??= id; }
        else { orderedIds = orderedIds.filter(x => x !== id); if (activeReviewId === id) activeReviewId = orderedIds[0] ?? null; }
        changed();
    }));
    [search,lifecycle,category,technical,proliferation,selectedOnly].forEach(control => control?.addEventListener(control === search ? "input" : "change", applyFilters));
    selectMatching?.addEventListener("click", () => { visibleRows().filter(row => !isSelected(Number(row.dataset.id))).slice(0,100).forEach(row => orderedIds.push(Number(row.dataset.id))); orderedIds=[...new Set(orderedIds)]; activeReviewId ??= orderedIds[0]??null; changed(); });
    clearSelection?.addEventListener("click", () => { orderedIds=[]; activeReviewId=null; changed(); });
    orderList?.addEventListener("click", event => { const item=event.target.closest("[data-order-id]"); if(!item)return; const id=Number(item.dataset.orderId), index=orderedIds.indexOf(id); if(event.target.closest("[data-remove]")){orderedIds=orderedIds.filter(x=>x!==id);if(activeReviewId===id)activeReviewId=orderedIds[0]??null;}else if(event.target.closest("[data-move-up]")&&index>0){[orderedIds[index-1],orderedIds[index]]=[orderedIds[index],orderedIds[index-1]];}else if(event.target.closest("[data-move-down]")&&index>=0&&index<orderedIds.length-1){[orderedIds[index+1],orderedIds[index]]=[orderedIds[index],orderedIds[index+1]];}else{return;}changed(); });
    let draggedOrderId = null;
    orderList?.addEventListener("dragstart", event => {
        const item = event.target.closest("[data-order-id]");
        if (!item) return;
        draggedOrderId = Number(item.dataset.orderId) || null;
        item.classList.add("is-dragging");
        if (event.dataTransfer) { event.dataTransfer.effectAllowed = "move"; event.dataTransfer.setData("text/plain", String(draggedOrderId || "")); }
    });
    orderList?.addEventListener("dragover", event => { if (draggedOrderId != null) { event.preventDefault(); if (event.dataTransfer) event.dataTransfer.dropEffect = "move"; } });
    orderList?.addEventListener("drop", event => {
        if (draggedOrderId == null) return;
        event.preventDefault();
        const target = event.target.closest("[data-order-id]");
        const targetId = Number(target?.dataset.orderId || 0);
        if (!targetId || targetId === draggedOrderId) return;
        const from = orderedIds.indexOf(draggedOrderId), to = orderedIds.indexOf(targetId);
        if (from < 0 || to < 0) return;
        const [moved] = orderedIds.splice(from, 1);
        orderedIds.splice(to, 0, moved);
        changed();
    });
    orderList?.addEventListener("dragend", () => {
        orderList.querySelectorAll(".is-dragging").forEach(item => item.classList.remove("is-dragging"));
        draggedOrderId = null;
    });
    reviewNext?.addEventListener("click",()=>{if(!orderedIds.length)return;const i=Math.max(0,orderedIds.indexOf(activeReviewId));activeReviewId=orderedIds[(i+1)%orderedIds.length];renderReview();});
    form.querySelectorAll("[data-compendium-durable]").forEach(input => input.addEventListener("input",()=>{renderDirty();schedulePreflight();}));
    form.addEventListener("submit",()=>syncHidden());

    const updatePresetOption = preset => {
        if (!presetSelect || !preset) return;
        let option = [...presetSelect.options].find(o => Number(o.value) === Number(preset.id));
        if (!option) { option = new Option(preset.name, String(preset.id)); presetSelect.add(option); }
        option.textContent=preset.name; presets.set(Number(preset.id),{...preset,id:Number(preset.id)});
    };
    const setActivePreset = preset => { activePresetId=Number(preset?.id||0)||null; activeRowVersion=String(preset?.rowVersion||""); syncHidden(); if(presetSelect)presetSelect.value=activePresetId?String(activePresetId):""; if(presetMeta&&preset) presetMeta.textContent=`Shared · ${preset.projectCount} projects · Updated ${new Date(preset.updatedAtUtc).toLocaleDateString()} · ${preset.updatedByDisplay}`; if(activePresetId)history.replaceState(null,"",`${location.pathname}?presetId=${activePresetId}`); markClean(); };
    const presetUrl = id => id ? `${location.pathname}?presetId=${Number(id)}` : location.pathname;
    const requestLoad = id => { id=Number(id)||null; if(renderDirty()){pendingLoadPresetId=id;discardModal?.show();return;} location.assign(presetUrl(id)); };
    presetSelect?.addEventListener("change",()=>{});
    presetLoad?.addEventListener("click",()=>requestLoad(presetSelect?.value));
    document.querySelector("[data-discard-load]")?.addEventListener("click",()=>{discardModal?.hide();location.assign(presetUrl(pendingLoadPresetId));});

    const post = async (url, payload) => { const response=await fetch(url,{method:"POST",body:payload,headers:{"X-Requested-With":"XMLHttpRequest"}}); const body=await response.json().catch(()=>({})); if(!response.ok){const e=new Error(body.message||"The saved Compendium operation failed.");e.code=body.code;e.status=response.status;throw e;} return body; };
    const openSave = mode => { if(!canManage)return; saveMode=mode; const source=activePresetId?presets.get(activePresetId):null; if(saveName)saveName.value=mode==="duplicate"&&source?`${source.name} — Copy`:mode==="create"?(form.elements["Input.Title"]?.value||"Simulators Compendium"):source?.name||""; if(saveDescription)saveDescription.value=source?.description||""; if(saveMessage)saveMessage.textContent=""; saveModal?.show(); };
    saveAsNew?.addEventListener("click",()=>openSave("create"));
    duplicateButton?.addEventListener("click",()=>activePresetId&&openSave("duplicate"));
    document.querySelector("[data-save-confirm]")?.addEventListener("click",async()=>{const name=String(saveName?.value||"").trim();if(name.length<3){if(saveMessage)saveMessage.textContent="Enter a name of at least 3 characters.";return;}syncHidden();try{let result;if(saveMode==="duplicate"){const payload=new FormData();const token=form.querySelector('input[name="__RequestVerificationToken"]');if(token?.value)payload.append("__RequestVerificationToken",token.value);payload.append("presetId",String(activePresetId));payload.append("rowVersion",activeRowVersion);payload.append("name",name);payload.append("description",String(saveDescription?.value||""));result=await post(form.dataset.duplicateUrl,payload);}else{const payload=new FormData(form);payload.set("saveAsNew","true");payload.set("presetName",name);payload.set("presetDescription",String(saveDescription?.value||""));result=await post(form.dataset.saveUrl,payload);}updatePresetOption(result.preset);setActivePreset(result.preset);saveModal?.hide();}catch(e){if(saveMessage)saveMessage.textContent=e.message;}});
    saveChanges?.addEventListener("click",async()=>{if(!activePresetId||!renderDirty())return;syncHidden();try{const payload=new FormData(form);payload.set("saveAsNew","false");const result=await post(form.dataset.saveUrl,payload);updatePresetOption(result.preset);setActivePreset(result.preset);}catch(e){if(e.code==="presetConflict"){pendingLoadPresetId=activePresetId;discardModal?.show();}else alert(e.message);}});
    renameButton?.addEventListener("click",()=>{const p=presets.get(activePresetId);if(!p)return;if(renameName)renameName.value=p.name;renameModal?.show();});
    document.querySelector("[data-rename-confirm]")?.addEventListener("click",async()=>{const name=String(renameName?.value||"").trim();if(name.length<3)return;const payload=new FormData();const token=form.querySelector('input[name="__RequestVerificationToken"]');if(token?.value)payload.append("__RequestVerificationToken",token.value);payload.append("presetId",String(activePresetId));payload.append("rowVersion",activeRowVersion);payload.append("name",name);try{const result=await post(form.dataset.renameUrl,payload);updatePresetOption(result.preset);setActivePreset(result.preset);renameModal?.hide();}catch(e){alert(e.message);}});
    deleteButton?.addEventListener("click",()=>activePresetId&&deleteModal?.show());
    document.querySelector("[data-delete-confirm]")?.addEventListener("click",async()=>{const payload=new FormData();const token=form.querySelector('input[name="__RequestVerificationToken"]');if(token?.value)payload.append("__RequestVerificationToken",token.value);payload.append("presetId",String(activePresetId));payload.append("rowVersion",activeRowVersion);try{await post(form.dataset.deleteUrl,payload);deleteModal?.hide();location.assign(location.pathname);}catch(e){alert(e.message);}});

    syncHidden(); updateCheckboxes(); renderOrder(); renderReview(); applyFilters(); baselineSnapshot=captureSnapshot(); renderDirty(); schedulePreflight();
})();
