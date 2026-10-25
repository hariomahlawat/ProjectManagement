(() => {
    "use strict";

    const form = document.querySelector("[data-arpp-issue-form]");
    if (!form) return;

    const yearInput = form.querySelector("[data-arpp-financial-year]");
    const kindInput = form.querySelector("[data-arpp-issue-kind]");
    const sequenceInput = form.querySelector("[data-arpp-issue-sequence]");
    const sequenceHelp = form.querySelector("[data-arpp-sequence-help]");
    const sequenceLabel = form.querySelector("[data-arpp-sequence-label]");
    const sequenceField = form.querySelector("[data-arpp-addendum-number-field]");
    const suggestionUrl = form.dataset.arppSuggestionUrl;
    let suggestedAddendumSequence = Number(form.dataset.suggestedAddendumSequence || "1");
    let suggestionController = null;

    const KIND_ORIGINAL = "1";
    const KIND_AMENDMENT = "3";

    const updateSequence = () => {
        if (!kindInput || !sequenceInput) return;
        if (kindInput.value === KIND_ORIGINAL) {
            sequenceInput.value = "0";
            sequenceInput.min = "0";
            sequenceInput.readOnly = true;
            sequenceField?.classList.add("d-none");
            if (sequenceHelp) sequenceHelp.textContent = "Original ARPP; internal sequence 0 is stored automatically.";
        } else {
            const isAmendment = kindInput.value === KIND_AMENDMENT;
            sequenceInput.readOnly = false;
            sequenceInput.min = "1";
            sequenceField?.classList.remove("d-none");
            if (Number(sequenceInput.value) <= 0) sequenceInput.value = String(Math.max(1, suggestedAddendumSequence));
            if (sequenceLabel) sequenceLabel.textContent = isAmendment ? "Amendment number" : "Addendum number";
            if (sequenceHelp) {
                sequenceHelp.textContent = isAmendment
                    ? "PRISM numbers amendment letters separately from addenda within the financial year."
                    : "Enter the addendum number shown in the issued letter.";
            }
        }
    };

    // kindChanged: the user picked a type, so keep it and only fetch the next number for it.
    const refreshSuggestion = async ({ kindChanged = false } = {}) => {
        const year = Number(yearInput?.value);
        if (!suggestionUrl || !Number.isInteger(year)) return;
        suggestionController?.abort();
        suggestionController = new AbortController();
        const separator = suggestionUrl.includes("?") ? "&" : "?";
        const kindQuery = kindChanged && kindInput?.value ? `&kind=${encodeURIComponent(kindInput.value)}` : "";
        try {
            const response = await fetch(`${suggestionUrl}${separator}financialYearStart=${encodeURIComponent(year)}${kindQuery}`, {
                headers: { Accept: "application/json" },
                signal: suggestionController.signal
            });
            if (!response.ok) return;
            const suggestion = await response.json();
            suggestedAddendumSequence = Math.max(1, Number(suggestion.suggestedSequence || 1));
            if (kindInput && !kindChanged) kindInput.value = String(suggestion.suggestedKind || 1);
            if (sequenceInput) sequenceInput.value = String(suggestion.suggestedSequence ?? 0);
            updateSequence();
        } catch (error) {
            if (error.name !== "AbortError") console.debug("ARPP issue suggestion unavailable.", error);
        }
    };

    yearInput?.addEventListener("change", () => refreshSuggestion());
    kindInput?.addEventListener("change", () => {
        if (sequenceInput && kindInput.value !== KIND_ORIGINAL) sequenceInput.value = "0";
        updateSequence();
        refreshSuggestion({ kindChanged: true });
    });
    updateSequence();
})();
