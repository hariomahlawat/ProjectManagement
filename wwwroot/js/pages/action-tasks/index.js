// SECTION: Action Tracker page interactions.
(function () {
    "use strict";

    // SECTION: Re-open create-task modal after server-side validation errors.
    function openCreateTaskModalOnLoad() {
        const modalElement = document.getElementById("createTaskModal");
        if (!modalElement || typeof window.bootstrap === "undefined" || !window.bootstrap.Modal) {
            return;
        }

        const shouldOpen = modalElement.dataset.atOpenOnLoad === "true";
        if (!shouldOpen) {
            return;
        }

        const modal = window.bootstrap.Modal.getOrCreateInstance(modalElement);
        modal.show();
    }

    document.addEventListener("DOMContentLoaded", openCreateTaskModalOnLoad);
})();
