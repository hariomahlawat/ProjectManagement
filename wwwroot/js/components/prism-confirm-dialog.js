"use strict";

(() => {
    const dialog = document.querySelector("[data-prism-confirm-dialog]");
    if (!dialog || typeof dialog.showModal !== "function") return;

    const title = dialog.querySelector("[data-prism-confirm-title]");
    const message = dialog.querySelector("[data-prism-confirm-message]");
    const detail = dialog.querySelector("[data-prism-confirm-detail]");
    const accept = dialog.querySelector("[data-prism-confirm-accept]");
    const cancelButtons = [...dialog.querySelectorAll("[data-prism-confirm-cancel]")];
    const icon = dialog.querySelector("[data-prism-confirm-icon] .bi");

    let activeResolve = null;
    let previouslyFocused = null;
    const homeParent = dialog.parentNode;
    const homeNextSibling = dialog.nextSibling;

    const defaults = {
        title: "Confirm action",
        message: "Continue with this action?",
        detail: "",
        confirmText: "Confirm",
        cancelText: "Cancel",
        tone: "primary"
    };

    const iconClassForTone = tone => tone === "danger"
        ? "bi-trash3"
        : tone === "warning"
            ? "bi-exclamation-triangle"
            : "bi-question-circle";

    const restoreDialogHome = () => {
        if (!homeParent || dialog.parentNode === homeParent) return;
        if (homeNextSibling?.parentNode === homeParent) {
            homeParent.insertBefore(dialog, homeNextSibling);
        } else {
            homeParent.appendChild(dialog);
        }
    };

    const settle = value => {
        if (!activeResolve) return;
        const resolve = activeResolve;
        const focusTarget = previouslyFocused;
        activeResolve = null;
        if (dialog.open) dialog.close();
        restoreDialogHome();
        resolve(value);
        queueMicrotask(() => focusTarget?.focus?.({ preventScroll: true }));
    };

    cancelButtons.forEach(button => button.addEventListener("click", () => settle(false)));
    accept?.addEventListener("click", () => settle(true));

    dialog.addEventListener("cancel", event => {
        event.preventDefault();
        settle(false);
    });

    dialog.addEventListener("close", () => {
        if (activeResolve) settle(false);
        else restoreDialogHome();
    });

    window.PrismConfirm = Object.freeze({
        show(options = {}) {
            if (activeResolve) settle(false);

            const settings = { ...defaults, ...options };
            const tone = ["primary", "warning", "danger"].includes(settings.tone)
                ? settings.tone
                : "primary";

            previouslyFocused = document.activeElement instanceof HTMLElement
                ? document.activeElement
                : null;

            dialog.dataset.tone = tone;
            title.textContent = settings.title;
            message.textContent = settings.message;
            detail.textContent = settings.detail || "";
            detail.hidden = !settings.detail;
            accept.textContent = settings.confirmText;
            cancelButtons.forEach(button => {
                if (!button.classList.contains("prism-confirm-dialog__close")) {
                    button.textContent = settings.cancelText;
                }
            });
            icon.className = `bi ${iconClassForTone(tone)}`;

            return new Promise(resolve => {
                activeResolve = resolve;
                const activeOverlay = document.querySelector(".offcanvas.show, .modal.show");
                if (activeOverlay && !activeOverlay.contains(dialog)) {
                    activeOverlay.appendChild(dialog);
                }
                dialog.showModal();
                queueMicrotask(() => accept.focus({ preventScroll: true }));
            });
        }
    });
})();
