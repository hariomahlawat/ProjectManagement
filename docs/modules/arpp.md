# ARPP (Annual R&D Procurement Plan) Register and Reports

## Purpose

This module records each HQ-issued ARPP document for a financial year (FY). An FY has one Original and any number of Addenda. For each issue it captures:

- the issued rows: Serial No., PPP No., project reference, category, IPA cost, CFA, Fund and DFPDS schedule
- the issued PDF

A verified issue is published as an organisation-wide immutable snapshot. Published ARPP data is the authority for a project's IPA lifecycle milestone and for the FY project-update reports.

## Routes and pages

| Route | Page model | Policy |
|---|---|---|
| `/ProjectOfficeReports/ARPP` | `Areas/ProjectOfficeReports/Pages/ARPP/Index.cshtml.cs` | `ViewArpp` |
| `/ProjectOfficeReports/ARPP/Create` | `.../ARPP/Create.cshtml.cs` (also `Suggestion` handler) | `ManageArpp` |
| `/ProjectOfficeReports/ARPP/Manage` | `.../ARPP/Manage.cshtml.cs` (working-copy editor) | `ManageArpp` |
| `/ProjectOfficeReports/ARPP/Details?id=` | `.../ARPP/Details.cshtml.cs` | `ViewArpp`. Handlers:<br>`Excel`, `Attachment` (view)<br>`UploadPdf`, `DeletePdf` (`ManageArpp`, checked in the handler)<br>`Verify` (`VerifyArpp`)<br>`Unlock` (`UnlockArpp`) |
| `/ProjectOfficeReports/ARPP/Print`, `/ProjectHistory` | `.../ARPP/Print.cshtml.cs`, `ProjectHistory.cshtml.cs` | `ViewArpp` |
| `/ProjectOfficeReports/ARPP/Reconcile` | `.../ARPP/Reconcile.cshtml.cs` (link rows to PRISM projects) | `ManageArpp` |
| `/Projects/Arpp` (+ `/History`, `/Print`) | `Pages/Projects/Arpp/*.cshtml.cs`, the published library | `[Authorize]` only (any authenticated user). Reads **published snapshots only** |
| `GET /api/arpp/projects?q=&take=` | `Controllers/ArppProjectLookupController` (at most 50 results, non-deleted projects) | `ViewArpp` |
| `/Admin/MasterData/ArppReferences` | `Areas/Admin/Pages/MasterData/ArppReferences/Index.cshtml.cs` (CFA / Fund / DFPDS lists) | `AdminPolicies.MasterDataManage` (Admin) |
| `/Projects/Reports`, `/Projects/Reports/ArppFyUpdate`, `/Projects/Reports/FfcProjectsUpdate` | `Pages/Projects/Reports/*.cshtml.cs` | `ViewArpp` |

## Authorization

Policies are registered in `Program.cs`. Role lists are in `Areas/ProjectOfficeReports/Application/ProjectOfficeReportsPolicies.cs`.

| Policy | Roles |
|---|---|
| `ProjectOfficeReports.ViewArpp` | Admin, HoD, Comdt, ProjectOffice (legacy alias), Project Office, MCO, Project Officer |
| `ProjectOfficeReports.ManageArpp` | `ProjectOfficeManagerRoles`: Admin, HoD, ProjectOffice (legacy alias), Project Office |
| `ProjectOfficeReports.VerifyArpp` | Admin, HoD, Comdt |
| `ProjectOfficeReports.UnlockArpp` | Admin, HoD |

## Main types (`Services/Arpp`)

| Type | Role |
|---|---|
| `ArppCommandService` (`IArppCommandService`) | `CreateIssueAsync`, `SaveWorkspaceAsync`, `VerifyAsync`, `UnlockAsync`. All run inside a transaction, write audit entries after commit, and use row-version concurrency |
| `ArppReadService` | Management workspace reads |
| `ArppLibraryService` (`IArppLibraryService`), `ArppLibrarySearch` | Published library: navigation, document, FY current position, project history, attachment download |
| `ArppAttachmentService`, `FileSystemArppAttachmentStorage` | Issued-PDF upload, replace, delete and download. Optional ingestion into the Document Repository |
| `ArppReconciliationService` | Queue of unlinked rows and linking rows to projects. Linkage is allowed on verified rows (it is PRISM metadata) and is also written to the published snapshot |
| `ArppReferenceDataService` | CFA / Fund / DFPDS option lists (`ArppCfaOption`, `ArppFundOption`, `ArppDfpdsSchedule`) |
| `ArppExportService`, `ArppExcelWorkbookBuilder` | Per-issue Excel (ClosedXML) |
| `AuthoritativeIpaPositionResolver` | Only the latest **published** snapshots count as authoritative. Unverified corrections stay invisible to project pages and dashboards. Falls back to the project-level IPA record until a project is linked |
| `ArppIpaStageSynchronizer`, `ArppIpaStageAuthorityService`, `ArppIpaStageSynchronizationAudit` | Complete the IPA stage on the issue date of the first published ARPP containing the project. `ActualStart` is never invented. A full idempotent pass also runs at startup (`Program.cs`, "ARPP-derived IPA stage reconciliation") |
| `ArppManagedIpaException` | Raised when a user edits an IPA milestone that ARPP manages |

## Data entities (`Models/Arpp`)

- `ArppIssue`:
  - Fields: `FinancialYearStart`, `Kind` (`Original` | `Addendum`), `IssueSequence`, `Name`, `IssueDate`, `IsVerified`, `VerifiedAtUtc`/`By`, `VerificationNote`, `RowVersion`
  - One `ArppAttachment`, many `ArppEntry`, optional `ArppPublishedIssue`
- `ArppEntry`: `SerialNumber`, `PppNumber`, `ProjectReference`, `ProjectId?`, `Category` (`New`, `CommittedLiability`, `CarryForward`, `Delisted`), `IpaCost`, CFA/Fund/DFPDS as text plus optional option FK.
- `ArppPublishedIssue` (key `ArppIssueId`) and `ArppPublishedEntry`: the immutable published copy. Fields: `RevisionNumber` and a pointer to the attachment (`AttachmentStorageKey`, `Sha256`).

## Business rules

- **Original vs Addendum**: an Original must use sequence 0. An Addendum must use sequence > 0. (FY, sequence) is unique; a clash on save becomes a friendly error.
- **Locked when verified**: saving the workspace, uploading a PDF or deleting a PDF on a verified issue is rejected.
- **Verification prerequisites** (`VerifyAsync`):
  - at least one row
  - an attached PDF
  - every row mapped to CFA, Fund and DFPDS options
  - every non-Delisted row has both Serial No. and PPP No.
- **Verify publishes**: verification creates or replaces the published snapshot, increments `RevisionNumber` and synchronizes IPA stages for affected projects.
- **Unlock** needs a reason of 10–500 characters. It clears the verification fields, audits the unlock (`Arpp.IssueUnlocked`) and leaves the published snapshot untouched until re-verification.
- **PDF storage**: a replaced or deleted PDF file is deleted from disk only when no published snapshot references its storage key.
- **FY report**: `ArppFyProjectUpdateService` builds the report from the published FY current position (New/CL/CF) plus live project facts. Completed projects never appear in the current-stage query. Export returns 404 for an FY that is not available and 400 when there are no linked approved projects.

## Configuration

`ArppAttachments` section (`Configuration/ArppAttachmentOptions`):

| Key | Default |
|---|---|
| `MaxFileSizeBytes` | 100 MB |
| `StorageFolderName` | `arpp`, a safe relative folder under the upload root |
| `IngestIntoDocumentRepository` | `true` |

## Exports

- Per-issue Excel: `Details?handler=Excel` includes record-control metadata. The library version `Projects/Arpp?handler=Excel` includes neither control nor linkage columns.
- Print views.
- ARPP FY Project Update in Word, PDF and Excel (`Services/Reports/ArppFyProjectUpdate/*Builder.cs`). File name: `ARPP_Project_Update_FY_{fy}_{yyyyMMdd_HHmm}`.
- FFC Projects Update in Word, PDF and Excel (`Services/Reports/FfcProjectsUpdate/*`, data from `IFfcQueryService`).
