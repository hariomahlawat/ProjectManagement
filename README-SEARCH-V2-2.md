# PRISM Search V2.2 — Relevance, Faceting & Production Assurance

## Apply this package

This is an **incremental overlay** for the Search V2 Convergence build already installed in PRISM.

1. Stop the development instance / IIS application pool as appropriate.
2. Extract the ready-to-paste ZIP into the **ProjectManagement project root** (the folder containing `Program.cs`).
3. Preserve the folder structure and overwrite matching files.
4. Do **not** replace `appsettings.json`. This package intentionally excludes it so your current `Search:V2` rollout settings remain unchanged.
5. Run `VERIFY-SEARCH-V2.ps1` on the development machine.
6. Start PRISM and open **Admin > Diagnostics > Search Index**. Confirm Projection Version **3**, allow the automatic atomic rebuild to complete, and confirm Pending/Failed indexing jobs are healthy before final relevance assessment.

## Database impact

There is **no new EF Core migration** in Search V2.2. It reuses the Search V2 schema already created by `20261216200000_AddSearchV2Foundation`.

Search V2.2 increments the **projection compatibility version to 3**. An existing active projection built with the prior semantic version is therefore treated as stale and rebuilt atomically. The current active generation remains available until the replacement is complete; the gateway can fall back to authorized Legacy search while V2 is not ready.

## Major corrections in V2.2

- Separates projection compatibility from the legacy Search schema/index option and forces stale projection rebuilds.
- Adds Admin Search Index operations: request full rebuild, inspect failures, retry one, retry all.
- Preserves the active generation during full rebuild and records rebuild failures in Search Index state.
- Replaces overloaded Alias semantics with typed search terms: Identifier, Alias, Name, Organisation, Location, Person, Context.
- Stops locations, filenames, countries, categories, platforms, units and similar context from receiving exact-alias ranking privilege.
- Uses runtime `SearchAliases` as the terminology source and preserves mandatory terms in compound expansion (for example, `AURA ToT`).
- Adds Name retrieval while retaining deterministic exact Identifier / Title / genuine Alias tiers.
- Corrects matched-field precedence so a visible title hit reports `Matched in Title` before lower-level channels.
- Implements disjunctive Category, Source, Project, Status, File Type and Stage facets.
- Keeps facets available even when the current filter combination yields zero result rows.
- Counts source facets by distinct canonical entity within each source rather than only the current canonical representative.
- Propagates parent Project stage/status/category context to project-linked SearchEntries where applicable.
- Uses bounded PostgreSQL-generated OCR snippets instead of returning full OCR bodies for result rendering.
- Adds active filter chips, collapsible filter sections, searchable/show-more Project facets, a sticky Apply/Clear footer and clickable related-result filters.
- Records gateway end-to-end V2 latency, internal V2 engine latency and suggestion latency separately.
- Extends Search Health with query volume, p50/p95, engine p95, suggestion p95 and zero-result rate.
- Adds authorization-context regression coverage and opt-in real PostgreSQL Search V2 integration tests.

## Search Index administration

Open:

`/Admin/Diagnostics/SearchIndex`

Viewing uses the existing Admin security-view policy. Rebuild/retry operations require the existing ingestion-management authority.

A manual rebuild creates a replacement generation in the background; it does **not** delete the live index before rebuilding.

## Verification

Run from PowerShell in the project root:

```powershell
.\VERIFY-SEARCH-V2.ps1
```

To run the entire .NET test suite as well:

```powershell
.\VERIFY-SEARCH-V2.ps1 -FullTests
```

For real PostgreSQL Search V2 tests, point the tests at a **test database** containing the Search V2 schema:

```powershell
$env:PRISM_SEARCHV2_TEST_CONNECTION = "Host=...;Database=...;Username=...;Password=..."
.\VERIFY-SEARCH-V2.ps1
```

Do not point destructive/integration test workflows at the production database.

## Relevance benchmark

The evaluator and JSON schema are included under `tools/`. The scoring dataset must be populated with real PRISM entities and judgments; no fabricated entity IDs are supplied.

The evaluator reports:

- Exact navigation Rank@1
- MRR@10
- nDCG@10
- Recall@20

See `tools/README-search-v2-relevance.md`.

## Rollout recommendation

After Projection Version 3 is rebuilt and Search Health is clean:

1. validate exact Project/document/identifier queries;
2. validate compound aliases such as `AURA ToT`;
3. validate locations such as `Hyderabad` no longer behave as exact aliases;
4. validate Category/Source/Project/Status/File Type/Stage combinations and zero-result recovery;
5. run the PostgreSQL and authorization tests;
6. populate and run the PRISM relevance benchmark;
7. validate p95 on the production-class IIS/PostgreSQL LAN machine.

Only after those gates pass should production settle on `ServeV2=true` and `ShadowMode=false`. Keep Legacy as an emergency fallback for one release cycle.

## Environment limitation of this handoff

The packaging environment does not contain the .NET SDK/MSBuild, so it cannot provide authoritative `dotnet build` or xUnit execution evidence. The package is statically/structurally verified here and contains the PowerShell verification script for the authoritative build/test run on your development machine.
