# FFC (Friendly Foreign Countries) module architecture

Checked against the code on 2026-09-27. Code is authoritative. Pages are under `Areas/ProjectOfficeReports/Pages/FFC`, application services under `Services/Ffc`, and attachment storage under `Application/Ffc`. The navigation item is **"FFC simulators"** (`RoleBasedNavigationProvider`), which links to `/ProjectOfficeReports/FFC/Index`.

## 1. Responsibilities
- Store one record per country and year (`FfcRecord`), its linked project rows (`FfcProject`, each with a quantity and delivery/installation flags), and attachments (`FfcAttachment`).
- Offer a portfolio index, a record workspace, maps, a board, a detailed table with Word and Excel exports, a footprint view with PowerPoint export, and a dashboard widget.
- Feed global search (`Services/Search/GlobalFfcSearchService`) and the Progress Review report (`ProgressReviewService.LoadFfcAsync`).

## 2. Data model (`Areas/ProjectOfficeReports/Domain`)
- **`FfcCountry`**: name, ISO code and `IsActive`. Inactive countries are hidden from new-record pickers, but a country already on a record stays selectable (`FfcRecordWorkspaceService.GetCountryOptionsAsync`).
- **`FfcRecord`**: `CountryId` + `Year` (`short`); IPA and GSL flags, dates and remarks; legacy record-level delivery/installation fields; `OverallRemarks`; `IsDeleted` (archive flag); audit timestamps; `RowVersion`.
- **`FfcProject`**: `Name`, `Remarks`, optional `LinkedProjectId` pointing to a core `Project`, `Quantity` (default 1), `IsDelivered`/`DeliveredOn`, `IsInstalled`/`InstalledOn`, `RowVersion`. Delivery and installation progress is derived from these rows. `FfcProjectBucketHelper` classifies each row as Installed, Delivered or Planned.
- **`FfcAttachment`**: file metadata grouped by record, typed by `FfcAttachmentKind`.
- EF configuration lives in `ApplicationDbContext`. Projects and attachments cascade with their record; countries are restricted.

## 3. Authorisation (`ProjectOfficeReportsPolicies`)
| Capability | Rule |
| --- | --- |
| View index, maps, board, detailed table, footprint, record details, attachment viewer | Any authenticated user (`[Authorize]`) |
| Manage records, projects, attachments, countries, archive and restore (`ManageFfc` / `CanManageFfc`) | Admin, HoD, Comdt, ITO (`FfcManagerRoles`) |
| Inline edits in the detailed table (overall remarks, progress) (`InlineEditFfc` / `CanInlineEditFfc`) | Admin, HoD, Comdt (`FfcInlineEditorRoles`); ITO is excluded by design |

Pages with a policy attribute: `Records/Create`, `Records/Manage`, `Records/Archived` and `Countries/Manage` require `ManageFfc`. `Records/Details`, `Records/Projects/Manage` and `Records/Attachments/Upload` use `[Authorize]`, and each mutating handler calls `CanManageFfc(User)`.

**Known defect:** `FfcAttachmentStorage.SaveAsync` and `DeleteAsync` (`Application/Ffc/FfcAttachmentStorage.cs`) still allow only Admin and HoD (`IsAdminOrHod`). Comdt and ITO pass the page checks but their uploads and deletes are rejected with the storage authorisation error.

## 4. Services (`Services/Ffc`)
- `FfcPortfolioService`: portfolio summary and paging for the index (page size 25; filters for query, year, country and IPA/GSL/delivery/installation state).
- `FfcRecordWorkspaceService`: record workspace DTOs, country options, archived records, and project picker options.
  - **Project linking:** Repeat Build projects **may** be linked. Only deleted projects are excluded from new links, and a deleted project already linked to the record is kept.
  - Every option carries an `IsDcd` flag. `ResolveDcdCategoryIdsAsync` finds the root category named "DCD Projects" and all of its descendants recursively (`ProjectCategoryHierarchyService`).
  - The picker UI (`Records/Partials/_ProjectEditor.cshtml`, `wwwroot/js/pages/project-office-reports/ffc/ffc-record-workspace.js`) defaults to DCD scope and offers a "DCD Projects | All Projects" switch.
- `FfcRecordCommandService`, `FfcProjectCommandService`, `FfcAttachmentCommandService`: create, update, archive and restore records, project rows and attachments, with audit.
- `FfcProgressService`: current progress per FFC project. For a linked project, progress is the latest **External** project remark, written and edited through `IRemarkService`. The actor may be `RemarkActorRole.Ito`, which this path alone supplies. For an unlinked row, progress is stored in `FfcProject.Remarks`.
- `FfcFootprintService` and `Presentation/*` (`FfcPowerPointExportService`, `FfcSlideComposer`, `FfcPresentationMapRenderer`): the footprint page and its PowerPoint export (`Footprint` → `OnPostExportPowerPointAsync`).
- `Exports/*`: `FfcDetailedTableExportService`, `FfcDetailedWordDocumentBuilder` (Open XML `.docx`) and `FfcDetailedExcelWorkbookBuilder`, titled "FFC Projects Update".

## 5. Pages
| Page | Purpose |
| --- | --- |
| `Index` | Portfolio list built with `FfcPortfolioService`, with management actions for managers. |
| `Records/Create` | New country-year record. |
| `Records/Details` | Record workspace: update record, save or delete linked projects, upload or delete attachments, archive (soft delete via `IsDeleted`). |
| `Records/Archived` | Lists archived records; `OnPostRestoreAsync` restores one. |
| `Records/Manage` | Legacy list-plus-form management page (`FfcRecordListPageModel`). |
| `Records/Projects/Manage`, `Records/Attachments/Upload` | Legacy child CRUD pages. |
| `Countries/Manage` | Country master data. |
| `Attachments/View` | Streams an attachment. Returns not-found when the parent record is missing or archived. |
| `Map` | Leaflet map; the `Data` handler returns rollups from `FfcCountryRollupDataSource`. |
| `MapBoard` | Screenshot-friendly country board. |
| `MapTable` | Redirects to `MapBoard`. |
| `MapTableDetailed` | Project-level table. `OnGetExportExcelAsync`, `OnPostExportWordAsync`; inline `OnPostUpdateOverallRemarksAsync` / `OnPostUpdateProgressAsync` (JSON bodies, `InlineEditFfc`). |
| `Footprint` | Footprint summary, country cards and PowerPoint export drawer. |

## 6. Rollups and dashboard widget
- `FfcCountryRollupDataSource.LoadAsync` groups `FfcProject` rows by country, multiplies by `Quantity`, and returns ISO3-keyed Installed, Delivered and Planned unit counts. The map, board and dashboard widget all use it.
- Dashboard (`Pages/Dashboard/Index.cshtml.cs`) builds `FfcSimulatorMapVm`, rendered by `Areas/Dashboard/Components/FfcSimulatorMap/_Widget.cshtml` and `wwwroot/js/widgets/ffc-simulator-map.js`. See `docs/ffc-widget-spec.md`.

## 7. Maintenance guidance
- Add new completion buckets in `FfcProjectBucketHelper` / `FfcCountryRollupDataSource`, so the map, board, widget and exports stay consistent.
- New mutations should go through the `Ffc*CommandService` classes and emit audit events, as the existing ones do.
- Keep any new role gate aligned across three places: `FfcManagerRoles`, the page or handler checks, and `FfcAttachmentStorage`. The last one is currently out of step (see §3).
- CSP: scripts are served from `wwwroot/js/...` only; no inline scripts.
