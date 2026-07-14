(() => {
    "use strict";
    const type = document.querySelector("[data-holiday-type]");
    const behaviour = document.querySelector("[data-holiday-behaviour]");
    if (!type || !behaviour) return;

    const render = () => {
        const restricted = type.value === "Restricted" || type.value === "2";
        const icon = behaviour.querySelector("i");
        const title = behaviour.querySelector("strong");
        const copy = behaviour.querySelector("span");
        behaviour.classList.toggle("admin-holiday-behaviour--restricted", restricted);
        if (icon) icon.className = restricted ? "bi bi-info-square" : "bi bi-building-x";
        if (title) title.textContent = restricted ? "Restricted Holiday" : "Gazetted Holiday";
        if (copy) copy.textContent = restricted
            ? "The date appears on the calendar for information. The office remains open until observance is separately declared."
            : "The office is closed and the date is excluded from future working-day calculations.";
    };
    type.addEventListener("change", render);
    render();
})();
