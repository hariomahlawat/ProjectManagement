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

Short searches: the gateway answers a search with fewer than two letters or digits with
`IsTooShort` and runs no engine. Contains (`%x%`) and word-prefix (`x:*`) matching need
three characters, because below that the trigram index cannot serve them and they match
almost every entry; whole-word and title-prefix matching still run for two.

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
| 7 | near-miss (trigram) fallback: only when there are fewer than `FuzzyFallbackStrongCandidateThreshold` lexical candidates inside the active filters, and only for words of four letters or more |

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
transaction, so pooled connections are never left changed. Words under four letters
(`SearchEngine.MinimumNearMissCharacters`) take no part: at the word-similarity threshold a
three-letter word shares half its trigrams with every word starting with the same two
letters ("drn" matched 123 records on the development data). When the best result is a near
miss (tier 7), every result is, and the page says "No exact matches. Showing similar wording." Record-level fuzzy matching
uses `word_similarity` per query token, because `similarity()` against long text never
reaches a useful threshold.

## Paging

`NextCursor` is a keyset over the ranked order (tier, entity priority, rounded score,
event date, update time, id), bound to the query, filters and index generation. A
cursor that no longer applies (index regenerated, different query, or a pre-keyset
cursor) returns the first page with `ExecutionStatus = StaleCursor` rather than
silently restarting.

The cursor also carries a stamp of the records that matched (their count and the sum of
their ids). Fusion ranks each record against the other matches, so re-indexing one
matching record can move others across the cursor, and continuing would skip or repeat
rows. Re-indexing deletes and re-inserts an entry under a new id, so the stamp changes
exactly then, and the next page starts from the top as `StaleCursor`. Changes to records
that do not match leave paging alone. Cursors issued before the stamp existed continue
as before.

## Autocomplete and did-you-mean

Autocomplete is one statement: a union of index-backed candidate predicates, then
scoring of those candidates only. Each predicate passes on at most 200 candidates
(`SearchEngine.SuggestionCandidatesPerBranch`), taken in the final ranking's own tie-break
order (projects, closeness, most recently updated), so a common prefix no longer scores
thousands of rows per keystroke and the six suggestions shown are the same. It never calls
spelling correction, and repeated identical titles are shown once.

Did-you-mean runs only for committed searches whose first page has no lexical match.
It makes one batched query against `"SearchVocabularyTerms"`. That table is kept up to
date by triggers from entry titles and Name, Location and Organisation terms; Person
terms and body text are not included. Only entries the user may search contribute.

Each normalised token is checked against its own typed spelling, found inside the word it
came from: in "anti-tank ATGM" the acronym "ATGM" is protected and "anti" and "tank" are
not. If the typed text cannot be matched to the tokens, nothing is corrected. The
correction replaces tokens in place, so joiners stay, and is offered in the typed text with
each corrected word in the case it was typed: "IR&D hydrbad" offers "IR&D hyderabad", and
"Hydrbad Trials" offers "Hyderabad Trials".

## Excluded words

A word typed with a leading minus ("simulator -drone") is an exclusion, as in web search. It
is taken out of the words searched for and highlighted, and records whose exact or English
vector contains it are left out of results, counts and facets (`candidate_scored`). A minus
inside a word ("anti-tank") is not an exclusion, and a query of nothing but exclusions is
searched as typed. Before, the minus was folded away and the word searched for, so the query
returned exactly the records it meant to exclude.

## Excerpts

Document text is matched by English stem, so the excerpt is built the same way. Each typed
word is paired with its stem from PostgreSQL's English stemmer (`query_stems`; stop words
are dropped): the excerpt window starts before the first word-start hit of a typed word ("ir"
in "IR sensor", never in "their") or of a stem followed by an inflection ending
(`SearchInflections`, never "Stationery" for "stations"), found with one regular expression
per row, and `ts_headline` runs with the English
parser on an OR of the typed words (with AND it only builds fragments around a passage that
holds every word, and fell back to the opening words when they were far apart), and the stems
are matched as the same inflections when the excerpt is chosen, highlighted and explained
("Found in"). For "acceptance report" on the
development data, results showing the matched words went from 5 of 20 to 20 of 20; the
excerpt work adds about 35–50 ms to a page of long documents. The excerpt shown starts a few
words before the first match rather than centring it, because phones show two lines.

## Measuring

`ProjectManagement.Tests/Search/SearchV2RelevanceCaptureTests.cs` is an opt-in harness.
It runs a relevance dataset against a database copy, records results, median latency
and `auto_explain` plans, and scores them with `tools/search-v2-relevance-evaluator.mjs`.
See `tools/README-search-v2-relevance.md`. Keep datasets and outputs that contain
corpus text outside the repository.

Measured 15 Nov 2026 on a copy of the development index grown twenty times (34,160
entries): a word found in most records ("projects", 22,000 candidates) took 1.47 s; the
same search after PR 9 of `docs/ux/search-review-fixes.md` takes about 1.0 s with the same
results, totals and suggestions. What remains is split between the retrieval channels
(each ranks every match, about 0.55 s) and clustering the candidates. Two rules keep it
there:

- Candidate rows stay narrow. Clustering and every facet sort all candidates, so titles,
  text and `MetadataJson` are read for the final page only (and, by id, for the stage
  filter and stage facet).
- Counts use a `DISTINCT` subquery, never `COUNT(DISTINCT ...)`, which PostgreSQL always
  sorts with the text collation; a `DISTINCT` subquery can hash. This was most of the gain.

A per-channel limit was measured against this and not adopted: filters apply after
retrieval, so a limit would drop filtered results ranked beyond it (an IPR tab missing
IPR records) and make every count a lower bound, for a gain that shows only at many times
the current index size.
