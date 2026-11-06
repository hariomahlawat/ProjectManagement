"use strict";

// Declarative page behaviours that replace inline on* handlers, which the
// Content-Security-Policy (script-src 'self', no 'unsafe-inline') blocks.
//
//   data-confirm="Message"     on a <form>, a submit button or a link: ask before continuing,
//                              in the shared PRISM dialog (window.PrismConfirm). "Question? Consequence."
//                              becomes the dialog's title and message. Optional, on the same element:
//                              data-confirm-title, data-confirm-detail, data-confirm-action (button
//                              label), data-confirm-tone ("primary" | "warning" | "danger").
//                              See docs/ux/confirm-dialogs.md.
//   data-auto-submit[="input"] on a form control: submit its form on change (or the named event)
//   data-auto-navigate         on a <select> whose option values are same-origin URLs: go there on change
//   data-print                 on a button: open the browser print dialog
//   data-close-window          on a button: close the window
//
// An element whose page script implements its own behaviour for one of these
// attributes carries data-page-handled, and this module leaves it alone.
(() => {
    const isPageHandled = element => element.hasAttribute("data-page-handled");

    const cancel = event => {
        event.preventDefault();
        event.stopImmediatePropagation();
    };

    // ---------- data-confirm ----------

    const dangerWords = /\b(delete|remove|purge|erase|clear|discard|for good|cannot be undone|not a (valid )?face)\b/i;
    const warningWords = /\b(withdraw|disconnect|archive|deactivate|unlink|merge|retire|retry|rebuild|reset)\b/i;

    // "Delete this visit? The record and its photos are removed." -> title and message.
    const splitMessage = text => {
        const match = /^([^?]{1,160}\?)\s+([\s\S]+)$/.exec(text.trim());
        return match ? { title: match[1], message: match[2] } : { title: text.trim(), message: "" };
    };

    // The button names the action: "Delete this task?" -> "Delete task"; long titles keep the verb.
    const actionLabel = title => {
        const plain = title.replace(/\?$/, "").replace(/\b(this|the|your)\s+/gi, "").replace(/[“”"]/g, "").trim();
        if (plain && plain.length <= 32) return plain;
        const verb = plain.split(/\s+/)[0] || "Continue";
        return verb.charAt(0).toUpperCase() + verb.slice(1);
    };

    const confirmOptions = source => {
        const text = source.getAttribute("data-confirm") || "";
        const split = splitMessage(text);
        const title = source.getAttribute("data-confirm-title") || split.title;
        const toneAttr = source.getAttribute("data-confirm-tone");
        const tone = ["primary", "warning", "danger"].includes(toneAttr)
            ? toneAttr
            : dangerWords.test(text) ? "danger" : warningWords.test(text) ? "warning" : "primary";
        return {
            title,
            message: split.title === title ? split.message : text,
            detail: source.getAttribute("data-confirm-detail") || "",
            confirmText: source.getAttribute("data-confirm-action") || actionLabel(title),
            cancelText: "Cancel",
            tone
        };
    };

    // Resolves true or false. Without the shared dialog (it failed to load) the browser's box
    // still protects the action.
    const ask = source => {
        const text = source.getAttribute("data-confirm");
        if (!text) return Promise.resolve(true);
        if (window.PrismConfirm?.show) {
            return window.PrismConfirm.show(confirmOptions(source)).then(Boolean, () => false);
        }
        return Promise.resolve(window.confirm(text));
    };

    // A confirmed form or link is replayed once; the replay passes straight through.
    const approved = new WeakSet();

    const replaySubmit = (form, submitter) => {
        approved.add(form);
        if (typeof form.requestSubmit === "function") {
            form.requestSubmit(submitter && submitter.form === form ? submitter : undefined);
            return;
        }
        if (submitter?.name) {
            const carrier = document.createElement("input");
            carrier.type = "hidden";
            carrier.name = submitter.name;
            carrier.value = submitter.value;
            form.appendChild(carrier);
        }
        form.submit();
    };

    // Capture phase, so the submit is held before any form-level listener (for example a
    // disable-on-submit handler) runs; those run on the confirmed replay.
    document.addEventListener("submit", event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || event.defaultPrevented) return;

        if (approved.has(form)) {
            approved.delete(form);
            return;
        }

        const submitter = event.submitter;
        const source = submitter?.hasAttribute("data-confirm") ? submitter : form;
        if (!source.hasAttribute("data-confirm") || isPageHandled(source)) return;

        cancel(event);
        ask(source).then(accepted => {
            if (accepted) replaySubmit(form, submitter);
        });
    }, true);

    document.addEventListener("click", event => {
        if (!(event.target instanceof Element)) return;

        const link = event.target.closest("a[data-confirm]");
        if (link && !isPageHandled(link)) {
            if (approved.has(link)) {
                approved.delete(link);
                return;
            }
            cancel(event);
            ask(link).then(accepted => {
                if (!accepted) return;
                approved.add(link);
                link.click();
            });
            return;
        }

        if (event.target.closest("[data-print]")) {
            event.preventDefault();
            window.print();
            return;
        }

        if (event.target.closest("[data-close-window]")) {
            event.preventDefault();
            window.close();
        }
    }, true);

    const autoSubmitEvent = element => {
        const configured = element.getAttribute("data-auto-submit");
        return configured && configured !== "true" ? configured : "change";
    };

    const submitForm = form => {
        if (typeof form.requestSubmit === "function") form.requestSubmit();
        else form.submit();
    };

    const navigateTo = value => {
        if (!value) return;
        const target = new URL(value, window.location.href);
        if (target.origin !== window.location.origin) return;
        window.location.assign(target.href);
    };

    const commit = element => {
        if (element.hasAttribute("data-auto-submit")) {
            if (element.form) submitForm(element.form);
        } else if (element.hasAttribute("data-auto-navigate")) {
            navigateTo(element.value);
        }
    };

    // Keyboard users browse a <select> with arrow keys, and some browsers fire "change" on
    // every step. Hold those changes and apply them on Enter or when focus leaves, so the page
    // does not reload or navigate while someone is still choosing (WCAG 3.2.2).
    const browseKeys = new Set(["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End", "PageUp", "PageDown"]);
    const browsing = new WeakSet();
    const pending = new WeakSet();

    const isKeyboardSafeSelect = element => element instanceof HTMLSelectElement
        && (element.hasAttribute("data-auto-submit") || element.hasAttribute("data-auto-navigate"))
        && !isPageHandled(element);

    document.addEventListener("keydown", event => {
        const element = event.target;
        if (!isKeyboardSafeSelect(element)) return;

        if (browseKeys.has(event.key)) {
            browsing.add(element);
        } else if (event.key === "Enter") {
            browsing.delete(element);
            if (pending.has(element)) {
                pending.delete(element);
                event.preventDefault();
                commit(element);
            }
        }
    });

    // A pointer pick is a deliberate choice, even after some arrow-key browsing.
    document.addEventListener("pointerdown", event => {
        if (isKeyboardSafeSelect(event.target)) browsing.delete(event.target);
    }, true);

    document.addEventListener("focusout", event => {
        const element = event.target;
        if (!isKeyboardSafeSelect(element)) return;

        browsing.delete(element);
        if (pending.has(element)) {
            pending.delete(element);
            commit(element);
        }
    });

    const onValueEvent = event => {
        const element = event.target;
        if (!(element instanceof Element) || isPageHandled(element)) return;

        const autoSubmit = element.hasAttribute("data-auto-submit") && autoSubmitEvent(element) === event.type;
        const autoNavigate = event.type === "change" && element.hasAttribute("data-auto-navigate");
        if (!autoSubmit && !autoNavigate) return;

        if (browsing.has(element)) {
            pending.add(element);
            return;
        }

        commit(element);
    };

    document.addEventListener("change", onValueEvent);
    document.addEventListener("input", onValueEvent);
})();
