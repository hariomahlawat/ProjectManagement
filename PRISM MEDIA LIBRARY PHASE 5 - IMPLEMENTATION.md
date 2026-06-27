# PRISM Media Library Phase 5

## Scope

Phase 5 stabilises the unified catalogue read path and prevents optional aggregate/facet failures from forcing the Photos page back to the legacy in-memory reader.

## Implemented

- Critical timeline query separated from optional statistics, year, project and source-status queries.
- Timeline remains catalogue-backed when an optional facet fails.
- Safe operation references (`MLQ-...`) are logged and exposed to Admin/HoD without disclosing SQL or connection details.
- Catalogue smoke-test service verifies database, schema, internal source, timeline, statistics, years and project facets independently.
- Administration dashboard now distinguishes PRISM catalogue assets from external sources.
- Internal PRISM source is labelled system-managed and always included rather than hidden.
- Operational actions added: Test catalogue, Synchronize PRISM and Consistency check.
- Consistency report compares authoritative PRISM media records with available catalogue records.
- Core Photos fallback remains intact if the primary timeline query fails.

## Deployment

No new database migration is required for this phase.

1. Copy the replacement files into the ProjectManagement root.
2. Run `dotnet clean`, `dotnet restore`, `dotnet build`, and `dotnet test`.
3. Restart PRISM and hard-refresh the browser.
4. Open `/Admin/MediaSources` and select **Test catalogue**.
5. If consistency is not clean, select **Synchronize PRISM**, then run **Consistency check** again.

## Expected outcome

- A healthy primary timeline query serves Photos from `MediaAssets` even if a project/year/statistics facet is temporarily degraded.
- Only a primary timeline failure activates the direct PRISM fallback.
- Admin/HoD receives a diagnostic reference that can be matched to server logs.
