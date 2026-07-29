const onReady = (callback) => {
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", callback, { once: true });
        return;
    }

    callback();
};

const normalise = (value) => (value ?? "").trim().toLocaleLowerCase();

const initialiseFilters = () => {
    document.querySelectorAll("[data-admin-auto-submit]").forEach((control) => {
        control.addEventListener("change", () => {
            const form = control.closest("form");
            if (!form) return;

            const pageInput = form.querySelector('input[name="PageNo"]');
            if (pageInput) pageInput.value = "1";
            form.requestSubmit();
        });
    });
};

const initialiseFlashMessages = () => {
    document.querySelectorAll("[data-admin-flash-dismiss]").forEach((button) => {
        button.addEventListener("click", () => {
            const message = button.closest("[data-admin-flash]");
            if (!message) return;

            message.setAttribute("hidden", "");
        });
    });
};

const initialiseRoleGrids = () => {
    document.querySelectorAll("[data-admin-role-assignment]").forEach((assignment) => {
        const grid = assignment.querySelector("[data-admin-role-grid]");
        if (!(grid instanceof HTMLElement)) return;

        const options = Array.from(grid.querySelectorAll("[data-admin-role-option]"));
        const count = assignment.querySelector("[data-admin-role-count]");
        const warning = assignment.querySelector("[data-admin-privileged-warning]");
        const accessCount = assignment.querySelector("[data-admin-access-count]");
        const roleChips = assignment.querySelector("[data-admin-access-role-chips]");
        const emptyState = assignment.querySelector("[data-admin-access-empty]");
        const accessGroups = assignment.querySelector("[data-admin-access-groups]");
        const accessItems = Array.from(assignment.querySelectorAll("[data-admin-access-item]"));
        const groups = Array.from(assignment.querySelectorAll("[data-admin-access-group]"));

        const splitRoles = (value) => (value ?? "")
            .split("|")
            .map(normalise)
            .filter((role) => role.length > 0);

        const refresh = () => {
            const selectedOptions = options.filter((option) => {
                const checkbox = option.querySelector("[data-admin-role-checkbox]");
                const checked = checkbox instanceof HTMLInputElement && checkbox.checked;
                option.classList.toggle("is-selected", checked);
                return checked;
            });

            const selectedRoleNames = new Set(selectedOptions
                .map((option) => option.querySelector("[data-admin-role-checkbox]"))
                .filter((checkbox) => checkbox instanceof HTMLInputElement)
                .map((checkbox) => normalise(checkbox.value)));

            if (count) {
                count.textContent = `${selectedOptions.length} role${selectedOptions.length === 1 ? "" : "s"} selected`;
            }

            if (warning) {
                const privilegedSelected = selectedOptions.some((option) => {
                    const checkbox = option.querySelector("[data-admin-role-checkbox]");
                    return checkbox instanceof HTMLInputElement
                        && checkbox.dataset.privileged === "true";
                });
                warning.hidden = !privilegedSelected;
            }

            if (roleChips instanceof HTMLElement) {
                const labels = selectedOptions
                    .map((option) => option.querySelector("[data-admin-role-checkbox]"))
                    .filter((checkbox) => checkbox instanceof HTMLInputElement)
                    .map((checkbox) => checkbox.dataset.roleDisplay || checkbox.value)
                    .filter((label, index, all) => all.indexOf(label) === index);

                roleChips.replaceChildren(...labels.map((label) => {
                    const chip = document.createElement("span");
                    chip.textContent = label;
                    return chip;
                }));
                roleChips.hidden = labels.length === 0;
            }

            let visibleCapabilityCount = 0;
            accessItems.forEach((item) => {
                const permittedRoles = splitRoles(item.dataset.roleNames);
                const visible = permittedRoles.some((role) => selectedRoleNames.has(role));
                item.hidden = !visible;
                if (visible) visibleCapabilityCount += 1;
            });

            groups.forEach((group) => {
                const hasVisibleItem = Array.from(group.querySelectorAll("[data-admin-access-item]"))
                    .some((item) => !item.hidden);
                group.hidden = !hasVisibleItem;
            });

            if (accessCount) {
                accessCount.textContent = `${visibleCapabilityCount} capabilit${visibleCapabilityCount === 1 ? "y" : "ies"}`;
            }

            const hasSelection = selectedRoleNames.size > 0;
            if (emptyState instanceof HTMLElement) emptyState.hidden = hasSelection;
            if (accessGroups instanceof HTMLElement) accessGroups.hidden = !hasSelection;
        };

        grid.addEventListener("change", refresh);
        refresh();
    });
};

const initialiseConfirmations = () => {
    document.querySelectorAll("[data-admin-confirmation]").forEach((form) => {
        const username = form.querySelector("[data-admin-confirm-username]");
        const acknowledge = form.querySelector("[data-admin-confirm-ack]");
        const submit = form.querySelector("[data-admin-confirm-submit]");

        if (!(username instanceof HTMLInputElement)
            || !(acknowledge instanceof HTMLInputElement)
            || !(submit instanceof HTMLButtonElement)) {
            return;
        }

        const refresh = () => {
            const expected = normalise(username.dataset.expected);
            const supplied = normalise(username.value);
            submit.disabled = expected.length === 0
                || supplied !== expected
                || !acknowledge.checked;
        };

        username.addEventListener("input", refresh);
        acknowledge.addEventListener("change", refresh);
        refresh();
    });
};

onReady(() => {
    initialiseFilters();
    initialiseFlashMessages();
    initialiseRoleGrids();
    initialiseConfirmations();
});
