# Search: fifth review (16 Nov 2026) and fix plan

The fifth review followed the fourth plan (`docs/ux/search-review-4-fixes.md`, all PRs merged)
and the project covers on results (#3289). It was done at `341a0101a`:

- **Code:** a reviewer who had not seen the earlier reviews read `Services/SearchV2/**`,
  `Services/Search/**`, the results page, its scripts and styles, the admin Search index page
  and the search tests.
- **Ways in:** the layout, the Menu (`RoleBasedNavigationProvider`) and the top-bar search
  script, checked on a running app.
- **Coverage:** which PRISM records the index holds, against the modules in the Menu.

Every finding below was checked against the code.

**Verdict:** code 8/10. Nothing High.

- **Access:** no path shows a record the user may not open. Results, suggestions, the
  did-you-mean vocabulary, project filter names and related counts all go through
  `SearchSourcePolicies`.
- **Not looked at again:** the page's look. It was checked at desktop and phone width after
  the fourth plan, and covers were checked with #3289.

## Ways in

The "Search PRISM" box is in the top bar of every page except the search page. Ctrl K or `/`
focuses it. On phones it is a magnifier that opens the search page. This follows common
practice: Google Drive, GitHub, Jira and SharePoint do the same.

Two gaps:

- **Not in the Menu.** Someone who looks for Search in the Menu does not find it, though
  the Menu's "What's new" note says every area is there.
- **No way to the empty search page on desktop.** An empty top-bar search is held back on
  purpose (`search-combobox.js`). The empty search page only shows the box and recent
  searches, and the top-bar box shows those too, so little is lost. But there is no link to it.

## Coverage

**IT, MOLTI and SRE projects are not searchable.** They live in the `ItProjects` table and
appear in the Menu under Projects, but no search source reads them. A title or IT PPP
number finds nothing. The index covers projects, project documents, the document
repository, FFC, IPR, activities, visits, social media, training, ToT, Proliferation and ARPP.

## Medium

| # | Finding | Where |
| --- | --- | --- |
| M1 | **"No results" when search is broken.** The basic engine catches every provider's error and returns an empty list. So when PostgreSQL is down, the page says "No results for X" instead of "Search is unavailable", which `docs/ux/search.md` rules out. The failure-state test misses it because its fake basic engine throws, and the real one never does | `GlobalSearchService.SafeSearchAsync`; `SearchGateway.ReadLegacyAuthorizedAsync` |
| M2 | **A stale row can be written last.** Re-indexing a project also rewrites its Training, FFC, ARPP, IPR, ToT and Proliferation rows; a ToT or IPR change rewrites its projects. Those rows are written without their own work-item lease, and incremental passes take only a shared lock, so two processes run side by side (rolling deploy). One can write a row from a read taken before the other's newer change: a just-rejected Proliferation entry comes back until the nightly rebuild. `docs/search-and-ocr-architecture.md` says "No lost updates" | `SearchIndexWorker.IndexWithDependentsAsync`, `IndexWithItsProjectsAsync`, `ReplaceAsync`; `SearchIndexCoordinator` |

## Low

| # | Finding |
| --- | --- |
| L1 | When search falls back to the basic engine, "-drone" is searched for, not excluded: the gateway passes the typed text, not `Searched` |
| L2 | A hand-edited URL with two categories filters on both, but only the first tab looks selected; a source value in another case ("ipr") matches nothing, as the SQL `IN` is case-sensitive |
| L3 | Under the basic engine, project, status, stage and date filters from the URL are listed as "not applied", but there is no way to remove them except editing the URL, and tab links carry them on |
| L4 | A related-records chip counts the record's whole cluster before filters, but its link opens source plus parent project, so the number can differ from the list it opens |
| L5 | Broad searches have never been measured on production-sized data. The 10-second statement limit bounds them, then the basic engine serves |
| L6 | The shadow comparison stores the basic engine's titles and URLs for sampled searches; confirm only administrators can read them |

## Build plan

Each PR keeps the current routes, configuration keys and stored data. Only PR 6 needs a
migration, and that one is additive (a trigger on the IT projects tables).

| PR | Scope | Findings |
| --- | --- | --- |
| 1 | This plan | — |
| 2 | **Search in the Menu:** "Search" first under Main, opening the search page, for every signed-in user. Shown as the current item on the search page | Ways in |
| 3 | **Unavailable, not empty:** the basic engine reports how many sources failed. When all of them fail, the page says search is unavailable, whether the basic engine is the fallback or the configured engine. When some fail, the results are marked partial. Tested with the real basic engine and failing sources | M1 |
| 4 | **No stale rows across processes:** each record's projection is read and written under a per-record transaction lock (`pg_advisory_xact_lock` on type and key), so a process that reads after another's change also writes after it. Covers the dependent rows. Two-process test on PostgreSQL. Architecture doc corrected | M2 |
| 5 | **Edges:** the basic engine gets the searched text and drops records containing an excluded word. One category is bound, and sources are compared ignoring case. A "Remove these filters" link appears in the basic-engine notice. Related-chip counts follow the link (source and parent project). Shadow-table access confirmed or limited to administrators | L1–L4, L6 |
| 6 | **IT, MOLTI and SRE projects in search:** a new source labelled "IT projects", "MOLTI projects" or "SRE projects" by module, under the Projects tab. It is found by title, IT PPP number, project officer and description, and shows stage and financial year. It needs `Policies.ItProjects.View`, the rule its pages use, for results, suggestions and counts. Trashed projects are left out. An additive migration installs the Search V2 trigger on `ItProjects` and its stage table, and `ProjectionVersion` is raised so the next rebuild adds them. The basic engine gets the same source. Never expand MOLTI or SRE | Coverage |

PRs 3 and 5 both change `SearchGateway.cs`, so PR 5 starts after PR 3 is merged. PR 6 starts
after PR 4 is merged, as both change the index worker's path.

L5 stays a note: measure a broad search on the production server before the data grows, and
record the EXPLAIN in `docs/search-and-ocr-architecture.md`.
