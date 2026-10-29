# Document Repository pages

How the Document Repository (`/DocumentRepository`) looks and behaves. Access rules, audit events and upload rules are in [Document Repository](../document-repository.md).

## Design

The library is built around the documents themselves rather than table rows: covers of the first page, a search that answers while you type, and a reader made for reading.

- One stylesheet, `wwwroot/css/docrepo-ui.css`, under a `.library` root. Colours come only from the `--pm-library-*` tokens in `site.css` (light and dark, including the hero band, the paper and the reading stage), so the file has no colour literals.
- Each document type has one of six hues (`DocRepoFormat.Hue`), used for its cover, its marker in lists, its filter check box and its timeline dot, so a run of minutes or letters reads at a glance.
- The PDF viewer is [pdf.js](https://mozilla.github.io/pdf.js/) 4.10.38, shipped in `wwwroot/lib/pdfjs-dist/4.10.38` (legacy build, character maps and standard fonts included), so it works on the offline LAN. `wwwroot/js/docrepo/viewer.mjs` wraps it for the reader, the reading pane, the Edit preview and the upload queue.
- Without script the pages still work as ordinary pages: links and filters load, forms post, covers show (an image that is missing has empty alternative text, so the coloured cover shows through), and the single-file upload form replaces the queue. The reader then offers to open the PDF in the browser's own viewer.
- Motion is short and respects `prefers-reduced-motion`.

## The library (`/DocumentRepository/Documents`)

| Area | What it does |
| --- | --- |
| Hero | A dark band with the search box first. On the front page it also shows a headline, quick links to the commonest types and Newest uploads, the shortcut hint, and four figures (documents, added this month, AOTS unread by you; managers also see the share of text that is searchable, a link to Unreadable PDFs for approvers when some failed). Once anything narrows the list it folds to one bar: name, search, **Manage** and **Add PDFs**. |
| Search shortcuts | The search box accepts `type:minutes`, `office:TEC`, `tag:radar`, `year:2025` and `from:DRDO` (quotes for spaces: `office:"Test Range"`). The server turns them into the ordinary filters with a redirect, so the address bar and the chips show what applies. A typed name matches a category exactly, else by its start, else anywhere in it, and only when one category matches; otherwise the word stays in the search. A shortcut must start a word, so a pasted URL is left alone. |
| Quick search (`/`) | Press `/` anywhere outside a field (or the `/` button in the search box) for instant results: up to eight documents matching the title, or any word of the text with the last word as a prefix (`Index?handler=Quick`), with covers, type, office and date. ↑ ↓ move, Enter opens the reader with the words highlighted, and **See all results** runs the full search. Ctrl+K stays PRISM's own search. |
| Tabs | **Library**, **Favourites** (with your count) and **All officers to see** (with "N new" when you have unread ones). |
| Shelves (front page only) | **Continue reading** (documents opened in this browser, newest first, with the page last read; kept per user in `localStorage` and filled through `Index?handler=Cards`, which applies the access rule), **New for you** (AOTS documents you have not opened), **Recently added** and **Your favourites**, each a sideways row of covers with arrows on wide screens; then **Browse by type** tiles with counts. |
| Filters | A sidebar from 992px (a **Filters** fold on narrower screens): Office and Type as lists with counts and a check (six shown, then **Show all**), Year as chips, Tag (suggesting the 40 most used) and Received from as text boxes that apply after a pause, and **Show deactivated** as a switch for `DocRepo.ViewInactive` only. Each count applies every other active filter. |
| Filters apply in place | `docrepo-ui.js` fetches the results region with `?partial=true` and swaps it; the address bar keeps the same query, so reloads, Back, Forward and shared links match the screen. Every link and form also works without script. The default sort and layout are left out of the URL. |
| Heading and tools | A heading that says what is shown ("Results for “radar”", "Your favourites", a type or office name), the count, **Sort** (Best match for searches, Newest first, Oldest first, Recently added, Title A–Z) and the layout switch. Active filters show as removable chips with **Clear all**; when the search words are also a tag, a hint offers to show only that tag. |
| Layouts | **Covers** (the default when browsing), **List** (the default for a search: snippets labelled by where they matched, with the page when known), **Table** (one line per document) and **Timeline** (grouped by month of the document date, a dot per document in its type's colour). `view=cards` from older links opens Covers. |
| Covers | The first page as a JPEG (`Documents/Cover?id=…`, drawn by Poppler on the server) over a cover typeset in the type's colour, which shows whenever there is no image. Pills for AOTS / New / Deactivated, a star that appears on hover (always on phones and when starred), "Found on page N" for a search match, the title (two lines), type and date, and the ⋮ menu. |
| Opening a document | A click opens the reader; a search result opens it with the words highlighted and at the page of the match. From 1600px the reading pane keeps its own column beside the results, so opening one never reflows the list; it shows the PDF in the pdf.js viewer (pages, zoom, the search words highlighted), Open in reader, Download, Edit and the facts. The choice is kept as `?open=<id>`. ↑ ↓ (and ← → along covers), Home and End move between documents; Escape closes the preview. |
| ⋮ menu | Open in reader, Open PDF in new tab, Download; then, by permission, Edit details, Read text again… (failed only), Deactivate… / Activate again… and Request deletion…. The menu is positioned `fixed` so no container clips it. The manage actions open one confirmation dialog (`_LibraryDialog`); deletion needs a reason, Deactivate starts on Cancel, and focus returns to the ⋮ button afterwards. Without script they go to the Manage page. |
| Phones | The hero folds to name, actions and search; tabs scroll sideways; shelves and covers shrink to two columns; filters fold; rows stack; 44px touch targets; no sideways page scroll at 390px. |

What the list deliberately leaves out: OCR status for readers (only managers see "Text not searchable yet" / "Text not readable") and relevance numbers.

## The reader (`Documents/Reader/{id}`)

A full-height reading room:

- **Title bar**: back, a small cover, title and type · office · date with pills; the reading tools (thumbnails on/off, page *n* of *m* with previous / next, zoom out / percentage (click to fit the width) / zoom in, find, dark reading); favourite, **Download**, details on/off and ⋮ (open in the browser's own viewer, Edit details and the manage actions).
- **Thumbnails** of every page down the left, drawn as they scroll into view; the current page is marked and kept in view.
- **Pages** in the pdf.js viewer with selectable text. Opening from a search highlights the words and jumps to the first match (`?q=`), or to a given page (`?page=`). Otherwise the reader reopens where you left off in this browser ("Opened where you left off, page 12 · Start from page 1").
- **Find** (Ctrl+F, or the search button) with match count, Enter / Shift+Enter for next and previous, Escape to close. A scanned PDF has no text layer to highlight; when a search opened it, the reader uses the stored OCR text instead, opens the first page that holds the words and says "In the scanned text on page N".
- **Dark reading** draws the pages light-on-dark (images are inverted too); the choice is remembered.
- **Keys**: ← → pages, + − zoom, Home / End first and last page, Ctrl+F find, `/` search the library.
- **Details** beside the pages from 1200px (a drawer below that; the choice to hide it is remembered): about the document (office and type link to the filtered library, tags), AOTS read state with **Read by N officers**, and the file (name, size, searchable-text state, added and last changed).
- Phones get the same viewer at page width, with the tools on a second row and no thumbnails.
- If pdf.js cannot open the file (damaged or password-protected), the reader says so and offers the browser's own viewer and the download. A missing file shows a clear empty state.

## Adding PDFs (`Documents/Upload`)

- **Several at once**: drop up to 20 PDFs at a time (or choose them); once a batch is in the library, "Add more" takes the next 20 while the added cards stay listed. Each gets a card with its first page, its size and page count, and details read from the PDF in the browser for the person to check: the title (the largest text near the top of page 1, sentence-cased when it is all capitals; else the file name) and the document date (the first date written on page 1, day-first as in Indian government papers, or with a month name). Both are marked "Read from the PDF". A scan has no text to read until OCR runs on the server, so its card asks for the title to be typed from the paper.
- **The same for every PDF**: type, office, received from, tags and AOTS set once at the top fill every card that has not been changed by hand.
- **Before sending**, each file is hashed in the browser and checked with `Upload?handler=CheckHash`: a duplicate (or the same file twice in the queue) is marked and cannot be added, with a link to the existing document; a PDF in the trash is flagged as a restore. The hash check needs a secure context (HTTPS); without one the server's own check still applies.
- **Add N PDFs to the library** sends them one after another to `Upload?handler=Json`, which applies every rule of the ordinary upload, with a progress line per file. Each card then says Added / Restored (with a link) or why not, with field errors under their fields. A throttled upload waits a minute and is sent again automatically.
- Without script, or when an ordinary form post comes back with errors, the single-file form is shown instead: a drop zone, a preview, the grouped fields (**What it is**, **Where it came from**, **Help people find it**) and Upload.

## Edit details

The current PDF in the pdf.js viewer beside the grouped form (sticky from 1200px). Saving returns to the reader. If someone else saved meanwhile, their version is shown with a warning (see the module doc).

## Other pages

| Page | Notes |
| --- | --- |
| Manage (`Documents/Manage/{id}`) | Three cards: **Who can see it** (deactivate / activate), **Searchable text** (state in plain words, the raw OCR message, Read text again) and **Request deletion** (reason required; a pending request shows who asked, when and why). Names are shown instead of user ids. |
| Read by (`Documents/AotsViews/{id}`) | One progress line (read by *n* of *m* people), then two lists: who has read it (with when) and who has not yet (active human accounts). |
| Trash | Search by title (only when there is something to search), totals, who deleted each document and why, and when it will be deleted for good. **Delete for good** asks for confirmation (`data-confirm`). |
| Unreadable PDFs | A plain explanation of each failure (`DocRepoFormat.ExplainOcrFailure`) with the raw message folded underneath; **Read again** and **Read all again** only queue the work. |
| Missing files | Checks every document's file, not just one page, and lists the missing ones with where the file is expected. |
| Offices, Document types | Add on the left; on the right each category's position and the number of documents filed under it (a link to the filtered library). Deactivating one in use asks first. |
