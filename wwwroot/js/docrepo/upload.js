// Document Repository upload page: drag-and-drop or choose a PDF, preview it, suggest a title from
// the file name, and warn about a duplicate before the file is sent. The form posts normally; the
// server repeats every check.
(() => {
    "use strict";

    const form = document.querySelector("[data-upload-form]");
    const input = form?.querySelector("[data-upload-input]");
    if (!form || !input) return;

    const drop = form.querySelector("[data-upload-drop]");
    const chosen = form.querySelector("[data-upload-chosen]");
    const nameText = form.querySelector("[data-upload-name]");
    const sizeText = form.querySelector("[data-upload-size]");
    const notice = form.querySelector("[data-upload-notice]");
    const preview = form.querySelector("[data-upload-preview]");
    const frame = form.querySelector("[data-upload-frame]");
    const subject = form.querySelector("[data-upload-subject]");
    const suggestion = form.querySelector("[data-upload-suggestion]");
    const submit = form.querySelector("[data-upload-submit]");
    const maxSize = parseInt(input.dataset.maxSize || "0", 10) || Number.POSITIVE_INFINITY;
    let previewUrl = null;
    let check = 0;

    // SECTION: Drag and drop onto the drop zone
    ["dragenter", "dragover"].forEach(type => drop.addEventListener(type, event => {
        event.preventDefault();
        drop.classList.add("is-over");
    }));
    ["dragleave", "dragend", "drop"].forEach(type => drop.addEventListener(type, () => drop.classList.remove("is-over")));
    drop.addEventListener("drop", event => {
        event.preventDefault();
        const file = event.dataTransfer?.files?.[0];
        if (!file) return;
        const transfer = new DataTransfer();
        transfer.items.add(file);
        input.files = transfer.files;
        input.dispatchEvent(new Event("change", { bubbles: true }));
    });

    form.querySelector("[data-upload-change]")?.addEventListener("click", () => input.click());

    // SECTION: A file was chosen
    input.addEventListener("change", () => {
        const file = input.files?.[0];
        reset();
        if (!file) return;

        const isPdf = file.type === "application/pdf" || /\.pdf$/i.test(file.name);
        if (!isPdf) return reject("Choose a PDF. Other file types cannot be added to the library.");
        if (file.size > maxSize) return reject(`This PDF is ${formatSize(file.size)}; the limit is ${formatSize(maxSize)}.`);

        drop.hidden = true;
        chosen.hidden = false;
        nameText.textContent = file.name;
        sizeText.textContent = formatSize(file.size);

        previewUrl = URL.createObjectURL(file);
        frame.src = previewUrl;
        preview.hidden = false;

        suggestTitle(file.name);
        checkDuplicate(file);
    });

    function reset() {
        input.setCustomValidity("");
        if (previewUrl) URL.revokeObjectURL(previewUrl);
        previewUrl = null;
        frame.removeAttribute("src");
        preview.hidden = true;
        chosen.hidden = true;
        drop.hidden = false;
        notice.hidden = true;
        notice.className = "library-notice";
        submit.disabled = false;
        check += 1;
    }

    function reject(message) {
        input.value = "";
        showNotice(message, "danger");
    }

    function showNotice(content, tone) {
        const icon = document.createElement("i");
        icon.className = `bi ${tone === "danger" ? "bi-exclamation-circle" : "bi-info-circle"}`;
        icon.setAttribute("aria-hidden", "true");
        const text = document.createElement("span");
        text.append(...(Array.isArray(content) ? content : [document.createTextNode(content)]));
        notice.replaceChildren(icon, text);
        notice.className = `library-notice is-${tone}`;
        notice.hidden = false;
    }

    // SECTION: Title from the file name ("TEC_minutes-radar_v2.pdf" → "TEC minutes radar v2")
    function suggestTitle(fileName) {
        const title = fileName
            .replace(/\.pdf$/i, "")
            .replace(/[_]+/g, " ")
            .replace(/\s*-\s*/g, " - ")
            .replace(/\s+/g, " ")
            .trim();
        if (!title) return;
        if (!subject.value.trim()) {
            subject.value = title.charAt(0).toUpperCase() + title.slice(1);
            suggestion.textContent = "Taken from the file name. Change it to the title on the paper if it differs.";
            suggestion.hidden = false;
        }
    }

    subject.addEventListener("input", () => { suggestion.hidden = true; });

    // SECTION: Duplicate pre-check (needs a secure context for crypto.subtle; skipped otherwise)
    async function checkDuplicate(file) {
        if (!window.crypto?.subtle || !form.dataset.checkUrl) return;
        const run = check;
        try {
            const digest = await window.crypto.subtle.digest("SHA-256", await file.arrayBuffer());
            const hex = [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, "0")).join("");
            const url = new URL(form.dataset.checkUrl, window.location.origin);
            url.searchParams.set("sha256", hex);
            const response = await fetch(url, { credentials: "same-origin" });
            if (!response.ok || run !== check) return;
            const result = await response.json();
            if (run !== check) return;
            if (result.duplicate) {
                submit.disabled = true;
                const parts = [document.createTextNode(result.external
                    ? "This PDF is already in the library: it was added from another module. "
                    : "This PDF is already in the library")];
                if (result.subject) {
                    const title = document.createElement("strong");
                    title.textContent = result.subject;
                    parts.push(document.createTextNode(" as "), title, document.createTextNode(". "));
                } else if (!result.external) {
                    parts.push(document.createTextNode(". "));
                }
                parts.push(document.createTextNode("Uploading it again would create a duplicate, so Upload is turned off for this file."));
                if (result.url) {
                    const open = document.createElement("a");
                    open.href = result.url;
                    open.className = "btn btn-sm btn-outline-secondary";
                    open.textContent = "Open the existing document";
                    const actions = document.createElement("span");
                    actions.className = "library-notice__actions";
                    actions.append(open);
                    parts.push(actions);
                }
                showNotice(parts, "danger");
            } else if (result.restoresTrashed) {
                showNotice("This PDF was deleted from the library before. Uploading it restores that document with the details below.", "info");
            }
        } catch {
            // The server checks again on upload.
        }
    }

    function formatSize(bytes) {
        if (!Number.isFinite(bytes)) return "";
        if (bytes < 1024) return `${bytes} bytes`;
        if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
        const mb = bytes / (1024 * 1024);
        return `${mb < 10 ? mb.toFixed(1) : Math.round(mb)} MB`;
    }
})();
