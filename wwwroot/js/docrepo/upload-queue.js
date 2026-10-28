// Document Repository: add several PDFs at once. Each chosen PDF gets a card with its first page,
// a title and date read from the PDF (for the person to check) and the shared details; then each
// is sent on its own to Upload?handler=Json, which applies every rule of the ordinary upload. The
// single-file form (upload.js) stays for browsers without script and for posts that came back
// with errors.
(() => {
    "use strict";

    const app = document.querySelector("[data-upload-app]");
    const form = document.querySelector("[data-upload-form]");
    if (!app || !window.fetch || !window.FormData || !("content" in document.createElement("template"))) return;
    if (app.dataset.keepForm === "true") return;

    app.hidden = false;
    if (form) form.hidden = true;

    const input = app.querySelector("[data-queue-input]");
    const drop = app.querySelector("[data-queue-drop]");
    const workspace = app.querySelector("[data-queue-workspace]");
    const list = app.querySelector("[data-queue-list]");
    const total = app.querySelector("[data-queue-total]");
    const uploadButton = app.querySelector("[data-queue-upload]");
    const addButton = app.querySelector("[data-queue-add]");
    const doneLink = app.querySelector("[data-queue-done]");
    const template = document.getElementById("queueItemTemplate");
    const token = app.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
    const maxSize = parseInt(app.dataset.maxSize || "0", 10) || Number.POSITIVE_INFINITY;
    const maxFiles = parseInt(app.dataset.maxFiles || "20", 10);
    const items = [];
    let uploading = false;
    let viewerModule = null;

    // SECTION: Choosing files (drop zone, file picker, "Add more")
    ["dragenter", "dragover"].forEach(type => drop.addEventListener(type, event => {
        event.preventDefault();
        drop.classList.add("is-over");
    }));
    ["dragleave", "dragend", "drop"].forEach(type => drop.addEventListener(type, () => drop.classList.remove("is-over")));
    drop.addEventListener("drop", event => {
        event.preventDefault();
        addFiles([...(event.dataTransfer?.files || [])]);
    });
    input.addEventListener("change", () => {
        addFiles([...(input.files || [])]);
        input.value = "";
    });
    addButton.addEventListener("click", () => input.click());

    // Dropping anywhere on the page while the queue is open adds the files too.
    document.addEventListener("dragover", event => { if (!workspace.hidden) event.preventDefault(); });
    document.addEventListener("drop", event => {
        if (workspace.hidden || drop.contains(event.target)) return;
        event.preventDefault();
        addFiles([...(event.dataTransfer?.files || [])]);
    });

    function addFiles(files) {
        if (uploading) return;
        const room = maxFiles - items.filter(item => item.state !== "removed").length;
        const accepted = files.slice(0, Math.max(0, room));
        if (files.length > accepted.length) {
            notify(`Up to ${maxFiles} PDFs at a time. Add the rest once these are in the library.`);
        }
        accepted.forEach(file => {
            if (items.some(item => item.state !== "removed" && item.file.name === file.name && item.file.size === file.size)) return;
            createItem(file);
        });
        workspace.hidden = items.every(item => item.state === "removed");
        drop.classList.toggle("is-compact", !workspace.hidden);
        updateTotal();
    }

    // SECTION: One card per PDF
    function createItem(file) {
        const element = template.content.firstElementChild.cloneNode(true);
        const item = { file, element, state: "checking", touched: new Set(), id: `queue${items.length}` };
        items.push(item);

        element.querySelector("[data-item-name]").textContent = file.name;
        element.querySelector("[data-item-size]").textContent = formatSize(file.size);
        element.querySelectorAll("[data-field]").forEach(field => {
            const name = field.dataset.field;
            field.id = `${item.id}${name}`;
            element.querySelector(`[data-for="${name}"]`)?.setAttribute("for", field.id);
            field.addEventListener("input", () => {
                item.touched.add(name);
                element.querySelector(`[data-suggest="${name}"]`)?.setAttribute("hidden", "");
                setError(item, name, "");
                updateTotal();
            });
            field.addEventListener("change", () => { item.touched.add(name); updateTotal(); });
        });
        element.querySelector("[data-item-remove]").addEventListener("click", () => removeItem(item));
        applyShared(item);
        list.append(element);

        const isPdf = file.type === "application/pdf" || /\.pdf$/i.test(file.name);
        if (!isPdf) return fail(item, "This is not a PDF, so it cannot be added.");
        if (file.size > maxSize) return fail(item, `This PDF is ${formatSize(file.size)}; the limit is ${formatSize(maxSize)}.`);

        setField(item, "Subject", titleFromFileName(file.name), false);
        item.state = "ready";
        updateTotal();
        readFacts(item);
        checkDuplicate(item);
    }

    function removeItem(item) {
        if (item.state === "uploading") return;
        item.state = "removed";
        item.element.remove();
        workspace.hidden = items.every(candidate => candidate.state === "removed");
        drop.classList.toggle("is-compact", !workspace.hidden);
        updateTotal();
    }

    function field(item, name) {
        return item.element.querySelector(`[data-field="${name}"]`);
    }

    function setField(item, name, value, suggested) {
        const element = field(item, name);
        if (!element || item.touched.has(name) || !value) return;
        if (element.type === "checkbox") element.checked = Boolean(value);
        else element.value = value;
        item.element.querySelector(`[data-suggest="${name}"]`)?.toggleAttribute("hidden", !suggested);
    }

    // SECTION: Details shared by every PDF
    app.querySelectorAll("[data-shared]").forEach(shared => {
        shared.addEventListener(shared.type === "checkbox" ? "change" : "input", () => {
            items.filter(item => item.state === "ready" || item.state === "checking" || item.state === "error").forEach(applyShared);
            updateTotal();
        });
    });

    function applyShared(item) {
        app.querySelectorAll("[data-shared]").forEach(shared => {
            const name = shared.dataset.shared;
            const target = field(item, name);
            if (!target || item.touched.has(name)) return;
            if (target.type === "checkbox") target.checked = shared.checked;
            else target.value = shared.value;
        });
    }

    // SECTION: Cover, title and date read from the PDF in the browser
    async function readFacts(item) {
        try {
            viewerModule ??= await import(app.dataset.viewerModule);
            const facts = await viewerModule.readPdfFacts(app.dataset.pdfjsBase, new Uint8Array(await item.file.arrayBuffer()), { coverWidth: 96 });
            if (item.state === "removed") return;
            const cover = item.element.querySelector("[data-item-cover]");
            cover.replaceChildren(facts.cover);
            item.element.querySelector("[data-item-size]").textContent =
                `${formatSize(item.file.size)} · ${facts.pages} ${facts.pages === 1 ? "page" : "pages"}`;
            if (facts.title && facts.title.length >= 6) setField(item, "Subject", tidyTitle(facts.title), true);
            if (facts.date) setField(item, "DocumentDate", facts.date, true);
        } catch (error) {
            if (error?.name === "PasswordException") status(item, "This PDF is protected by a password. It can be added, but its text cannot be read or searched.", "info");
            // Otherwise the file name stays as the title; the server decides whether the file is a valid PDF.
        }
    }

    // SECTION: Duplicate check before sending (the server checks again)
    async function checkDuplicate(item) {
        if (!window.crypto?.subtle || !app.dataset.checkUrl) return;
        try {
            const digest = await window.crypto.subtle.digest("SHA-256", await item.file.arrayBuffer());
            const hex = [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, "0")).join("");
            if (item.state === "removed") return;
            const duplicateInQueue = items.find(other => other !== item && other.state !== "removed" && other.hash === hex);
            item.hash = hex;
            if (duplicateInQueue) return fail(item, `This is the same PDF as “${duplicateInQueue.file.name}” above.`);
            const url = new URL(app.dataset.checkUrl, window.location.origin);
            url.searchParams.set("sha256", hex);
            const response = await fetch(url, { credentials: "same-origin" });
            if (!response.ok) return;
            const result = await response.json();
            if (item.state === "removed") return;
            if (result.duplicate) {
                const parts = [document.createTextNode(result.external
                    ? "Already in the library: it was added from another module. "
                    : "Already in the library")];
                if (result.subject) {
                    const title = document.createElement("strong");
                    title.textContent = result.subject;
                    parts.push(document.createTextNode(" as "), title, document.createTextNode(". "));
                } else if (!result.external) {
                    parts.push(document.createTextNode(". "));
                }
                if (result.url) {
                    const open = document.createElement("a");
                    open.href = result.url;
                    open.textContent = "Open it";
                    parts.push(open);
                }
                fail(item, parts);
            } else if (result.restoresTrashed) {
                status(item, "This PDF was deleted from the library before; adding it restores that document with the details here.", "info");
            }
        } catch {
            // The server checks again when the PDF is sent.
        }
    }

    // SECTION: Sending, one PDF after another
    uploadButton.addEventListener("click", async () => {
        if (uploading) return;
        const pending = items.filter(item => item.state === "ready" || item.state === "error");
        if (!pending.length) return;
        if (!validate(pending)) return;
        uploading = true;
        uploadButton.disabled = true;
        addButton.disabled = true;
        app.querySelectorAll("[data-shared]").forEach(shared => { shared.disabled = true; });
        for (const item of pending) {
            if (item.state === "removed") continue;
            await send(item);
        }
        uploading = false;
        addButton.disabled = false;
        app.querySelectorAll("[data-shared]").forEach(shared => { shared.disabled = false; });
        updateTotal();
    });

    function validate(pending) {
        let firstInvalid = null;
        pending.forEach(item => {
            ["Subject", "DocumentCategoryId", "OfficeCategoryId"].forEach(name => {
                const element = field(item, name);
                if (element && !element.value.trim()) {
                    setError(item, name, name === "Subject" ? "Give this PDF a title." : name === "DocumentCategoryId" ? "Choose a type." : "Choose an office.");
                    firstInvalid ??= element;
                }
            });
        });
        firstInvalid?.focus();
        if (firstInvalid) notify("Some PDFs still need details. They are marked in red.");
        return !firstInvalid;
    }

    function send(item, attempt = 0) {
        return new Promise(resolve => {
            item.state = "uploading";
            item.element.classList.remove("is-error");
            setDisabled(item, true);
            const progress = status(item, "Sending… 0%", "progress");

            const data = new FormData();
            data.append("Input.File", item.file, item.file.name);
            item.element.querySelectorAll("[data-field]").forEach(element => {
                const name = element.dataset.field;
                if (element.type === "checkbox") data.append(`Input.${name}`, element.checked ? "true" : "false");
                else data.append(`Input.${name}`, element.value.trim());
            });
            data.append("__RequestVerificationToken", token);

            const request = new XMLHttpRequest();
            request.open("POST", app.dataset.jsonUrl);
            request.setRequestHeader("X-CSRF-TOKEN", token);
            request.upload.addEventListener("progress", event => {
                if (event.lengthComputable && progress) progress.textContent = `Sending… ${Math.round(100 * event.loaded / event.total)}%`;
            });
            request.addEventListener("load", () => {
                // A throttled upload is redirected back to the upload page: wait and send again.
                if (/[?&]throttled=true/i.test(request.responseURL || "")) {
                    if (attempt >= 3) return finish(fail(item, "Too many uploads in a short time. Try this one again in a minute.", true));
                    return waitThenRetry(item, attempt).then(resolve);
                }
                let result = null;
                try { result = JSON.parse(request.responseText); } catch { result = null; }
                if (request.status >= 200 && request.status < 300 && result?.ok) {
                    item.state = "done";
                    item.element.classList.add("is-done");
                    const link = document.createElement("a");
                    link.href = result.url;
                    link.textContent = "Open it";
                    status(item, [document.createTextNode(result.restored ? "Restored to the library. " : "Added to the library. "), link], "done");
                    return finish();
                }
                if (result?.errors) {
                    const messages = [];
                    Object.entries(result.errors).forEach(([key, list]) => {
                        const text = (list || []).join(" ");
                        if (field(item, key) && key !== "File") setError(item, key, text);
                        else messages.push(text);
                    });
                    return finish(fail(item, messages.join(" ") || "Some details need fixing.", true));
                }
                finish(fail(item, "The server did not accept this PDF. Try again.", true));
            });
            request.addEventListener("error", () => finish(fail(item, "The connection failed while sending. Try again.", true)));
            request.send(data);

            function finish() {
                setDisabled(item, item.state === "done");
                updateTotal();
                resolve();
            }
        });
    }

    async function waitThenRetry(item, attempt) {
        for (let seconds = 60; seconds > 0; seconds--) {
            status(item, `Waiting ${seconds}s: too many uploads in a minute.`, "progress");
            await new Promise(done => window.setTimeout(done, 1000));
        }
        return send(item, attempt + 1);
    }

    // SECTION: Item state and messages
    // "blocked" cannot be added (not a PDF, too large, a duplicate); "error" can be sent again.
    function fail(item, message, retryable = false) {
        item.state = retryable ? "error" : "blocked";
        item.element.classList.add("is-error");
        item.element.classList.toggle("is-blocked", !retryable);
        status(item, message, "error");
        updateTotal();
    }

    function status(item, content, tone) {
        const line = item.element.querySelector("[data-item-status]");
        line.className = `library-queue__status${tone === "done" ? " is-done" : tone === "error" ? " is-error" : ""}`;
        const icon = document.createElement("i");
        icon.setAttribute("aria-hidden", "true");
        icon.className = `bi ${tone === "done" ? "bi-check-circle-fill" : tone === "error" ? "bi-exclamation-circle" : tone === "progress" ? "bi-arrow-up-circle" : "bi-info-circle"}`;
        const text = document.createElement("span");
        text.append(...(Array.isArray(content) ? content : [document.createTextNode(content)]));
        line.replaceChildren(icon, text);
        line.hidden = false;
        return text;
    }

    function setError(item, name, message) {
        const error = item.element.querySelector(`[data-error="${name}"]`);
        if (error) error.textContent = message;
        field(item, name)?.classList.toggle("is-invalid", Boolean(message));
    }

    function setDisabled(item, disabled) {
        item.element.querySelectorAll("[data-field]").forEach(element => { element.disabled = disabled; });
        item.element.querySelector("[data-item-remove]").disabled = disabled;
    }

    function updateTotal() {
        const live = items.filter(item => item.state !== "removed");
        const ready = live.filter(item => item.state === "ready" || item.state === "error").length;
        const done = live.filter(item => item.state === "done").length;
        const blocked = live.filter(item => item.state === "blocked").length;
        const parts = [];
        if (ready) parts.push(`${ready} ready to add`);
        if (done) parts.push(`${done} added`);
        if (blocked) parts.push(`${blocked} cannot be added`);
        total.textContent = parts.join(" · ") || "No PDFs chosen";
        uploadButton.disabled = uploading || ready === 0;
        uploadButton.querySelector("span").textContent = ready > 1 ? `Add ${ready} PDFs to the library` : "Add to the library";
        doneLink.hidden = done === 0 || uploading;
    }

    function notify(message) {
        if (typeof window.showToast === "function") window.showToast(message, "info");
        else total.textContent = message;
    }

    // SECTION: Titles
    // "TEC_minutes-radar_v2.pdf" → "TEC minutes - radar v2"
    function titleFromFileName(name) {
        const title = name.replace(/\.pdf$/i, "").replace(/[_]+/g, " ").replace(/\s*-\s*/g, " - ").replace(/\s+/g, " ").trim();
        return title ? title.charAt(0).toUpperCase() + title.slice(1) : "";
    }

    // A title read in capitals ("MINUTES OF THE TEC MEETING") reads better in sentence case.
    function tidyTitle(text) {
        const title = text.replace(/\s+/g, " ").trim();
        const letters = title.replace(/[^A-Za-z]/g, "");
        if (letters.length > 8 && letters === letters.toUpperCase()) {
            return title.charAt(0) + title.slice(1).toLowerCase().replace(/\b(tec|ifa|drdo|sow|hq|cpds|it|ai)\b/g, word => word.toUpperCase());
        }
        return title;
    }

    function formatSize(bytes) {
        if (!Number.isFinite(bytes)) return "";
        if (bytes < 1024) return `${bytes} bytes`;
        if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
        const mb = bytes / (1024 * 1024);
        return `${mb < 10 ? mb.toFixed(1) : Math.round(mb)} MB`;
    }

    updateTotal();
})();
