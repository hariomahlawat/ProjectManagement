# Search: fourth review (15 Nov 2026) and fix plan

The fourth review followed the third plan (`docs/ux/search-review-3-fixes.md`, all PRs merged).
It was done at `bb8292aa5`:

- **Code:** a reviewer who had not seen the earlier reviews read `Services/SearchV2/**`,
  `Services/Search/**`, the results page, its scripts and styles, the admin Search index page,
  the Search V2 triggers and the search tests.
- **Page:** about twenty varied searches live, and phone width, on the development data.

Every finding below was checked against the code.

**Verdict:** UX 8/10, code 8/10. Nothing High.

- **What is left:** excerpt quality and small edges; nothing structural.
- **Access:** no path shows a record the user may not open.
- **Tests:** all 378 search tests pass.

## Medium

| # | Finding | Where |
| --- | --- | --- |
| M1 | The excerpt window is anchored on a typed word found as a substring: "ir" inside "their", "art" inside "start". The window then starts at the top of the document, `ts_headline` finds no match in it and returns the opening words, and the card shows nothing marked and no "Found in". The C# excerpt choice (`FindFirstMatch`, `IndexOf`) has the same weakness. A gap in review 3's M2 fix | `first_hit` in `paged_results` (`SearchEngine.cs`); `SearchHighlightService.FindFirstMatch` |
| M2 | The minus operator works backwards. "simulator -drone" ranks a drone record first. The full-text channels honour the minus (`websearch_to_tsquery`), but the normalised query turns "-drone" into the word "drone", so the title channels (exact tokens, phrase, prefix, trigram) look for records with both words | `SearchQueryNormalizer.NormalizeExact`; title channels in `SearchEngine.cs` |
| M3 | On phones the marked word is usually cut off. The excerpt centres the match in about 300 characters, but phone cards show two lines (since review 2's PR 7), so the card says "Found in the document text" while the visible text shows no marked word | `SearchHighlightService.BuildSnippet` (match centred); `search.css` phone clamp |

## Low

| # | Finding |
| --- | --- |
| L1 | Did-you-mean is decided on the selected tab's first page; an empty tab can offer a "correction" for a word that finds results under All |
| L2 | The excerpt markers `<<` and `>>` are removed with a global replace, which also deletes real `<<` and `>>` in documents |
| L3 | `FailAsync` and `ReleaseAsync` ignore the lease token: a record re-queued during a failing attempt is counted as a failure (and after five, waits for an administrator); a release after a re-queue leaves the start stamp, holding the item until the lease ends |
| L4 | An alias expansion containing `$` corrupts the alias query: it goes into `Regex.Replace` unescaped (`SearchAliasProvider.ReplacePhrase`). Aliases come only from the database |
| L5 | Re-indexing a project after a rename or stage change loads all its documents' OCR text at once; the full rebuild reads them 100 at a time |
| L6 | Focus rings are box-shadows with `outline: 0`, which Windows High Contrast removes, so keyboard focus is invisible there |
| L7 | A refused search's browser tab title says "0 results" while the page says "Type at least two letters or digits" |

## Build plan

Each PR keeps the current routes, configuration keys and stored data, and needs no migration.

| PR | Scope | Findings |
| --- | --- | --- |
| 1 | This plan | — |
| 2 | **Excerpts that show the match:** typed words anchor the window only as whole words (in SQL and in the C# excerpt choice); the excerpt starts just before the match instead of centring it, so two phone lines show it. Checked live at phone width; relevance harness before and after | M1, M3 |
| 3 | **Minus excludes:** a word typed with a leading minus is left out of the words searched for and highlighted, and records containing it (exact or English form) are excluded from results, counts and facets; a query of only exclusions is searched as typed. Also, from PRs 4 and 5 (same file): excerpt markers dropped (L2) and did-you-mean not offered on an empty tab of a search that found results (L1) | M2, L1, L2 |
| 4 | **Edges:** `FailAsync`/`ReleaseAsync` respect the lease token like `CompleteAsync`; alias expansions escaped. (L2 moved to PR 3) | L3, L4 |
| 5 | **Polish:** a project's documents re-indexed 100 at a time; focus rings visible in High Contrast on the search page; tab title for a refused search. (L1 moved to PR 3) | L5, L6, L7 |

PRs 2 and 3 both change `SearchEngine.cs`; PR 3 starts after PR 2 is merged.
