# Search and OCR Architecture

This document describes how PRISM extracts text from documents (OCR), how PostgreSQL
full-text search (FTS) is wired for the two document stores, and how global search works.
It reflects the current code; file and type names are given instead of line numbers.

PostgreSQL is required for every FTS and Search V2 path. The code short-circuits
(returns no FTS results or skips vector maintenance) on other providers.

## 1. Components at a glance

| Concern | Main types / files |
| --- | --- |
| Document Repository (DocRepo) OCR | `Hosted/DocRepoOcrWorker`, `Services/DocRepo/OcrmypdfDocumentOcrRunner`, `Services/DocRepo/DocumentOcrService` |
| Project document OCR / text extraction | `Hosted/ProjectDocumentOcrWorker`, `Services/Projects/OcrmypdfProjectOcrRunner`, `IProjectDocumentTextExtractor` |
| Shared OCR pipeline | `Services/Ocr/OcrmypdfSharedRunner`, `PdfPigTextExtractor`, `ProcessOcrmypdfInvoker`, `OcrTextUtilities`, `OcrTextLimiter` |
| One-off banner-text backfill | `Hosted/OcrTextBackfillWorker`, `Services/Ocr/OcrTextBackfillService` |
| DocRepo FTS query (repository page) | `Services/DocRepo/DocumentSearchService` |
| Global search (user-facing) | `Areas/Common/Pages/Search/Index` -> `Services/SearchV2/Query/SearchGateway` |
| Search V2 engine and index | `Services/SearchV2/**` (see `docs/global-search.md`) |
| Legacy global search (fallback/shadow only) | `Services/Search/GlobalSearchService` and the `Global*SearchService` providers |

## 2. Shared OCR pipeline

`OcrmypdfSharedRunner.RunAsync` is used by both document stores:

1. Copies the source PDF into the work area (`<WorkRoot>/<input>`), unless it is already there.
2. **Embedded-text fast path**: `PdfPigTextExtractor` reads the PDF text layer. If it contains
   useful text (after `OcrTextUtilities.CleanBanners` strips ocrmypdf "OCR skipped on page" /
   "Prior OCR" banners), that text is returned and `ocrmypdf` is not run.
3. Otherwise runs `ocrmypdf --skip-text --sidecar ...`; if the sidecar has no useful text it
   escalates to `--force-ocr`, then `--redo-ocr`.
4. Missing sidecar output or banner-only text is returned as a failure whose message points to
   the log file.
5. Each run writes a unique log file under the logs folder and mirrors it to `<documentId>.log`.
   Temporary input/output/sidecar files are deleted in `finally`.

`ProcessOcrmypdfInvoker` starts the process with `UseShellExecute=false` and waits for exit.
There is **no per-run timeout**, and the child process is not killed on cancellation.

### Executable and folders

| Store | Options section | Keys |
| --- | --- | --- |
| DocRepo | `DocRepo` (`DocRepoOptions`) | `OcrExecutablePath` (optional; defaults to `ocrmypdf` on `PATH`), `OcrWorkRoot` (required when the worker is enabled), `OcrInput`, `OcrOutput`, `OcrLogs`, `EnableOcrWorker` |
| Project documents | `ProjectDocuments:Ocr` (`ProjectDocumentOcrOptions`) | `OcrExecutablePath`, `WorkRoot` (required), `InputSubpath`, `OutputSubpath`, `LogsSubpath`, `EnableWorker` |

A configured executable path that does not exist throws `InvalidOperationException` when the
runner is constructed. The Windows/Linux host must have `ocrmypdf` (and Tesseract/Ghostscript)
installed for scanned PDFs; PDFs with a text layer never need it.

## 3. Document Repository OCR and FTS

### Data model
- `Data/DocRepo/Document` holds metadata, `OcrStatus` (`DocOcrStatus`: `None`, `Pending`,
  `Succeeded`, `Failed`), `OcrFailureReason`, `OcrLastTriedUtc`, and the `SearchVector` tsvector.
- OCR text is stored separately in `DocumentText` (table `DocRepoDocumentTexts`), keyed by `DocumentId`.

### Queueing
Documents are set to `Pending` on upload (`Areas/DocumentRepository/Pages/Documents/Upload`),
on re-upload of an identical hash, from `Manage`, and on external ingestion
(`DocRepoIngestionService.IngestExternalPdfAsync`, called by FFC, IPR, ARPP, Activities and the
admin PDF ingestion coordinator).

### Worker (`DocRepoOcrWorker`, registered when `DocRepo:EnableOcrWorker` is true, default true)
- Picks up to **3** non-deleted `Pending` documents, oldest `CreatedAtUtc` first.
- Idle poll: **2 minutes**. Unhandled loop error: waits **30 seconds**.
- Success: stores text capped at **200,000 characters**, sets `Succeeded`.
- Failure/exception: sets `Failed`, reason trimmed to **1,000 characters**, clears stored text.
- All documents in the batch are saved with one `SaveChangesAsync` at the end of the batch.
- There is no automatic retry of `Failed` documents.

### Manual requeue
`Areas/DocumentRepository/Pages/Admin/OCRFailures` (policy `DocRepo.DeleteApprove`) calls
`DocumentOcrService.ReprocessAsync`, which marks the document `Pending`, saves, and then runs OCR
**synchronously inside the HTTP request**.

### FTS wiring (migration `20260115000000_AddDocRepoFullTextSearch`)
- Function `docrepo_documents_build_search_vector(...)`: Subject (A), ReceivedFrom (A), tag names (B),
  office and document category names (C), OCR text (D), `english` configuration.
- Triggers: `docrepo_documents_search_vector_before` on `Documents`,
  `docrepo_document_tags_search_vector_after` on `DocumentTags`,
  `docrepo_document_texts_search_vector_after` on `DocRepoDocumentTexts`.
- GIN index `idx_docrepo_documents_search`.

### Repository search (`DocumentSearchService`)
Used by the Document Repository list page. `websearch_to_tsquery('english', q)` filter,
`ts_rank_cd` (`RankCoverDensity`) ordering, then document date and created date;
`ts_headline` over OCR text with `<mark>` tags, `MaxFragments=2`, `MaxWords=20`.

## 4. Project document OCR and FTS

### Data model
- `Models/ProjectDocument`: `OcrStatus` (`ProjectDocumentOcrStatus`: `None`, `Pending`, `Succeeded`,
  `Failed`, `Skipped`), `OcrFailureReason`, `OcrLastTriedUtc`, `SearchVector`.
- OCR/extracted text in `Data/Projects/ProjectDocumentText` (table `ProjectDocumentTexts`).

### Queueing
`Services/Documents/DocumentService` sets `Pending` on publish, on file replacement (and purges old
text), and on manual retry (`Pages/Projects/Documents/RetryOcr`).

### Worker (`ProjectDocumentOcrWorker`, registered when `ProjectDocuments:Ocr:EnableWorker` is true)
- Picks up to **5** `Published`, non-archived, `Pending` documents by `UploadedAtUtc`.
- PDFs go through the shared OCR runner; output that contains only skip banners is a failure
  ("OCR produced only a skip message.").
- Non-PDF Office files go through `IProjectDocumentTextExtractor` (options
  `ProjectDocuments:TextExtraction`). If a PDF derivative is produced it is also OCRed and the
  texts are combined. Unsupported types become `Skipped`; no extractable text becomes `Skipped`.
- Same caps as DocRepo (200,000 text / 1,000 reason). Saves **per document**, then refreshes that
  row's `SearchVector` with an explicit parameterised `UPDATE` (weights identical to the trigger).
- Idle poll 2 minutes; loop error wait 30 seconds.

### FTS wiring
The current definition is installed by `20261201140000_ConsolidateProductionSchemaMaintenance`
(earlier migrations `20260922120000_RestoreProjectDocumentFullTextSearch`,
`20261001090000_FixProjectDocumentSearchVector` and `20261022100000_AddProjectDocumentOcrPipelineFix`
defined intermediate versions):
- Function `project_documents_build_search_vector(document_id, title, description, stage_id, original_file_name)`:
  Title (A), Description (B), OriginalFileName (C), StageCode (C), OCR text (D).
- Triggers `project_documents_search_vector_trigger` (on `ProjectDocuments`) and
  `project_document_texts_search_vector_after` (on `ProjectDocumentTexts`).
- GIN index `IX_ProjectDocuments_SearchVector` (older databases may have `idx_project_documents_search`).
- At startup `ProjectDocumentSearchVectorMaintenance.ValidateAsync` only verifies that these
  objects exist; creation and repair are migration-owned.

## 5. OCR banner backfill

`OcrTextBackfillWorker` runs once at startup only when `OcrBackfill:Enabled=true` (default false).
It finds stored OCR text containing ocrmypdf banners in either store and reprocesses those
documents through the current pipeline.

## 6. Global search

Global search is served by **Search V2** through `ISearchGateway`; the legacy fan-out service is
retained only as a fallback and for shadow comparison. See `docs/global-search.md` for the full
design (projection index, triggers, authorization, rollout flags, operations).

Legacy fan-out (`GlobalSearchService`), when it runs, queries seven providers concurrently in
separate DI scopes: Document Repository (FTS rank), FFC, IPR, Activities, Projects, Project
documents (FTS `ts_rank_cd`), and Project Office trackers (Visits, Social media, Training, TOT,
Proliferation). Non-FTS providers use fixed heuristic scores. Hits are de-duplicated by URL and
ordered by score then date. `SearchGateway` removes legacy hits the user is not authorised to see
(Document Repository, IPR, Visits, Training, TOT and Proliferation policies) before returning them.

## 7. Operational notes and limitations
- OCR latency depends on queue depth; both workers process small batches and poll every 2 minutes when idle.
- Stored OCR text is truncated to 200,000 characters.
- `Failed` documents are not retried automatically; use the DocRepo OCR failures page or the
  project document Retry OCR action.
- A hung `ocrmypdf` process blocks its worker indefinitely (no timeout).
- If saving OCR results itself fails (for example text containing a NUL character, which
  PostgreSQL `text` columns reject), the document stays `Pending` and is re-OCRed on every loop.
- Search dashboards: `Services/Dashboard/SearchHealthService` reports pending/failed OCR and
  Search V2 index health.
