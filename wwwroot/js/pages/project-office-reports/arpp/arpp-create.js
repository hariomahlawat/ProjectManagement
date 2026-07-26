(() => {
    "use strict";

    const form = document.querySelector("[data-arpp-issue-form]");
    if (!form) return;

    const yearInput = form.querySelector("[data-arpp-financial-year]");
    const yearDisplay = form.querySelector("[data-arpp-financial-year-display]");
    const kindInput = form.querySelector("[data-arpp-issue-kind]");
    const sequenceInput = form.querySelector("[data-arpp-issue-sequence]");
    const suggestionUrl = form.dataset.arppSuggestionUrl;
    let suggestedAddendumSequence = Number(form.dataset.suggestedAddendumSequence || "1");
    let suggestionTimer = null;
    let suggestionController = null;

    const formatFinancialYear = (value) => {
        const year = Number(value);
        if (!Number.isInteger(year) || year < 2000 || year > 9998) return "Enter a valid start year";
        return `${year}-${String((year + 1) % 100).padStart(2, "0")}`;
    };

    const updateYearDisplay = () => {
        if (yearDisplay && yearInput) yearDisplay.textContent = formatFinancialYear(yearInput.value);
    };

    const updateSequence = () => {
        if (!kindInput || !sequenceInput) return;
        if (kindInput.value === "1") {
            sequenceInput.value = "0";
            sequenceInput.min = "0";
            sequenceInput.readOnly = true;
        } else {
            sequenceInput.readOnly = false;
            sequenceInput.min = "1";
            if (Number(sequenceInput.value) <= 0) {
                sequenceInput.value = String(Math.max(1, suggestedAddendumSequence));
            }
        }
    };

    const refreshSuggestion = async () => {
        const year = Number(yearInput?.value);
        if (!suggestionUrl || !Number.isInteger(year) || year < 2000 || year > 9998) return;

        suggestionController?.abort();
        suggestionController = new AbortController();
        const separator = suggestionUrl.includes("?") ? "&" : "?";

        try {
            const response = await fetch(
                `${suggestionUrl}${separator}financialYearStart=${encodeURIComponent(year)}`,
                {
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
            if (error.name !== "AbortError") {
                // Server-side validation remains authoritative if a suggestion cannot be loaded.
                console.debug("ARPP issue suggestion unavailable.", error);
            }
        }
    };

    yearInput?.addEventListener("input", () => {
        updateYearDisplay();
        window.clearTimeout(suggestionTimer);
        suggestionTimer = window.setTimeout(refreshSuggestion, 250);
    });
    kindInput?.addEventListener("change", updateSequence);
    updateYearDisplay();
    updateSequence();
})();
