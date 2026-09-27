# Project Office Reports module

Checked against the code on 2026-09-27. Code is authoritative. The area `Areas/ProjectOfficeReports` groups the Project Office trackers: Visits, Social Media, ToT, Proliferation, IPR, Training, FFC, ARPP and Progress Review. The layout is as follows:
- Razor Pages: `Areas/ProjectOfficeReports/Pages/*`
- Application services: `Areas/ProjectOfficeReports/Application`, plus `Application/Ipr`, `Application/Ffc`, `Services/Ffc`, `Services/Arpp` and `Services/Reports/ProgressReview`
- Domain entities: `Areas/ProjectOfficeReports/Domain`
- Proliferation JSON API: `Areas/ProjectOfficeReports/Api`

Uploads are stored under the upload root (`IUploadRootProvider`).

## Access policies

Policy names are in `ProjectOfficeReportsPolicies` and `Policies.Ipr`; they are registered in `Program.cs`. "Project Office" means both role spellings `Project Office` and `ProjectOffice`.

| Policy | Allowed |
| --- | --- |
| `ViewVisits` | Any authenticated user |
| `ManageVisits`, `ManageSocialMediaEvents` | Admin, HoD, Project Office (`ProjectOfficeManagerRoles`) |
| `ViewTotTracker` | Any authenticated user |
| `ManageTotTracker` (submit) | Admin, HoD, Project Office, Project Officer |
| `ApproveTotTracker` | Admin, HoD |
| `ViewProliferationTracker` | Any authenticated user |
| `SubmitProliferationTracker` | Admin, HoD, Project Office |
| `ApproveProliferationTracker` | Admin, HoD |
| `ManageProliferationPreferences` | Admin, HoD, Project Office. Setting a year preference itself needs `ApproveProliferationTracker`. |
| `ViewTrainingTracker` | Admin, HoD, Project Office, Project Officer, Comdt, MCO, TA, Main Office |
| `ManageTrainingTracker` | Admin, HoD, Project Office |
| `ApproveTrainingTracker` | Admin, HoD |
| `ViewProgressReview` | Admin, HoD, Project Office, Comdt |
| `ViewArpp` | Admin, HoD, Comdt, Project Office, MCO, Project Officer |
| `ManageArpp` | Admin, HoD, Project Office |
| `VerifyArpp` | Admin, HoD, Comdt |
| `UnlockArpp` | Admin, HoD |
| `ManageFfc` | Admin, HoD, Comdt, ITO |
| `InlineEditFfc` | Admin, HoD, Comdt |
| `Policies.Ipr.View` | Any authenticated user |
| `Policies.Ipr.Edit` | Admin, HoD, Project Office |

Other page-level role gates:
- `VisitTypes/*`: `[Authorize(Roles = "Admin")]`
- `Admin/SocialMediaTypes/*` (event types and platforms): `[Authorize(Roles = "Admin,HoD")]`
- `Projects/LegacyImport`: `AdminPolicies.IngestionManage`

The navigation menu (`RoleBasedNavigationProvider`) hides entries the user cannot open.

## Repeat Build rules (`Project.IsBuild`)

| Tracker | Rule | Enforced in |
| --- | --- | --- |
| ToT | Not applicable. Only non-deleted, non-archived, **Completed**, non-Repeat-Build projects are ToT projects. | `ProjectTotApplicabilityPolicy` (`EligibleProjectPredicate`, `EligibleTotPredicate`, `GetIneligibilityReason`), used by `ProjectTotService` (submit, update, approve), `ProjectTotTrackerReadService`, `DocumentRequestService`/`DocumentService`, `OpsSignalsService`, `CompendiumReadService`, search indexing, and the overview ToT handlers |
| Proliferation | New yearly and granular records, and new year-preference rules, are refused for Repeat Build projects. Existing records may still be edited if the project is unchanged, and existing preferences stay editable. Project pickers list only eligible projects. | `ProliferationProjectEligibility`, `ProliferationSubmissionService`, `ProliferationController` (`projects`, `projects/{id}`) |
| Proliferation data quality | Historical rows linked to Repeat Builds are counted as `RepeatBuildLinkCount` and shown on the Summary page. | `ProliferationDataQualityService`, `Proliferation/Summary` |
| IPR | Cannot be linked to a Repeat Build project (create **and** update). The project picker excludes Repeat Builds. Switching a project to Repeat Build unlinks its IPR records. | `IprProjectEligibilityPolicy`, `IprWriteService.EnsureProjectAvailableAsync`, `Ipr/Index.SelectLists`, `IprProjectLinkMaintenance` (called from `Pages/Projects/Meta/Edit` and `ProjectMetaChangeDecisionService`) |
| FFC | Repeat Build projects **may** be linked. Only deleted projects are excluded from new links. | `FfcRecordWorkspaceService.GetProjectOptionsAsync` |

One-off data clean-ups: migration `20261216210000_RemoveTotDataFromRepeatBuildProjects` deleted ToT rows, requests, ToT document requests and ToT remarks for existing Repeat Builds. Migration `20261216220000_UnlinkIprFromRepeatBuildProjects` unlinked their IPR records.

**Gaps:**
- Switching a project to Repeat Build later does not clean up ToT data. A pending `ProjectTotRequest` is then hidden from both the tracker and the approvals queue, so it cannot be decided.
- `RemarkService` accepts `TransferOfTechnology`-scoped remarks for Repeat Builds through the remarks API. Only the UI scope picker hides the option.
- `ProgressReviewService.LoadTotRemarksAsync` does not exclude Repeat Builds, and it filters on `Active` rather than `Completed` projects.

## Visits
- **Domain:** `Visit`, `VisitType`, `VisitPhoto` (`Areas/ProjectOfficeReports/Domain`).
- **Services:**
  - `VisitService`: CRUD, search and export rows.
  - `VisitTypeService`.
  - `VisitPhotoService`, configured by `VisitPhotoOptions`:
    - JPEG, PNG or WebP only; 20 MB per file; 20 files and 100 MB per batch; minimum 720×540.
    - Derivatives `xl`/`md`/`sm`/`xs`.
    - Storage prefix `project-office-reports/visits`.
  - `VisitExportService`: Excel via `VisitExcelWorkbookBuilder`, PDF via `VisitPdfReportBuilder`, with audit (`VisitExported`).
- **Pages:**
  - `Visits/Index`: filters (type, date range, visitor/remarks), Excel and PDF export for any authenticated user, and delete for managers.
  - `Visits/All`.
  - `Visits/Details`: in-page photo gallery viewer with previous/next, keyboard (← → Esc), swipe, counter, and cover indicator. Only the active XL image is loaded, and neighbours are preloaded (`wwwroot/js/pages/project-office-reports/visits.js`). `ViewPhoto` remains the fallback when JavaScript is off. Delete is available for managers.
  - `Visits/New` and `Visits/Edit` (`ManageVisits`): form plus gallery management (upload, caption, cover, delete).
  - `Visits/ViewPhoto`: authenticated photo stream.
  - `VisitTypes/*`: Admin only.
- See `docs/VisitExcelExportPlan.md` for export details.

## Social media
- **Domain:** `SocialMediaEvent`, `SocialMediaEventType`, `SocialMediaPlatform`, `SocialMediaEventPhoto`.
- **Services:**
  - `SocialMediaEventService`, `SocialMediaEventTypeService`, `SocialMediaPlatformService`.
  - `SocialMediaEventPhotoService`, configured by `SocialMediaPhotoOptions`: 10 MB per file; derivatives `story` 1080×1920, `feed` 1200×1200 and `thumb` 600×600; storage prefix `org/social/{eventId}`.
  - `SocialMediaExportService`: Excel via `SocialMediaExcelWorkbookBuilder`, PDF via `SocialMediaPdfReportBuilder`.
- **Pages:**
  - `SocialMedia/Index`: filters and export, open to any authenticated user.
  - `Details`, `ViewPhoto`: authenticated.
  - `Create`, `Edit`, `Delete`: `ManageSocialMediaEvents`.
  - `Admin/SocialMediaTypes/*` and `Admin/SocialMediaTypes/Platforms/*`: Admin and HoD.

## Transfer of Technology (ToT)
- **Domain:** `ProjectTot` and `ProjectTotRequest` (one request row per project, with `DecisionState` Pending, Approved or Rejected; `RowVersion` is an EF concurrency token).
- **Services:**
  - `ProjectTotTrackerReadService`: eligible projects only. It falls back to narrower column sets on `UndefinedColumn`.
  - `ProjectTotService`: `SubmitRequestAsync` allows one pending request per project. `DecideRequestAsync` is for Admin/HoD; approval re-checks applicability and re-validates, rejection is always allowed. Dates are validated against the IST "today" and chronology (MET and first-production dates must fall between the start and completion dates).
  - `ProjectTotExportService` → `ProjectTotExcelWorkbookBuilder`.
- **Pages:** `Tot/Index` is a list/detail workspace with filters (status, request state, only pending, requires ToT, MET completed), a submit modal (submitters who are not approvers), a HoD decision card (approvers), a latest-request modal and export. `Tot/Summary` summarises "ToT-applicable projects". Pending ToT requests also appear in the central approvals queue (`ApprovalQueueService`).
- **Known issue:** `Tot/Index` records submit and decision context as a ToT remark. For a user whose top remark role is Project Office, `ResolveRemarkType` picks `External`, which `RemarkService` rejects. The ToT change saves, but the toast reports that the remark failed.
- See `docs/manual-tests/tot-tracker-view-modes.md` and `docs/bugs/tot-module-issues.md`.

## Proliferation
- **Domain:** `ProliferationYearly`, `ProliferationGranular`, `ProliferationYearPreference`, `ProliferationSource` (SDD and 515 ABW), `ApprovalStatus`, `ProliferationYearPolicy`.
- **Services:**
  - `ProliferationSubmissionService`: create, update, decide and delete.
    - Only completed projects qualify.
    - Admin/HoD entries are approved immediately; other submitters' entries are Pending.
    - An update by a non-approver sets the record back to Pending.
    - Approved records can be deleted only by Admin or HoD.
    - Rejection needs a reason.
    - Optimistic concurrency uses `RowVersion`.
  - Read side: `ProliferationOverviewService`, `ProliferationTrackerReadService`, `ProliferationSummaryReadService`, `ProliferationAggregateReadService`, `ProliferationProjectReadService`.
  - Reports: `ProliferationReportsService`, `ProliferationAnalysisService`.
  - Quality: `ProliferationDataQualityService`, `ProliferationChronologyQualityService`.
  - Exports: `ProliferationExportService`, `ProliferationCardExportService` and the `Proliferation*ExcelWorkbookBuilder` classes.
- **API** (`api/proliferation`, `ProliferationController`, `[AutoValidateAntiforgeryToken]`): every action has a policy. Reads use `ViewProliferationTracker`, writes and single-record reads use `SubmitProliferationTracker`, and decisions, data-quality correction and year preference use `ApproveProliferationTracker`. The analysis controller (`api/proliferation/reports/analysis`) uses `ValidateAntiForgeryToken`; the reports controller (`api/proliferation/reports`) is read-only.
- **Pages:** `Proliferation/Index`, `Manage` (Submit policy), `Project`, `Reports`, `Summary`.

## Intellectual property (IPR)
- **Domain:** `IprRecord` and `IprAttachment` (`Infrastructure/Data`). `IprType` is Patent or Copyright. `IprStatus` has FilingUnderProcess, Filed, Granted, Rejected and Withdrawn, but the page maps input to Filed or Granted only.
- **Services:**
  - `IprReadService`.
  - `IprWriteService`: unique filing number per type; filed date required and not in the future (IST); protection date required when Granted, not in the future and not before filing; project eligibility.
  - `IprAttachmentStorage`, configured by `IprAttachmentOptions`: PDF only, 20 MB by default, folder `ipr-attachments`.
  - `IprExportService` → `IprExcelWorkbookBuilder`.
- **Pages:** `Ipr/Index` (dashboard, filters, create/edit/delete, attachments; split into partial classes `Index.*.cs`) and `Ipr/Download` use `Ipr.View`. `Ipr/Manage` uses `Ipr.Edit`. Alias routes `/ProjectOfficeReports/Patent` and `/Patent/Manage` also work.

## Training
- **Domain:** `Training`, `TrainingType`, `TrainingCategory`, `TrainingTrainee`, `TrainingProject`, `TrainingCounters`, `TrainingDeleteRequest`.
- **Services:** `TrainingTrackerReadService`, `TrainingWriteService`, `TrainingExportService` (`TrainingExcelWorkbookBuilder`), `Services/ProjectOfficeReports/Training/TrainingNotificationService`.
- **Pages:** `Training/Index` (with export), `Records` and `View` use the View policy. `Manage` (save, request delete) uses the Manage policy. `Approvals` (decide delete requests) uses the Approve policy.

## FFC
See `docs/ffc-module-architecture.md` and `docs/ffc-widget-spec.md`.

## ARPP
- **Services:** `Services/Arpp`: `ArppCommandService`, `ArppReadService`, `ArppLibraryService`, `ArppReconciliationService`, `ArppAttachmentService` (with `FileSystemArppAttachmentStorage`), `ArppExportService` (`ArppExcelWorkbookBuilder`) and `ArppIpaStageSynchronizer`. When a published ARPP includes a project, the synchroniser completes that project's IPA stage on the first HQ-issued document date.
- **Pages:**
  - `ARPP/Index`, `Details`, `Print`, `ProjectHistory`: `ViewArpp`.
  - `Create`, `Manage`, `Reconcile`: `ManageArpp`.
  - On `Details`, PDF upload and delete need `ManageArpp`, **Verify** needs `VerifyArpp`, and **Unlock** needs `UnlockArpp` plus a mandatory reason.

## Progress review
See `docs/progress-review-verification.md`.

## Shared notes
- Upload-capable services resolve paths through `IUploadRootProvider` (the `PM_UPLOAD_ROOT` environment variable or configuration). Point it at a writable, backed-up volume.
- Exports run synchronously inside the request.
- When you add a role or tracker, update `ProjectOfficeReportsPolicies`, the `Program.cs` policy registration, the page attributes, any service-level role checks (for example `FfcAttachmentStorage`) and this table together.
