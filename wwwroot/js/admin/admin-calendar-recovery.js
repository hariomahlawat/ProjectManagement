(() => {
    "use strict";
    const modal = document.getElementById("calendarRestoreModal");
    if (!modal) return;
    const id = modal.querySelector("[data-calendar-event-id]");
    const title = modal.querySelector("[data-calendar-event-title]");
    const schedule = modal.querySelector("[data-calendar-event-schedule]");
    const explanation = modal.querySelector("[data-calendar-restore-explanation]");
    const form = modal.querySelector("[data-calendar-restore-form]");
    const submit = modal.querySelector("[data-calendar-submit]");
    document.querySelectorAll("[data-calendar-restore]").forEach(button => {
        button.addEventListener("click", () => {
            id.value = button.dataset.eventId || "";
            title.textContent = button.dataset.eventTitle || "Selected event";
            schedule.textContent = button.dataset.eventSchedule || "When";
            explanation.textContent = button.dataset.eventRecurring === "true"
                ? "The whole series goes back on the shared calendar and repeats as before."
                : "The event goes back on the shared calendar, on its original dates.";
        });
    });
    form?.addEventListener("submit", () => {
        submit.disabled = true;
        submit.setAttribute("aria-busy", "true");
    });
})();
