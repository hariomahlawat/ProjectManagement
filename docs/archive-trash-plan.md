# Project Archive and Trash

**Status: Implemented, with deviations from the original plan.** This page now describes
current behaviour. The original plan's items that were never built are listed at the end.

Main code:
- `Services/Projects/ProjectModerationService.cs`
- `Services/Projects/ProjectRetentionWorker.cs`
- `Configuration/ProjectRetentionOptions.cs`
- the `/api/projects/{id}/…` endpoints in `Program.cs`
- `Areas/Admin/Pages/Projects/Trash.cshtml(.cs)` and `wwwroot/js/admin/project-trash.js`
- `Services/Admin/Recovery/*` and `Areas/Admin/Pages/Recovery`
- `Pages/Projects/_ProjectModerationActions.cshtml`

## Data model (`Projects` table)

| Column | Type | Notes |
| --- | --- | --- |
| `IsArchived` | boolean, default false | |
| `ArchivedAt` | `timestamptz` (`DateTimeOffset?`) | |
| `ArchivedByUserId` | varchar(450) | |
| `IsDeleted` | boolean, default false | Means "in Trash" |
| `DeletedAt` | `timestamptz` | Start of the retention clock |
| `DeletedByUserId` | varchar(450) | |
| `DeleteReason` | varchar(512) | Required when moving to Trash |
| `DeleteMethod` | varchar(32) | Always `"Trash"` today |
| `DeleteApprovedByUserId` | varchar(450) | Reset to null; not used |

Indexes: `IX_Projects_IsDeleted_IsArchived` on `(IsDeleted, IsArchived)`, and the filtered index `IX_Projects_IsDeleted_Filtered` (`"IsDeleted" = TRUE`).

There is **no global query filter** on `Project`. Every listing must exclude trashed or archived projects explicitly:
- `ProjectSearchQueryExtensions.ApplyProjectSearch` (`Services/Projects/ProjectSearchFilters.cs`) always adds `!IsDeleted`, and adds `!IsArchived` unless `IncludeArchived` is set.
- Dashboards, analytics, workspace, publications and briefing services each filter on their own.

## Actions and authorisation

| Action | Endpoint / UI | Who | Behaviour |
| --- | --- | --- | --- |
| Archive | `POST /api/projects/{id}/archive` | Admin or HoD (`IsProjectArchiveActor` in `Program.cs`) | Rejected with 409 if the project is in Trash. Idempotent. Writes a `ProjectAudit` row with `Action = "Archive"`. |
| Restore from archive | `POST /api/projects/{id}/restore-archive` | Admin or HoD | `ProjectAudit` `RestoreArchive` |
| Move to Trash | `POST /api/projects/{id}/trash` with body `{ reason }` | Admin or HoD | Reason is required, at most 512 characters. Sets `IsDeleted`, `DeletedAt`, `DeletedByUserId`, `DeleteReason` and `DeleteMethod = "Trash"`. `ProjectAudit` `Trash`. Works in any lifecycle state. |
| Restore from Trash | `POST /api/projects/{id}/restore-trash`, or Admin › Project trash | `AdminPolicies.RecoveryManage` | Clears the delete fields. `ProjectAudit` `RestoreTrash`. |
| Purge | `POST /api/projects/{id}/purge` with body `{ removeAssets }`, or Admin › Project trash | `AdminPolicies.RecoveryManage` | See below |

The project page shows the archive, restore and trash buttons through `_ProjectModerationActions.cshtml`. The page renders them only when the viewer can assign roles; the server-side check is the Admin/HoD role test above. Archived projects show an "Archived" badge in `_ProjectCommandHeader.cshtml`.

The Admin Project trash page (`Areas/Admin/Pages/Projects/Trash`) adds two safeguards that the API does not have:
- Permanent deletion is refused until the retention period has elapsed (`PurgeScheduledUtc`).
- The operator must type the exact project name. The page also writes an admin audit entry, `ProjectPurgeAuthorised`, before it calls `PurgeAsync`.

## Purge

`ProjectModerationService.PurgeCoreAsync` runs inside a single relational transaction:
1. If `removeAssets` is set, it moves the project upload folder (`{uploadRoot}/{ProjectDocuments:ProjectsSubpath}/{projectId}`) into quarantine with `Services/Storage/FileSystemQuarantine`.
2. It explicitly removes photos, videos, documents, comments, remarks, ToT, stages, plan snapshots and their rows, plan versions, stage plans, approval logs, `StageChangeLogs` and `ProjectMetaChangeRequests`, then the `Projects` row. Other dependants go through database foreign keys:
   - Cascade: facts, ToT requests, document requests, `ProjectAudits`, `TrainingProjects`, `IndustryPartnerProjects`, and so on.
   - SetNull: IPR, FFC, ARPP, brochure and compendium links.
3. It writes a global `AuditLog` entry, `Projects.Purge`, with the reason and metadata (asset disposition and quarantine reference). The per-project `ProjectAudits` rows are removed by the cascade.
4. It commits. On failure it rolls back and restores the quarantined folder. After a successful commit it finalises deletion of the quarantine. If that clean-up fails, it logs and audits `Projects.PurgeAssetCleanupPending`.

Known limits:
- `ProjectBriefingDeckItems.ProjectId` is `Restrict`. Purging a project that is still in a briefing deck fails, and the transaction rolls back.
- Rows with no foreign key survive as orphans: `ProliferationYearly`/`Granular`/`YearPreference`, `StageChangeRequests`, `StageShiftLogs`, `PlanRealignmentAudits` and `Notifications`.

## Retention job

`ProjectRetentionWorker` is a `BackgroundService` that runs every 24 hours. It calls `PurgeExpiredAsync(cutoff, RemoveAssetsOnPurge)` for projects where `IsDeleted` is true and `DeletedAt <= now - TrashRetentionDays`. Candidates are purged one at a time with actor `system`; a failure is logged and the worker moves on to the next project. Its status appears in the admin worker registry as "Project trash retention".

Options live in the `Projects:Retention` configuration section (`ProjectRetentionOptions`); no appsettings file sets them today:

| Option | Default |
| --- | --- |
| `TrashRetentionDays` | 30 |
| `RemoveAssetsOnPurge` | true |

## Not implemented (from the original plan)

- Archived projects are **not** read-only. No central write guard exists; archiving only hides the project from default listings.
- There is no "Export project" ZIP from Trash, no daily purge report or metrics, and no feature flag for the rollout.
- `DeleteMethod = "Purge"` and `DeleteApprovedByUserId` are never set.
- Trashed projects are not hidden everywhere. For example, `Pages/Projects/Overview.cshtml.cs` loads a project by id without checking `IsDeleted`, and `ApprovalQueueService` lists pending stage requests without checking the project's trash or archive state.
