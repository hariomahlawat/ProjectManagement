# Search V2 Relevance & Production Convergence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Converge the implemented PRISM Search V2 into a trustworthy production-ready search experience by correcting query semantics, autocomplete/correction relevance, facets/navigation, result attribution, pagination stability, and operational/relevance verification infrastructure.

**Architecture:** Preserve the unified PostgreSQL SearchEntries projection and V1 fallback. Improve query normalization and retrieval in focused components, enrich the existing SearchEntry metadata rather than introducing another search subsystem, bind cursors to the active index generation, and keep all authorization filtering before ranking/faceting. Production validation that requires the real PostgreSQL corpus is delivered as executable benchmark/integration harnesses rather than guessed pass claims.

**Tech Stack:** .NET 8 Razor Pages, EF Core, PostgreSQL full-text search, pg_trgm, Bootstrap 5, vanilla JavaScript.

**Spec:** Approved Search V2 professional review and convergence priorities from the preceding conversation.

## Global Constraints
- Do not replace Search V2 architecture or add embeddings/LLM reranking/Elasticsearch.
- Preserve Legacy fallback and shadow mode.
- Never render search-source HTML through Html.Raw.
- Authorization must be applied before ranking, counts, snippets, suggestions, and corrections.
- Do not invent a 150-query relevance gold set without production-domain judgments; provide the evaluator/import format and enforce benchmark gates when a curated dataset is supplied.
- No destructive change to authoritative business records.

---

### Task 1: Query semantics and correction trust
**Files:** `Services/SearchV2/Query/SearchQueryNormalizer.cs`, `Services/SearchV2/Query/SearchEngine.cs`, `ProjectManagement.Tests/Search/SearchV2QueryTests.cs`, `tools/test-search-v2-contract.mjs`
- [ ] Add regression tests for multi-term alias expansion preserving mandatory non-alias terms.
- [ ] Add correction gating tests/source-contract checks: no correction for strong/non-fuzzy result sets; correction only for low-confidence single-token queries.
- [ ] Implement token/phrase-level expansion and conservative correction gating.

### Task 2: Navigational autocomplete
**Files:** `Services/SearchV2/Query/SearchEngine.cs`, `wwwroot/js/pages/search.js`, `wwwroot/css/pages/search.css`, tests
- [ ] Add tests/contracts for identifier prefix, any-title-token prefix, alias prefix, deterministic priorities, and bounded fuzzy fallback.
- [ ] Implement suggestion SQL that avoids narrative/OCR matching.
- [ ] Fix suggestion title/subtitle line layout and ARIA option semantics.

### Task 3: Facet/navigation correctness
**Files:** `Services/SearchV2/Indexing/SearchProjectionBuilder.cs`, `Areas/Common/Pages/Search/Index.cshtml`, query contracts/tests
- [ ] Fix Source Clear so it removes all Source parameters.
- [ ] Move Visits and Social Media into Trackers.
- [ ] Render category tabs in stable product order rather than count order.
- [ ] Preserve source facets count ordering.

### Task 4: Result explainability and related results
**Files:** `Services/SearchV2/Models/SearchContracts.cs`, `Services/SearchV2/Indexing/SearchProjectionBuilder.cs`, `Services/SearchV2/Query/SearchEngine.cs`, `Areas/Common/Pages/Search/Index.cshtml`
- [ ] Add field-aware metadata for Project Brief, Capability, Technical Specification, identifiers and OCR/body text.
- [ ] Resolve MatchedField using channel + metadata instead of generic Details/Content where possible.
- [ ] Return related-source counts for canonical clusters and render useful chips.

### Task 5: Stable pagination and bounded snippets
**Files:** `Services/SearchV2/Query/SearchCursorCodec.cs`, `Services/SearchV2/Query/SearchEngine.cs`, tests
- [ ] Bind cursor to active index generation as well as query/filter fingerprint.
- [ ] Stop selecting full NarrativeText for display rows; produce bounded database-side display text for SearchEngine results while retaining full text in the search vector/index.
- [ ] Keep first-page counts/facets stable and cursor invalidation fail-safe.

### Task 6: Search health and rollout controls
**Files:** `Services/SearchV2/SearchV2Options.cs`, `Services/SearchV2/Query/SearchGateway.cs`, `Services/Dashboard/SearchHealthService.cs`, `ViewModels/Dashboard/SearchHealthVm.cs`, `Pages/Dashboard/Partials/_SearchHealthWidget.cshtml`, `appsettings.json`
- [ ] Add optional ServeV2Users/ServeV2Roles allow-list while preserving global ServeV2.
- [ ] Add p50/p95/zero-result metrics and last reconciliation to Search Health using retained query logs.
- [ ] Hide shadow-mode implementation copy from ordinary users.

### Task 7: Production verification harness
**Files:** new `ProjectManagement.Tests/Search/SearchV2PostgresIntegrationTests.cs`, new `tools/search-v2-relevance-evaluator.mjs`, new `tools/search-v2-relevance-dataset.schema.json`, README/verification script
- [ ] Add opt-in PostgreSQL integration tests that run against a real configured PostgreSQL test database and skip clearly when not configured.
- [ ] Add relevance dataset schema and deterministic MRR/nDCG/Recall evaluator.
- [ ] Extend verification script to run source contracts, JS syntax, unit tests, and print explicit commands for PostgreSQL/benchmark gates.
