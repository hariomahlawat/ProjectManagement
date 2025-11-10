# Global search integration

To surface a module in the global search experience:

1. **Ingest PDF attachments into the document repository.**
   - After persisting the attachment to the module's own storage, call `IDocRepoIngestionService.IngestExternalPdfAsync` with the source module name and the attachment identifier.
   - The ingestion service deduplicates files by SHA-256 hash and schedules OCR through the existing document repository pipeline.

2. **Expose structured search results.**
   - Implement an `IGlobal*SearchService` inside `ProjectManagement.Services.Search` (for example `GlobalFfcSearchService`).
   - Query the module's records using `EF.Functions.ILike` and map matches to `GlobalSearchHit` with a meaningful title, snippet, score, and URL.
   - Register the service in `Program.cs` so the orchestrator can fan out to it.

3. **Keep everything server rendered.**
   - The Razor page under `Areas/Common/Pages/Search` renders all results without inline scripts to satisfy the CSP requirements.

Following these steps keeps every module indexed consistently without duplicating OCR or search infrastructure.
