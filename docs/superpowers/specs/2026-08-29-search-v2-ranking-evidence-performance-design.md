# PRISM Global Search V2 — Ranking Precision, Match Evidence & Performance Hardening Design

## Goal
Preserve the converged Search V2 architecture while improving lexical precision, trustworthy match evidence/highlighting, filter-state clarity, and common-query latency.

## Scope
- Add exact whole-token title matching above final-token prefix matching.
- Add controlled built-in aliases for `high tech` ↔ `hi tech` without a schema migration.
- Preserve exact phrase > whole-token > prefix > structured/narrative > OCR > fuzzy ordering.
- Add a modest canonical-entity tie-breaker without hard-coding Projects ahead of other sources.
- Replace single-field heuristic evidence with compositional query-term evidence.
- Highlight full lexical words for prefix matches rather than arbitrary substrings.
- Preserve canonical clustering and overlapping facet semantics.
- Make filter section badges represent active selections only; keep active-filter chips and make the Filters button show an active count badge.
- Reduce normal query work by omitting detailed advanced facet computation until the filter panel is opened, while always returning category facets.
- Gate expensive fuzzy channels when enough strong lexical candidates already exist.
- Keep Search V2 security, gateway fallback, diagnostics, category semantics, URLs and visual design intact.

## Non-goals
No vector/semantic search, no broad synonym corpus, no new search engine dependency, no category redesign, no database schema migration, and no ranking personalization.

## Acceptance
- `high` exact-token title candidates beat otherwise comparable `higher` prefix candidates.
- `high tech` exact phrase/whole-token candidates beat `high ... technology` prefix-only candidates.
- `HI-TECH` and `HIGH-TECH` are controlled aliases while punctuation/case normalization remains separate.
- Multi-field matches report composite evidence such as `Title + document text`.
- `technology` is highlighted as a whole word when matched from the `tech` prefix.
- Initial result search does not compute detailed Source/Project/Status/File type/Stage facets unless advanced filters are active; opening Filters fetches them safely.
- Fuzzy title/body channels are skipped when the query already has a sufficient strong lexical candidate pool.
- Existing canonical clustering, All count semantics and authorization rules remain unchanged.
