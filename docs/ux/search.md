# Search: page and search box

How the Search page (`/Common/Search`) and the top-bar search box behave. Ranking,
indexing and the engines are described in [Search and OCR architecture](../search-and-ocr-architecture.md);
this page covers what the user sees.

## Search box (top bar and Search page)

One module, `wwwroot/js/navigation/search-combobox.js`, drives every search box. The top bar
uses it through `navigation/global-search.js` (loaded by `site.js`); the Search page through
`pages/search.js`. Both mark their input with `data-search-combobox`.

- **Accessible combobox.** The input has `role="combobox"`, `aria-controls` points at a
  listbox with a stable id (`<input id>-listbox`), and the highlighted row is announced with
  `aria-activedescendant`. Every row, including **See all results**, is a `role="option"`
  reachable with the arrow keys. A polite live region announces how many suggestions there are.
- **States.** Loading, no quick matches and suggestions unavailable each show their own row.
  A failed request never reads as "no matches"; Enter still runs the full search.
- **Escape** closes the suggestions and never clears the text (browsers clear `type="search"`
  on Escape by default). An empty submit is ignored.
- **Suggestion links** are followed only when they stay on this site's origin.
- **Highlighting** wraps the first match, preferring the start of a word, in `<mark>`. All
  text is set with `textContent`; nothing from the server is parsed as HTML.
- **Recent searches.** The last six searches are kept in this browser only (`localStorage`,
  key `prism.search.recent`), shown when the box is empty and on the landing and zero-result
  pages, with a **Clear recent searches** row. Blocked storage is ignored silently.
- **Shortcuts.** Ctrl+K (⌘K on Apple devices; the hint follows the platform) and `/` outside a
  text field focus the visible search box. Where the top-bar box is hidden (narrow screens),
  Ctrl+K opens the Search page.

## Results page

- **Headings and landmarks.** One `<main>` (the layout's). A visually hidden `h1`
  ("Search results for …"), each result title is an `h2`. The tab title reads
  `“query” – 512 results – Search`. The result count is a `role="status"`; the active
  category tab has `aria-current="page"`.
- **Failure states** (from `SearchGatewayResponse`):
  - `IsUnavailable`: no engine could run. A red `role="alert"` banner with **Try again**;
    no result list and never "No results".
  - `IsDegraded`: Search V2 should have served but the bounded Legacy engine answered.
    An amber banner says results may be incomplete and, if any were set, that project,
    status, file type, stage and date filters were not applied.
  - Legacy as the configured engine (V2 not rolled out to the user) with advanced filters in
    the URL: an amber banner says those filters were not applied.
- **Result card.** Type icon, title (link), subtitle, three-line snippet, and a details row:
  source, parent project (from the indexed match fields), status, date, file type, and where
  the match was found when it was not the title. Separators come from CSS, so a missing field
  never leaves a stray dot. Query latency is shown only to users who can open the admin
  Search diagnostics (`AdminPolicies.SecurityView`) or in Development.
- **More results.** "Showing 1–20 of 512 results". **Show more results** appends the next
  page in place, updates the range and moves focus to the first new result; without
  JavaScript (or if the request fails) it opens the next page, which shows its own range and
  a **First results** link.
- **Zero results.** Offers the did-you-mean search (filters kept), **Remove all filters**,
  **Search all categories**, recent searches and search tips.
- **Filters panel.** Anchored under the Filters button; header and Apply/Clear stay visible
  while the list scrolls (height capped to the space below the button). On phones it is a
  bottom sheet. Detailed facets load on first open with a skeleton, a **Retry** on failure,
  and an `AbortController`. Clicking outside or on the Filters button with unapplied changes
  keeps the panel open and says "Changes not applied yet"; Escape or the close button discards
  them and returns focus to the button. Date presets: last 30 days, last 12 months, this year.
  Active filter chips all read "Dimension: value".
- **Print** prints the result list only, with each result's address.

## Styling

`wwwroot/css/pages/search.css` uses site tokens only (no literal colours), so Light and Dark
share every rule; the search-box popup lives in `navigation-shell.css` (`.pm-search-popup`,
`.pm-search-option`). Highlights use a translucent `--pm-warning` tint; metadata is at least
12px in `--pm-text-secondary`.

## Not done here

- **Sort by newest.** The engine orders by relevance only; a sort option needs a server-side
  change in `SearchEngine` and is left to the search-engine work.
