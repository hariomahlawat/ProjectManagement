# Search V2 Relevance & Quality Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Harden PRISM Search V2 so normalized title intent wins over loose OCR/body matches, every autocomplete candidate family remains discoverable after commit, snippets are clean, ranking is inspectable by authorized administrators, and the remaining search UX issues are closed.

**Architecture:** Preserve the existing SearchEntries PostgreSQL projection and tier-first RRF architecture. Add explicit title phrase/title token channels, lower narrative-only relevance, persist a deterministic text-quality factor in projection metadata, and use that quality in both body ranking and snippet selection. Keep internal `Trackers` category values stable while mapping them to `Records` for display.

**Tech Stack:** .NET 8 Razor Pages, EF Core, PostgreSQL FTS/pg_trgm, Bootstrap 5, vanilla JavaScript.

**Spec:** `docs/superpowers/specs/2026-08-29-search-v2-relevance-quality-hardening-design.md`

## Global Constraints
- No external search engine, embeddings, or LLM reranking.
- Authorization is applied before suggestions, ranking, facets, snippets and counts.
- Internal `Trackers` category key remains unchanged.
- Autocomplete returns at most six entity suggestions.
- Bump Search V2 projection version when metadata semantics change so stale projections cannot serve.

---

### Task 1: Query normalization and highlighting invariants
**Files:**
- Modify: `Services/SearchV2/Query/SearchQueryNormalizer.cs`
- Test: `ProjectManagement.Tests/Search/SearchV2QueryTests.cs`

**Interfaces:**
- Produces: `NormalizeExact(string)` treating Unicode dash punctuation and `_` as separators; `NormalizedSearchQuery.HighlightTerms` containing normalized tokens as well as original tokens.

- [x] Add tests proving `high-tech`, `HI–TECH`, `high_tech` normalize to `high tech` and `high-tech` can highlight `High Tech` through normalized token terms.
- [x] Run the focused query tests and verify the new tests fail on the existing implementation.
- [x] Implement Unicode dash normalization and normalized highlight-token merging.
- [x] Re-run focused tests and verify green on a .NET SDK host; perform source/static checks here if SDK is unavailable.

### Task 2: Text-quality scoring and snippet sanitation
**Files:**
- Create: `Services/SearchV2/Query/SearchTextQuality.cs`
- Modify: `Services/SearchV2/Query/SearchHighlightService.cs`
- Test: `ProjectManagement.Tests/Search/SearchV2QueryTests.cs`

**Interfaces:**
- Produces: `SearchTextQuality.Score(string?) -> double`, `SearchTextQuality.SanitizeForDisplay(string?) -> string`.

- [x] Add tests for replacement/control-character cleanup, preservation of ordinary punctuation, and suppression of very low-quality narrative snippets.
- [x] Verify the tests fail before the utility exists.
- [x] Implement bounded deterministic quality scoring and display sanitation.
- [x] Make snippet selection quality-aware without changing safe structured highlighting.
- [x] Re-run focused tests/static checks.

### Task 3: Projection quality metadata and index rebuild boundary
**Files:**
- Modify: `Services/SearchV2/Indexing/SearchProjectionBuilder.cs`
- Modify: `Services/SearchV2/SearchV2Options.cs`
- Test: `ProjectManagement.Tests/Search/SearchV2SourceContractTests.cs`

**Interfaces:**
- Produces: root metadata property `searchTextQuality` on every SearchProjection; `ProjectionVersion = 4`.

- [x] Add source-contract assertions for `searchTextQuality` metadata and projection version 4.
- [x] Verify they fail against the existing source.
- [x] Merge quality into the existing anonymous metadata JSON without nesting or changing `matchFields` paths.
- [x] Bump ProjectionVersion to force an atomic rebuild.
- [x] Re-run source-contract/static verification.

### Task 4: Tiered title-first relevance and autocomplete/full-search consistency
**Files:**
- Modify: `Services/SearchV2/Query/SearchEngine.cs`
- Test: `ProjectManagement.Tests/Search/SearchV2SourceContractTests.cs`

**Interfaces:**
- Produces full-search channels `identifier_prefix`, `title_phrase`, `alias_prefix`, `title_token_prefix` and `title_fuzzy`; narrative FTS is a lower tier and quality-weighted.

- [x] Add source-contract tests for normalized title phrase detection, committed-search title-prefix query use, `english_fts` lower tier, and `searchTextQuality` use in narrative ordering.
- [x] Verify tests fail before SQL changes.
- [x] Add `@prefixTsQuery` to committed search.
- [x] Add exact/prefix identifier and alias coverage plus a title-fuzzy channel so every autocomplete path has a committed-search counterpart.
- [x] Add `title_phrase` ahead of broad FTS and `title_token_prefix` with the same prefix semantics used by suggestions.
- [x] Demote narrative/English FTS and fuzzy fallback to lower tiers.
- [x] Quality-weight narrative ordering with safe JSON numeric parsing.
- [x] Keep canonical clustering, facets, authorization and cursor ordering unchanged.
- [x] Re-run source/static verification.

### Task 5: Autocomplete cap and user-facing result taxonomy
**Files:**
- Modify: `Services/SearchV2/SearchV2Options.cs`
- Modify: `Areas/Common/Pages/Search/Index.cshtml.cs`
- Modify: `Areas/Common/Pages/Search/Index.cshtml`
- Modify: `wwwroot/css/pages/search.css`
- Test: `ProjectManagement.Tests/Search/SearchV2SourceContractTests.cs`

**Interfaces:**
- Produces: six entity suggestions; display helper maps `Trackers` to `Records`; All tab displays `TotalHits`.

- [x] Add source-contract assertions for six-suggestion requests, Records display mapping, and All count.
- [x] Verify failures.
- [x] Set default/requested suggestion limit to six.
- [x] Add category-label helper and use it in tabs/breadcrumbs.
- [x] Add count badge to All.
- [x] Offset the sticky search header below the PRISM 52 px navigation shell, with a 50 px mobile fallback.
- [x] Keep `Category=Trackers` URLs unchanged.
- [x] Re-run static verification.

### Task 6: Authorized ranking diagnostics
**Files:**
- Modify: `Services/SearchV2/Models/SearchContracts.cs`
- Modify: `Services/SearchV2/Query/SearchEngine.cs`
- Modify: `Areas/Admin/Pages/Diagnostics/SearchIndex.cshtml.cs`
- Modify: `Areas/Admin/Pages/Diagnostics/SearchIndex.cshtml`
- Test: `ProjectManagement.Tests/Search/SearchV2SourceContractTests.cs`

**Interfaces:**
- Produces optional `SearchResult.MatchTier` / `MatchChannels` diagnostics and an admin-only Ranking Inspector that runs the real Search V2 engine under the current user's authorization context.

- [x] Add source-contract assertions for diagnostic tier/channel propagation and the inspector UI/backend.
- [x] Verify the contract fails before the inspector exists.
- [x] Preserve rank tier/channels on V2 SearchResult while keeping legacy constructor compatibility through optional fields.
- [x] Add a query inspector to Search Index diagnostics showing top ten rank, source, matched field, tier, channels and RRF score.
- [x] Ensure inspector calls SearchEngine directly so it does not write ordinary query analytics and still applies authorization.
- [x] Re-run static verification.

### Task 7: Verification and delivery packaging
**Files:**
- Modify: `README-SEARCH-V2-CONVERGENCE.md`
- Create/update: `SEARCH-V2-QUALITY-HARDENING-STATIC-VERIFICATION.txt`

- [x] Run available Node/search contract checks and syntax-oriented static checks.
- [x] Search for stale `SuggestAsync(q, User, 8` and direct `@facet.Value` tracker display.
- [x] Verify no `Html.Raw` was introduced for results/snippets.
- [x] Document that a projection rebuild is automatic because ProjectionVersion changed from 3 to 4.
- [x] Package only the ready-to-paste changed/new files preserving project-relative paths, plus a complete patched-source ZIP.
