# PRISM Search V2 relevance benchmark

The benchmark is intentionally corpus-grounded. Do not invent expected entity IDs just to fill the dataset.

## Recommended dataset

Start with at least 150 reviewed queries:

- 50 **golden navigation** queries: exact Project names, aliases, case/file numbers, PPP/ARPP identifiers, IPR identifiers, and known document titles.
- 100 broader relevance queries: technical concepts, organisation/location/person names, abbreviations, hyphenation variants, controlled misspellings, Project capability/specification terms, and OCR-only text.

Expand toward 250–300 queries once the first set is stable.

Use `tools/search-v2-relevance-dataset.schema.json` to validate the dataset structure.

## Capturing a run

`ProjectManagement.Tests/Search/SearchV2RelevanceCaptureTests.cs` captures results, median latencies and
`auto_explain` plans from a real database. It is skipped unless configured:

```powershell
$env:PRISM_SEARCHV2_BENCH_CONNECTION = "Host=localhost;Port=5433;Database=<copy>;Username=...;Password=..."
$env:PRISM_SEARCHV2_BENCH_DATASET = "C:\bench\dataset.json"
$env:PRISM_SEARCHV2_BENCH_OUTPUT = "C:\bench\run"
$env:PRISM_SEARCHV2_BENCH_REBUILD = "1"         # optional: rebuild the index for this build's ProjectionVersion
$env:PRISM_SEARCHV2_BENCH_EXPLAIN = "simulator"  # optional: ';'-separated queries to capture plans for
dotnet test ProjectManagement.Tests --filter FullyQualifiedName~SearchV2RelevanceCaptureTests
node tools/search-v2-relevance-evaluator.mjs C:\bench\dataset.json C:\bench\run\results.json
```

Use a copy of the database. A rebuild replaces the active generation, and another running PRISM instance with a
different `ProjectionVersion` would rebuild it back. Datasets and outputs contain corpus text, so keep them out of
the repository.

## Metrics

`tools/search-v2-relevance-evaluator.mjs` reports:

- Exact navigation Rank@1
- MRR@10
- nDCG@10
- Recall@20

Use the benchmark to tune weights; do not tune individual queries by hard-coding special cases unless the rule is a general search-language rule (for example, exact identifiers outrank fuzzy text).

## Acceptance intent

The golden exact-navigation set should be essentially deterministic. The wider benchmark should show Search V2 materially outperforming Legacy on MRR/nDCG while maintaining zero authorization leakage.
