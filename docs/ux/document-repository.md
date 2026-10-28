# Document Repository pages

How the Document Repository (`/DocumentRepository`) looks and behaves. Access rules, audit events and upload rules are in [Document Repository](../document-repository.md).

All pages share one stylesheet, `wwwroot/css/docrepo-ui.css`, under a `.library` root. Colours come only from the `--pm-library-*` tokens in `site.css` (light and dark), so the file has no colour literals. The page frame follows the IPR Register: eyebrow, title and one-line description with actions on the right, then a summary strip, view tabs, pill filters and a rounded table or list.

## The library (`/DocumentRepository/Documents`)

| Area | What it does |
| --- | --- |
| Header | **Upload PDF** (`DocRepo.Upload`) and a **Manage** menu with links to Deletion requests, Unreadable PDFs, Missing files, Trash, Offices and Document types, each shown only to people with that permission. |
| Summary strip | Four figures for the whole library, whatever the filters: documents (and how many were added this month), AOTS documents unread by you, your favourites, and the share whose text is searchable. Managers who can approve deletions see the not-readable count as a link to Unreadable PDFs. |
| Tabs | **All documents**, **Favourites** (with your count) and **AOTS** (with "N new" when you have unread ones). |
| Search | One box for titles, senders, tags and the text inside PDFs. It sits outside the refreshed region, so typing keeps focus. |
| Filters | Office, Type and Year are single choices, each option showing how many documents it would leave, counted with every other filter applied. Tag takes free text. **Show deactivated** appears only for `DocRepo.ViewInactive`. **Sort**: Best match (searches only), Newest first (default), Oldest first, Recently added, Title A–Z. |
| Filters apply in place | `docrepo-ui.js` fetches the results region with `?partial=true` and swaps it; the address bar keeps the same query, so reloads, Back and shared links match the screen. Every link and form also works without script. The default sort is left out of the URL. |
| Chips | Each active filter is a removable chip; **Clear all** keeps the tab and layout. When the search words are also a tag, a hint offers to show only that tag. |
| Rows (List, the default) | A marker coloured by document type (six hues, `DocRepoFormat.Hue`), the title with AOTS / New / Deactivated pills, type · office · sender, a highlighted snippet with where it matched ("In the title", "In a tag", "In the text"), tags (each a filter link), date and size, then favourite, download and a ⋮ menu. |
| Cards | The same facts in a grid, with a coloured top edge by type. |
| Reading pane | From 1400px wide, choosing a row opens a pane beside the list (`Index?handler=Preview`): the PDF, Open in reader, Download, Edit and the facts. Arrow keys move between documents and, with the pane open, preview the next one; Escape closes it. Narrower screens open the reader instead. |
| ⋮ menu | Open in reader, Open PDF in new tab, Download; then, by permission, Edit details, Read text again (failed only), Deactivate / Activate again and Request deletion…. The manage actions open one confirmation dialog (`_LibraryDialog`); deletion needs a reason. Without script they go to the Manage page. |
| Phones | Two-by-two summary figures, full-width filters, rows that stack the date and actions under the title, 44px touch targets. |

What the list deliberately leaves out: OCR status for readers (only managers see "Text being read" / "Text not readable"), relevance numbers, and thumbnails (there is no server-side PDF renderer, and drawing them in the browser would download every PDF on the page).

## The reader (`Documents/Reader/{id}`)

Title bar (type · office · date, title, pills; favourite, Open in new tab, Download, Edit details, ⋮ with the same manage actions), the PDF in the browser's own viewer, and details beside it from 1200px (below it on narrower screens): about the document, AOTS read state with **Read by N officers**, and the file (name, size, searchable-text state, added and last changed). If the browser takes more than 8 seconds to show the PDF, the reader offers a new tab or a download. A missing file shows a clear empty state instead of a viewer.

## Upload and Edit

Two columns from 1200px: the PDF on the left, the form on the right (sticky).

- **Upload**: a drop zone (drag a PDF or choose one) that becomes the file name, size and a live preview. The title is suggested from the file name. Before anything is sent, the browser hashes the file and asks `Upload?handler=CheckHash` whether the library already holds it; a duplicate disables Upload and links to the existing document, and a PDF that is in the trash is flagged as a restore. The hash check needs a secure context (HTTPS); without one it is skipped and the server's own check still applies.
- **Edit details**: the current PDF beside the same form. Saving returns to the reader. If someone else saved meanwhile, their version is shown with a warning (see the module doc).
- Fields are grouped as **What it is** (title*, type*, office*), **Where it came from** (received from, document date) and **Help people find it** (tags, AOTS). Required fields carry a red asterisk; errors appear under their field.

## Other pages

| Page | Notes |
| --- | --- |
| Manage (`Documents/Manage/{id}`) | Three cards: **Who can see it** (deactivate / activate), **Searchable text** (state in plain words, the raw OCR message, Read text again) and **Request deletion** (reason required; a pending request shows who asked, when and why). Names are shown instead of user ids. |
| Read by (`Documents/AotsViews/{id}`) | Read *n* of *m* people with a progress bar, then two lists: who has read it (with when) and who has not yet (active human accounts). |
| Trash | Search by title, totals, who deleted each document and why, and when it will be deleted for good. **Delete for good** asks for confirmation (`data-confirm`). |
| Unreadable PDFs | A plain explanation of each failure (`DocRepoFormat.ExplainOcrFailure`) with the raw message folded underneath; **Read again** and **Read all again** only queue the work. |
| Missing files | Checks every document's file, not just one page, and lists the missing ones with where the file is expected. |
| Offices, Document types | Add on the left; on the right each category's position and the number of documents filed under it (a link to the filtered library). Deactivating one in use asks first. |
