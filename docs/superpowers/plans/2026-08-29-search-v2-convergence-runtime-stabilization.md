# Search V2 Convergence & Runtime Stabilization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make committed PRISM searches reliably serve Search V2, expose safe fallback diagnostics, keep All/category counts semantically correct, and remove stale Search CSS collisions.

**Architecture:** Replace the invalid two-statement CTE query with one statement containing summary/facet and paged-result CTEs. Carry separate global/current counts through V2 and Legacy gateway contracts, add typed execution/fallback status, then make `search.css` the sole owner of `.pm-gs-*` styling.

**Tech Stack:** .NET 8, ASP.NET Core Razor Pages, EF Core 8, Npgsql/PostgreSQL FTS + pg_trgm, Bootstrap 5, vanilla JavaScript/CSS, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-29-search-v2-convergence-runtime-stabilization-design.md`

## Global Constraints
- No EF migration.
- ProjectionVersion remains 4.
- Legacy remains resilience fallback only.
- No relevance-weight tuning or new search features.
- Authorization must apply before counts/results/facets are exposed.
- Production UI must not expose exception text.

---

### Task 1: Lock the convergence regressions

**Files:**
- Modify: `ProjectManagement.Tests/Search/SearchV2SourceContractTests.cs`
- Modify: `tools/test-search-v2-contract.mjs`

**Interfaces:**
- Consumes: current SearchEngine/Gateway/View/CSS source.
- Produces: regressions proving single-statement CTE scope, explicit filtered/global counts, fallback status, and CSS ownership.

- [x] Add tests asserting SearchEngine no longer uses `reader.NextResultAsync`/a second `SELECT ... FROM ranked` and instead returns summary + paged rows from one statement.
- [x] Add tests asserting `FilteredHits` and safe V2 execution/fallback status exist in contracts/gateway.
- [x] Add tests asserting All badge uses global count while result heading uses filtered count.
- [x] Add tests asserting the legacy Global Search block no longer exists in `site.css`.
- [x] Run `node tools/test-search-v2-contract.mjs` and confirm RED on the new assertions.

### Task 2: Fix committed Search V2 SQL and execution status

**Files:**
- Modify: `Services/SearchV2/Models/SearchContracts.cs`
- Modify: `Services/SearchV2/Query/SearchEngine.cs`

**Interfaces:**
- Produces: `SearchV2ExecutionStatus`, `SearchResponse.FilteredHits`, `SearchResponse.ExecutionStatus`, `SearchResponse.DiagnosticId`.

- [x] Add typed status values `Success`, `Disabled`, `IndexNotReady`, `QueryFailed`.
- [x] Return Disabled/IndexNotReady explicitly before query execution.
- [x] Reshape BuildSearchSql into one statement with `summary`, `paged_results`, and a left join that yields one summary-only row when there are no results.
- [x] Parse summary once and rows only when `Id` is non-null; remove `NextResultAsync` logic.
- [x] Compute global `TotalHits` from category-filter-excluded canonical candidates; compute `FilteredHits` from `ranked`.
- [x] Make `projectStages` JSON expansion type-safe to avoid scalar/null JSON runtime failures.
- [x] On query failure, log structured failure ID/query fingerprint/projection/filter context and return `QueryFailed` without exposing exception text.
- [x] Run source contract tests and verify GREEN for Task 2 assertions.

### Task 3: Make fallback explicit and count semantics consistent

**Files:**
- Modify: `Services/SearchV2/Models/SearchContracts.cs`
- Modify: `Services/SearchV2/Query/SearchGateway.cs`
- Modify: `ProjectManagement.Tests/Search/SearchV2SourceContractTests.cs`

**Interfaces:**
- Produces: `SearchGatewayResponse.FilteredHits`, `FellBackToLegacy`, `V2ExecutionStatus`, `V2DiagnosticId`, `EngineLabel`.

- [x] Adapt successful V2 responses preserving global/current counts and status.
- [x] For Legacy, compute All count excluding category but respecting source filters; compute current count after category/source filters.
- [x] Build Legacy category/source facets using disjunctive scopes consistent with their selected counterpart.
- [x] Emit structured warning when a served V2 request falls back, including status and diagnostic ID.
- [x] Keep shadow comparison observational.
- [x] Run source contract tests and verify GREEN for gateway/count assertions.

### Task 4: Surface safe development diagnostics and correct count rendering

**Files:**
- Modify: `Areas/Common/Pages/Search/Index.cshtml.cs`
- Modify: `Areas/Common/Pages/Search/Index.cshtml`
- Modify: `wwwroot/css/pages/search.css`

**Interfaces:**
- Consumes: SearchGatewayResponse diagnostics/counts.
- Produces: production-neutral UI; compact development-only engine indicator.

- [x] Inject `IWebHostEnvironment` into IndexModel and expose `ShowEngineDiagnostics` only in Development.
- [x] Keep All badge on `TotalHits`; render result heading from `FilteredHits`.
- [x] Show `Engine: V2` or `Engine: Legacy fallback (IndexNotReady/QueryFailed)` only in Development, never exception text.
- [x] Keep Filters visible based on V2 and global/current results/active filters.
- [x] Add unobtrusive diagnostic CSS.

### Task 5: Isolate Search V2 CSS and fix geometry

**Files:**
- Modify: `wwwroot/css/site.css`
- Modify: `wwwroot/css/pages/search.css`

**Interfaces:**
- Produces: one authoritative `.pm-gs-*` stylesheet.

- [x] Remove the obsolete `/* ---------- Global Search (Google-style) ---------- */` block from `site.css` through the old Global Search no-results section.
- [x] Verify the previously suspected stray brace/duplicate declaration is absent in the current baseline; no speculative CSS edit required.
- [x] Explicitly align `.pm-gs-results`, pagination and no-results surfaces with the body content grid; retain readable 820px result width without `margin:auto` inheritance.
- [x] Confirm no `.pm-gs-*` selector remains in `site.css`.

### Task 6: Add runtime/PostgreSQL regression coverage

**Files:**
- Modify: `ProjectManagement.Tests/Search/SearchV2PostgresIntegrationTests.cs`
- Modify: `ProjectManagement.Tests/Search/SearchV2QueryTests.cs`

**Interfaces:**
- Produces: opt-in PostgreSQL smoke coverage for statement-local CTE semantics and stable count semantics.

- [x] Add a PostgreSQL test demonstrating a single statement can return summary + zero/multiple paged rows from one CTE graph.
- [x] Add contract/unit assertions for explicit execution status/count fields.
- [x] Keep tests opt-in through `PRISM_SEARCHV2_TEST_CONNECTION`.

### Task 7: Verification and packaging

**Files:**
- Create: `README-SEARCH-V2-RUNTIME-STABILIZATION.md`
- Create package artifacts under `/mnt/data`.

**Interfaces:**
- Produces: ready-to-paste overlay ZIP, full patched source ZIP, unified patch, SHA-256 manifest.

- [x] Run `node tools/test-search-v2-contract.mjs`.
- [x] Run `node --check wwwroot/js/pages/search.js`.
- [x] Run structural checks for zero `.pm-gs-*` declarations in `site.css`, one-statement Search SQL, count field wiring and package diff integrity.
- [ ] Attempt `dotnet build`/`dotnet test`; **blocked in the generation environment because the .NET SDK is not installed. Must be run on the PRISM development machine before deployment.**
- [x] Generate overlay and full-source packages with exact relative paths.
