# PRISM Global Search V2 — Runtime Stabilization / Convergence

## Purpose

This package closes the Search V2 hybrid-serving defect observed after the relevance-quality phase: autocomplete was served by Search V2, while committed searches fell back to Legacy Search.

## Root cause corrected

`SearchEngine.BuildSearchSql()` previously emitted two PostgreSQL statements. The first statement defined the `ranked` CTE and returned summary/facet data; the second statement attempted to read `ranked` again for the result rows. PostgreSQL CTEs are scoped to a single statement, so the second statement could not reference that CTE. The exception was caught by Search V2 and converted to `NotReady`, after which `SearchGateway` silently served Legacy results.

The query is now one PostgreSQL statement. `summary` and `paged_results` are CTEs in the same graph, and the final SELECT left-joins them so summary/facets remain available even when a page contains zero result rows.

## What changed

- Full Search V2 query now returns summary, facets and paged results from one statement; `NextResultAsync` is removed.
- `SearchV2ExecutionStatus` distinguishes `Success`, `Disabled`, `IndexNotReady` and `QueryFailed`.
- V2 failures receive a safe diagnostic ID. Exception text remains in server logs only.
- Gateway catches any unexpected V2 exception at its boundary and falls back safely instead of allowing the search page to fail.
- Development mode shows a compact engine indicator (`V2`, `Legacy`, or `Legacy fallback` plus safe diagnostic ID). Production does not render this diagnostic surface.
- `TotalHits` is the global **All** count for the current non-category filters; `FilteredHits` is the current selected-category result count.
- Legacy fallback mirrors the same All/current count semantics for the filters it supports.
- Search analytics now records the current filtered hit count for V2 category searches.
- `projectStages` JSON expansion is guarded against null/scalar values.
- Obsolete `.pm-gs-*` Global Search CSS was removed from `site.css`; `wwwroot/css/pages/search.css` is authoritative.
- Results, pagination and no-results surfaces no longer inherit the old `margin:auto` centering, eliminating the large unexplained left gutter.
- PostgreSQL smoke tests cover the one-statement summary/paged-result pattern and the summary-only zero-page case.

## No database migration / re-index requirement

- No EF migration is added.
- Search projection schema/version remains **4**.
- A Search V2 full rebuild is not required solely for this patch if Version 4 is already healthy.

## Ready-to-paste files

Copy the package contents over the project root, preserving paths. The production/runtime files are:

- `Services/SearchV2/Models/SearchContracts.cs`
- `Services/SearchV2/Query/SearchEngine.cs`
- `Services/SearchV2/Query/SearchGateway.cs`
- `Areas/Common/Pages/Search/Index.cshtml.cs`
- `Areas/Common/Pages/Search/Index.cshtml`
- `wwwroot/css/pages/search.css`
- `wwwroot/css/site.css`

Regression/test files are also included and should be retained:

- `ProjectManagement.Tests/Search/SearchV2QueryTests.cs`
- `ProjectManagement.Tests/Search/SearchV2PostgresIntegrationTests.cs`
- `ProjectManagement.Tests/Search/SearchV2SourceContractTests.cs`
- `tools/test-search-v2-contract.mjs`

## Required verification on the development machine

Run from the solution/project root:

```powershell
dotnet restore
dotnet build
dotnet test ProjectManagement.Tests
```

If a disposable PostgreSQL test database is available, set `PRISM_SEARCHV2_TEST_CONNECTION` before `dotnet test` so the PostgreSQL-specific tests execute rather than self-skip.

Then run the application in **Development** and confirm committed searches display:

- `Engine: V2`
- a numeric result count plus query latency (not the Legacy-only `Results` label)
- the V2 Filters control when applicable

Regression queries:

1. `aura`
2. `high tech`
3. `high-tech`
4. `HI-TECH`
5. `hyderabad`

Acceptance points:

- Committed search remains on V2.
- If V2 fails, the development indicator shows `Legacy fallback` with a diagnostic ID; use that ID to find the corresponding server log exception.
- All count remains stable when switching between Documents and Records; the result heading changes to the selected category count.
- `high tech` / `high-tech` / `HI-TECH` have substantially equivalent V2 candidate families.
- A suggestion emitted for an identical query remains discoverable in the committed V2 result universe.
- Search results sit near the left content grid rather than being centred with the old ~180 px gutter.
- Production (`ASPNETCORE_ENVIRONMENT=Production`) does not show the engine diagnostic indicator.

## Environment note for this generated package

The generation environment did not contain the .NET SDK or a PostgreSQL client/server, so the authoritative C# build/xUnit/PostgreSQL execution could not be performed here. The package was subjected to the repository Search V2 source contract, JavaScript syntax checks, structural C# delimiter checks, CSS ownership checks, and package/diff integrity verification. The commands above remain mandatory on the PRISM development machine before deployment.
