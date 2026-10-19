"use strict";

// Declarative page behaviours that replace inline on* handlers, which the
// Content-Security-Policy (script-src 'self', no 'unsafe-inline') blocks.
//
//   data-confirm="Message"     on a <form>, a submit button or a link: ask before continuing
//   data-auto-submit[="input"] on a form control: submit its form on change (or the named event)
//   data-auto-navigate         on a <select> whose option values are same-origin URLs: go there on change
//   data-print                 on a button: open the browser print dialog
//   data-close-window          on a button: close the window
//
// An element whose page script implements its own behaviour for one of these
// attributes carries data-page-handled, and this module leaves it alone.
(() => {
    const isPageHandled = element => element.hasAttribute("data-page-handled");

    const confirmed = message => !message || window.confirm(message);

    const cancel = event => {
        event.preventDefault();
        event.stopImmediatePropagation();
    };

    // Capture phase, so a declined confirmation stops the submit before any
    // form-level listener (for example a disable-on-submit handler) runs.
    document.addEventListener("submit", event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || event.defaultPrevented) return;

        const submitter = event.submitter;
        const source = submitter?.hasAttribute("data-confirm") ? submitter : form;
        if (!source.hasAttribute("data-confirm") || isPageHandled(source)) return;

        if (!confirmed(source.getAttribute("data-confirm"))) cancel(event);
    }, true);

    document.addEventListener("click", event => {
        if (!(event.target instanceof Element)) return;

        const link = event.target.closest("a[data-confirm]");
        if (link && !isPageHandled(link)) {
            if (!confirmed(link.getAttribute("data-confirm"))) cancel(event);
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
