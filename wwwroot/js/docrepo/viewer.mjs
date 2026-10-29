// Document Repository PDF viewer, built on pdf.js shipped with the app (wwwroot/lib/pdfjs-dist;
// no CDN, so it works on the offline LAN). Used by the reader page and the library's reading
// pane: continuous pages with selectable text, page thumbnails, find with highlights, zoom,
// a dark reading mode and the last page read, remembered in this browser.
//
//   const viewer = await mountViewer(root, { url, libraryBase, documentId, query, page, compact });
//
// The root holds [data-viewer-stage] (scrolling pages), and optionally [data-viewer-thumbs],
// [data-viewer-page], [data-viewer-pages], [data-viewer-find] and the buttons wired below.

const memoryKey = (userKey, id) => `prism.docrepo.page.${userKey || "anon"}.${id}`;
const darkKey = "prism.docrepo.dark";
const recentKey = "prism.docrepo.recent";

let libraryPromise = null;

function loadLibrary(base) {
    libraryPromise ??= (async () => {
        const pdfjs = await import(`${base}build/pdf.min.mjs`);
        // The viewer components read the core library from this global.
        globalThis.pdfjsLib = pdfjs;
        pdfjs.GlobalWorkerOptions.workerSrc = `${base}build/pdf.worker.min.mjs`;
        const components = await import(`${base}web/pdf_viewer.mjs`);
        return { pdfjs, components };
    })();
    return libraryPromise;
}

function readStore(key) {
    try { return window.localStorage.getItem(key); } catch { return null; }
}

function writeStore(key, value) {
    try {
        if (value === null) window.localStorage.removeItem(key);
        else window.localStorage.setItem(key, value);
    } catch {
        // Private windows and blocked storage: nothing is remembered, everything still works.
    }
}

/** Records a document as recently read (for "Continue reading" in the library). */
export function rememberRecent(userKey, documentId, page) {
    if (!userKey || !documentId) return;
    const key = `${recentKey}.${userKey}`;
    let list = [];
    try { list = JSON.parse(readStore(key) || "[]"); } catch { list = []; }
    if (!Array.isArray(list)) list = [];
    list = list.filter(entry => entry && entry.id !== documentId);
    list.unshift({ id: documentId, page, at: Date.now() });
    writeStore(key, JSON.stringify(list.slice(0, 12)));
}

export async function mountViewer(root, options) {
    const { url, libraryBase, documentId, query, compact = false } = options;
    const stage = root.querySelector("[data-viewer-stage]");
    const pagesHost = stage.querySelector(".pdfViewer");
    const thumbs = root.querySelector("[data-viewer-thumbs]");
    const pageInput = root.querySelector("[data-viewer-page]");
    const pageCount = root.querySelector("[data-viewer-pages]");
    const status = root.querySelector("[data-viewer-status]");
    const findBar = root.querySelector("[data-viewer-find]");
    const findInput = findBar?.querySelector("input");
    const findCount = findBar?.querySelector("[data-viewer-find-count]");

    const { pdfjs, components } = await loadLibrary(libraryBase);
    const { EventBus, PDFLinkService, PDFFindController, PDFViewer } = components;

    const eventBus = new EventBus();
    const linkService = new PDFLinkService({ eventBus, externalLinkTarget: 2 /* new window */ });
    const findController = new PDFFindController({ eventBus, linkService });
    const pdfViewer = new PDFViewer({
        container: stage,
        viewer: pagesHost,
        eventBus,
        linkService,
        findController,
        textLayerMode: 1,
        removePageBorders: true
    });
    linkService.setViewer(pdfViewer);

    // SECTION: Open the document
    const loading = pdfjs.getDocument({
        url,
        withCredentials: true,
        cMapUrl: `${libraryBase}cmaps/`,
        cMapPacked: true,
        standardFontDataUrl: `${libraryBase}standard_fonts/`,
        isEvalSupported: false
    });
    loading.onProgress = progress => {
        // Range requests keep reporting after the pages are shown; only the opening is told.
        if (!status || !progress.total || root.classList.contains("is-ready")) return;
        status.textContent = `Opening the PDF… ${Math.min(100, Math.round(100 * progress.loaded / progress.total))}%`;
    };

    let pdf;
    try {
        pdf = await loading.promise;
    } catch (error) {
        root.classList.add("is-failed");
        if (status) {
            status.textContent = error?.name === "PasswordException"
                ? "This PDF is protected by a password, so it cannot be shown here. Download it to open it."
                : "This PDF could not be shown here. Download it or open it in a new tab.";
        }
        throw error;
    }

    pdfViewer.setDocument(pdf);
    linkService.setDocument(pdf, null);
    if (pageCount) pageCount.textContent = String(pdf.numPages);
    if (pageInput) {
        pageInput.max = String(pdf.numPages);
        pageInput.setAttribute("aria-label", `Page, of ${pdf.numPages}`);
    }

    // SECTION: Scale and starting page
    const remembered = parseInt(readStore(memoryKey(options.userKey, documentId)) || "", 10);
    const startPage = clampPage(options.page) ?? (query ? null : clampPage(remembered));

    eventBus.on("pagesinit", () => {
        pdfViewer.currentScaleValue = compact ? "page-width" : (window.innerWidth < 768 ? "page-width" : "auto");
        root.classList.add("is-ready");
        if (status) status.textContent = "";
        if (startPage && startPage > 1) {
            pdfViewer.currentPageNumber = startPage;
            if (!options.page && remembered === startPage) {
                root.dispatchEvent(new CustomEvent("viewer:resumed", { detail: { page: startPage } }));
            }
        }
        if (query) find(query);
    });

    function clampPage(value) {
        const number = parseInt(value, 10);
        return Number.isFinite(number) && number >= 1 && number <= pdf.numPages ? number : null;
    }

    // SECTION: Page indicator, memory and thumbnails follow the page in view
    eventBus.on("pagechanging", event => {
        const page = event.pageNumber;
        if (pageInput && document.activeElement !== pageInput) pageInput.value = String(page);
        writeStore(memoryKey(options.userKey, documentId), page > 1 ? String(page) : null);
        if (options.userKey) rememberRecent(options.userKey, documentId, page);
        markThumb(page);
        root.dispatchEvent(new CustomEvent("viewer:page", { detail: { page, pages: pdf.numPages } }));
    });
    if (options.userKey) rememberRecent(options.userKey, documentId, startPage || 1);

    pageInput?.addEventListener("change", () => {
        const page = clampPage(pageInput.value);
        if (page) pdfViewer.currentPageNumber = page;
        else pageInput.value = String(pdfViewer.currentPageNumber);
    });

    // SECTION: Zoom
    const zoomSteps = [0.5, 0.67, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4];
    function zoom(direction) {
        const current = pdfViewer.currentScale;
        const next = direction > 0
            ? zoomSteps.find(step => step > current + 0.01) ?? zoomSteps[zoomSteps.length - 1]
            : [...zoomSteps].reverse().find(step => step < current - 0.01) ?? zoomSteps[0];
        pdfViewer.currentScale = next;
    }

    eventBus.on("scalechanging", event => {
        const label = root.querySelector("[data-viewer-zoom-label]");
        if (label) label.textContent = `${Math.round(event.scale * 100)}%`;
    });

    // SECTION: Dark reading (pages drawn light-on-dark; the setting is remembered)
    function setDark(on) {
        root.classList.toggle("is-dark-reading", on);
        root.querySelectorAll("[data-viewer-action='dark']").forEach(button => button.setAttribute("aria-pressed", on ? "true" : "false"));
        writeStore(darkKey, on ? "1" : null);
    }
    setDark(readStore(darkKey) === "1");

    // SECTION: Find
    // A library search ("radar evaluation", "\"radar subsystem\"", -draft, type:x) becomes the
    // words and phrases to highlight; pdf.js finds each one rather than the whole line as a phrase.
    function searchTerms(text) {
        const terms = [];
        for (const match of String(text || "").matchAll(/"([^"]+)"|(\S+)/g)) {
            const term = (match[1] ?? match[2] ?? "").trim();
            if (!term || (!match[1] && (/^-/.test(term) || /^[a-z]+:/i.test(term) || /^(or|and|not)$/i.test(term)))) continue;
            terms.push(term);
        }
        return terms.length > 1 ? terms : terms[0] ?? "";
    }

    let lastQuery = "";
    function find(text, again = false, previous = false) {
        lastQuery = text;
        if (findBar) {
            findBar.hidden = false;
            if (findInput && findInput.value !== text) findInput.value = text;
        }
        eventBus.dispatch("find", {
            source: null,
            type: again ? "again" : "",
            query: searchTerms(text),
            caseSensitive: false,
            entireWord: false,
            highlightAll: true,
            findPrevious: previous,
            matchDiacritics: false
        });
    }

    function closeFind() {
        if (!findBar) return;
        findBar.hidden = true;
        eventBus.dispatch("findbarclose", { source: null });
        stage.focus({ preventScroll: true });
    }

    eventBus.on("updatefindmatchescount", event => showMatches(event.matchesCount));
    eventBus.on("updatefindcontrolstate", event => showMatches(event.matchesCount, event.state));

    // A scanned PDF has no text to highlight; the stored OCR text still says which pages match.
    const matchPages = Array.isArray(options.matchPages) ? options.matchPages.filter(page => clampPage(page)) : [];
    let usedMatchPages = false;

    function showMatches(counts, state) {
        if (!findCount) return;
        if (!lastQuery) { findCount.textContent = ""; return; }
        if (state === 1 /* not found */ || counts?.total === 0) {
            const pages = matchPages.length ? matchPages : options.textMatches && pdf.numPages === 1 ? [1] : [];
            if ((pages.length || options.textMatches) && lastQuery === query) {
                findCount.textContent = pages.length === 0
                    ? "In the scanned text"
                    : pages.length === 1
                        ? `In the scanned text on page ${pages[0]}`
                        : `In the scanned text on pages ${pages.slice(0, 6).join(", ")}${pages.length > 6 ? "…" : ""}`;
                findCount.title = "This page is a scan: its words can be searched but not highlighted.";
                if (!usedMatchPages && pages.length) {
                    usedMatchPages = true;
                    pdfViewer.currentPageNumber = pages[0];
                }
                return;
            }
            findCount.textContent = "Not found";
            return;
        }
        findCount.removeAttribute("title");
        if (counts?.total) findCount.textContent = `${counts.current} of ${counts.total}`;
    }

    let findTimer = null;
    findInput?.addEventListener("input", () => {
        window.clearTimeout(findTimer);
        findTimer = window.setTimeout(() => find(findInput.value.trim()), 250);
    });
    findInput?.addEventListener("keydown", event => {
        if (event.key === "Enter") {
            event.preventDefault();
            find(findInput.value.trim(), true, event.shiftKey);
        } else if (event.key === "Escape") {
            event.preventDefault();
            closeFind();
        }
    });

    // SECTION: Buttons
    root.addEventListener("click", event => {
        const button = event.target.closest("[data-viewer-action]");
        if (!button || !root.contains(button)) return;
        switch (button.dataset.viewerAction) {
            case "zoom-in": zoom(1); break;
            case "zoom-out": zoom(-1); break;
            case "fit-width": pdfViewer.currentScaleValue = "page-width"; break;
            case "fit-page": pdfViewer.currentScaleValue = "page-fit"; break;
            case "previous": pdfViewer.previousPage(); break;
            case "next": pdfViewer.nextPage(); break;
            case "dark": setDark(!root.classList.contains("is-dark-reading")); break;
            case "find":
                if (findBar) {
                    findBar.hidden = false;
                    findInput?.focus();
                    findInput?.select();
                }
                break;
            case "find-next": find(findInput?.value.trim() || lastQuery, true, false); break;
            case "find-previous": find(findInput?.value.trim() || lastQuery, true, true); break;
            case "find-close": closeFind(); break;
            case "start": pdfViewer.currentPageNumber = 1; break;
            case "thumbs": root.classList.toggle("is-thumbs-hidden"); break;
        }
    });

    // SECTION: Keyboard (only when focus is not in a form field elsewhere on the page)
    if (!compact) {
        document.addEventListener("keydown", event => {
            const inField = event.target.closest?.("input, textarea, select, [contenteditable='true']");
            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "f" && findBar) {
                event.preventDefault();
                findBar.hidden = false;
                findInput?.focus();
                findInput?.select();
                return;
            }
            if (inField || event.altKey || event.ctrlKey || event.metaKey || document.querySelector(".modal.show")) return;
            if (event.key === "ArrowRight" && !stageScrollsSideways()) { pdfViewer.nextPage(); event.preventDefault(); }
            else if (event.key === "ArrowLeft" && !stageScrollsSideways()) { pdfViewer.previousPage(); event.preventDefault(); }
            else if (event.key === "+" || event.key === "=") { zoom(1); event.preventDefault(); }
            else if (event.key === "-") { zoom(-1); event.preventDefault(); }
            else if (event.key === "Home" && !event.shiftKey) { pdfViewer.currentPageNumber = 1; event.preventDefault(); }
            else if (event.key === "End" && !event.shiftKey) { pdfViewer.currentPageNumber = pdf.numPages; event.preventDefault(); }
        });
    }

    function stageScrollsSideways() {
        return stage.scrollWidth > stage.clientWidth + 2;
    }

    // SECTION: Thumbnails, drawn as they scroll into view
    const thumbButtons = [];
    if (thumbs) buildThumbnails();

    function buildThumbnails() {
        const list = document.createElement("ol");
        list.className = "viewer-thumbs__list";
        const queue = [];
        let drawing = false;

        const observer = new IntersectionObserver(entries => {
            for (const entry of entries) {
                if (!entry.isIntersecting) continue;
                observer.unobserve(entry.target);
                queue.push(entry.target);
            }
            drawNext();
        }, { root: thumbs, rootMargin: "300px 0px" });

        for (let number = 1; number <= pdf.numPages; number++) {
            const item = document.createElement("li");
            const button = document.createElement("button");
            button.type = "button";
            button.className = "viewer-thumb";
            button.dataset.page = String(number);
            button.setAttribute("aria-label", `Page ${number}`);
            const frame = document.createElement("span");
            frame.className = "viewer-thumb__frame";
            const label = document.createElement("span");
            label.className = "viewer-thumb__number";
            label.textContent = String(number);
            button.append(frame, label);
            button.addEventListener("click", () => { pdfViewer.currentPageNumber = number; });
            item.append(button);
            list.append(item);
            thumbButtons.push(button);
            observer.observe(button);
        }

        thumbs.replaceChildren(list);

        async function drawNext() {
            if (drawing) return;
            const button = queue.shift();
            if (!button) return;
            drawing = true;
            try {
                const page = await pdf.getPage(parseInt(button.dataset.page, 10));
                const base = page.getViewport({ scale: 1 });
                const ratio = window.devicePixelRatio || 1;
                const viewport = page.getViewport({ scale: (120 / base.width) * ratio });
                const canvas = document.createElement("canvas");
                canvas.width = Math.ceil(viewport.width);
                canvas.height = Math.ceil(viewport.height);
                await page.render({ canvasContext: canvas.getContext("2d"), viewport }).promise;
                button.querySelector(".viewer-thumb__frame").replaceChildren(canvas);
            } catch {
                // A page that cannot be drawn keeps its blank frame and number.
            } finally {
                drawing = false;
                drawNext();
            }
        }
    }

    function markThumb(page) {
        if (!thumbButtons.length) return;
        thumbButtons.forEach(button => {
            const current = button.dataset.page === String(page);
            button.classList.toggle("is-current", current);
            if (current) button.setAttribute("aria-current", "page");
            else button.removeAttribute("aria-current");
        });
        const current = thumbButtons[page - 1];
        if (current && !root.classList.contains("is-thumbs-hidden")) {
            current.scrollIntoView({ block: "nearest" });
        }
    }

    return {
        pages: pdf.numPages,
        goTo(page) { const number = clampPage(page); if (number) pdfViewer.currentPageNumber = number; },
        find,
        destroy() {
            loading.destroy();
            pdfViewer.cleanup?.();
        }
    };
}

// SECTION: Reading a chosen file before upload
// Draws page 1 as a small cover and suggests a title (the largest text near the top of page 1)
// and a document date (the first date written on page 1). Suggestions only: the person checks them.
export async function readPdfFacts(libraryBase, bytes, { coverWidth = 192 } = {}) {
    const { pdfjs } = await loadLibrary(libraryBase);
    const loading = pdfjs.getDocument({
        data: bytes,
        cMapUrl: `${libraryBase}cmaps/`,
        cMapPacked: true,
        standardFontDataUrl: `${libraryBase}standard_fonts/`,
        isEvalSupported: false
    });
    try {
        const pdf = await loading.promise;
        const page = await pdf.getPage(1);
        const base = page.getViewport({ scale: 1 });
        const ratio = window.devicePixelRatio || 1;
        const viewport = page.getViewport({ scale: (coverWidth / base.width) * ratio });
        const canvas = document.createElement("canvas");
        canvas.width = Math.ceil(viewport.width);
        canvas.height = Math.ceil(viewport.height);
        await page.render({ canvasContext: canvas.getContext("2d"), viewport }).promise;

        const content = await page.getTextContent();
        const lines = groupLines(content.items, base.height);
        return {
            pages: pdf.numPages,
            cover: canvas,
            // No text on page 1 means a scan: nothing can be read until OCR runs on the server.
            scanned: lines.length === 0,
            title: guessTitle(lines),
            date: guessDate(lines.map(line => line.text).join("\n"))
        };
    } finally {
        loading.destroy();
    }
}

function groupLines(items, pageHeight) {
    const lines = [];
    for (const item of items) {
        const text = (item.str || "").trim();
        if (!text) continue;
        const y = Math.round(item.transform[5]);
        const size = Math.round(Math.abs(item.transform[3]) || item.height || 0);
        let line = lines.find(candidate => Math.abs(candidate.y - y) <= 2);
        if (!line) {
            line = { y, size, parts: [] };
            lines.push(line);
        }
        line.size = Math.max(line.size, size);
        line.parts.push({ x: item.transform[4], text: item.str });
    }
    return lines
        .map(line => ({
            y: line.y,
            fromTop: pageHeight - line.y,
            size: line.size,
            text: line.parts.sort((a, b) => a.x - b.x).map(part => part.text).join(" ").replace(/\s+/g, " ").trim()
        }))
        .filter(line => line.text)
        .sort((a, b) => b.y - a.y);
}

function guessTitle(lines) {
    const candidates = lines
        .slice(0, 25)
        .filter(line => line.text.length >= 6 && line.text.length <= 160 && /[A-Za-zऀ-ॿ]{3}/.test(line.text))
        .filter(line => !/^(page \d+|confidential|restricted|secret|copy no)/i.test(line.text));
    if (!candidates.length) return null;
    const largest = Math.max(...candidates.map(line => line.size));
    // The largest lines near the top, joined when a title wraps onto a second line.
    const title = candidates.filter(line => line.size >= largest - 0.5).slice(0, 2).map(line => line.text).join(" ");
    return title.length > 200 ? title.slice(0, 200) : title;
}

const months = { jan: 1, feb: 2, mar: 3, apr: 4, may: 5, jun: 6, jul: 7, aug: 8, sep: 9, sept: 9, oct: 10, nov: 11, dec: 12 };

function guessDate(text) {
    const found = [];
    const add = (year, month, day) => {
        const y = Number(year < 100 ? 2000 + Number(year) : year);
        const m = Number(month);
        const d = Number(day);
        if (y < 1950 || y > new Date().getFullYear() + 1 || m < 1 || m > 12 || d < 1 || d > 31) return;
        const date = new Date(Date.UTC(y, m - 1, d));
        if (date.getUTCDate() !== d) return;
        found.push(`${y}-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}`);
    };
    const monthName = "(jan|feb|mar|apr|may|jun|jul|aug|sept?|oct|nov|dec)[a-z]*\\.?";
    for (const match of text.matchAll(new RegExp(`\\b(\\d{1,2})(?:st|nd|rd|th)?\\s+${monthName},?\\s+(\\d{4})\\b`, "gi"))) add(match[3], months[match[2].toLowerCase()], match[1]);
    for (const match of text.matchAll(new RegExp(`\\b${monthName}\\s+(\\d{1,2})(?:st|nd|rd|th)?,?\\s+(\\d{4})\\b`, "gi"))) add(match[3], months[match[1].toLowerCase()], match[2]);
    // Day first, as written in Indian government papers: 12/03/2025, 12.03.2025, 12-03-25.
    for (const match of text.matchAll(/\b(\d{1,2})[./-](\d{1,2})[./-](\d{4}|\d{2})\b/g)) add(match[3], match[2], match[1]);
    for (const match of text.matchAll(/\b(\d{4})-(\d{2})-(\d{2})\b/g)) add(match[1], match[2], match[3]);
    // The first date in reading order is usually the date of the paper.
    const positions = found.map(value => ({ value, index: indexOfDate(text, value) }));
    positions.sort((a, b) => a.index - b.index);
    return positions[0]?.value ?? null;
}

function indexOfDate(text, iso) {
    const [y, m, d] = iso.split("-").map(Number);
    const patterns = [
        new RegExp(`\\b0?${d}(?:st|nd|rd|th)?\\s+[A-Za-z]+\\.?,?\\s+${y}\\b`),
        new RegExp(`\\b[A-Za-z]+\\.?\\s+0?${d}(?:st|nd|rd|th)?,?\\s+${y}\\b`),
        new RegExp(`\\b0?${d}[./-]0?${m}[./-](?:${y}|${String(y).slice(2)})\\b`),
        new RegExp(`\\b${y}-0?${m}-0?${d}\\b`)
    ];
    const indexes = patterns.map(pattern => text.search(pattern)).filter(index => index >= 0);
    return indexes.length ? Math.min(...indexes) : Number.MAX_SAFE_INTEGER;
}
