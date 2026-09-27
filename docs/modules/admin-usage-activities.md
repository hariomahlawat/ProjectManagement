# Administration Centre, ERP Usage, Activities, Conference Remarks, Calendar/Celebrations/Todo

## 1. Administration centre (`Areas/Admin`)

### Authorization model

- `Program.cs` applies `AuthorizeAreaFolder("Admin", "/")` to the whole area, so every Admin page requires an authenticated user. Each page model then adds its own capability policy.
- Capability policies are defined in `Services/Admin/AdminCapabilityCatalog`. It is the single catalogue behind policy registration (`RegisterPolicies`), Access Governance, navigation and tests. Policy names are in `Configuration/AdminPolicies`.

| Capability policy | Roles | Pages |
|---|---|---|
| `Admin.Access` | Admin | `/Admin` (overview), `/Admin/Help` |
| `Admin.Users.Manage` | Admin | `/Admin/Users/{Index,Create,Edit,Details,Reset,Disable,Enable,Delete}` |
| `Admin.AccessGovernance.View` | Admin | `/Admin/AccessGovernance` (+ `Export`) |
| `Admin.Security.View` | Admin | `/Admin/Analytics/{Index,Logins}`, `/Admin/Diagnostics/{DbHealth,SearchIndex}` |
| `Admin.Logs.View` | Admin | `/Admin/Logs` (+ `ExportCsv`) |
| `Admin.Recovery.Manage` | Admin | `/Admin/Recovery`, `/Admin/Projects/{Trash,Archived}`, `/Admin/Documents/Recycle`, `/Admin/Calendar/Deleted`, project purge/restore minimal APIs in `Program.cs` |
| `Admin.MasterData.Manage` | Admin | `/Admin/MasterData`, `/Admin/MasterData/ArppReferences`, `/Admin/Categories/*`, `/Admin/TechnicalCategories/*`, `/Admin/Lookups/{SponsoringUnits,LineDirectorates,ProjectTypes}/*` |
| `Admin.MasterData.Integrity.Manage` | Admin | `/Admin/MasterData/Integrity` (`NormaliseOrder`) |
| `Admin.Ingestion.Manage` | Admin | `/Admin/Maintenance`, `/Admin/Documents/IngestExternalPdfs` |
| `Admin.ActivityTypes.Manage` | Admin, HoD | `/Admin/ActivityTypes/*` |
| `Admin.Holidays.Manage` | Admin, HoD | `/Settings/Holidays/Index` (outside the area) |
| `Admin.Media.*` (Manage, View, Configure, OperateQueue, Recover, Classification.Manage) | Admin, HoD | media administration pages under `/Pages/Admin/*` |
| `ERP.Usage.View` (external) | Admin, Comdt, HoD | `/Usage` |
| `Calendar.ManageCelebrations` (external) | Admin, TA, Main_Office_Clerk, Main Office | `/Celebrations` |

### Main services (`Services/Admin`)

| Area | Services |
|---|---|
| Overview | `AdminDashboardService`, `AdminSystemHealthService`, `AdminWorkerStatusRegistry` |
| Users | `AdminUserQueryService`, `UserAccountStateResolver`, `AdminRoleDescriptorCatalog`, `AdminRoleAccessCatalog`, plus `Services/UserManagementService` and `UserLifecycleService` |
| Access governance | `AccessGovernance/AdminAccessGovernanceService` |
| Audit and logs | `AdminAuditService` (wraps `IAuditService` with before/after/actor/origin), `AdminLogQueryService` (reads `AuditLogs`), `AdminAuditPayloadParser`, `AuditActionPresentationCatalog`, `AdminAuditEntityLinkResolver` |
| Logins | `AdminLoginMonitoringService` (reads `AuthEvents` and `AuditLogs`), `AdminLoginOverviewService`, `AdminClientDescriptorService` |
| Health | `DatabaseHealthService` |
| Recovery | `Recovery/AdminRecoverySummaryService`, `ProjectRecoveryQueryService`, `DocumentRecoveryQueryService`, `Calendar/CalendarRecoveryService` |
| Master data | `MasterData/AdminMasterDataCommandService`, `MasterDataAdministrationQueryService`, `AdminHierarchyValidationService`, `Integrity/AdminMasterDataIntegrityService`, `CelebrationAdministrationService`, `Calendar/HolidayAdminService` |
| Maintenance | `Maintenance/AdminMaintenanceSummaryService`, `LegacyImportPreflightService`, `Ingestion/PdfIngestionCoordinator`, `PdfIngestionRunHistory` |
| CSV | `SafeCsvWriter` (guards against formula injection) |

### User lifecycle rules

Enforced in `Services/UserLifecycleService` and `UserManagementService`:

- A user cannot disable or delete their own account.
- The last active Admin cannot be disabled or deleted, and cannot lose the Admin role.
- Hard delete is allowed only for accounts younger than `UserLifecycle:HardDeleteWindowHours` (72). Older accounts must be disabled instead.
- A deletion request stays open for `UserLifecycle:UndoWindowMinutes` (15). `UserPurgeWorker` sweeps every minute and purges due accounts, audited as `AdminUserPurged`.
- Identity password rules: minimum 8 characters, at least one lowercase letter, lockout after 5 failed attempts (`Program.cs`).

### Exports

| Export | Limit |
|---|---|
| Audit logs CSV | 50,000 rows (`Logs/Index.cshtml.cs` `MaximumExportRows`); truncation is flagged |
| Login analytics CSV | 5,000 rows |
| Users CSV | none stated |
| Access governance export | none stated |

### Configuration

- `AdminLoginMonitoring`: `WorkdayStart`, `WorkdayEnd`, `MarkWeekendsForReview`, `DefaultLookbackDays`, `MaximumLookbackDays`, `MaximumChartPoints`, `DefaultReviewPageSize`, `MaximumReviewPageSize`.
- `UserLifecycle`: `HardDeleteWindowHours`, `UndoWindowMinutes`.

## 2. ERP usage intelligence

- Route `/Usage` → `Pages/Usage/Index.cshtml.cs`, policy `ERP.Usage.View` (Admin, Comdt, HoD). `Export` returns at most `ErpUsage:MaximumExportRows` rows.
- Capture:
  - `Infrastructure/Usage/ErpUsageMiddleware` records navigation.
  - `POST /api/usage/heartbeat` (`Program.cs`) requires authorization and antiforgery validation, and rejects unknown module keys (`IErpUsageModuleCatalog`).
  - Both write through `Services/Usage/UserActivityRecorder` into time buckets.
- Queries: `ErpUsageQueryService`, `ErpUsagePatternQueryService`, `ErpCommandAdoptionQueryService` (also used by the command workspace), `ErpUsageActionClassifier`, `ErpUsageModuleCatalog`.
- Retention: `UserActivityRetentionWorker` starts after 10 minutes and deletes detailed buckets older than `RetentionDays`, clamped to 30–1095. Per-day summaries are kept.
- Configuration section `ErpUsage` (`Configuration/ErpUsageOptions`, validated by `ErpUsageOptionsValidator`):

  | Key | Default |
  |---|---|
  | `TrackingInceptionUtc` | 2026-07-14T07:30Z; earlier absence must not be inferred |
  | `BucketMinutes` | 5 |
  | `HeartbeatIntervalSeconds` | 180 |
  | `InteractiveIdleMinutes` | 10 |
  | `RetentionDays` | 400 |
  | `RegularUserThresholdPercent` | 80 |
  | `MaximumLookbackDays` | 365 |
  | `MaximumExportRows` | 2000 |
  | `WorkingDays` | Monday to Saturday; duplicate days and Sunday are rejected |

## 3. Activities (miscellaneous activity register)

- Routes (all `[Authorize]`):
  - `/Activities` (list, `RequestDelete`, `Export`)
  - `/Activities/Edit/{id?}`
  - `/Activities/Details/{id}` (`Upload`, `RemoveAttachment`)
  - `/Activities/Approvals` (`[Authorize(Roles="Admin,HoD")]`, redirects to `/Approvals/Pending?Type=ActivityDelete`)
- Authorization (`Services/Activities/ActivityAuthorizationPolicy`, `ActivityRoleLists`):
  - Create, and request deletion: `ManagerRoles` = Admin, HoD, Project Officer, Project Office, TA.
  - Manage an activity: a manager or the activity's creator.
  - Manage an attachment: a manager, the creator, or the uploader.
  - Delete or approve deletion: Admin, HoD.
- Services: `ActivityService` (enforces manage and delete), `ActivityDeleteRequestService` (request → approve/reject, with notifications through `ActivityNotificationService`), `ActivityAttachmentManager`, `FileSystemActivityAttachmentStorage`, `ActivityAttachmentValidator`, `ActivityTypeService`, `ActivityExportService` (Excel, ClosedXML), `ActivityInputValidator`.
- Entities (`Models/Activities`): `Activity` (soft delete, RowVersion), `ActivityAttachment`, `ActivityDeleteRequest`, `ActivityType`.
- Attachment limits (`ActivityAttachmentValidator`): 25 MB per standard file, 200 MB per video, 200 MB per upload batch.

## 4. Conference remarks

The Conference review is available at `/Workspace/Conference` with policy `ConferenceRemarks.Manage` (Comdt, HoD).

`Services/ConferenceRemarks/ConferenceRemarkCommandService` writes directions into the source item's own remark stream:

| Item kind | Written through |
|---|---|
| Project | `IRemarkService` |
| Project Idea | `IProjectIdeaCommandService.AddConferenceCommentAsync` |
| Action Task | task collaboration |

`ConferenceDirectionTextFormatter` formats the text. Details are in [reports-workspace-dashboard.md](reports-workspace-dashboard.md).

## 5. Calendar, Celebrations, Todo (brief)

- **Calendar**:
  - `/Calendar` is `[Authorize]`.
  - The `/calendar/events` minimal API requires `Calendar.ManageEvents` for writes: Admin, HoD, TA, Comdt, MCO, Project Officer, Project Office (both aliases).
  - Office calendar logic: `OfficeCalendarService`. Holidays: `/Settings/Holidays` (`Admin.Holidays.Manage`).
- **Celebrations**:
  - `/Celebrations` and `/Celebrations/Edit/{id?}` require `Calendar.ManageCelebrations`.
  - Birthdays and anniversaries have their own policies, `Calendar.ManageBirthdays` and `Calendar.ManageAnniversaries`, which use the same roles.
- **Todo**:
  - `/Tasks` is `[Authorize]` and backed by `Services/TodoService`.
  - `TodoPurgeWorker` purges old items.
  - Configuration section `Todo` (`Models/TodoOptions`): `RetentionDays` (7), `MaxOpenTasks` (500).
