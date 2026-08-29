# PRISM Global Search V2 — Ranking Precision, Match Evidence & Performance Hardening

## Scope

This package hardens the already-converged Search V2 implementation without changing the established Search V2 architecture, canonical clustering, authorization model, category taxonomy, or Legacy fallback safety net.

The phase addresses the remaining issues visible in the production-style acceptance queries (`high`, `high tech`, `AURA`): exact whole-word matches versus prefixes, controlled HI-TECH/HIGH-TECH terminology, trustworthy match evidence, whole-word prefix highlighting, filter-state clarity, and avoidable query cost from eager detailed facets and unnecessary fuzzy retrieval.

## Implemented changes

### Ranking precision

- Added an exact whole-title-token channel (`title_tokens_exact`) ahead of title-prefix channels.
- Exact `high`/`tech` tokens therefore outrank prefix-only matches such as `higher`/`technology` when other relevance signals are comparable.
- Added a controlled built-in `HIGH TECH` ↔ `HI TECH` alias pair. Runtime database aliases remain authoritative/extensible and are merged with these small product-owned rules.
- Added an alias title-phrase channel so a title containing `HI-TECH` remains a strong candidate for a `high tech` query without treating arbitrary synonyms as equivalent.
- Added a small same-tier canonical-entity boost. It can break close ties in favour of the canonical entity, but cannot leapfrog a stronger lexical tier.
- Fuzzy/trigram retrieval is now a true fallback: it is skipped when the normal exact/phrase/prefix/name/FTS lexical pool already contains candidates.

### Match evidence and highlighting

- Added `SearchMatchEvidenceResolver`.
- `Matched in …` is now based on complete-query field coverage rather than the first field containing any query token.
- Multi-field matches can be described compositionally (for example `Title + document text`).
- Document/project/description/metadata evidence remains user-facing; internal channel names remain diagnostic-only.
- Prefix highlighting expands to the complete lexical word (`tech` → **Technology**) rather than bolding only a substring.
- The green success/check icon was replaced by a neutral search/evidence icon.
- Machine-form statuses such as `NotStarted` are humanised for display without changing persisted values.
- Long snippets are visually clamped to three lines to preserve scanability.

### Filter-state UX and performance

- Detailed Source / Project / Status / File Type / Stage facets are no longer calculated on every ordinary result-page request.
- Initial searches retain the inexpensive category counts needed by the tabs.
- Detailed facets are fetched from an authorised Razor Pages `Facets` handler only when the user opens Filters, unless advanced filters are already active.
- Facet-only requests suppress paged-result/snippet work and do not fall back to Legacy Search.
- Collapsed facet headers now show counts only for active selections, not the number of available options.
- The Filters button shows an active-selection badge when filters are applied; existing removable active-filter chips and explicit `Apply` / `Clear all` behaviour are preserved.
- Fuzzy-channel gating removes avoidable pg_trgm work for ordinary lexical searches.
- Search V2 emits Development/debug timing for total database and spelling-correction time to support production query-plan profiling.

## Database / index impact

- **No EF Core migration.**
- **No Search V2 schema change.**
- **ProjectionVersion remains 4.**
- An already healthy Version 4 index does **not** need a rebuild solely for this phase.
- Built-in HI-TECH/HIGH-TECH terminology is query-time behaviour and does not require projection regeneration.

## Security

- All result and facet requests continue through the existing Search V2 authorization scope.
- The lazy facet endpoint uses the same query/filter and user authorization path as normal Search V2.
- Facet-only requests do not use Legacy fallback, preventing different authorization semantics from being introduced by the auxiliary endpoint.
- Diagnostic ranking/channel terminology remains non-user-facing.

## Recommended application procedure

1. Back up the current source tree.
2. Copy the ready-to-paste overlay over the project root, preserving directories.
3. Do **not** create a migration for this phase.
4. Restore/build/test:

```powershell
dotnet restore
dotnet build
dotnet test ProjectManagement.Tests
```

5. Start PRISM in Development and confirm committed searches still show `Engine: V2`.
6. No index rebuild is required if Search Index Version/ProjectionVersion 4 is already Ready.

## Acceptance tests

### Ranking

Search `high`:
- whole-token title matches (`High Power`, `High-Tech`) should rank ahead of otherwise comparable prefix-only `Higher …` candidates.

Search `high tech`:
- `Mockup based Pinaka High-Tech Sml` should remain a top result.
- `MANAGEMENT OF HI-TECH / EXTENDED TENURE APPTS` should be treated as a controlled alias title match.
- `High … Technology` remains discoverable, but as a weaker title-prefix candidate than an equivalent exact-token/alias phrase match.

Search `high-tech`, `HIGH TECH`, and Unicode-dash variants:
- candidate families should remain materially equivalent.

### Match evidence

For a result where one term is in the title and another is only in document text, verify the UI does **not** claim simply `Matched in Title`; it should report the composite evidence.

For a complete title match, `Matched in Title` remains appropriate.

### Highlighting

For a prefix expansion such as `tech` matching `Technology`, the complete word `Technology` should be highlighted rather than only the `Tech` prefix.

### Filters

- Open Filters on an unfiltered search: detailed facets should load on demand.
- Facet section headers should not show counts merely because options are available.
- Select two checkbox filters: the corresponding section badge(s) and Filters badge should reflect active selections.
- Apply the filters: active filter chips remain visible and removable.
- `Clear all` continues to reset filter state.

### Performance

Compare representative timings for `AURA`, `high tech`, `hyderabad`, and a typo/fuzzy query before and after this phase. Exact gains depend on database size and production hardware. The two implemented cost reductions are:

1. detailed facets are lazy for ordinary searches;
2. trigram fuzzy channels are skipped whenever normal lexical retrieval already has candidates.

For deeper tuning, capture `EXPLAIN (ANALYZE, BUFFERS)` against the generated committed-search SQL on the production-like PostgreSQL instance rather than changing rank weights speculatively.

## Verification note for this package

The packaging environment used to prepare these files does not provide the .NET SDK or PostgreSQL client/server. Source-contract and JavaScript syntax checks are therefore run here, but the authoritative C# build, xUnit suite and live PostgreSQL integration tests must be run on the PRISM development machine using the commands above.
