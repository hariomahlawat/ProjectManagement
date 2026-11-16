# Search: sixth review (16 Nov 2026) and fix plan

The sixth review followed the fifth plan (`docs/ux/search-review-5-fixes.md`, all PRs merged).
It was done at `5b69be371`:

- **Code:** a reviewer who had not seen the earlier reviews read `Services/SearchV2/**`,
  `Services/Search/**`, the results page, its scripts and styles, the search migrations and
  the search tests.
- **Page:** twenty varied searches and the suggestion list, live on a copy of the
  development data, as a signed-in test user.

Every finding below was checked against the code.

**Verdict:** UX 8/10, code 8/10. Nothing High.

- **Access:** no path shows a record the user may not open. Results, counts, related chips,
  facets, suggestions and did-you-mean all go through the same authorization; the basic
  engine filters deny-by-default.
- **Live:** typos ("simulatr"), reference numbers ("IT PPP 07/2026"), short names
  ("drona"), phrases, the minus, IT/MOLTI/SRE projects and the "too short" state all
  behaved. Most searches took 150–600 ms; the first after start took 1.3 s.
- **Not issues:** the tab counts add up to more than All on purpose (All folds linked
  records under their project; its tooltip says so). Two-letter searches ("ab") return
  mostly document noise, as expected for so little text.

Most of what is left is in the basic (Legacy) engine, which serves only when Search V2
cannot.

## Medium

| # | Finding | Where |
| --- | --- | --- |
| M1 | **A source filter can empty the basic engine's results.** The engines name four sources differently ("ToT" vs "TOT tracker", "Visits" vs "Visits tracker", "Social media" vs "Social media tracker", "Proliferation" vs "Proliferation survey"), and ARPP has no basic-engine source. A Source filter picked under V2 then removes every basic-engine hit: 0 results, the chip still shown, and the notice does not list Source as not applied | `SearchGateway.ApplyLegacyFilters`; source labels in `GlobalProjectReportsSearchService` |
| M2 | **The minus empties two basic-engine sources.** The IPR and IT/MOLTI/SRE providers match the whole typed text with `ILIKE`, so "cabling -fibre" looks for the literal "cabling -fibre" and finds nothing; records matching "cabling" are lost. (Sources using `websearch_to_tsquery` handle the minus correctly, which is why #3319 kept the typed text.) | `GlobalIprSearchService`, `GlobalItProjectSearchService`; `SearchGateway` |

## Low

| # | Finding |
| --- | --- |
| L1 | The IT projects trigger migration's comment says the previous build *fails* `ItProject` work items; it actually completes them as empty (harmless: the version-11 rebuild re-reads the tables). The migration is shipped and is not edited; the architecture doc states the real behaviour |
| L2 | During the first rolling deploy of the record locks (#3313) the old process writes without them, so the stale-write race remains for that overlap. A hash collision between a ToT/IPR key and its project's key would make that item wait for its own lock (on a second connection, invisible to deadlock detection) until the 60-second timeout, on every retry |
| L3 | The basic engine's minus check is too broad and can fail: "-art" also drops "artillery" (prefix match); "-T-90" is normalised to "t 90" and never matches "T-90"; a regex timeout is not caught (a 500) |
| L4 | The related-chip counts use `COUNT(DISTINCT (row))` over every candidate, which the engine's own comment avoids (PostgreSQL sorts by text collation), and are computed for every cluster, not just the page |
| L5 | A related chip's count is per project (`MAX(ParentProjectId)` over the cluster) while its link uses the representative hit's project, which can be empty; then count and list differ |
| L6 | A Project Officer's new name or rank reaches IT/MOLTI/SRE results only at the nightly rebuild: nothing queues their projects when the user record changes |
| L7 | Basic-engine IT/MOLTI/SRE results put the whole description in the excerpt, not the part around the match |
| L8 | Dead code in the basic-engine path: the "Project portfolio" category entry (no source emits it), and the access lookup is left unobserved when the search fails |
| L9 | Suggestions: a title that contains the word in brackets can come before titles that start with it ("…SIMULATOR (DRONA)…" above "DRONA - MK-3") |

## Build plan

Each PR keeps the current routes, configuration keys and stored data. Only PR 4 needs a
migration, and that one is additive (a trigger on the users table).

| PR | Scope | Findings |
| --- | --- | --- |
| 1 | This plan | — |
| 2 | **Basic engine:** source filters translated to the basic engine's names, and a Source it does not have (ARPP) listed as not applied instead of emptying the list; the `ILIKE` providers get the text without exclusions; the minus check matches whole words and their usual endings, as typed (hyphens kept), and keeps the hits on a timeout; IT/MOLTI/SRE excerpts start near the match; dead entries removed and the access lookup always observed | M1, M2, L3, L7, L8 |
| 3 | **Related chips:** counts with `SELECT DISTINCT` subqueries, only for the page's clusters; the engine returns the project the chip filters on, and the page builds the link from it | L4, L5 |
| 4 | **Indexing edges:** a migration adds a trigger that queues an officer's IT/MOLTI/SRE projects when their name or rank changes; a record lock whose hash equals one already held by the pass is not taken twice; the architecture doc covers the rollout overlap and the real old-build behaviour of `ItProject` items | L1, L2, L6 |
| 5 | **Suggestions:** titles that start with the typed word come first | L9 |

PRs 2 and 3 both change `SearchGateway.cs` or `SearchEngine.cs` in different places; PR 3
starts after PR 2 is merged.
