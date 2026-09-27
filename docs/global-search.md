# Global search (Search V2)

The global search page is `Areas/Common/Pages/Search/Index` (`[Authorize]`, script
`wwwroot/js/pages/search.js`, styles `wwwroot/css/pages/search.css`). Page handlers:
`OnGetAsync` (results), `OnGetFacetsAsync` (lazy detailed facets), `OnGetSuggestionsAsync`
(autocomplete), `OnPostClickAsync` (click telemetry). All of them call `ISearchGateway`.

## Architecture

```
Search page -> SearchGateway -> SearchEngine (V2)            -> SearchEntries (PostgreSQL)
                             -> GlobalSearchService (legacy) -> module tables (fallback / shadow)
```

- **Search V2** (`Services/SearchV2`) is the primary engine. It queries a denormalised projection
  table, `SearchEntries`, plus `SearchEntryTerms` (typed identifiers/aliases/names/etc.),
  `SearchEntryPrincipals`, `SearchAliases`, and uses `pg_trgm` for fuzzy matching and correction.
  Schema: migration `20261216200000_AddSearchV2Foundation`.
- **Legacy V1** (`Services/Search/GlobalSearchService` and `Global*SearchService` providers) is
  still registered. It is not called directly by any page; the gateway uses it only when V2 is
  disabled, not served to the user, not ready (fallback), or when `ShadowMode` is on.

## Rollout flags (`Search:V2`, `SearchV2Options`)

| Key | Code default | appsettings.json | Meaning |
| --- | --- | --- | --- |
| `Enabled` | true | true | Enables the V2 engine and index workers. |
| `ServeV2` | false | true | Serve V2 results to all users. |
| `ServeV2Users` / `ServeV2Roles` | empty | - | Serve V2 to specific users/roles when `ServeV2` is false. |
| `ShadowMode` | true | true | Also run legacy and log comparisons to `SearchShadowComparisons`. |
| `ProjectionVersion` | 4 | 4 | Bump when projection semantics change; forces an atomic full rebuild. |
| `WorkerIntervalSeconds` | 15 | 15 | Incremental index worker tick. |
| `FullReconciliationMinutes` | 1440 | 1440 | Periodic full rebuild. |
| `QueryLogRetentionDays` | 90 | 90 | Telemetry pruning. |

Other ranking/correction thresholds are in `SearchV2Options` and validated on start.
With the shipped settings (`ServeV2=true`, `ShadowMode=true`) every committed search runs both
engines concurrently; set `ShadowMode=false` to stop legacy work once comparison is no longer needed.

If V2 is not ready or throws, the gateway falls back to legacy results and marks the response
`FellBackToLegacy` with a diagnostic id (details only in logs). Suggestions are only returned
when V2 is being served to the user.

## Indexing

- `SearchProjectionBuilder` builds `SearchProjection` rows for: Projects, Project documents,
  Document Repository documents, FFC records, IPR records, Activities, Visits, Social media
  events, Trainings, Project TOT, Proliferation (granular) and ARPP issues.
- Database triggers (`search_v2_enqueue_row`) on the source tables enqueue
  `SearchIndexWorkItems` rows (entity type + key) on insert/update/delete.
- `SearchIndexWorker` (hosted): builds the index if not ready, then every tick recovers stale
  leases, processes up to 50 queued items (`BuildEntityAsync` + `ReplaceEntityAsync`), and runs a
  full rebuild when `FullReconciliationMinutes` has elapsed. Full rebuilds write a new generation
  and activate it atomically; the previous generation keeps serving on failure.
- Failed items retry with exponential backoff (30 s doubling, max 15 min) and become failed
  after 5 attempts.
- `SearchTelemetryRetentionWorker` prunes query/click/shadow logs daily.
- Admin operations: `Areas/Admin/Pages/Diagnostics/SearchIndex` (policy `AdminPolicies.SecurityView`)
  shows health and failed items and can request a full rebuild or retry failed items.

## Authorization

Authorization is applied in SQL before ranking, counts, facets, snippets, suggestions and
corrections (`SearchAuthorizationSql`, shared by engine and correction service):
- Each entry may carry a `RequiredPolicy`. `SearchAccessContextFactory` evaluates the user against
  `DocRepo.View`, `Ipr.View`, and the Project Office Reports view policies for Visits, TOT,
  Training, Proliferation and ARPP; entries whose policy is not granted are excluded.
- `VisibilityMode` supports authenticated (0), owner-only (1) and principal-list (2) entries.
  All current projections use authenticated visibility; there is no per-project access list
  (consistent with `ProjectAccessGuard`, which lets any authenticated user view project information).
- All user input is bound as parameters; tsquery strings are built from letter/digit tokens only.

## Adding a module to global search

1. Add a `Build<Module>Async` method to `SearchProjectionBuilder`, include it in `BuildAllAsync`
   and in the `BuildEntityAsync` switch, and set `RequiredPolicy` to the module's view policy.
2. Add a migration that installs `search_v2_enqueue_row` triggers on the module's tables
   (pattern in `AddSearchV2Foundation`).
3. If the module has a view policy, add it to `SearchAccessContextFactory.SearchPolicies`
   (and to the legacy filter in `SearchGateway.FilterLegacyByAccess` if a legacy provider exists).
4. Bump `Search:V2:ProjectionVersion` so the index is rebuilt.
5. PDF attachments that should be text-searchable can be ingested into the Document Repository
   with `IDocRepoIngestionService.IngestExternalPdfAsync` (SHA-256 de-duplication, OCR queued).

Related tooling: `tools/test-search-v2-contract.mjs`, `tools/search-v2-relevance-evaluator.mjs`
(see `tools/README-search-v2-relevance.md`).
