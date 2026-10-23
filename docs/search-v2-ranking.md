# Search V2 ranking and query execution

Code: `Services/SearchV2/Query/SearchEngine.cs` (committed search and autocomplete),
`SearchCorrectionService.cs` (did-you-mean), `SearchQueryNormalizer.cs` (normalisation),
`Services/SearchV2/Indexing/SearchProjectionBuilder.cs` (what is indexed).

## Normalisation

`SearchQueryNormalizer.NormalizeExact` is shared by indexed titles/terms and queries:
NFKC, lower case, dashes/underscores and stand-alone punctuation (quotes, brackets,
commas) become single spaces; joiners inside a token (`0.22`, `1:4`, `IR&D`,
`30102/SDD`) and combining marks (Devanagari matras/virama) are kept. Queries are
clamped to 256 characters.

Identifiers also get a separator-insensitive `IdentifierKey` term (`PRJ-0042`,
`PRJ0042` and `prj 42` all become `prj42`). Letter/digit spelling variants
(`T90` / `T-90`) are tried as alternative exact titles and alias phrases.

tsqueries are built from whole quoted tokens so PostgreSQL's parser tokenises the
query exactly as it tokenised the indexed text.

## Retrieval channels and tiers

Each channel reads `"SearchEntries"` directly with the active-generation and
authorization predicates inline. This lets PostgreSQL use the matching index (term
btrees, `LEFT(...)` `text_pattern_ops` btrees for prefixes, GIN tsvector, GIN trigram).
Do not reintroduce a shared "authorised rows" CTE: PostgreSQL materialises a CTE that
is referenced many times, and every channel then scans the materialised copy without
indexes.

| Tier | Channels |
|---|---|
| 0 | exact identifier (or identifier key) |
| 1 | exact title (or letter/digit variant), identifier prefix |
| 2 | whole-query title phrase, exact alias term |
| 3 | alias title phrase, all title tokens, Name term, identifier trailing number |
| 4 | title word prefix, title prefix, alias prefix, English-stemmed title match |
| 5 | any-field simple full-text |
| 6 | English body full-text, alias full-text |
| 7 | trigram fallback (only when there are fewer than `FuzzyFallbackStrongCandidateThreshold` lexical candidates) |

Within a channel, term matches are aggregated per entry before ranking. Channel ranks
are fused with reciprocal-rank fusion (`SUM(weight / (k + rank))`).

Results are clustered by canonical entity (a Project with its ToT, Proliferation,
single-project Training and IPR rows). The entity's own row represents the cluster
whenever it matched, unless another member has a strictly better exact match (tier 0
or 1). The cluster is ranked by the best tier and score of any member. The final order
is: tier, then Project rows before other records, then fused score, then event date,
update time and id.

Trigram operators (`%`, `<%`) use pg_trgm session thresholds. The engine sets
`FuzzyThreshold` and `FuzzyWordSimilarityThreshold` with `SET LOCAL` inside a read
transaction, so pooled connections are never left changed. Record-level fuzzy matching
uses `word_similarity` per query token, because `similarity()` against long text never
reaches a useful threshold.

## Paging

`NextCursor` is a keyset over the ranked order (tier, entity priority, rounded score,
event date, update time, id), bound to the query, filters and index generation. A
cursor that no longer applies (index regenerated, different query, or a pre-keyset
cursor) returns the first page with `ExecutionStatus = StaleCursor` rather than
silently restarting.

## Autocomplete and did-you-mean

Autocomplete is one statement: a union of index-backed candidate predicates, then
scoring of those candidates only. It never calls spelling correction, and repeated
identical titles are shown once.

Did-you-mean runs only for committed searches whose first page has no lexical match.
It makes one batched query against `"SearchVocabularyTerms"`. That table is kept up to
date by triggers from entry titles and Name, Location and Organisation terms; Person
terms and body text are not included. Only entries the user may search contribute.

## Measuring

`ProjectManagement.Tests/Search/SearchV2RelevanceCaptureTests.cs` is an opt-in harness.
It runs a relevance dataset against a database copy, records results, median latency
and `auto_explain` plans, and scores them with `tools/search-v2-relevance-evaluator.mjs`.
See `tools/README-search-v2-relevance.md`. Keep datasets and outputs that contain
corpus text outside the repository.
