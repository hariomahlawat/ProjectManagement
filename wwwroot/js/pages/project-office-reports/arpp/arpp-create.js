(() => {
    "use strict";

    const form = document.querySelector("[data-arpp-issue-form]");
    if (!form) return;

    const yearInput = form.querySelector("[data-arpp-financial-year]");
    const kindInput = form.querySelector("[data-arpp-issue-kind]");
    const sequenceInput = form.querySelector("[data-arpp-issue-sequence]");
    const sequenceHelp = form.querySelector("[data-arpp-sequence-help]");
    const sequenceField = form.querySelector("[data-arpp-addendum-number-field]");
    const suggestionUrl = form.dataset.arppSuggestionUrl;
    let suggestedAddendumSequence = Number(form.dataset.suggestedAddendumSequence || "1");
    let suggestionController = null;

    const updateSequence = () => {
        if (!kindInput || !sequenceInput) return;
        if (kindInput.value === "1") {
            sequenceInput.value = "0";
            sequenceInput.min = "0";
            sequenceInput.readOnly = true;
            sequenceField?.classList.add("d-none");
            if (sequenceHelp) sequenceHelp.textContent = "Original ARPP; internal sequence 0 is stored automatically.";
        } else {
            sequenceInput.readOnly = false;
            sequenceInput.min = "1";
            sequenceField?.classList.remove("d-none");
            if (Number(sequenceInput.value) <= 0) sequenceInput.value = String(Math.max(1, suggestedAddendumSequence));
            if (sequenceHelp) sequenceHelp.textContent = "Enter the addendum number shown in the issued document.";
        }
    };

    const refreshSuggestion = async () => {
        const year = Number(yearInput?.value);
        if (!suggestionUrl || !Number.isInteger(year)) return;
        suggestionController?.abort();
        suggestionController = new AbortController();
        const separator = suggestionUrl.includes("?") ? "&" : "?";
        try {
            const response = await fetch(`${suggestionUrl}${separator}financialYearStart=${encodeURIComponent(year)}`, {
                headers: { Accept: "application/json" },
                signal: suggestionController.signal
            });
            if (!response.ok) return;
            const suggestion = await response.json();
            suggestedAddendumSequence = Math.max(1, Number(suggestion.suggestedSequence || 1));
            if (kindInput) kindInput.value = String(suggestion.suggestedKind || 1);
            if (sequenceInput) sequenceInput.value = String(suggestion.suggestedSequence ?? 0);
            updateSequence();
        } catch (error) {
            if (error.name !== "AbortError") console.debug("ARPP issue suggestion unavailable.", error);
        }
    };

    yearInput?.addEventListener("change", refreshSuggestion);
    kindInput?.addEventListener("change", updateSequence);
    updateSequence();
})();
