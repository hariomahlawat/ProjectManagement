# Search and OCR Architecture

## 1. Overview

This document explains how the application performs OCR and full-text search (FTS) for two distinct document types-Document Repository items and Project Documents-and how the global search layer fans out across modules, merges scores, and orders results. All behaviours, limits, and database objects are taken directly from the current codebase.

## 2. DocRepo OCR + FTS Pipeline

### 2.1 Data model
- **Entities**: `Document` stores metadata (including `OcrStatus`, `OcrFailureReason`, `OcrLastTriedUtc`, and optional `SearchVector`), while OCR text lives in `DocumentText` keyed by `DocumentId`.[F:Data/DocRepo/Document.csL9-L63][F:Data/DocRepo/DocumentText.csL6-L17]
- **Status enum**: `DocOcrStatus` values are `None`, `Pending`, `Succeeded`, and `Failed`.[F:Data/DocRepo/Document.csL9-L14]

### 2.2 OCR queueing
- **When set to Pending**: External ingestion seeds new `Document` rows with `OcrStatus = Pending` during save, so every upload is queued immediately.[F:Services/DocRepo/DocRepoIngestionService.csL150-L175]

### 2.3 OCR worker
- **Eligibility**: Picks documents that are **not deleted** and have `OcrStatus == Pending`, loading existing OCR text for overwrite if needed.[F:Hosted/DocRepoOcrWorker.csL34-L39]
- **Batching & ordering**: Processes the **oldest first** (`OrderBy CreatedAtUtc`) and caps each loop to **3 documents** via `Take(3)`.[F:Hosted/DocRepoOcrWorker.csL34-L39]
- **Polling**: Sleeps **2 minutes** when no work is found; on unexpected errors it waits **30 seconds** before retrying.[F:Hosted/DocRepoOcrWorker.csL41-L45][F:Hosted/DocRepoOcrWorker.csL107-L110]
- **Retry/failure handling**: Any runner failure or exception sets status to `Failed`, trims the reason to **1000 characters**, clears stored OCR text, and logs the error; success clears the failure reason and marks `Succeeded`.[F:Hosted/DocRepoOcrWorker.csL55-L98][F:Hosted/DocRepoOcrWorker.csL115-L134]

### 2.4 OCR runner
- **Tool**: `ocrmypdf` invoked via `ProcessStartInfo` with redirected output; initial pass uses `--sidecar`.[F:Services/DocRepo/OcrmypdfDocumentOcrRunner.csL57-L68][F:Services/DocRepo/OcrmypdfDocumentOcrRunner.csL170-L198]
- **Two-pass / three-pass logic**: Tagged PDFs trigger a second `--skip-text` run; if that text lacks content, a forced third pass (`--force-ocr`) is executed. Prior OCR detection (`ExitCode == 6` or message) triggers a forced second pass directly. Sidecar absence or unusable text returns failure with log pointers.[F:Services/DocRepo/OcrmypdfDocumentOcrRunner.csL70-L150][F:Services/DocRepo/OcrmypdfDocumentOcrRunner.csL200-L223]
- **Logs**: Each run writes to a unique log file and mirrors to a stable `<docId>.log` for review.[F:Services/DocRepo/OcrmypdfDocumentOcrRunner.csL63-L107]

### 2.5 Text persistence
- **Storage**: OCR text is saved to `DocumentText.OcrText`; the helper caps stored text to **200,000 characters** before persistence.[F:Hosted/DocRepoOcrWorker.csL55-L66][F:Hosted/DocRepoOcrWorker.csL126-L134]
- **Failure reasons**: Truncated to **1000 characters** to keep errors bounded.[F:Hosted/DocRepoOcrWorker.csL71-L88][F:Hosted/DocRepoOcrWorker.csL115-L123]

### 2.6 FTS wiring in DB
- **Search vector builder**: `docrepo_documents_build_search_vector(document_id, subject, received_from, office_category_id, document_category_id)` composes weighted fields-Subject (A), ReceivedFrom (A), Tag names (B), Office+Category names (C), and OCR text (D).[F:Migrations/20260115000000_AddDocRepoFullTextSearch.csL63-L100]
- **Triggers**:
  - `docrepo_documents_search_vector_before` (BEFORE INSERT/UPDATE on `Documents`) calls the builder.[F:Migrations/20260115000000_AddDocRepoFullTextSearch.csL104-L127]
  - `docrepo_document_tags_search_vector_after` (AFTER INSERT/UPDATE/DELETE on `DocumentTags`) rebuilds affected vectors.[F:Migrations/20260115000000_AddDocRepoFullTextSearch.csL129-L163]
  - `docrepo_document_texts_search_vector_after` (AFTER INSERT/UPDATE/DELETE on `DocRepoDocumentTexts`) refreshes vectors when OCR text changes.[F:Migrations/20260115000000_AddDocRepoFullTextSearch.csL165-L199]
- **Index**: `idx_docrepo_documents_search` is a **GIN** index on `Documents.SearchVector`.[F:Migrations/20260115000000_AddDocRepoFullTextSearch.csL212-L217]

### 2.7 Search and ranking
- **Query parser**: `websearch_to_tsquery('english', preparedQuery)` via `SearchVector.Matches` filters results.[F:Services/DocRepo/DocumentSearchService.csL37-L59]
- **Rank function**: `RankCoverDensity` orders results before dates; projected queries surface rank as a `double?`.[F:Services/DocRepo/DocumentSearchService.csL44-L49][F:Services/DocRepo/DocumentSearchService.csL75-L101]
- **Snippet generation**: `ts_headline` uses `<mark>`/`</mark>`, `MaxFragments=2`, `MaxWords=20` against OCR text when present.[F:Services/DocRepo/DocumentSearchService.csL79-L88]
- **Ordering**: Primary sort by rank, then DocumentDate (when present) and CreatedAtUtc for ApplySearch; projected results sort by rank then DocumentDate.[F:Services/DocRepo/DocumentSearchService.csL44-L49][F:Services/DocRepo/DocumentSearchService.csL99-L101]

## 3. Project Documents OCR + FTS Pipeline

### 3.1 Data model
- **Entities**: `ProjectDocument` carries metadata plus OCR status fields and `SearchVector`; OCR text is stored in `ProjectDocumentText` by `ProjectDocumentId`.[F:Models/ProjectDocument.csL10-L77][F:Data/Projects/ProjectDocumentText.csL6-L17]
- **Enums**: `ProjectDocumentOcrStatus` has `None`, `Pending`, `Succeeded`, `Failed`. Published/SoftDeleted states are represented by `ProjectDocumentStatus`.[F:Models/ProjectDocument.csL10-L22]

### 3.2 OCR queueing
- **On publish**: New uploads set `OcrStatus = Pending`, clear failure metadata, and start at FileStamp 1 so every published document is enqueued.[F:Services/Documents/DocumentService.csL221-L266]
- **On replacement**: Overwrites reset OCR fields to `Pending` and purge prior OCR text before saving.[F:Services/Documents/DocumentService.csL333-L347]
- **Manual retry**: Admin-driven retries also set status to `Pending` and clear stored OCR text before requeueing.[F:Services/Documents/DocumentService.csL466-L489]

### 3.3 OCR worker
- **Eligibility**: Processes documents that are **Published**, **not archived**, and `OcrStatus == Pending`, loading OCR text if present.[F:Hosted/ProjectDocumentOcrWorker.csL35-L40]
- **Batching & ordering**: Fetches up to **5 documents** ordered by **UploadedAtUtc** (oldest first).[F:Hosted/ProjectDocumentOcrWorker.csL35-L41]
- **Polling**: Sleeps **2 minutes** when idle; unexpected errors pause the loop for **30 seconds**.[F:Hosted/ProjectDocumentOcrWorker.csL43-L46][F:Hosted/ProjectDocumentOcrWorker.csL158-L162]
- **Failure handling**: Any failure trims the reason to **1000 characters**, nulls OCR text if it existed, sets `Failed`, and logs the warning; success stores OCR text and marks `Succeeded`. Skip-text-only banners are treated as failure with a fixed reason and trigger search-vector refresh.[F:Hosted/ProjectDocumentOcrWorker.csL55-L142][F:Hosted/ProjectDocumentOcrWorker.csL166-L184]

### 3.4 OCR runner
- **Tool and passes**: Uses `ocrmypdf` with a sidecar pass; tagged PDFs invoke a `--skip-text` pass, then a forced pass if needed. Prior OCR detection also forces a rerun. Missing or useless sidecar text results in failure with log references. Logs mirror to a stable `<docId>.log`.[F:Services/Projects/OcrmypdfProjectOcrRunner.csL57-L150][F:Services/Projects/OcrmypdfProjectOcrRunner.csL170-L240][F:Services/Projects/OcrmypdfProjectOcrRunner.csL292-L340]
- **Text quality checks**: `HasUsefulText` ignores banner lines like "OCR skipped on page" or "Prior OCR" before accepting text.[F:Services/Projects/OcrmypdfProjectOcrRunner.csL292-L340]

### 3.5 Text persistence
- **Storage**: OCR text saved into `ProjectDocumentText.OcrText`; insert-or-update path ensures a row exists. Text is capped to **200,000 characters** on save.[F:Hosted/ProjectDocumentOcrWorker.csL86-L105][F:Hosted/ProjectDocumentOcrWorker.csL177-L185]
- **Failure reason limit**: Trimmed to **1000 characters** before storage.[F:Hosted/ProjectDocumentOcrWorker.csL108-L175]

### 3.6 FTS wiring in DB
- **Search vector builder**: `project_documents_build_search_vector(document_id, title, description, stage_id, original_file_name)` weights Title (A), Description (B), OriginalFileName (C), StageCode (C), OCR text (D).[F:Migrations/20260922120000_RestoreProjectDocumentFullTextSearch.csL28-L60]
- **Triggers**:
  - `project_documents_search_vector_trigger` (BEFORE INSERT/UPDATE on `ProjectDocuments`).[F:Migrations/20260922120000_RestoreProjectDocumentFullTextSearch.csL64-L88]
  - `project_document_texts_search_vector_after` (AFTER INSERT/UPDATE/DELETE on `ProjectDocumentTexts`).[F:Migrations/20260922120000_RestoreProjectDocumentFullTextSearch.csL90-L125]
- **Index**: `idx_project_documents_search` GIN index on `ProjectDocuments.SearchVector`.[F:Migrations/20260901093000_AddProjectDocumentOcrPipeline.csL195-L199]
- **Runtime refresh**: Worker refreshes vectors after OCR updates using the same weighted fields and stage/ocr subqueries.[F:Hosted/ProjectDocumentOcrWorker.csL187-L210]

### 3.7 Search and ranking
- **Query parser**: Uses `websearch_to_tsquery('english', query)` with `Matches` filter on `SearchVector`.[F:Services/Search/GlobalProjectDocumentSearchService.csL45-L55]
- **Rank function**: Orders by `ts_rank_cd` (mapped via `ApplicationDbContext.TsRankCd`) then by `UploadedAtUtc`.[F:Services/Search/GlobalProjectDocumentSearchService.csL52-L56][F:Data/ApplicationDbContext.csL136-L153]
- **Snippet generation**: `ts_headline` against OCR text with `<mark>` tags, `MaxWords=25`, `MinWords=10`, `ShortWord=3`, and `FragmentDelimiter=...`.[F:Services/Search/GlobalProjectDocumentSearchService.csL63-L69]
- **Ordering**: Global project document search sorts by rank then upload date before limiting results.[F:Services/Search/GlobalProjectDocumentSearchService.csL52-L75]

## 4. Global Search

### 4.1 Orchestration
- **Fan-out**: The global search service spawns parallel searches for Document Repository, FFC, IPR, Activities, Projects, Project Documents, and Project Reports using separate DI scopes (distinct DbContexts).[F:Services/Search/GlobalSearchService.csL33-L68]
- **Parallelism**: All module tasks run concurrently via `Task.WhenAll`, then results are concatenated.[F:Services/Search/GlobalSearchService.csL59-L77]

### 4.2 Result merging
- **Dedup key**: Results are grouped by **URL** (case-insensitive); within each group the entry with the **highest Score** then **latest Date** is kept.[F:Services/Search/GlobalSearchService.csL84-L93]
- **Score sources**:
  - Document Repository: Postgres FTS rank converted to decimal (`RankCoverDensity`).[F:Services/DocRepo/GlobalDocRepoSearchService.csL55-L99][F:Services/DocRepo/DocumentSearchService.csL75-L101]
  - Project Documents: Postgres `ts_rank_cd` converted to decimal.[F:Services/Search/GlobalProjectDocumentSearchService.csL52-L75]
  - Projects/FFC/IPR/Activities/Reports: fixed heuristic scores (e.g., Projects `0.6m`, FFC records `0.65m`, FFC attachments `0.55m`).[F:Services/Search/GlobalProjectSearchService.csL59-L76][F:Services/Search/GlobalFfcSearchService.csL29-L83][F:Services/Search/GlobalFfcSearchService.csL96-L130]

### 4.3 Final order and tie-breakers
- After deduplication, results are globally sorted by **Score descending** then **Date descending**, preserving module-neutral ranking.[F:Services/Search/GlobalSearchService.csL84-L93]

## 5. Operational Notes
- **Worker cadence**: Both OCR workers poll every **2 minutes** when idle and process small batches (DocRepo: 3, Project Documents: 5), so latency depends on queue depth and OCR runtime.[F:Hosted/DocRepoOcrWorker.csL34-L45][F:Hosted/ProjectDocumentOcrWorker.csL35-L46]
- **Admin visibility**: Failure reasons are capped at **1000 characters**; statuses transition Pending -> Succeeded/Failed with `OcrLastTriedUtc` updated per attempt.[F:Hosted/DocRepoOcrWorker.csL49-L98][F:Hosted/ProjectDocumentOcrWorker.csL49-L142]
- **Common failures**: Runner detects missing sidecar output or "Prior OCR"/"OCR skipped" banners and records precise messages pointing to the stored log path.[F:Services/DocRepo/OcrmypdfDocumentOcrRunner.csL70-L150][F:Services/Projects/OcrmypdfProjectOcrRunner.csL86-L150]

## 6. Limitations and Roadmap
- **PostgreSQL required**: Both OCR/FTS pipelines and global project document search short-circuit if the provider is not PostgreSQL.[F:Migrations/20260115000000_AddDocRepoFullTextSearch.csL13-L19][F:Services/Search/GlobalProjectDocumentSearchService.csL40-L55]
- **OCR text cap**: Stored OCR is truncated to **200,000 characters**, which may drop content from very large PDFs; increasing the cap would require updating the helper methods and reviewing storage costs.[F:Hosted/DocRepoOcrWorker.csL126-L134][F:Hosted/ProjectDocumentOcrWorker.csL177-L185]
- **Heuristic scores across modules**: Non-FTS modules use fixed decimal scores, which may not be directly comparable to FTS ranks; a future roadmap item could normalize scores or apply module weights.

## 7. Developer Reference Appendix

| Area | File/Path | Key elements |
| --- | --- | --- |
| DocRepo data model | `Data/DocRepo/Document.cs` | `DocOcrStatus`, `SearchVector`, metadata columns.[F:Data/DocRepo/Document.csL9-L63] |
| DocRepo OCR text | `Data/DocRepo/DocumentText.cs` | `OcrText`, `UpdatedAtUtc`.[F:Data/DocRepo/DocumentText.csL6-L17] |
| DocRepo queueing | `Services/DocRepo/DocRepoIngestionService.cs` | Sets `OcrStatus = Pending` on ingest.[F:Services/DocRepo/DocRepoIngestionService.csL150-L175] |
| DocRepo worker | `Hosted/DocRepoOcrWorker.cs` | Batch size 3, 2-minute poll, cap 200,000 chars, 1000-char failure trim.[F:Hosted/DocRepoOcrWorker.csL34-L134] |
| DocRepo runner | `Services/DocRepo/OcrmypdfDocumentOcrRunner.cs` | `ocrmypdf` multi-pass logic, log mirroring.[F:Services/DocRepo/OcrmypdfDocumentOcrRunner.csL57-L223] |
| DocRepo FTS | `Migrations/20260115000000_AddDocRepoFullTextSearch.cs` | Builder function, triggers, GIN index `idx_docrepo_documents_search`.[F:Migrations/20260115000000_AddDocRepoFullTextSearch.csL63-L217] |
| Project doc model | `Models/ProjectDocument.cs`, `Data/Projects/ProjectDocumentText.cs` | OCR status enums, search vector, OCR text store.[F:Models/ProjectDocument.csL10-L77][F:Data/Projects/ProjectDocumentText.csL6-L17] |
| Project doc queueing | `Services/Documents/DocumentService.cs` | Pending on publish/replace, retry helper clears OCR text.[F:Services/Documents/DocumentService.csL221-L347][F:Services/Documents/DocumentService.csL466-L489] |
| Project doc worker | `Hosted/ProjectDocumentOcrWorker.cs` | Batch size 5, 2-minute poll, banner detection, caps, vector refresh.[F:Hosted/ProjectDocumentOcrWorker.csL35-L215] |
| Project doc runner | `Services/Projects/OcrmypdfProjectOcrRunner.cs` | `ocrmypdf` multi-pass with sidecar checks and log mirroring.[F:Services/Projects/OcrmypdfProjectOcrRunner.csL57-L240][F:Services/Projects/OcrmypdfProjectOcrRunner.csL292-L340] |
| Project doc FTS | `Migrations/20260922120000_RestoreProjectDocumentFullTextSearch.cs`; `Migrations/20260901093000_AddProjectDocumentOcrPipeline.cs`; worker refresh helper | Builder weights, triggers, `idx_project_documents_search`, runtime refresh SQL.[F:Migrations/20260922120000_RestoreProjectDocumentFullTextSearch.csL28-L136][F:Migrations/20260901093000_AddProjectDocumentOcrPipeline.csL126-L200][F:Hosted/ProjectDocumentOcrWorker.csL187-L210] |
| DocRepo search logic | `Services/DocRepo/DocumentSearchService.cs` | `websearch_to_tsquery`, `RankCoverDensity`, `ts_headline` options.[F:Services/DocRepo/DocumentSearchService.csL37-L101] |
| Project doc search | `Services/Search/GlobalProjectDocumentSearchService.cs` | `ts_rank_cd`, snippet options, ordering.[F:Services/Search/GlobalProjectDocumentSearchService.csL45-L105] |
| Global search orchestrator | `Services/Search/GlobalSearchService.cs` | Parallel fan-out, dedup by URL, order by Score then Date.[F:Services/Search/GlobalSearchService.csL33-L95] |
| Global score sources | `Services/DocRepo/GlobalDocRepoSearchService.cs`; `Services/Search/GlobalProjectDocumentSearchService.cs`; `Services/Search/GlobalProjectSearchService.cs`; `Services/Search/GlobalFfcSearchService.cs` | Rank vs fixed decimal scores across modules.[F:Services/DocRepo/GlobalDocRepoSearchService.csL55-L99][F:Services/Search/GlobalProjectDocumentSearchService.csL52-L105][F:Services/Search/GlobalProjectSearchService.csL59-L76][F:Services/Search/GlobalFfcSearchService.csL29-L130] |
