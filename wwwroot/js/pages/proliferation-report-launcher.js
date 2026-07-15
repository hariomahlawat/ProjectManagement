"use strict";

(() => {
    const input = document.getElementById("pf-report-project-total");
    const hidden = document.getElementById("pf-report-project-total-id");
    const suggestions = document.getElementById("pf-report-project-total-suggestions");
    const open = document.getElementById("pf-report-open-project");
    const kind = document.getElementById("rep-kind");
    if (!input || !hidden || !suggestions || !open) return;

    let controller;
    let timer;

    function clearSuggestions() {
        suggestions.replaceChildren();
        suggestions.classList.add("d-none");
    }

    function selectProject(project) {
        hidden.value = String(project.id);
        input.value = project.display || (project.code ? `${project.name} (${project.code})` : project.name);
        open.disabled = false;
        clearSuggestions();
    }

    async function search() {
        const query = input.value.trim();
        hidden.value = "";
        open.disabled = true;
        if (query.length < 2) {
            clearSuggestions();
            return;
        }

        controller?.abort();
        controller = new AbortController();
        try {
            const response = await fetch(`/api/proliferation/projects?q=${encodeURIComponent(query)}`, {
                headers: { Accept: "application/json" },
                signal: controller.signal
            });
            if (!response.ok) return;
            const rows = await response.json();
            suggestions.replaceChildren();
            rows.forEach(project => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "pf-suggestion";
                button.setAttribute("role", "option");
                const name = document.createElement("strong");
                name.textContent = project.name;
                button.appendChild(name);
                if (project.code) {
                    const code = document.createElement("small");
                    code.textContent = project.code;
                    button.appendChild(code);
                }
                button.addEventListener("click", () => selectProject(project));
                suggestions.appendChild(button);
            });
            suggestions.classList.toggle("d-none", rows.length === 0);
        } catch (error) {
            if (error.name !== "AbortError") clearSuggestions();
        }
    }

    input.addEventListener("input", () => {
        hidden.value = "";
        open.disabled = true;
        clearTimeout(timer);
        timer = setTimeout(search, 250);
    });
    input.addEventListener("blur", () => setTimeout(clearSuggestions, 150));
    input.addEventListener("keydown", event => {
        if (event.key !== "Enter") return;
        if (hidden.value) {
            event.preventDefault();
            open.click();
            return;
        }
        const first = suggestions.querySelector("button");
        if (first) {
            event.preventDefault();
            first.click();
        }
    });
    open.addEventListener("click", () => {
        const id = Number(hidden.value);
        if (Number.isInteger(id) && id > 0) {
            window.location.assign(`/ProjectOfficeReports/Proliferation/Project/${id}`);
        }
    });

    document.querySelectorAll("[data-report-kind]").forEach(button => {
        button.addEventListener("click", () => {
            const value = button.dataset.reportKind;
            if (!value || !kind) return;
            kind.value = value;
            kind.dispatchEvent(new Event("change", { bubbles: true }));
            document.querySelectorAll("[data-report-kind]").forEach(item => item.classList.toggle("active", item === button));
            document.getElementById("report-filters-heading")?.scrollIntoView({ behavior: "smooth", block: "center" });
        });
    });
})();
