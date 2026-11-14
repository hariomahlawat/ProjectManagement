# Search: review (15 Nov 2026) and fix plan

Three reviewers read the search module cold at `2e1a0b9`, without earlier plans:

- **Indexing and access:** `Services/SearchV2/Indexing`, `Security`, the Search V2 migrations and
  the database triggers that queue index work.
- **Query path:** `Services/SearchV2/Query`, `Analytics`, the Legacy fan-out in `Services/Search`,
  the page handlers and the search-health dashboard.
- **Page and search box:** `Areas/Common/Pages/Search`, `search.js`, `search-combobox.js`,
  `search.css`, and the module search boxes (Documents quick search, Photos).

The page was also checked live at desktop and phone widths on the development data.

**Verdict:** UX 7/10, code 7.5/10. Nothing High. No injection or HTML injection path; the access
predicate is applied to hits, suggestions, did-you-mean, facets, project labels and counts, and
the source→policy map matches what each target page requires. The findings below were checked
against the code before this plan was written.

## Medium: index correctness

| # | Finding | Where |
| --- | --- | --- |
| M1 | Approved Proliferation entries of trashed or archived projects stay indexed (project name, unit, quantity, remarks); the link answers Not found | `BuildProliferationAsync` in `SearchProjectionBuilder.cs` filters only on `ApprovalStatus` |
| M2 | Child rows do not follow their project. Trashing, archiving, renaming or a stage change queues only `Project/<id>`, and `ReplaceEntityAsync` replaces only the project and its documents. ToT (eligibility: not trashed, not archived, not a repeat build, Completed), IPR, Training, FFC, ARPP and Proliferation rows keep the old title, stage and visibility until the daily rebuild. The Project row's ToT/IPR summary goes stale when a ToT or IPR changes | `Projects` trigger in `20261216200000_AddSearchV2Foundation.cs`; `SearchIndexStore.ReplaceEntityAsync` |
| M3 | An index update can be lost. The trigger stamps `RequestedAtUtc = NOW()`, which is the start of the user's transaction. If the worker starts the item after that transaction began but before it commits, it reads the old row, and `CompleteAsync` (`RequestedAtUtc <= StartedAtUtc`) deletes the re-queued item. The change waits for the daily rebuild | `search_v2_enqueue` in the foundation migration; `SearchIndexStore.CompleteAsync` |

## Medium: query cost and robustness

| # | Finding | Where |
| --- | --- | --- |
| M4 | A submitted search of one or two characters is accepted (suggestions need two). `'a':*` and `LIKE '%a%'` match nearly every entry, and the title-prefix channel computes `to_tsvector('simple', Title)` per row. There is no `statement_timeout` and no rate limit on search | `SearchEngine.SearchAsync`; channel SQL in `SearchEngine.cs` |
| M5 | Each of the 18 retrieval channels returns every match (no per-channel limit), and `MetadataJson` is read for every candidate. A common word costs the whole match set on every page, every Show more and every facet load | channel CTEs in `SearchEngine.cs` |
| M6 | Every debounced suggestion writes a `SearchQueryLogs` row (`V2-Suggest`) with the partly typed text and the role list, kept 90 days; about three round trips per keystroke | `SearchGateway.SuggestAsync`; `SearchAnalyticsService` |
| M7 | Every `ProjectionVersion` bump sends everyone to the Legacy engine until the rebuild finishes, because the engine requires `IndexVersion == ProjectionVersion` | `SearchEngine.SearchAsync` readiness check |
| M8 | The full rebuild builds every projection in one list, including the OCR text of every document (up to 200,000 characters each), and writes row by row in one transaction. Time and memory at production size are not measured; it runs daily on the server that also runs Remove background | `SearchProjectionBuilder.BuildAllAsync`; `SearchIndexStore.ReplaceFullGenerationAsync` |
| M9 | Show more can skip or repeat results when incremental indexing changes the ranked order between pages; the cursor checks only the generation | `SearchCursorCodec`; `SearchIndexStore.ReplaceEntityAsync` |
| M10 | Did-you-mean pairs normalised and original tokens by position, so `anti-tank ATGM` loses the capital protection on `ATGM`; a corrected query drops joiners (`IR&D` → `ir d`) | `SearchCorrectionService` |

## Medium: page

| # | Finding | Where |
| --- | --- | --- |
| M11 | A second tap on Show more while it loads follows the link: the next page opens and the list built so far is lost | `initLoadMore` in `search.js` (returns before `preventDefault`) |
| M12 | In fallback mode a Source filter is applied but has no chip, and the banner says project, status, file type, stage and date filters were not applied | `Index.cshtml` banners and chips; `HasAdvancedFilters` |
| M13 | On phones the type icon is hidden; nine sources share one icon on every width; the category tabs run off screen with no sign that they scroll | `search.css` narrow query; `IconFor` in `Index.cshtml` |
| M14 | The phone filter sheet is not a dialog: no `role="dialog"`/`aria-modal`, and Tab reaches links behind the backdrop | `search.css`, `search.js` |
| M15 | Touch targets of 22–32 px on phones: date presets, related chips, filter chips, clear and close buttons | `search.css` |

## Low

| # | Finding |
| --- | --- |
| L1 | "Found in structured details", "Found in Identifier", "Project Brief + Description": field names, not plain words (`SearchMatchEvidence`) |
| L2 | Choosing a suggestion saves the typed fragment ("aur") as a recent search |
| L3 | Show more loses its arrow icon after the first load |
| L4 | Hover and keyboard highlight can mark two suggestion rows at once |
| L5 | The suggestion list stays open when focus leaves without a click |
| L6 | The amber banners use `role="alert"` and are read out on every load |
| L7 | The category tab counts do not add up to All (443 against 528 on the dev data), because ToT and IPR rows fold under their project in All |
| L8 | The landing page is mostly empty; the "Try" examples are fixed and the tagline names modules the user may not have |
| L9 | IPR and Training rows can show a trashed project's name |
| L10 | Category and reference-list renames have no trigger and wait for the daily rebuild |
| L11 | Legacy-Fallback searches do not show on the search-health dashboard |
| L12 | Suggestions with the same title (two "Acceptance report" documents) collapse to one |
| L13 | Docs: `search-and-ocr-architecture.md` §4.1–4.3 still reads as if the Legacy fan-out were the main engine, and §4.6 "hidden with their parent" holds only for project documents |
| L14 | Tests: none for children following their project, the enqueue race, paging under re-indexing, short queries, correction token alignment, or `pages/search.js` |

## Build plan

Each PR keeps the current routes, configuration keys and persisted data. Index changes bump
`ProjectionVersion`, which rebuilds the index atomically (the old generation serves meanwhile
once PR 8 lands; until then searches fall back to Legacy during the rebuild).

| PR | Scope | Findings |
| --- | --- | --- |
| 1 | This plan | — |
| 2 | Index rows follow their project: Proliferation uses the counted-projects rule; processing `Project/<id>` also re-indexes its ToT and every row whose parent is that project; ToT/IPR/Proliferation changes also re-index their Project row; IPR and Training ignore trashed projects; `ProjectionVersion` 8; Postgres tests | M1, M2, L9, L13 (§4.6) |
| 3 | No lost index updates: completing a work item deletes it only if its row is unchanged since the lease (PostgreSQL `xmin`), so no migration is needed; test for the dequeue/commit race | M3 |
| 4 | Limits: submitted searches need two characters, prefix/contains channels three; `SET LOCAL statement_timeout` inside the search transaction, answered as unavailable; tests | M4 |
| 5 | Suggestions stop writing query-log rows (latency kept in process); Legacy-Fallback counts on the search-health dashboard | M6, L11 |
| 6 | Page fixes: Show more double tap, Source chips and true banner wording, `role="status"` warnings, per-source icons and a type label on phones, tab scroll hint, filter sheet as a dialog, 44 px targets; `docs/ux/search.md` | M11–M15, L3, L6 |
| 7 | Small page polish: plain "Found in" wording, recent searches keep full searches only, close on focus leaving, one highlighted row, tab counts explained, landing examples from the user's own recent searches | L1, L2, L4, L5, L7, L8 |
| 8 | Keep serving the active generation while a newer projection rebuilds (minimum compatible version instead of an exact match) | M7 |
| 9 | Bounded retrieval: per-channel limit, `MetadataJson` read only for the page, totals shown as "500+" past the limit; measured with the relevance harness before and after | M5 |
| 10 | Paging stability (cursor carries an index-change stamp; client drops repeated rows) and did-you-mean token alignment | M9, M10 |
| 11 | Full rebuild streamed by module with batched writes; time and memory measured on a production-size copy | M8 |

L10, L12 and the remaining tests in L14 are picked up in the PR that touches the same code.

## Owner decisions (not in this plan)

- **Retire the Legacy engine.** After PR 8 and one release with the fallback counter from PR 5
  at or near zero, remove `Services/Search/*`, the gateway's Legacy branch, the shadow dispatcher
  and its table. About 1,300 lines.
- **Features a professional ERP search has and PRISM does not:** sort by newest; search from a
  module or project with the scope as a chip; the parent project on a card as a link;
  suggestions grouped by type ("See all 12 in IPR"); recently opened records on an empty box;
  per-type facts on cards (stage, filing number, FY); one shared search box for global search,
  Documents quick search and Photos.
