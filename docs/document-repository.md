# Document Repository

The Document Repository (`/DocumentRepository/Documents`) is PRISM's office library of PDFs: letters, policies, minutes and other papers, searchable by their metadata and by the text inside them. This page records the rules the code enforces. How the pages look and behave is in [Document Repository pages](ux/document-repository.md); OCR and full-text search internals are in [Search and OCR architecture](search-and-ocr-architecture.md).

Code lives in `Areas/DocumentRepository`, `Services/DocRepo` and `Hosted/DocRepo*`.

## Who can read what

Every read path (the list, the reader, the inline PDF view, download and favourites) filters documents through `IDocRepoAccess` (`Services/DocRepo/DocRepoAccess.cs`), so the rule is applied in one place.

| Document state | Who can read it |
| --- | --- |
| Active | Every signed-in user (`DocRepo.View`). |
| Deactivated | Only `DocRepo.ViewInactive` holders: everyone who can deactivate or edit documents (Project Office, Main Office, MC Cell and IT Cell clerks, Admin, HoD, TA, ITO, MCO). Everyone else gets *not found*, including from a direct link. |
| Deleted (in the trash) | Nobody, through the reader, view or download. Admins see it on the Trash page. |

- **Include inactive** in the filters is shown only to `DocRepo.ViewInactive` holders; the `includeInactive` query value is ignored for anyone else.
- **Global search** (`GlobalDocRepoSearchService`) and Search V2 return active documents only. Managers find deactivated documents from the repository list.
- **Documents copied in from other modules.** FFC, Activities, ARPP, IPR and the admin PDF ingestion copy their PDFs in with `IsExternal = true` (`DocRepoIngestionService`). They are hidden from the repository list but, like any active document, readable by every signed-in user by link and through global search. This is a deliberate decision (October 2026): those source modules are themselves open to all signed-in users. If a source module is ever restricted, its copies must be authorised through that module's policy instead, using the `DocRepoExternalLinks` rows that record each copy's source.
- Ingestion of a PDF whose hash matches a document in the trash links the source to that row and leaves it in the trash; deletion was approved by someone, so ingestion never undoes it.

## What is audited

Audit rows go to `DocRepoAudits` (`IDocRepoAuditService`). Changes are saved in the same `SaveChanges` (or transaction) as their audit row, so a change is never left without its record.

| Event | When |
| --- | --- |
| `Uploaded`, `Reuploaded`, `OcrQueued` | Upload; re-uploading the PDF of a trashed document restores that row. |
| `Viewed` | Opening the reader or the inline PDF ("Open in new tab"). Counted once per user and document within 5 minutes, so the reader and the PDF it embeds record one row. |
| `Downloaded` | Download, with the same 5-minute de-duplication. |
| `MetadataEdited` | Edit metadata; details hold each changed field as `[from, to]`. |
| `Deactivated`, `Activated`, `DeleteRequested` | The document's ⋮ menu (Manage). |
| `DeleteRejected`, `SoftDeleted` | A decision in the Decision Centre. |
| `RestoredFromTrash`, `Purged` | Trash page, or the retention worker (`system`). |
| `OcrRequeued` | Retry from the reader, or Re-queue on the OCR failures page. |
| `CategoryChanged` | Office or document category created, updated, activated or deactivated (no document id). |

A failure to write a `Viewed` or `Downloaded` row is logged and never blocks reading the document.

## Upload rules

- PDF only: `.pdf` extension, `application/pdf` content type and a `%PDF-` header.
- The size limit comes from `ProjectDocuments:MaxSizeMb`. `DocRepo:MaxFileSizeBytes` is not read by the upload page; it is kept for compatibility.
- The upload is streamed to a private temp file while it is hashed (never held in memory) and scanned with the registered `IFileScanner`. The registered scanner is `NoopFileScanner`, so no malware scanning takes place on the LAN deployment today.
- A duplicate (same SHA-256 as an existing, non-deleted document) is rejected **before** anything is written to storage. Re-uploading a trashed document's PDF restores that row and deletes its old file once the database write commits.
- Several PDFs at once are sent one per request to `Upload?handler=Json`, which runs exactly the same checks as the form post (`UploadModel.SaveUploadAsync`) and answers `{ ok, id, restored, url }` or `422` with the errors by field. The per-user upload rate limit (`DocumentUploadRateLimiting`, 20 a minute by default) applies to each one; a throttled file waits and is retried by the page.

## Changing documents

- **Edit metadata** carries a fingerprint of the values the form opened with. If someone else saved different values meanwhile, the save is refused and their version is shown; nothing is overwritten silently.
- **Delete** is a request plus a decision. The request needs a reason, which the approver sees in the Decision Centre. The requester cannot decide their own request (`Forbidden`), and the request is claimed with a conditional update so only one approver's decision applies.
- **Deactivate** hides the document from everyone except `DocRepo.ViewInactive` holders; it is not deletion.

## Covers

- The library shows each document's first page as a cover (`Documents/Cover?id=…`). It follows the same access rule as the PDF (`IDocRepoAccess`): a document the user may not open has no cover either (404). Covers are not audited; they show what the list already shows.
- `DocRepoCoverService` draws a cover the first time it is asked for, with Poppler's `pdftoppm` (page 1, 360px wide, JPEG), and keeps it in `<DocRepo:RootPath>/covers/<2 hex>/<id>.jpg`. At most two are drawn at once and one per document; a drawing has 30 seconds. Purging a document deletes its cover. Covers can be deleted at any time; they are drawn again.
- Without Poppler, with `DocRepo:EnableCovers=false`, or for a PDF that cannot be drawn (damaged, encrypted), the page shows a cover typeset in the document type's colour instead. A PDF that failed is marked (`<id>.jpg.failed`) and tried again after 12 hours (a drawing that only timed out is not marked); when `pdftoppm` cannot be started at all, covers are skipped for 10 minutes and nothing is marked, so installing Poppler later needs no clean-up.
- Configure the executable with `DocRepo:PdfToPpmPath` when it is not on the server's `PATH` (for example `C:\poppler\Library\bin\pdftoppm.exe`).

## Search shortcuts and quick search

- `type:`, `office:`, `tag:`, `year:` and `from:` typed in the search box are turned into the ordinary filters by a redirect (`DocRepoQueryTokens`, `IndexModel.ShortcutUrlAsync`). `from` filters on *Received from* (contains, case-insensitive).
- `Index?handler=Quick` answers the quick search: active, readable library documents whose title contains the text or whose search vector matches every word with the last as a prefix (`DocumentSearchService.PrefixQuery`; only letters and digits reach `to_tsquery`). Deactivated documents are left out of quick search for everyone.
- Search results carry the page of the first match when the OCR text has page breaks (ocrmypdf's sidecar separates pages with a form feed); the reader opens there with the words highlighted.

## Caching

The inline view, the download and the cover image all send `Cache-Control: private, no-store`, so neither a PDF nor its first page is kept in the browser cache of shared LAN machines after a logout, or after the document is deactivated or deleted. Covers are small (360px JPEGs) and served on the LAN, so fetching them again on each list view costs little. What the browser remembers for convenience (recently opened documents, the last page read, dark reading, the details panel) is kept in `localStorage` under a per-user key (`DocRepoFormat.UserKey`, a hash of the user id) and is never used for access.

## Tests

- `ProjectManagement.Tests/DocRepo/*Tests.cs`: access rules, audit, upload (form and queue), edit, ingestion, OCR re-queue, snippet encoding, covers (`DocRepoCoverServiceTests` runs the real `pdftoppm` when it is installed), search shortcuts and the shelves (in-memory database).
- `ProjectManagement.Tests/DocRepo/DocRepoPostgresIntegrationTests.cs`: OCR queue claims, delete-approval decisions, real `ts_headline` snippets, the page of a match and quick search on PostgreSQL. Run by the *Production Database Migration Gate* workflow; locally set `PRISM_RUN_POSTGRES_MIGRATION_TESTS=true` and `PRISM_TEST_POSTGRES_CONNECTION`.
