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

## Changing documents

- **Edit metadata** carries a fingerprint of the values the form opened with. If someone else saved different values meanwhile, the save is refused and their version is shown; nothing is overwritten silently.
- **Delete** is a request plus a decision. The request needs a reason, which the approver sees in the Decision Centre. The requester cannot decide their own request (`Forbidden`), and the request is claimed with a conditional update so only one approver's decision applies.
- **Deactivate** hides the document from everyone except `DocRepo.ViewInactive` holders; it is not deletion.

## Caching

The inline view and download send `Cache-Control: private, no-store`, so PDFs are not kept in the browser cache of shared LAN machines after they are deactivated or deleted.

## Tests

- `ProjectManagement.Tests/DocRepo/*Tests.cs`: access rules, audit, upload, edit, ingestion, OCR re-queue and snippet encoding (in-memory database).
- `ProjectManagement.Tests/DocRepo/DocRepoPostgresIntegrationTests.cs`: OCR queue claims, delete-approval decisions and real `ts_headline` snippets on PostgreSQL. Run by the *Production Database Migration Gate* workflow; locally set `PRISM_RUN_POSTGRES_MIGRATION_TESTS=true` and `PRISM_TEST_POSTGRES_CONNECTION`.
