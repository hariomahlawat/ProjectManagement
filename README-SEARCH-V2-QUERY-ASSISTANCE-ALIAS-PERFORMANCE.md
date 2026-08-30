# PRISM Global Search — Query Assistance, Alias Semantics & Performance Closure

## Purpose
This package implements the bounded Search V2 closure phase following the Ranking Precision / Match Evidence / Performance hardening build.

## Implemented
- Authorization-aware spelling assistance for zero/weak Search V2 queries using a bounded title/name/location/organisation vocabulary.
- Multi-signal correction scoring using Damerau-Levenshtein edit distance, pg_trgm similarity, token length, first-character agreement, frequency and field authority.
- Conservative protection for project acronyms, numeric/alphanumeric references and identifier-like tokens.
- Multi-token correction while preserving unaffected query terms.
- Autocomplete spelling-assistance fallback only when the ordinary V2 suggestion set is empty.
- Literal Search V2 FTS remains literal; controlled alias variants use separate alias channels.
- Dedicated `alias_fts` channel and independent alias-title phrase ranking/evidence.
- Shared Search V2 authorization SQL for normal retrieval and correction vocabulary.
- Human-readable search status display (`NotStarted` -> `Not Started`) without changing indexed/filter values.
- Regression/source-contract coverage for spelling assistance, acronym protection, alias separation and PostgreSQL semantics.

## Important invariants
- ProjectionVersion remains **4**. This phase does not require a Search V2 projection rebuild solely because of these files.
- No EF migration was added or modified.
- Search aliases remain deliberately controlled; this is not a general-purpose synonym engine.
- Correction vocabulary is authorization-filtered and deliberately excludes arbitrary OCR/narrative body text.
- Corrections are suggestions/fallback assistance. PRISM does not silently rewrite committed user queries.

## Apply
1. Back up the solution.
2. Extract the Ready-To-Paste ZIP.
3. Copy the contents of `PRISM-Search-V2-Query-Assistance-Alias-Performance-Closure-Ready-To-Paste` into the **ProjectManagement project root**, preserving directories and overwriting the matching files.
4. New files under `Services/SearchV2/Query` must also be copied.
5. Do not delete existing migrations and do not create a migration for this phase.

## Mandatory verification on the development machine
```powershell
dotnet restore
dotnet build
dotnet test ProjectManagement.Tests
```

Then launch PRISM with PostgreSQL/Search V2 available and test at minimum:
- `AURA` — canonical Project remains first or at the highest relevance class.
- `high` — whole-token `High` candidates remain above `Higher` prefix-only candidates.
- `high tech`, `high-tech`, `HI-TECH` — exact/alias title semantics remain strong and controlled.
- `hyderabad` — existing strong location/document/activity recall remains intact.
- `hydrbad`, `hyderbad`, `hydrbd` — zero/weak-result assistance should offer a useful Hyderabad correction where confidence is sufficient.
- `AURA`, `ARPP`, `T90`, numeric/bid/reference-like queries — must not be aggressively spelling-corrected.
- Autocomplete for a typo — ordinary suggestions win when present; correction is a fallback only.

## Environment verification completed here
The package was verified with the repository Search V2 source-contract script, JavaScript syntax check, whitespace/diff check, a C# structural delimiter scan, migration-tree hash comparison, ProjectionVersion check, independent overlay reproduction, independent patch reproduction, ZIP integrity testing and SHA-256 hashing.

This execution environment does **not** contain the .NET SDK or PostgreSQL client/server, so it cannot provide an authoritative C# build/xUnit/live-PostgreSQL execution result. Those commands above remain mandatory after paste.
