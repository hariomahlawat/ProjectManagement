# PRISM Search V2 relevance benchmark

The benchmark is intentionally corpus-grounded. Do not invent expected entity IDs just to fill the dataset.

## Recommended dataset

Start with at least 150 reviewed queries:

- 50 **golden navigation** queries: exact Project names, aliases, case/file numbers, PPP/ARPP identifiers, IPR identifiers, and known document titles.
- 100 broader relevance queries: technical concepts, organisation/location/person names, abbreviations, hyphenation variants, controlled misspellings, Project capability/specification terms, and OCR-only text.

Expand toward 250–300 queries once the first set is stable.

Use `tools/search-v2-relevance-dataset.schema.json` to validate the dataset structure.

## Metrics

`tools/search-v2-relevance-evaluator.mjs` reports:

- Exact navigation Rank@1
- MRR@10
- nDCG@10
- Recall@20

Use the benchmark to tune weights; do not tune individual queries by hard-coding special cases unless the rule is a general search-language rule (for example, exact identifiers outrank fuzzy text).

## Acceptance intent

The golden exact-navigation set should be essentially deterministic. The wider benchmark should show Search V2 materially outperforming Legacy on MRR/nDCG while maintaining zero authorization leakage.
