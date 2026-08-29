# Search V2.2 Production Assurance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Search V2 projection freshness automatic, ranking semantics correct, facets truthful, operations recoverable, and relevance/security/performance measurable.

**Architecture:** Preserve the current unified PostgreSQL SearchEntry engine and UI. Add a projection compatibility version, typed search terms, DB-backed query alias expansion, disjunctive facet scopes, cross-source project context, Search Health commands, and assurance tooling. Exact identifiers/titles remain deterministic tiers; authorization remains upstream of all retrieval and counts.

**Tech Stack:** .NET 8 Razor Pages, EF Core, Npgsql/PostgreSQL FTS + pg_trgm, vanilla JavaScript/CSS, xUnit, Node relevance evaluator.

**Spec:** `docs/superpowers/specs/2026-08-29-search-v2-2-production-assurance-design.md`

## Global Constraints
- No external search service or internet dependency.
- No embeddings/vector/LLM ranking in this phase.
- Do not index Notebook/private Tasks/Media/Industry/Calendar in this phase.
- Do not overwrite current ServeV2/ShadowMode deployment settings in the incremental handoff.
- Search authorization must occur before ranking, snippets, suggestions and facet counts.

---

### Task 1: Projection freshness and operational control
**Files:** SearchV2Options, SearchProjection model, SearchIndexStore/Worker, Search Health service/view model/widget, Dashboard page handler.
- [ ] Add `ProjectionVersion` with a bumped default and use it for SearchEntry/index-state compatibility.
- [ ] Force automatic atomic rebuild when active generation projection version differs.
- [ ] Add admin rebuild request and failed-job retry/read operations without deleting the live generation first.
- [ ] Add regression/source-contract coverage.

### Task 2: Semantic term model and query aliases
**Files:** SearchProjection, SearchProjectionBuilder, new SearchAliasProvider, SearchEngine, DI registration, tests.
- [ ] Add typed terms: Identifier/Alias/Name/Organisation/Location/Person/Context.
- [ ] Refactor projections so locations/categories/filenames/platforms/etc. are not aliases.
- [ ] Use SearchAliases as runtime query-expansion source of truth with AND-preserving compound expansion.
- [ ] Keep genuine project/acronym aliases as exact-alias terms.

### Task 3: Ranking and matched-field correctness
**Files:** SearchEngine, SearchProjectionBuilder, tests.
- [ ] Introduce title/name prefix channel and lower contextual weighting beneath true aliases.
- [ ] Ensure title-visible matches report Title before Alias.
- [ ] Report Location/Organisation/Person/Context precisely from metadata/typed terms.
- [ ] Retain exact identifier/title/alias deterministic tiers and fuzzy last.

### Task 4: Disjunctive facets and canonical source accounting
**Files:** SearchEngine, SearchContracts/tests.
- [ ] Build per-dimension filter clauses that exclude the current facet dimension.
- [ ] Count categories/sources/projects/status/file type/stage truthfully under all other active filters.
- [ ] Count source facets by distinct canonical entity available in each source.
- [ ] Add real PostgreSQL facet integration coverage when `PRISM_SEARCHV2_TEST_CONNECTION` is supplied.

### Task 5: Cross-source project context
**Files:** SearchProjectionBuilder, tests.
- [ ] Propagate parent project currentStage/status/category/technical category to Project Documents, ToT, Proliferation, IPR and linked FFC/ARPP where resolvable.
- [ ] Ensure Stage filtering behaves across those sources.

### Task 6: Filter UX and related navigation
**Files:** Search page Razor/CSS/JS.
- [ ] Add active filter chips.
- [ ] Make filter footer sticky and facet sections collapsible.
- [ ] Add project-facet client filtering/show-more instead of silent truncation.
- [ ] Label Date as Relevant date with helper text.
- [ ] Make related source chips link to canonical-project/source-filtered search where possible.

### Task 7: Telemetry and assurance
**Files:** SearchGateway/Analytics/SearchHealth, tests, tools.
- [ ] Log gateway/end-to-end and suggestion latency separately from engine latency.
- [ ] Expose p50/p95 for both search and suggestions.
- [ ] Extend PostgreSQL smoke tests for exact, alias, fuzzy, facet and authorization primitives.
- [ ] Keep relevance evaluator/schema and add a corpus-grounded template, not fabricated judgments.

### Task 8: Verification and packaging
- [ ] Run Search V2 source contracts.
- [ ] Run JS syntax checks and existing JS suite; compare baseline failures.
- [ ] Run structural C# checks and JSON/schema checks.
- [ ] Package incremental production-only and ready-to-paste ZIPs excluding appsettings.json.
- [ ] Generate patches, manifests and SHA-256 checksums.
