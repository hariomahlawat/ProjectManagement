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
            schedule.textContent = button.dataset.eventSchedule || "Original schedule";
            explanation.textContent = button.dataset.eventRecurring === "true"
                ? "The recurring series definition will be restored to the shared calendar. Future occurrences will again be generated from the retained recurrence rule."
                : "The event will return to the shared calendar with its original schedule and metadata.";
        });
    });
    form?.addEventListener("submit", () => {
        submit.disabled = true;
        submit.setAttribute("aria-busy", "true");
    });
})();
