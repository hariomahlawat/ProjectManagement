# Document Repository full-text search: design note

**Status: implemented.** This was the original design brief for PostgreSQL FTS on the Document
Repository. The implementation follows it with the differences listed below. For the current,
authoritative description see `docs/search-and-ocr-architecture.md`.

## Design decisions that were kept
- **OCR text lives in a sibling 1:1 table**, not on the main document row, so list queries stay
  lean. Implemented as `DocumentText` (table `DocRepoDocumentTexts`, PK/FK `DocumentId`, columns
  `OcrText`, `UpdatedAtUtc`).
- **One `SearchVector` tsvector on the main `Documents` table**, maintained by database triggers
  that pull OCR text from the sibling table, with a single GIN index (`idx_docrepo_documents_search`).
- **Weighted fields**: subject and received-from (A), tag names (B), office and document
  category names (C), OCR text (D), so metadata matches outrank body matches.
- **Search behind an interface**: `IDocumentSearchService` / `DocumentSearchService`.

## Differences from the brief
| Brief | Implementation (migration `20260115000000_AddDocRepoFullTextSearch`) |
| --- | --- |
| Table name `docrepo_document_texts`, column `search_vector` | EF-style names: `DocRepoDocumentTexts`, `Documents."SearchVector"` |
| Denormalise tags into a column | Tags are re-aggregated by trigger: `docrepo_document_tags_search_vector_after` on `DocumentTags` |
| `plainto_tsquery` / `to_tsquery` | `websearch_to_tsquery('english', q)` (supports quotes, `OR`, `-term`) |
| `ts_rank_cd` ordering | `ts_rank_cd` via EF `RankCoverDensity`, then document date, then created date |
| Text search config to be chosen | `english`, fixed inside the builder function and the query service |

Additional pieces not in the brief: `ts_headline` snippets over OCR text, an OCR worker
(`Hosted/DocRepoOcrWorker`) that writes to `DocumentText` (the write fires the trigger), and
inclusion of DocRepo documents in global search through Search V2 (`DocRepoDocument` projections,
policy `DocRepo.View`).

## Not implemented
- No Elasticsearch or alternative engine. Search V2 (also PostgreSQL) is the current global
  search platform; see `docs/global-search.md`.
