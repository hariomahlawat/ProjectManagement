# Project Office Reports module

The Project Office Reports area bundles multiple operational trackers under a single navigation node so support teams can manage evidence, approvals, and exports without bouncing between bespoke tools. Every sub-module shares the same patterns: Razor Pages with role-gated handlers, services under `Areas/ProjectOfficeReports/Application`, and persistent storage rooted beneath the shared upload directory resolved by `IUploadRootProvider`.

## Access policies

`ProjectOfficeReportsPolicies` defines granular policies for each tracker:

| Policy | Roles |
| --- | --- |
| `ViewVisits` / `ManageVisits` | Admin, HoD, Project Office |
| `ManageSocialMediaEvents` | Admin, HoD, Project Office |
| `ViewTotTracker` / `ManageTotTracker` / `ApproveTotTracker` | Admin, HoD, Project Office |
| `ViewProliferationTracker` / `SubmitProliferationTracker` / `ApproveProliferationTracker` | Admin, HoD, Project Office |
| `ManageProliferationPreferences` | Admin |
| `Policies.Ipr.View` / `Policies.Ipr.Edit` | Admin, HoD, Project Office, Comdt, MCO |

Navigation is generated dynamically by `RoleBasedNavigationProvider`, which hides trackers the user cannot access. (see Services/Navigation/RoleBasedNavigationProvider.cs lines 16-152)

## Visits tracker

- **Domain** – `Visit`, `VisitType`, and `VisitPhoto` live under `Areas/ProjectOfficeReports/Domain`. Photos carry derivative metadata (width/height, caption, version stamp) so responsive galleries and exports stay consistent. (see Areas/ProjectOfficeReports/Domain/Visit.cs lines 1-112)
- **Services** – `VisitService` handles CRUD, filtering, and exports; `VisitPhotoService` validates images using `VisitPhotoOptions`, generates derivatives, enforces max file sizes, and stores assets beneath `project-office-reports/visits/{visitId}`. (see Areas/ProjectOfficeReports/Application/VisitService.cs lines 15-260)
- **Pages** –
  - `Visits/Index.cshtml` lists visits with filters (type, date range, keyword), photo counts, and Excel export action. (see Areas/ProjectOfficeReports/Pages/Visits/Index.cshtml.cs lines 20-210)
  - `Visits/New.cshtml` & `Visits/Edit.cshtml` present the form plus gallery management (cover toggle, delete) via asynchronous modals. (see Areas/ProjectOfficeReports/Pages/Visits/Edit.cshtml.cs lines 17-200)
  - `VisitTypes/Index.cshtml` lets admins activate/deactivate visit types with concurrency-safe edits and usage checks before deletion. (see Areas/ProjectOfficeReports/Pages/VisitTypes/Index.cshtml.cs lines 15-160)
- **Exports** – `VisitExportService` writes Excel workbooks using `VisitExcelWorkbookBuilder` for consistent formatting. (see Areas/ProjectOfficeReports/Application/VisitExportService.cs lines 14-140)

## Social media tracker

- **Domain** – `SocialMediaEvent`, `SocialMediaEventType`, `SocialMediaPlatform`, and `SocialMediaEventPhoto` track campaign metadata, platform associations, and galleries. (see Areas/ProjectOfficeReports/Domain/SocialMediaEvent.cs lines 1-166)
- **Services** – `SocialMediaEventService` handles search, detail retrieval, and exports (Excel/PDF). `SocialMediaEventPhotoService` enforces image options from `SocialMediaPhotoOptions`, generates derivatives, and manages cover selection. (see Areas/ProjectOfficeReports/Application/SocialMediaEventService.cs lines 15-260)
- **Pages** –
  - `SocialMedia/Index.cshtml` lists events with filters by type/platform/date, inline status badges, gallery management modals, and export actions. (see Areas/ProjectOfficeReports/Pages/SocialMedia/Index.cshtml.cs lines 19-240)
  - `Admin/SocialMediaTypes/Index.cshtml` lets admins curate event types and platforms, toggling activity and editing display names. (see Areas/ProjectOfficeReports/Pages/Admin/SocialMediaTypes/Index.cshtml.cs lines 16-180)
- **Exports** – `SocialMediaExportService` (Excel) and `SocialMediaEventPhotoService.ExportForPdfAsync` (PDF) provide ready-to-share digests. Workbook builders live in `Utilities/Reporting`. (see Areas/ProjectOfficeReports/Application/SocialMediaExportService.cs lines 15-120)

## Transfer-of-Technology (ToT) tracker

- **Domain** – `ProjectTot` and `ProjectTotRequest` extend the core project model; tracker snapshots map onto `ProjectTotTrackerRow`. (see Models/ProjectTot.cs lines 1-37)
- **Services** – `ProjectTotTrackerReadService` builds resilient snapshots, retrying with narrower column sets when legacy databases lack recent schema additions. `ProjectTotExportService` generates Excel digests. `ProjectTotService` writes request decisions with concurrency protection. (see Areas/ProjectOfficeReports/Application/ProjectTotTrackerReadService.cs lines 19-200)
- **Page** – `Tot/Index.cshtml` offers cards/list view toggles, filters for ToT status/request state/pending requests/date ranges, remark drawers via `IRemarkService`, submit/approve modals, and export functionality. Authorization ensures only submitters/approvers see the correct modals. (see Areas/ProjectOfficeReports/Pages/Tot/Index.cshtml.cs lines 24-260)

## Proliferation tracker

- **Domain** – `ProliferationYearly`, `ProliferationSubmission`, `ProliferationSource`, and preference entities capture yearly rollups, submission status, and reference data. (see Areas/ProjectOfficeReports/Domain/ProliferationYearly.cs lines 1-120)
- **Services** – `ProliferationOverviewService` aggregates summaries, `ProliferationManageService` mutates submissions with audit history, `ProliferationSubmissionService` handles approval decisions, and `ProliferationSummaryReadService` feeds dashboards. (see Areas/ProjectOfficeReports/Application/ProliferationOverviewService.cs lines 16-180)
- **Pages** – `Proliferation/Index.cshtml` provides filterable grids, submission/approval modals, and export buttons; partials render year pickers and approval queues. Preferences pages let admins set active years and defaults. (see Areas/ProjectOfficeReports/Pages/Proliferation/Index.cshtml.cs lines 23-220)
- **Exports** – Excel and PDF exports reuse `ProliferationExportService` and reporting builders to guarantee consistent layout. (see Areas/ProjectOfficeReports/Application/ProliferationExportService.cs lines 14-160)

## Intellectual property (IPR) tracker

- **Domain** – `IprRecord` and `IprAttachment` persist filing details, lifecycle status, grant metadata, optional project links, and attachment information with unique constraints and row-version concurrency. (see Infrastructure/Data/IprRecord.cs lines 1-44)
- **Services** – `IprReadService` handles search/KPIs/export, `IprWriteService` creates/updates/deletes records with validation on status transitions and unique filing numbers, `IprAttachmentStorage` streams attachment files to disk, and `IprExportService` generates Excel reports. (see Application/Ipr/IprWriteService.cs lines 19-220)
- **Pages** –
  - `Ipr/Index.cshtml` renders the main dashboard with filters (type, status, project, year), KPI header, inline forms for create/edit/delete, attachment management, and export endpoints. (see Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml.cs lines 24-260)
  - `Ipr/Manage.cshtml` provides a focused create/edit form with validation summary and attachment list for accessibility scenarios. (see Areas/ProjectOfficeReports/Pages/Ipr/Manage.cshtml.cs lines 16-200)
  - `Ipr/Download.cshtml` streams attachments with authorization checks, ensuring only permitted roles access evidence. (see Areas/ProjectOfficeReports/Pages/Ipr/Download.cshtml.cs lines 17-120)
- **Configuration** – `IprAttachmentOptions` controls max file size and MIME types (default 20 MB PDF). Options are bound in `Program.cs`. (see Configuration/IprAttachmentOptions.cs lines 6-13)

## Shared exports and reporting

- Excel workbooks live under `Utilities/Reporting` (`VisitExcelWorkbookBuilder`, `SocialMediaExcelWorkbookBuilder`, `ProjectTotExcelWorkbookBuilder`, `ProliferationExcelWorkbookBuilder`, `IprExcelWorkbookBuilder`). Each builder standardises headers, formatting, and summary rows. (see Utilities/Reporting/IprExcelWorkbookBuilder.cs lines 1-120)
- PDF builders for visit/social media photo packs ensure layouts stay on-brand while pulling assets from the upload root. (see Utilities/Reporting/VisitPdfReportBuilder.cs lines 1-160)
- Services return `ExportFile` DTOs with content-type/file-name metadata so Razor Pages can respond via `File()` helpers without duplicating naming logic.

## Operational notes

- All upload-capable services depend on `UploadRootProvider`; ensure `PM_UPLOAD_ROOT` (or `ProjectOfficeReports:*:StoragePrefix`) points to a writable, backed-up volume. (see Services/Storage/UploadRootProvider.cs lines 17-110)
- Long-running exports run synchronously today; if reports grow significantly, wrap workbook generation in background jobs and stream results via a notification when ready.
- Keep `ProjectOfficeReportsPolicies` in sync with navigation and documentation so new roles immediately see the correct trackers.

Use this guide when extending a tracker or introducing a new one—mirror the existing service patterns, enforce role-specific policies, and update the exports table so operators know where to find supporting files.
