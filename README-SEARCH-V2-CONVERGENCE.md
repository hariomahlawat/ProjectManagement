# PRISM Search V2 — Relevance & Production Convergence

This is an **incremental overlay for an existing Search V2 installation**. It is intended for the PRISM source state that already contains the Search V2 foundation and the `SearchEngine.cs` raw-string build hotfix.

## Apply

1. Back up source and database.
2. Extract the ZIP into the **ProjectManagement project root** (the folder containing `Program.cs`).
3. Preserve folders and overwrite matching files.
4. This convergence package deliberately **does not replace `appsettings.json`**. Your current `Search:V2:ServeV2` / `ShadowMode` rollout state is preserved.
5. Run `VERIFY-SEARCH-V2.ps1`.
6. Rebuild/restart the application. No new EF migration is introduced by this convergence overlay.

## What this convergence phase fixes

- Suppresses false `Did you mean` prompts when healthy exact/prefix/FTS results already exist.
- Restricts spelling correction to small weak/fuzzy result sets.
- Redesigns suggestions around exact identifier, identifier prefix, exact title, title/token prefix, alias and limited title fuzzy matching.
- Canonically deduplicates suggestions and keeps body/OCR text out of keystroke-time suggestion ranking.
- Preserves mandatory terms during terminology expansion (`AURA ToT` => `AURA AND (ToT OR Transfer of Technology)` semantics rather than broad `OR Transfer of Technology`).
- Uses database `SearchAliases` as an additional full-search retrieval channel.
- Moves Visits and Social Media into the stable **Trackers** category taxonomy.
- Uses stable category tab order instead of count-based reordering.
- Fixes source/advanced filter clearing and keeps filters available even after narrowing.
- Adds Project, Status, File Type, Stage and Date filters/facets.
- Makes cursors generation-aware and binds them to all active filters.
- Guards inverted date ranges and `DateOnly.MaxValue` upper-bound overflow.
- Adds more precise matched-field attribution using structured projection metadata.
- Adds counted related-source context for canonically clustered results.
- Enriches Project search content with ToT and IPR relationship summaries.
- Adds Project category and technical-category context to Project Document search projections.
- Uses an indexable `pg_trgm` fuzzy prefilter before similarity scoring.
- Bounds database-to-application snippet transfer with PostgreSQL `ts_headline`; full OCR bodies are no longer returned merely to render a result snippet.
- Adds selected-user/role V2 rollout controls (`ServeV2Users`, `ServeV2Roles`) without forcing any appsettings change.
- Adds Search Health 24-hour query count, p50/p95 latency and zero-result rate.
- Removes development-only Legacy shadow-mode explanatory copy from the ordinary results page.
- Adds a formal relevance dataset schema/evaluator for MRR@10, nDCG@10, Recall@20 and exact-navigation Rank@1.
- Adds authorization-context tests and opt-in real PostgreSQL FTS/`pg_trgm` smoke tests.

## Configuration

No configuration edit is required for this overlay. The new rollout arrays default to empty:

```json
"ServeV2Users": [],
"ServeV2Roles": []
```

If `ServeV2` is `true`, V2 serves all users as before. If `ServeV2` is `false`, specific test users/roles can be opted in through those arrays. `ShadowMode=true` remains useful while comparing V2 and Legacy; after production acceptance, use `ShadowMode=false` to avoid running Legacy on every served V2 query.

## Relevance benchmark

The package includes:

- `tools/search-v2-relevance-dataset.schema.json`
- `tools/search-v2-relevance-evaluator.mjs`

A genuine benchmark must be curated against **your actual PRISM corpus**; entity IDs and relevance judgments should not be fabricated. Capture at least 150 representative queries before final rank tuning. The evaluator computes:

- MRR@10
- nDCG@10
- Recall@20
- exact-navigation Rank@1

## Real PostgreSQL integration tests

`ProjectManagement.Tests/Search/SearchV2PostgresIntegrationTests.cs` runs against a real database only when this environment variable is set:

```powershell
$env:PRISM_SEARCHV2_TEST_CONNECTION = "Host=...;Database=...;Username=...;Password=..."
```

Use a disposable/test database. These tests intentionally do not use EF InMemory for FTS/trigram validation.

## Verification limitation

The packaging environment does not contain the .NET SDK/MSBuild/C# compiler. Source contracts, JavaScript syntax, JSON/schema integrity, packaging and differential checks are executed here, but the authoritative C# compile/xUnit run must be performed on your development machine with `VERIFY-SEARCH-V2.ps1`.
