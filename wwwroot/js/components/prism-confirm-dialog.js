"use strict";

// =============================
// PRISM CONFIRM
// window.PrismConfirm.show(options) opens the shared confirmation dialog
// (Pages/Shared/_PrismConfirmDialog.cshtml, rendered once by _Layout) and resolves:
//   true            the user confirmed
//   false           the user cancelled (button, close, Esc, or another dialog took over)
//   "<reason text>" the user confirmed and options.reason was set (the trimmed reason)
//
// Options (all optional):
//   title, message, detail     the question, its consequence, and a smaller extra line
//   confirmText, cancelText    button labels: verbs, e.g. "Delete document" / "Keep document"
//   tone                       "primary" | "warning" | "danger"
//   icon                       a Bootstrap Icons name ("bi-trash3") to override the tone's icon
//   subject                    { name, meta, icon }: the record the action applies to
//   note                       a short line under the subject (e.g. how to undo)
//   irreversible               true: show "This cannot be undone." as the note, in the tone colour
//   reason                     { label, placeholder, hint, minLength }: require a written reason
//   confirmPhrase              text the user must type exactly before confirming
//   focus                      "cancel" to focus Cancel whatever the tone (Enter never confirms)
//
// Focus: the field when there is one; otherwise Cancel for "danger" (Enter never deletes by
// accident) and the action for everything else. Wording rules: docs/ux/confirm-dialogs.md.
// =============================
(() => {
    const dialog = document.querySelector("[data-prism-confirm-dialog]");
    if (!dialog || typeof dialog.showModal !== "function") return;

    const q = selector => dialog.querySelector(selector);
    const title = q("[data-prism-confirm-title]");
    const message = q("[data-prism-confirm-message]");
    const detail = q("[data-prism-confirm-detail]");
    const accept = q("[data-prism-confirm-accept]");
    const cancelButtons = [...dialog.querySelectorAll("[data-prism-confirm-cancel]")];
    const cancelLabel = cancelButtons.find(button => !button.classList.contains("prism-confirm-dialog__close")) ?? null;
    const icon = q("[data-prism-confirm-icon] .bi");
    const subject = q("[data-prism-confirm-subject]");
    const subjectIcon = q("[data-prism-confirm-subject-icon]");
    const subjectName = q("[data-prism-confirm-subject-name]");
    const subjectMeta = q("[data-prism-confirm-subject-meta]");
    const field = q("[data-prism-confirm-field]");
    const fieldLabel = q("[data-prism-confirm-field-label]");
    const reasonInput = q("[data-prism-confirm-reason]");
    const phraseInput = q("[data-prism-confirm-phrase]");
    const note = q("[data-prism-confirm-note]");
    const noteIcon = q("[data-prism-confirm-note-icon]");
    const noteText = q("[data-prism-confirm-note-text]");

    let activeResolve = null;
    let activeSettings = null;
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

    const safeIcon = (name, fallback) => /^bi-[a-z0-9-]+$/.test(name ?? "") ? name : fallback;

    const restoreDialogHome = () => {
        if (!homeParent || dialog.parentNode === homeParent) return;
        if (homeNextSibling?.parentNode === homeParent) {
            homeParent.insertBefore(dialog, homeNextSibling);
        } else {
            homeParent.appendChild(dialog);
        }
    };

    const reasonMinLength = () => Math.max(1, Number(activeSettings?.reason?.minLength) || 10);

    // The action stays disabled until a required reason or phrase is in place.
    const refreshAcceptState = () => {
        if (!activeSettings) return;
        let ready = true;
        if (activeSettings.reason) {
            ready = reasonInput.value.trim().length >= reasonMinLength();
        } else if (activeSettings.confirmPhrase) {
            const matches = phraseInput.value.trim() === String(activeSettings.confirmPhrase);
            phraseInput.classList.toggle("is-match", matches);
            ready = matches;
        }
        accept.disabled = !ready;
    };

    const settle = value => {
        if (!activeResolve) return;
        const resolve = activeResolve;
        const focusTarget = previouslyFocused;
        activeResolve = null;
        activeSettings = null;
        if (dialog.open) dialog.close();
        restoreDialogHome();
        resolve(value);
        queueMicrotask(() => focusTarget?.focus?.({ preventScroll: true }));
    };

    const confirmNow = () => {
        if (!activeSettings || accept.disabled) return;
        settle(activeSettings.reason ? reasonInput.value.trim() : true);
    };

    cancelButtons.forEach(button => button.addEventListener("click", () => settle(false)));
    accept?.addEventListener("click", confirmNow);
    reasonInput?.addEventListener("input", refreshAcceptState);
    phraseInput?.addEventListener("input", refreshAcceptState);

    // Enter in the phrase box, or Ctrl/Cmd+Enter in the reason box, confirms once it is valid.
    phraseInput?.addEventListener("keydown", event => {
        if (event.key === "Enter") {
            event.preventDefault();
            confirmNow();
        }
    });
    reasonInput?.addEventListener("keydown", event => {
        if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) {
            event.preventDefault();
            confirmNow();
        }
    });

    // A click on the dimmed page outside the sheet cancels, like Esc.
    dialog.addEventListener("click", event => {
        if (event.target === dialog) settle(false);
    });

    dialog.addEventListener("cancel", event => {
        event.preventDefault();
        settle(false);
    });

    // Browsers fire "close" in a later task. When a second confirmation replaced the first,
    // the dialog is already open again by then; that late event must not cancel it.
    dialog.addEventListener("close", () => {
        if (dialog.open) return;
        if (activeResolve) settle(false);
        else restoreDialogHome();
    });

    // A menu item that asks (⋯ › Delete) should not leave its menu open behind the dialog.
    // Focus moves to the menu's button, so it is where focus returns when the dialog closes.
    // A menu may have been moved to <body> while open (Tasks does this to escape clipping), so
    // its button is found through Bootstrap's own record of the menu, else beside the menu.
    const toggleForMenu = menu => {
        const api = window.bootstrap?.Dropdown;
        const toggles = [...document.querySelectorAll('[data-bs-toggle="dropdown"]')];
        const owner = toggles.find(toggle => api?.getInstance?.(toggle)?._menu === menu);
        if (owner) return owner;
        const parent = menu.parentElement;
        return parent && parent !== document.body
            ? parent.querySelector(':scope > [data-bs-toggle="dropdown"]')
            : null;
    };

    const closeOpenMenus = () => {
        document.querySelectorAll(".dropdown-menu.show").forEach(menu => {
            const toggle = toggleForMenu(menu);
            const hadFocus = menu.contains(document.activeElement);
            const api = window.bootstrap?.Dropdown;
            if (toggle && api?.getOrCreateInstance) api.getOrCreateInstance(toggle).hide();
            if (menu.classList.contains("show")) menu.classList.remove("show");
            if (hadFocus && toggle instanceof HTMLElement) toggle.focus({ preventScroll: true });
        });
    };

    const setText = (element, text) => {
        if (!element) return;
        element.textContent = text || "";
        element.hidden = !text;
    };

    window.PrismConfirm = Object.freeze({
        show(options = {}) {
            if (activeResolve) settle(false);

            const settings = { ...defaults, ...options };
            const tone = ["primary", "warning", "danger"].includes(settings.tone)
                ? settings.tone
                : "primary";
            settings.tone = tone;

            closeOpenMenus();
            previouslyFocused = document.activeElement instanceof HTMLElement
                ? document.activeElement
                : null;

            dialog.dataset.tone = tone;
            title.textContent = settings.title;
            setText(message, settings.message);
            setText(detail, settings.detail);
            // Only a question: it sits level with the icon instead of above an empty space.
            dialog.toggleAttribute("data-title-only", !settings.message && !settings.detail);
            accept.textContent = settings.confirmText;
            if (cancelLabel) cancelLabel.textContent = settings.cancelText;
            if (icon) icon.className = `bi ${safeIcon(settings.icon, iconClassForTone(tone))}`;

            const subjectInfo = settings.subject && settings.subject.name ? settings.subject : null;
            if (subject) {
                subject.hidden = !subjectInfo;
                if (subjectInfo) {
                    subjectName.textContent = subjectInfo.name;
                    setText(subjectMeta, subjectInfo.meta);
                    subjectIcon.className = `bi ${safeIcon(subjectInfo.icon, "bi-file-earmark")}`;
                }
            }

            const noteMessage = settings.irreversible
                ? (settings.note || "This cannot be undone.")
                : settings.note;
            if (note) {
                note.hidden = !noteMessage;
                note.classList.toggle("is-final", Boolean(settings.irreversible));
                noteText.textContent = noteMessage || "";
                noteIcon.className = `bi ${settings.irreversible ? "bi-exclamation-octagon" : "bi-info-circle"}`;
            }

            const reason = settings.reason ? { label: "Reason", ...settings.reason } : null;
            const phrase = !reason && settings.confirmPhrase ? String(settings.confirmPhrase) : "";
            if (field) {
                field.hidden = !reason && !phrase;
                reasonInput.hidden = !reason;
                phraseInput.hidden = !phrase;
                reasonInput.value = "";
                phraseInput.value = "";
                phraseInput.classList.remove("is-match");
                fieldLabel.textContent = "";
                if (reason) {
                    fieldLabel.htmlFor = reasonInput.id;
                    fieldLabel.append(reason.label);
                    if (reason.hint) {
                        const hint = document.createElement("span");
                        hint.textContent = reason.hint;
                        fieldLabel.append(hint);
                    }
                    reasonInput.placeholder = reason.placeholder || "";
                } else if (phrase) {
                    fieldLabel.htmlFor = phraseInput.id;
                    fieldLabel.append("Type ");
                    const code = document.createElement("code");
                    code.textContent = phrase;
                    fieldLabel.append(code, " to confirm");
                }
            }
            settings.reason = reason;
            settings.confirmPhrase = phrase;
            activeSettings = settings;
            accept.disabled = false;
            refreshAcceptState();

            return new Promise(resolve => {
                activeResolve = resolve;
                const activeOverlay = document.querySelector(".offcanvas.show, .modal.show");
                if (activeOverlay && !activeOverlay.contains(dialog)) {
                    activeOverlay.appendChild(dialog);
                }
                dialog.showModal();
                const focusTarget = reason
                    ? reasonInput
                    : phrase
                        ? phraseInput
                        : (tone === "danger" || settings.focus === "cancel") && cancelLabel ? cancelLabel : accept;
                queueMicrotask(() => focusTarget?.focus({ preventScroll: true }));
            });
        }
    });
})();
