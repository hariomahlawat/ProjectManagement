# Training tracker rollout guide

This guide captures the artefacts required to launch the training tracker module, including a feature summary, schema diffs, configuration prerequisites, deployment checklist, and the agreed monitoring plan.

## Feature overview

- **Dedicated dashboard (Project Office Reports → Training).** Authorized users browse trainings with filters for type, rank category, technical category, time range, and keyword search. The page also surfaces KPIs and Excel exports with optional roster details. 【F:Areas/ProjectOfficeReports/Pages/Training/Index.cshtml.cs†L22-L179】
- **Unified editor with legacy + roster modes.** Managers create or update entries, attach linked projects, paste structured roster JSON/CSV payloads, and auto-calculate participant counters. Legacy records can omit roster uploads while still capturing aggregate counts. 【F:Areas/ProjectOfficeReports/Pages/Training/Manage.cshtml.cs†L24-L204】
- **Deletion workflow with approvals + notifications.** Delete requests capture a reason, queue for approval, and notify opted-in reviewers; approvals live under the Training → Approvals page. 【F:Areas/ProjectOfficeReports/Pages/Training/Manage.cshtml.cs†L205-L331】【F:Areas/ProjectOfficeReports/Pages/Training/Approvals.cshtml†L1-L80】【F:Services/ProjectOfficeReports/Training/TrainingNotificationService.cs†L14-L87】
- **Exports and KPI snapshots.** Excel exports respect filters, optionally include roster data, and deliver files through the export service. KPI aggregation runs alongside the search query for quick insights. 【F:Areas/ProjectOfficeReports/Pages/Training/Index.cshtml.cs†L101-L179】

## Schema changes

| Table | Purpose | Key columns |
| --- | --- | --- |
| `TrainingTypes` | Lookup of training categories with ordering, activation toggle, and seed data for Simulator & Drone sessions. | `Id (uuid)`, `Name`, `Description`, `IsActive`, `DisplayOrder`, `RowVersion`. 【F:Migrations/20251220110000_AddTrainingTrackerBaseline.cs†L14-L115】【F:Migrations/20251220110000_AddTrainingTrackerBaseline.cs†L160-L190】 |
| `TrainingRankCategoryMap` | Maps rank names to officer/JCO/OR categories used when parsing rosters and KPIs. Seeded with canonical ranks. | `Rank`, `Category`, `IsActive`, audit fields, `RowVersion`. 【F:Migrations/20251220110000_AddTrainingTrackerBaseline.cs†L15-L94】【F:Migrations/20251220110000_AddTrainingTrackerBaseline.cs†L170-L214】 |
| `Trainings` | Core training records with schedule (date range or month/year), legacy headcounts, notes, and type linkage. | `TrainingTypeId`, `StartDate`, `EndDate`, `TrainingMonth`, `TrainingYear`, legacy counters, audit fields. 【F:Migrations/20251220110000_AddTrainingTrackerBaseline.cs†L50-L118】 |
| `TrainingCounters` | Stores derived participant totals (officers/JCO/OR/total) with the source of truth flag. | `TrainingId (PK/FK)`, `Officers`, `JuniorCommissionedOfficers`, `OtherRanks`, `Total`, `Source`, `UpdatedAtUtc`. 【F:Migrations/20251220110000_AddTrainingTrackerBaseline.cs†L118-L148】 |
| `TrainingDeleteRequests` | Tracks delete requests, approval state, approver metadata, and concurrency token. | `TrainingId`, `RequestedByUserId`, `Status`, decision fields, `RowVersion`. 【F:Migrations/20251220110000_AddTrainingTrackerBaseline.cs†L148-L169】 |
| `TrainingProjects` | Many-to-many link from trainings to core projects with optional allocation share. | Composite key `(TrainingId, ProjectId)`, `AllocationShare`, `RowVersion`. 【F:Migrations/20251220110000_AddTrainingTrackerBaseline.cs†L169-L207】 |
| `TrainingTrainees` | Roster rows capturing individuals, rank, unit, and derived category for trainings with rosters. | `Id`, `TrainingId`, `ArmyNumber`, `Rank`, `Name`, `UnitName`, `Category`, `RowVersion`. 【F:Migrations/20251221120000_AddTrainingRoster.cs†L14-L55】 |

## Configuration requirements

Add the following block to production settings (e.g. `appsettings.Production.json`) and ensure the environment variable overrides stay in sync:

```json
"ProjectOfficeReports": {
  "TrainingTracker": {
    "Enabled": true
  }
}
```

When `Enabled` is `false`, the pages remain accessible but surface an inactive banner and block edits/exports. 【F:Areas/ProjectOfficeReports/Pages/Training/Index.cshtml.cs†L78-L110】【F:Areas/ProjectOfficeReports/Pages/Training/Manage.cshtml.cs†L60-L108】

## Deployment checklist

1. **Pre-flight**
   - Confirm the new migrations are included in the build artifact: `20251220110000_AddTrainingTrackerBaseline` and `20251221120000_AddTrainingRoster`. 【F:Migrations/20251220110000_AddTrainingTrackerBaseline.cs†L14-L207】【F:Migrations/20251221120000_AddTrainingRoster.cs†L14-L50】
   - Review storage capacity under the upload root; roster attachments reuse existing project office storage conventions (no new buckets required).
   - Ensure notification templates and SignalR hubs are reachable; Training notifications reuse the existing notification dispatcher. 【F:Services/ProjectOfficeReports/Training/TrainingNotificationService.cs†L14-L87】【F:Program.cs†L300-L371】
2. **Maintenance window tasks**
   - Put the site into read-only or announce downtime (expected 20–30 minutes for migration + smoke tests).
   - Back up the production database before applying migrations.
   - Apply EF Core migrations (e.g. `dotnet ef database update` or `dotnet ProjectManagement.dll --migrate`).
   - Deploy the web app build with the updated binaries and static assets.
   - Set `ProjectOfficeReports:TrainingTracker:Enabled` to `true` and reload the app pool/process.
3. **Post-migration validation**
   - Sign in as a Project Office user and verify the Training tab loads, filters, and shows seeded types/ranks.
   - Create a pilot training record with roster upload to ensure counters roll up correctly.
   - Submit and approve a delete request to confirm workflow + notifications.

## Media and data considerations

- Roster uploads are processed in-memory from JSON payloads posted by the UI; no additional blob storage keys are introduced. Verify `PM_UPLOAD_ROOT` points to persistent storage so derived exports remain accessible. 【F:Areas/ProjectOfficeReports/Pages/Training/Manage.cshtml.cs†L108-L190】【F:Services/ProjectOfficeReports/Training/TrainingNotificationService.cs†L14-L87】
- Excel exports stream files via `ITrainingExportService`, which uses the existing temp storage conventions. Monitor disk space in the temp directory during high-volume exports. 【F:Areas/ProjectOfficeReports/Pages/Training/Index.cshtml.cs†L101-L179】

## Deployment window & communications

- **Target window:** Off-peak Friday 18:30–19:00 IST (post-office hours, pre-weekend) to minimize disruption to project submissions.
- **Stakeholders:** Notify Admin/HOD leadership, Project Office supervisors, and IT infra two days prior. Remind Teaching Assistants of temporary downtime via Notifications broadcast.
- **Go/no-go checklist:** Confirm backups, configuration PR merged, and rollout sign-off captured in change ticket before entering the window.

## Post-release monitoring

- **Live validation (T+0–1h):** Watch application logs for training tracker errors, roster parsing failures, or notification dispatch issues. Leverage existing structured logs from `TrainingWriteService` and notification services. 【F:Areas/ProjectOfficeReports/Pages/Training/Manage.cshtml.cs†L108-L204】【F:Services/ProjectOfficeReports/Training/TrainingNotificationService.cs†L14-L87】
- **Usage analytics (T+1–24h):** Track training KPIs for unexpected zero counts. Validate that exports are being generated successfully by reviewing download logs.
- **Support readiness:** Prepare rollback plan (disable feature flag + revert migrations if necessary) and ensure helpdesk script routes questions to the Project Office product owner.

Document owner: Release Engineering / Project Office product team.
