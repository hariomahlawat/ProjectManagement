# Search: third review (15 Nov 2026) and fix plan

The third review followed the second plan (`docs/ux/search-review-2-fixes.md`, all 8 PRs merged).
It was done at `4b4664010`:

- **Code:** a reviewer who had not seen the earlier reviews read `Services/SearchV2/**`,
  `Services/Search/**`, the results page, `search.js`, `search-combobox.js`, `search.css`, the
  admin Search index page, the Search V2 triggers and the search tests.
- **Page:** checked live at 1366 px on the development data.

Every finding below was checked against the code.

**Verdict:** UX 8/10, code 8/10. Nothing High.

- **Access:** no path shows a record the user may not open.
- **Earlier fixes:** no regressions found.
- **Tests:** all 365 search tests pass.

## Medium

| # | Finding | Where |
| --- | --- | --- |
| M1 | An index update can be lost while two processes run at once (a rolling deployment, as AGENTS.md says to assume). The enqueue trigger's `ON CONFLICT … DO UPDATE SET "Status" = 0` also resets an item a worker is holding (`Status` 1). A worker in the other process then takes it, reads the new data, writes it and deletes the item; the first worker, holding the old data, writes last and finds no item to return to Pending. The index keeps old data until the daily check. The first plan's `xmin` lease fixed the single-process case only | `search_v2_enqueue` in `20261216200000_AddSearchV2Foundation.cs`; `SearchIndexStore.EnqueueAsync` |
| M2 | Many document results give no reason for matching. For "acceptance report", 15 of the first 20 results on the development data showed no highlighted word and no "Found in" line, only the metadata line. They matched by English stems in the document text ("accepted", "reports"). The text excerpt is a window around the first literal hit of the longest typed word, highlighted with the `simple` parser. With no literal hit the window is the start of the document and nothing is highlighted, so the card falls back to metadata | `paged_results` (`ts_headline('simple', …)`, `@snippetAnchor`) in `SearchEngine.cs`; `SearchHighlightService.ChooseSource`; `SearchMatchEvidenceResolver` |

## Low

| # | Finding |
| --- | --- |
| L1 | Each Show more page, tab switch and filter change writes a query-log row, so the admin "Last 24 hours" search count is inflated and the found-nothing share diluted (`SearchGateway.SearchAsync`) |
| L2 | Recent searches keep searches that were refused (one letter, "o") or found nothing ("drn") |
| L3 | `SearchIndexStore.IsReadyAsync` has no production caller and still requires the exact projection version, against the serve-the-previous-index rule; `RecordReconciliationAsync` is never called; `EnqueueAsync` is used only by tests |
| L4 | The suggestion query repeats its ranking order 11 times; the 200-record cap keeps the best suggestions only while every copy matches the final ranking |
| L5 | The index decides which Document repository documents are searchable with its own `IsActive && !IsDeleted` instead of the shared `DocRepoAccess.ReadableFilter` |
| L6 | ARPP rows can carry a trashed project's name in their terms, text and "Project" match field (`BuildArppAsync`), as IPR and Training did before the first plan |
| L7 | A digits-only search runs a regular expression over every identifier term (`identifier_number` channel), with no index. Not measured at production size |

## Build plan

Each PR keeps the current routes, configuration keys and stored data. Index changes bump
`ProjectionVersion`; the previous index keeps answering while the new one is built.

| PR | Scope | Findings |
| --- | --- | --- |
| 1 | This plan | — |
| 2 | **No lost update across processes:** the dequeue skips a Pending item still stamped as started within the lease (the trigger re-queued it while a worker holds it); the holder's completion or the lease end releases it. Done in code instead of the planned migration, so rolling back to an older build stays possible. A Postgres test of two workers | M1 |
| 3 | **Why a document matched:** the text excerpt and its highlights use the same English matching that found the document: the window is anchored on the first stemmed match, `ts_headline` uses the English parser, and the card says "Found in the document text" when that is where the match is. Checked live on "acceptance report"; relevance harness before and after | M2 |
| 4 | **Usage and recent searches:** Show more pages are not logged as new searches; tab and filter changes of the same query neither; recent searches keep only searches that ran and found something | L1, L2 |
| 5 | **Shared rules and clean-up:** Document repository visibility from `DocRepoAccess`; ARPP ignores trashed projects (`ProjectionVersion` 10); the unused store methods removed; the suggestion branches built from one C# template | L3–L6 |
| 6 | **Digits-only searches:** measured 15 Nov on a 34,160-entry copy of the development index: the `identifier_number` channel took 2.2 ms of a 96 ms search for "42" (3,040 identifier terms). Not worth an index; closed without a code change. Recorded here in PR 5 | L7 |

PRs 3 and 5 both touch `SearchEngine.cs`; PR 5 starts after PR 3 is merged. The others can go in
any order.

## Owner decisions (not in this plan)

- **Whether production ever runs two processes** outside deployments. M1 matters most if it does;
  PR 2 is right either way.
- **Retire the basic (Legacy) engine**, and the features listed in the first plan, are still open.
