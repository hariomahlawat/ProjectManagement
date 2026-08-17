(() => {
    "use strict";

    const forms = Array.from(document.querySelectorAll("[data-report-controls]"));
    if (forms.length === 0) {
        return;
    }

    const normalizeValue = value => String(value ?? "").trim();

    const createController = form => {
        const settings = Array.from(form.querySelectorAll("[data-report-setting]"));
        const updateButton = form.querySelector("[data-report-update]");
        const updateRequired = form.querySelector("[data-report-update-required]");
        const exportLinks = Array.from(document.querySelectorAll("[data-report-export]"));

        const baseExportDisabled = new Map(
            exportLinks.map(link => [
                link,
                link.classList.contains("disabled")
                    || String(link.getAttribute("aria-disabled")).toLowerCase() === "true"
            ]));

        const serializeSettings = () => {
            const grouped = new Map();

            settings.forEach((element, index) => {
                const key = normalizeValue(
                    element.dataset.reportSettingKey
                    || element.name
                    || element.id
                    || `setting-${index}`);

                if (!grouped.has(key)) {
                    grouped.set(key, []);
                }

                const values = grouped.get(key);
                const type = normalizeValue(element.type).toLowerCase();

                if (type === "checkbox" || type === "radio") {
                    if (element.checked) {
                        values.push(normalizeValue(element.value || "true"));
                    }
                    return;
                }

                if (element instanceof HTMLSelectElement && element.multiple) {
                    Array.from(element.selectedOptions)
                        .forEach(option => values.push(normalizeValue(option.value)));
                    return;
                }

                values.push(normalizeValue(element.value));
            });

            return Array.from(grouped.entries())
                .sort(([left], [right]) => left.localeCompare(right))
                .map(([key, values]) => [
                    key,
                    values
                        .slice()
                        .sort((left, right) =>
                            left.localeCompare(right, undefined, { numeric: true }))
                ]);
        };

        const signature = () => JSON.stringify(serializeSettings());
        const appliedSignature = signature();

        const setExportDisabled = (link, disabled) => {
            link.classList.toggle("disabled", disabled);
            link.setAttribute("aria-disabled", disabled ? "true" : "false");

            if (disabled) {
                link.setAttribute("tabindex", "-1");
            } else {
                link.removeAttribute("tabindex");
            }
        };

        const hasPendingChanges = () => signature() !== appliedSignature;

        const refresh = () => {
            const pending = hasPendingChanges();

            updateButton?.classList.toggle("report-refresh-button--pending", pending);

            if (updateRequired) {
                updateRequired.hidden = !pending;
            }

            exportLinks.forEach(link => {
                const disabled = pending || Boolean(baseExportDisabled.get(link));
                setExportDisabled(link, disabled);
            });

            form.dataset.reportPending = pending ? "true" : "false";
            return pending;
        };

        settings.forEach(element => {
            element.addEventListener("change", refresh);
            element.addEventListener("input", event => {
                if (event.target instanceof HTMLInputElement
                    && event.target.type.toLowerCase() === "checkbox") {
                    return;
                }

                refresh();
            });
        });

        form.addEventListener("prism:report-settings-changed", refresh);

        exportLinks.forEach(link => {
            link.addEventListener("click", event => {
                if (String(link.getAttribute("aria-disabled")).toLowerCase() === "true") {
                    event.preventDefault();
                }
            });
        });

        refresh();

        return Object.freeze({
            refresh,
            hasPendingChanges
        });
    };

    forms.forEach(form => {
        form.prismReportControls = createController(form);
    });
})();
