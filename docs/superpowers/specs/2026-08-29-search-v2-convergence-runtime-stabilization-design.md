# PRISM Global Search — V2 Convergence, Runtime Stabilization & Acceptance Design

## Goal
Make autocomplete and committed search converge on the same Search V2 pipeline, retain Legacy only as an observable resilience fallback, keep global category counts stable while filtering, and isolate the Search V2 page from obsolete global-search CSS.

## Root cause established from source
`SearchEngine.BuildSearchSql` emits two SQL statements separated by a semicolon. The CTE chain (`ranked`, facet CTEs, etc.) belongs only to the first statement, while the second statement attempts `FROM ranked r`. PostgreSQL CTE scope is statement-local, so the second statement cannot resolve `ranked`; the engine catches that query failure and returns `NotReady`, causing `SearchGateway` to serve Legacy. Suggestions do not use this two-statement query, which explains the observed V2-autocomplete/Legacy-full-search hybrid.

## Architecture
1. Convert the committed Search V2 query into one PostgreSQL statement. A single CTE graph will produce summary/facets and a paged-result relation, and a left join from one summary row to the paged rows will preserve summary/facets even for zero-result searches.
2. Separate `TotalHits` (the All-tab count, category filter excluded) from `FilteredHits` (the current result universe after the selected category and all other filters). Keep the same semantics in Legacy fallback.
3. Add typed Search V2 execution status and gateway fallback diagnostics. Full exceptions remain in structured logs; the user-facing response carries only safe failure kind/diagnostic ID. Development mode may show a compact engine indicator.
4. Remove obsolete `.pm-gs-*` CSS from `site.css`; `wwwroot/css/pages/search.css` becomes the authoritative Search V2 stylesheet. Correct the result alignment and latent filter collisions there.

## Constraints
- .NET 8 Razor Pages, EF Core 8, Npgsql/PostgreSQL.
- No new database migration is required.
- Preserve Search V2 ProjectionVersion 4.
- Preserve authorization filtering before search results/facets are exposed.
- Preserve Legacy fallback; do not silently suppress V2 failures in diagnostics/logs.
- Preserve existing user-facing internal category key `Trackers` while displaying `Records`.
- Do not tune relevance weights in this phase.
- Do not add semantic/vector/LLM search or new facets.

## Acceptance
- A served V2 user receives `UsedSearchV2=true` for committed queries when the index is ready.
- The full query no longer references a CTE from a later SQL statement.
- `All` count remains stable across All/Documents/Records tab selection.
- Legacy fallback exposes the same count semantics.
- V2 failure state distinguishes disabled/index-not-ready/query-failed.
- A fallback is logged with safe diagnostics and is visible only in development diagnostics.
- Obsolete Search CSS is removed from `site.css`; results no longer inherit auto-centering/pill filter styles.
- Existing normalization, six-suggestion limit, title-first ranking, snippet hardening, filters, authorization and ranking inspector remain intact.
