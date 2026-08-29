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

## 2026-08-29 relevance & result-quality hardening

The current package also includes the Search V2 relevance/quality hardening pass:

- `high tech`, `high-tech`, Unicode-dash variants and underscore-separated variants normalize to the same exact query representation.
- Full search now has explicit `identifier_prefix`, `title_phrase`, `alias_prefix`, `title_token_prefix` and `title_fuzzy` channels. Every autocomplete candidate family therefore has a committed-search counterpart, while strong normalized title intent still ranks ahead of broad structured/body matches.
- Broad/simple FTS is a lower relevance tier than title intent; narrative/English FTS is lower again, and fuzzy retrieval remains fallback-only.
- Each rebuilt SearchEntry stores deterministic `searchTextQuality` metadata. Narrative/OCR ordering uses this factor so corrupted extracted text is demoted among comparable body matches.
- Snippet sources are display-sanitized; very low-quality narrative windows are suppressed instead of showing OCR garbage.
- Autocomplete is capped at six entity suggestions, followed by the existing “See all results” action.
- Internal category key `Trackers` is preserved for URLs/filters, but the search UI displays the user-facing label **Records**.
- The All tab now shows the total result count and legacy partial mode uses the neutral heading **Results** instead of **Top results**.
- The results search/tabs header now sticks below the PRISM navigation bar instead of sliding underneath it while scrolling.
- Admin > Diagnostics > Search index now includes an authorization-aware **Ranking inspector** showing rank, matched field, tier, retrieval channels and RRF score for the top ten V2 results without writing ordinary search analytics.

`SearchV2Options.ProjectionVersion` is now **4** (also explicit in `appsettings.json`). The existing SearchIndexWorker detects the version mismatch and performs the normal atomic full projection rebuild; no new EF migration is required for this hardening pass.

Recommended acceptance queries after the rebuild are complete:

1. `high tech` — exact/normalized title phrase matches should rank ahead of loose OCR/body matches.
2. `high-tech` and `HI–TECH` — should converge with `high tech`.
3. `hyderabad` — autocomplete should show at most six entity suggestions plus “See all results”.
4. A title containing `Technology` with query ending in `tech` — if suggested, it must remain discoverable after committing the same query.
5. A partial identifier and a partial configured alias that appear in autocomplete — each must remain discoverable after commit.
6. A deliberately misspelled title that appears through autocomplete fuzzy matching — it must remain discoverable in full search and may still offer spelling correction.
7. Known noisy scanned PDFs — searchable, but poor OCR snippets should be suppressed/demoted rather than dominate presentation.
