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
    document.querySelectorAll("[data-admin-role-grid]").forEach((grid) => {
        const options = Array.from(grid.querySelectorAll("[data-admin-role-option]"));
        const summary = grid.nextElementSibling;
        const count = summary?.querySelector("[data-admin-role-count]");
        const warning = summary?.querySelector("[data-admin-privileged-warning]");

        const refresh = () => {
            const selected = options.filter((option) => {
                const checkbox = option.querySelector("[data-admin-role-checkbox]");
                const checked = checkbox instanceof HTMLInputElement && checkbox.checked;
                option.classList.toggle("is-selected", checked);
                return checked;
            });

            if (count) {
                count.textContent = `${selected.length} role${selected.length === 1 ? "" : "s"} selected`;
            }

            if (warning) {
                const privilegedSelected = selected.some((option) => {
                    const checkbox = option.querySelector("[data-admin-role-checkbox]");
                    return checkbox instanceof HTMLInputElement
                        && checkbox.dataset.privileged === "true";
                });
                warning.hidden = !privilegedSelected;
            }
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
