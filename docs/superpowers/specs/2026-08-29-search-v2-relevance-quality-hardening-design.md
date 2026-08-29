# Search V2 Relevance, Consistency & Result Quality Hardening Design

## Goal
Make PRISM Search V2 rank strong lexical intent ahead of loose body matches, keep autocomplete and committed search semantically consistent, normalize punctuation variants, suppress poor OCR presentation/ranking, and finish the small search-results UX corrections without introducing a new search platform.

## Constraints
- Keep PostgreSQL SearchEntries/SearchEntryTerms as the search platform; no Elasticsearch, embeddings or LLM reranking.
- Keep authorization filtering ahead of candidate exposure, counts, snippets and suggestions.
- Preserve Search V2 rollout/fallback architecture.
- Preserve canonical clustering and cursor-based pagination.
- Keep internal category key `Trackers` for compatibility, but present it as `Records` in the UI.
- Autocomplete is navigational: maximum six visible entity suggestions plus the explicit “See all results” action.

## Query semantics
`SearchQueryNormalizer` canonicalizes Unicode dash punctuation and underscores to spaces, collapses whitespace, and derives highlight terms from both the original and normalized token streams. Thus `high tech`, `high-tech`, `HI–TECH`, and `high_tech` share the same exact representation and highlight correctly across punctuation variants.

## Relevance model
Search remains tier-first, then rank-fusion within a tier. Add explicit full-search channels for:
1. exact identifier;
2. identifier prefix (to preserve identifier suggestions after commit);
3. exact title;
4. normalized title phrase anywhere in the title;
5. exact alias and alias prefix;
6. title token/prefix query (`high & tech:*`);
7. title prefix and typed name terms;
8. structured/simple FTS;
9. narrative/English FTS;
10. title-fuzzy and general fuzzy fallback.

Narrative-only matching is deliberately a lower tier than title/structured matching. This prevents long OCR documents from outranking normalized title phrase matches. Narrative channel ordering is multiplied by a persisted projection metadata quality factor so highly corrupted OCR is demoted among otherwise comparable body matches.

## Text quality and snippets
A deterministic `SearchTextQuality` utility scores text using replacement/control/symbol/token heuristics and provides conservative display sanitation. `SearchProjectionBuilder` stores `searchTextQuality` in every projection metadata document and increments the projection version to force an atomic rebuild. `SearchHighlightService` sanitizes candidate snippet sources, prefers a clean matching structured passage, then clean matching narrative, and refuses very low-quality narrative previews rather than emitting OCR garbage.

## Autocomplete consistency
Suggestions continue to use a low-latency navigation query, while committed search now covers every suggestion candidate family: exact/prefix identifier, exact/prefix alias, exact/prefix/title-token title, and title-fuzzy fallback. Suggestion fuzzy matching is stricter than committed fuzzy matching, so any emitted suggestion remains discoverable after commit. The page requests six suggestions.

## UX
- Render `Trackers` as `Records` without changing the query key.
- Show the All-result count in the All tab.
- Keep the search header as one sticky unit, offset below the 52 px PRISM application header (50 px on small screens).
- Keep the existing flat results list and constrained readable width.

## Ranking diagnostics
The Admin Search Index diagnostics page gains an authorization-aware Ranking Inspector. It runs the production V2 engine directly without writing ordinary query analytics and shows rank, source, matched field, tier, contributing retrieval channels and RRF score for the first ten canonical results. SearchResult carries optional diagnostic tier/channel fields; legacy adaptations leave them null.

## Verification
Add unit/source-contract regressions for dash normalization, cross-punctuation highlighting, low-quality snippet suppression, all autocomplete/committed-search candidate families, title-phrase/title-prefix relevance channels, narrative tier demotion, projection quality metadata, six-suggestion cap, Records label, All count, sticky-header offset and ranking diagnostics. Run available JavaScript/static verification locally; authoritative .NET compilation/xUnit remains required on a machine with the .NET 8 SDK.
