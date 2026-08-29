# PRISM Global Search V2 Ranking Precision, Evidence & Performance Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve Search V2 lexical precision, explainability, filter-state UX and common-query performance without changing the converged Search V2 architecture.

**Architecture:** Extend the existing PostgreSQL channel model with exact-token and alias-title channels, retain tier-first/RRF fusion and canonical clustering, derive user-facing evidence from term-level field coverage, and lazy-load detailed advanced facets. Fuzzy channels become true fallback channels based on the strong lexical candidate pool.

**Tech Stack:** .NET 8 Razor Pages, C#, PostgreSQL FTS/pg_trgm, EF Core database connection, vanilla JavaScript, CSS, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-29-search-v2-ranking-evidence-performance-design.md`

## Global Constraints
- No database schema migration.
- Keep Search V2 ProjectionVersion at 4.
- Preserve authorization filtering and Legacy fallback semantics.
- Preserve canonical clustering and overlapping facet semantics.
- No vector/semantic search or external search dependency.
- Production diagnostics remain non-user-facing.

---

### Task 1: Ranking precision and controlled aliases
**Files:**
- Modify: `Services/SearchV2/Query/SearchEngine.cs`
- Modify: `Services/SearchV2/Query/SearchAliasProvider.cs`
- Modify: `Services/SearchV2/SearchV2Options.cs`
- Test: `ProjectManagement.Tests/Search/SearchV2QueryTests.cs`
- Test: `ProjectManagement.Tests/Search/SearchV2SourceContractTests.cs`
- Test: `ProjectManagement.Tests/Search/SearchV2PostgresIntegrationTests.cs`

**Interfaces:**
- Produces exact whole-token title query/channel, controlled built-in alias rules, alias-title phrase channel, canonical tie-breaker and fuzzy fallback threshold.

- [x] Add failing tests for built-in high-tech aliases and exact-token/prefix ordering contracts.
- [x] Verify the new tests fail against the current implementation.
- [x] Implement built-in alias merging in `SearchAliasProvider` without replacing database aliases.
- [x] Add exact title-token query generation and `title_tokens_exact` channel before `title_token_prefix`.
- [x] Add alias title-phrase channel from normalized alias expansions.
- [x] Add modest canonical-entity fusion boost within the same match tier.
- [x] Gate fuzzy channels when the strong lexical candidate pool meets the configured threshold.
- [x] Add PostgreSQL primitive/regression coverage for exact tokens vs prefixes and fuzzy fallback gating.

### Task 2: Compositional match evidence and whole-word highlighting
**Files:**
- Create: `Services/SearchV2/Query/SearchMatchEvidence.cs`
- Modify: `Services/SearchV2/Query/SearchEngine.cs`
- Modify: `Services/SearchV2/Query/SearchHighlightService.cs`
- Modify: `Areas/Common/Pages/Search/Index.cshtml`
- Test: `ProjectManagement.Tests/Search/SearchV2QueryTests.cs`

**Interfaces:**
- Produces `SearchMatchEvidenceResolver.Resolve(...)` returning a concise user-facing field label.
- `ISearchHighlightService.Highlight(...)` continues returning structured `SearchTextSegment` values, but expands a matching query prefix to the complete lexical word.

- [x] Add failing tests for composite Title + document-text evidence and whole-word `tech` → `Technology` highlighting.
- [x] Verify failures.
- [x] Implement term-aware field coverage in `SearchMatchEvidenceResolver`.
- [x] Wire SearchEngine result conversion to the evidence resolver.
- [x] Update highlighting regex to highlight complete lexical words for prefix matches while remaining markup-safe.
- [x] Replace the green success icon with a neutral search icon.

### Task 3: Lazy detailed facets and filter-state semantics
**Files:**
- Modify: `Services/SearchV2/Models/SearchContracts.cs`
- Modify: `Services/SearchV2/Query/SearchEngine.cs`
- Modify: `Areas/Common/Pages/Search/Index.cshtml.cs`
- Modify: `Areas/Common/Pages/Search/Index.cshtml`
- Modify: `wwwroot/js/pages/search.js`
- Modify: `wwwroot/css/pages/search.css`
- Test: `ProjectManagement.Tests/Search/SearchV2SourceContractTests.cs`
- Test: `tools/test-search-v2-contract.mjs`

**Interfaces:**
- `SearchRequest.IncludeDetailedFacets` defaults to true for compatibility; page initial searches set it false unless advanced filters are active.
- `SearchRequest.FacetsOnly` allows the lazy facet endpoint to skip paged result/snippet work.
- `SearchFacets.DetailedLoaded` tells the page whether server-side detailed facets are present.
- Razor handler `OnGetFacetsAsync` returns authorized facet JSON for the current query/filter state.

- [x] Add failing source-contract tests for lazy facet endpoint, request flags and selected-count-only section badges.
- [x] Verify failures.
- [x] Add request/facet contract flags and condition detailed facet summary references in SearchEngine.
- [x] Add facet-only execution mode.
- [x] Make normal page searches skip detailed facets unless advanced filters are already active.
- [x] Add the authorized Facets handler.
- [x] Add DOM-safe lazy facet rendering and selected-count badge updates in JavaScript.
- [x] Change facet section badges to active-selection counts only and style Filters active-count badge.
- [x] Preserve active filter chips and explicit Apply/Clear all.

### Task 4: Regression, source validation and packaging
**Files:**
- Modify: `ProjectManagement.Tests/Search/SearchV2QueryTests.cs`
- Modify: `ProjectManagement.Tests/Search/SearchV2SourceContractTests.cs`
- Modify: `ProjectManagement.Tests/Search/SearchV2PostgresIntegrationTests.cs`
- Modify: `tools/test-search-v2-contract.mjs`
- Create: `README-SEARCH-V2-RANKING-EVIDENCE-PERFORMANCE.md`

- [x] Run the Search V2 JavaScript/source-contract checks.
- [x] Run JavaScript syntax validation.
- [x] Attempt `dotnet build` and xUnit tests; if SDK is unavailable, record the limitation explicitly.
- [x] Verify no migration or ProjectionVersion bump was introduced.
- [x] Build a changed-files overlay ZIP, full patched project ZIP, unified patch and SHA-256 manifest.
- [x] Reapply the patch to a fresh current baseline and verify byte-for-byte reproduction of changed files.
