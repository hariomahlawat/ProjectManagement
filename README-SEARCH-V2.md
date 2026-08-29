# PRISM Global Search V2 — Ready-to-Paste Handoff

## Purpose

This package implements the Search V2 foundation and staged cutover for PRISM ERP. It retains the current Global Search as a rollback/compatibility path while introducing a unified PostgreSQL search projection with deterministic exact matching, weighted lexical retrieval, aliases, typo tolerance, reciprocal-rank fusion, canonical-entity clustering, authorization-aware retrieval, real cursor pagination, facets, telemetry, indexing health and Ctrl+K suggestions.

## Apply

1. Take a source/database backup.
2. Extract this ZIP into the **ProjectManagement project root** (the folder containing `Program.cs`).
3. Preserve the folder structure and overwrite the matching files.
4. Run `VERIFY-SEARCH-V2.ps1` from PowerShell.
5. Deploy using your normal EF Core migration process. PRISM's normal automatic migration path may apply the included migration, but verify the migration succeeds before enabling V2 serving.

## Database migration

The package adds:

`20261216200000_AddSearchV2Foundation`

It creates the Search V2 derived-index tables, queues, ACL principals, telemetry tables, PostgreSQL FTS/trigram indexes and indexing invalidation triggers. It also runs:

```sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;
```

The PostgreSQL account applying migrations must therefore be permitted to create/use `pg_trgm`, or the extension should be installed by the database administrator beforehand.

No authoritative Project/Document/FFC/IPR/ARPP business record is moved into Search V2. `SearchEntries` is rebuildable derived data.

## Safe rollout state

The supplied `appsettings.json` intentionally deploys:

```json
"Search": {
  "V2": {
    "Enabled": true,
    "ServeV2": false,
    "ShadowMode": true
  }
}
```

This means:

- V1 remains the visible search engine initially.
- V2 builds and runs against the same queries for comparison/telemetry.
- V2 suggestions remain hidden while V2 is shadow-only.
- Search V2 can be enabled for user-facing results by setting `ServeV2` to `true` after the index is ready and relevance/security checks are satisfactory.

For a local development machine, you may set `ServeV2=true` after the Search Health widget reports the V2 index as **Ready**.

## Major implementation points

- Unified PostgreSQL `SearchEntries` projection.
- Separate `simple` and `english` search vectors.
- `pg_trgm` fuzzy/typo retrieval.
- Deterministic Rank-1 tiers for exact identifiers and exact canonical titles before normal RRF ranking.
- RRF across title-prefix, simple FTS, English FTS, aliases and fuzzy candidates.
- Search cursor bound to query + active category/source filters.
- Authorization applied before candidates participate in ranking.
- Explicit user/role search principals supported by the index schema/store.
- Projects enriched by Project Brief, description, capability statements, technical specifications, stage, categories and organisational context.
- Indexed coverage for Projects, Project Documents, Document Repository, FFC, IPR, Activities, Visits, Social Media, Training, ToT, Proliferation and ARPP.
- Project/document/child-record invalidation triggers and persistent work queue.
- Full-generation rebuilds with atomic active-generation cutover.
- Legacy provider failure isolation.
- Literal LIKE/ILIKE escaping for user `%`, `_` and `\\` characters.
- Safe structured highlighting: search-result content is Razor encoded; no raw OCR/search-result HTML is rendered.
- Correct read-only Proliferation result route (`/ProjectOfficeReports/Proliferation/Project/{id}`).
- Entity-aware clustering and related-source context.
- Search query/click/shadow telemetry with retention worker.
- Search Index health separated from OCR health.
- Dedicated results UX plus Ctrl+K palette once V2 serving is enabled.

## Verification limitation of this handoff environment

The generation environment used for this package does **not** contain the .NET SDK/MSBuild/C# compiler. Therefore the package has been source-, JavaScript- and archive-verified here, but the authoritative C# compilation and xUnit run must be performed on your development machine with `VERIFY-SEARCH-V2.ps1`.

Do not enable `ServeV2=true` in production until the build/tests pass and Search V2 index health is Ready.
