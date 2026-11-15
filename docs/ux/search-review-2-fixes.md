# Search: second review (15 Nov 2026) and fix plan

The second review followed the first plan (`docs/ux/search-review-fixes.md`, all 11 PRs merged).
It was done at `e3837169e`:

- **Code:** a reviewer who had not seen the first review read `Services/SearchV2/**`,
  `Services/Search/**`, the results page, `search.js`, `search-combobox.js`, `search.css`, the
  admin Search index page, the Search V2 triggers and the search tests.
- **Page:** checked live at 1366 px and 375 px on the development data.

Every finding below was checked against the code.

**Verdict:** UX 7.5/10, code 7.5/10. Nothing High.

- **Access:** hits, counts, facets, suggestions, did-you-mean and snippets never show a record
  the user may not open.
- **Index and paging:** paging, leases, the cross-process lock and the rebuild hold up when they
  run at the same time.
- **Tests:** all 342 search tests pass.

## Medium

| # | Finding | Where |
| --- | --- | --- |
| M1 | Search works out a project's current stage with its own rule: the in-progress stage with the highest order, otherwise the first not-started one, otherwise none. The app's rule is different: the first in-progress stage, then the first stage that is not completed or skipped, then the last stage. So a project whose current stage is Blocked is indexed under the next stage, and a completed project has no stage. The wrong stage reaches the Stage facet and filter, "Found in Lifecycle Stage", and the ToT, IPR, Training, FFC, ARPP and Proliferation rows that copy it | two copies in `SearchProjectionBuilder.cs` (project rows about line 272, dependants about line 1104); the app's rule is `PresentStageHelper.ResolveOrdered` |
| M2 | Any change to a project re-indexes every document of that project, OCR text included, and every dependent row. The `Projects` and `ProjectStages` triggers fire on any column, and a `Project` work item always rebuilds the project's documents. A project with hundreds of scanned documents re-reads tens of MB for one stage update. Not measured on the server | `TR_SearchV2_Projects` / `TR_SearchV2_ProjectStages` in `20261216200000_AddSearchV2Foundation.cs`; `BuildEntityAsync("Project")`; `SearchIndexStore.ReplaceEntityAsync` |
| M3 | A three-letter search with no exact match ("drn") shows "123 results", all from the fuzzy channels: at the 0.5 word-similarity threshold a 3-letter token matches every word starting with the same two letters. The page does not say that nothing matched exactly | `SearchQueryTerms.Create` (fuzzy tokens of 3+ letters); `fuzzy_matches` in `SearchEngine.cs`; `Index.cshtml` |
| M4 | ToT status reaches result cards as the enum name: "ToT NotRequired", "ToT InProgress", "ToT NotKnown". Other pages already show "ToT not required" | `SearchProjectionBuilder.cs` about line 260 (`$"ToT {row.Status}"`); the IPR summary already reads well ("IPR Patent Granted") |

## Low

| # | Finding |
| --- | --- |
| L1 | In basic (Legacy) mode activities land in the Records tab: the gateway looks for "Activity" in the source label, which is "Activities" (`SearchGateway.cs` about line 463) |
| L2 | Basic mode still finds Proliferation entries of trashed or archived projects (`GlobalProjectReportsSearchService.cs`, approval status only) |
| L3 | After a 10 s search time-out, the basic engine runs with no time limit of its own; the wait can reach about 40 s |
| L4 | Did-you-mean shows the lower-case form ("hyderabad radar" for "Hydrabad Radar") |
| L5 | On a tab such as Documents, an exact match outside the tab turns off the near-miss matches for the tab (`strong_candidate_count` counts before filters), so the tab can show No results |
| L6 | The date filter starts and ends at UTC midnight and dates are shown in server local time; the rest of PRISM uses IST |
| L7 | The selected category tab disappears when a filter leaves it empty, and no tab shows as selected |
| L8 | If the rebuild after an upgrade keeps failing, searches keep reading the older index, which gets no updates; only the admin page shows the error |
| L9 | Suggestions score every candidate on each keystroke; there is no cap per candidate branch |
| L10 | The click-logging handler accepts unlimited rows from any signed-in user |
| L11 | Result cards show the status twice: under the title ("Completed") and again as a pill. On a phone a card is about 430 px tall, so two results fit on a screen |
| L12 | Dead code: "My Work" and "Media" in the category order; the offset cursor methods of `ISearchCursorCodec` |

## Build plan

Each PR keeps the current routes, configuration keys and stored data, and needs no migration
unless it says so. Index changes bump `ProjectionVersion`. The previous index keeps answering
while the new one is built (`MinimumServableProjectionVersion`).

| PR | Scope | Findings |
| --- | --- | --- |
| 1 | This plan | — |
| 2 | **Index facts from the shared rules:** stage from `PresentStageHelper` for project rows and every dependant (one helper, not two); ToT status in the words other pages use; `ProjectionVersion` 9; tests for a blocked stage, a completed project and each ToT status | M1, M4 |
| 3 | **Project edits stop rewriting documents.** A `Project` work item rebuilds the project row and its dependants, and rewrites document rows only when the project facts they copy changed: name, case number, categories, stage, lifecycle, trash or archive. A fingerprint of those facts is kept in each document's metadata. Re-indexing time measured before and after on a project with many documents | M2 |
| 4 | **Near-miss matching:** no fuzzy matching below four letters; when every result comes from near-miss matching, the page says "No exact matches. Showing similar wording." Near-miss matches are turned off only by exact matches inside the active filters. Relevance harness run before and after | M3, L5 |
| 5 | **Did-you-mean and dates:** the suggestion keeps the typed capitals; date filter bounds and shown dates in IST (`TimeZoneHelper`) | L4, L6 |
| 6 | **Basic engine fixes:** Activities map to Organisation by exact label; Proliferation uses the counted-projects rule; a 10 s limit when the basic engine runs as fallback | L1, L2, L3 |
| 7 | **Page:** the selected tab always shows (count 0 when empty); status shown once per card; tighter cards on phones; dead category names removed | L7, L11, L12 |
| 8 | **Operations:** a failing upgrade rebuild shows in the admin "Last 24 hours" panel and the health dashboard; a cap of 200 candidates per suggestion branch; click logging limited per user; the unused offset cursor methods removed | L8, L9, L10, L12 |

PRs 2 and 3 both change the index builder; PR 3 starts after PR 2 is merged. The others touch
separate code and can go in any order.

## Owner decisions (not in this plan)

- **Retire the basic (Legacy) engine** when the fallback count on the admin panel stays at or near
  zero for a release. PR 6 only keeps it correct until then.
- **Features** from the first plan are still open: sort by newest, scoped search, grouped
  suggestions, the parent project as a link on a card.
