"use strict";

// =============================
// PRISM TOASTS
// window.PrismToast.show(options) shows the "done" message after an action, in the glass of the
// confirmation dialog (wwwroot/css/components/prism-toast.css). Server messages (TempData
// ToastSuccess / ToastError / ...) are rendered by Views/Shared/_Toasts.cshtml in the same markup
// and wired here on load. Wording and use: docs/ux/toasts.md.
//
// Options:
//   tone      "success" | "info" | "warning" | "danger" ("error" is read as "danger")
//   text      the sentence: "Remark saved."
//   subject   optional bold lead-in: "Bid Opening Minutes.pdf" + text "moved to Trash."
//   detail    optional smaller second line
//   action    optional { label, run } or { label, href }: one button (Undo, View, Retry)
//   life      milliseconds before it fades (default 4500, 7000 with an action); errors stay
//             until dismissed
//   sticky    true: no timer, it stays until dismissed or its action is used
//
// Returns { element, dismiss }.
// =============================
(() => {
    const MAX_VISIBLE = 3;
    const ICONS = {
        success: "bi-check-lg",
        info: "bi-info-lg",
        warning: "bi-exclamation-lg",
        danger: "bi-x-lg"
    };
    const reduceMotion = () => window.matchMedia?.("(prefers-reduced-motion: reduce)").matches === true;

    const normalizeTone = tone => {
        const value = String(tone || "").toLowerCase();
        if (value === "error") return "danger";
        return ICONS[value] ? value : "info";
    };

    let region = null;
    let paused = false;
    const timers = new Map(); // element -> { left, started, handle }

    // Timers run unless the stack is in use: the pointer is over it or heading for it (within
    // NEAR px), focus is inside it, or the page is not on screen (a background tab).
    const NEAR = 64;
    let pointerNear = false;
    const stackInUse = () => Boolean(region?.isConnected
        && (pointerNear
            || region.matches(":hover")
            || region.contains(document.activeElement)
            || document.visibilityState === "hidden"));

    const syncPaused = () => setPaused(stackInUse());

    // Reaching for Undo should not race the timer: near the stack counts as on it.
    let pointerFrame = 0;
    let lastPointer = null;
    const checkPointer = () => {
        pointerFrame = 0;
        const host = region?.isConnected ? region : null;
        let near = false;
        if (host && lastPointer && timers.size > 0) {
            const box = host.getBoundingClientRect();
            const { x, y } = lastPointer;
            near = box.width > 0
                && x >= box.left - NEAR && x <= box.right + NEAR
                && y >= box.top - NEAR && y <= box.bottom + NEAR;
        }
        if (near !== pointerNear) {
            pointerNear = near;
            syncPaused();
        }
    };
    document.addEventListener("pointermove", event => {
        if (event.pointerType && event.pointerType !== "mouse" && event.pointerType !== "pen") return;
        lastPointer = { x: event.clientX, y: event.clientY };
        if (!pointerFrame) {
            pointerFrame = 1;
            (window.requestAnimationFrame || (fn => window.setTimeout(fn, 16)))(checkPointer);
        }
    }, { passive: true });
    // The pointer left the window: it is no longer near the stack.
    document.documentElement.addEventListener("mouseleave", () => {
        lastPointer = null;
        if (pointerNear) {
            pointerNear = false;
            if (region?.isConnected) syncPaused();
        }
    });
    document.addEventListener("visibilitychange", () => {
        if (region?.isConnected) syncPaused();
    });

    const getRegion = () => {
        if (!region?.isConnected) region = document.querySelector("[data-prism-toasts]");
        if (!region) {
            region = document.createElement("div");
            region.className = "prism-toasts";
            region.setAttribute("data-prism-toasts", "");
            region.setAttribute("role", "region");
            region.setAttribute("aria-label", "Notifications");
            document.body.appendChild(region);
        }
        if (region.dataset.prismToastsBound !== "true") {
            region.dataset.prismToastsBound = "true";
            const pause = () => setPaused(true);
            const resume = () => {
                if (!stackInUse()) setPaused(false);
            };
            region.addEventListener("mouseenter", pause);
            region.addEventListener("mouseleave", resume);
            region.addEventListener("focusin", pause);
            region.addEventListener("focusout", () => setTimeout(resume, 0));
            region.addEventListener("keydown", event => {
                if (event.key !== "Escape") return;
                const toast = event.target.closest?.("[data-prism-toast]");
                if (toast) dismiss(toast);
            });
        }
        return region;
    };

    // Newest at the front, except that an error stays in front until it is dismissed.
    const restack = () => {
        const host = getRegion();
        const live = [...host.querySelectorAll("[data-prism-toast]:not(.is-leaving)")];
        const ordered = [
            ...live.filter(el => el.dataset.tone === "danger").reverse(),
            ...live.filter(el => el.dataset.tone !== "danger").reverse()
        ];
        let fan = 0;
        ordered.forEach((el, index) => {
            el.style.setProperty("--i", String(Math.min(index, MAX_VISIBLE - 1)));
            el.style.setProperty("--fan", `${fan}px`);
            el.style.visibility = index < MAX_VISIBLE ? "" : "hidden";
            fan += el.offsetHeight + 8;
        });
    };

    const clearTimer = el => {
        const timer = timers.get(el);
        if (timer) window.clearTimeout(timer.handle);
        timers.delete(el);
    };

    const startTimer = (el, life) => {
        const timer = { left: life, started: Date.now(), handle: 0 };
        timers.set(el, timer);
        // A toast shown in a background tab waits until the page is seen.
        if (document.visibilityState === "hidden") setPaused(true);
        if (!paused) timer.handle = window.setTimeout(() => dismiss(el), life);
    };

    function setPaused(value) {
        if (paused === value) return;
        paused = value;
        // The timer lines follow the same pause (see .prism-toasts.is-paused in prism-toast.css).
        region?.classList.toggle("is-paused", paused);
        timers.forEach((timer, el) => {
            if (paused) {
                window.clearTimeout(timer.handle);
                timer.left = Math.max(timer.left - (Date.now() - timer.started), 0);
            } else {
                timer.started = Date.now();
                timer.handle = window.setTimeout(() => dismiss(el), Math.max(timer.left, 1200));
            }
        });
    }

    function dismiss(el) {
        if (!el || el.classList.contains("is-leaving")) return;
        clearTimer(el);
        el.classList.add("is-leaving");
        const finish = () => {
            el.remove();
            restack();
            // Removing a focused toast moves focus without a focusout on the stack, so check
            // again: the others resume unless the stack is still hovered or focused.
            if (!stackInUse()) setPaused(false);
        };
        if (reduceMotion()) finish();
        else window.setTimeout(finish, 280);
        restack();
    }

    const wire = (el, action) => {
        if (el.dataset.prismToastWired === "true") return;
        el.dataset.prismToastWired = "true";
        el.querySelector("[data-prism-toast-close]")?.addEventListener("click", () => dismiss(el));
        const button = el.querySelector("[data-prism-toast-action]");
        if (button && action?.run) {
            button.addEventListener("click", () => {
                dismiss(el);
                action.run();
            });
        }
        if (el.dataset.tone !== "danger" && el.dataset.life !== "0") {
            const life = Number(el.dataset.life) || 4500;
            el.style.setProperty("--life", `${life}ms`);
            startTimer(el, life);
        }
    };

    const build = options => {
        const tone = normalizeTone(options.tone);
        const el = document.createElement("div");
        el.className = "prism-toast";
        el.dataset.prismToast = "";
        el.dataset.tone = tone;
        el.setAttribute("role", tone === "danger" ? "alert" : "status");
        const life = options.sticky ? 0 : Number(options.life) || (options.action ? 7000 : 4500);
        el.dataset.life = String(life);

        const icon = document.createElement("span");
        icon.className = "prism-toast__icon";
        icon.setAttribute("aria-hidden", "true");
        const glyph = document.createElement("i");
        glyph.className = `bi ${ICONS[tone]}`;
        icon.appendChild(glyph);

        const text = document.createElement("p");
        text.className = "prism-toast__text";
        if (options.subject) {
            const subject = document.createElement("b");
            subject.textContent = String(options.subject);
            text.append(subject, " ");
        }
        text.append(String(options.text || ""));
        if (options.detail) {
            const detail = document.createElement("span");
            detail.className = "prism-toast__detail";
            detail.textContent = String(options.detail);
            text.appendChild(detail);
        }
        el.append(icon, text);

        if (options.action?.label) {
            let button;
            if (options.action.href) {
                button = document.createElement("a");
                button.href = options.action.href;
            } else {
                button = document.createElement("button");
                button.type = "button";
            }
            button.className = "prism-toast__action";
            button.setAttribute("data-prism-toast-action", "");
            button.textContent = String(options.action.label);
            el.appendChild(button);
        }

        const close = document.createElement("button");
        close.type = "button";
        close.className = "prism-toast__close";
        close.setAttribute("data-prism-toast-close", "");
        close.setAttribute("aria-label", "Dismiss");
        close.innerHTML = '<i class="bi bi-x-lg" aria-hidden="true"></i>';
        el.appendChild(close);

        if (tone !== "danger" && life > 0) {
            const timer = document.createElement("span");
            timer.className = "prism-toast__timer";
            timer.setAttribute("aria-hidden", "true");
            el.appendChild(timer);
        }
        return el;
    };

    const show = (options = {}) => {
        const el = build(options);
        getRegion().appendChild(el);
        wire(el, options.action);
        restack();
        return { element: el, dismiss: () => dismiss(el) };
    };

    // Toasts the server rendered (Views/Shared/_Toasts.cshtml) get the same timers and stacking.
    const adoptServerToasts = () => {
        const host = document.querySelector("[data-prism-toasts]");
        if (!host) return;
        region = host;
        getRegion();
        host.querySelectorAll("[data-prism-toast]").forEach(el => wire(el));
        restack();
    };

    window.PrismToast = Object.freeze({ show, dismiss });

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", adoptServerToasts, { once: true });
    } else {
        adoptServerToasts();
    }
})();
