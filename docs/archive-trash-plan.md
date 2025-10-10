# Project Archiving and Trash Management Plan

## Roles and Permissions
- **Head of Department (HoD)**
  - Archive or restore any project from archive.
  - Move any project to Trash.
  - Cannot view the Trash tab, restore from Trash, or permanently delete projects.
- **Administrator**
  - Archive or restore any project from archive.
  - Move any project to Trash.
  - Access the Trash tab, list trashed projects, restore from Trash, and permanently delete.
- **All Other Users**
  - No archive or delete permissions.

## Data Model Changes
- Extend the `Projects` table with the following columns:
  - `IsArchived` (`bit`, default `false`)
  - `ArchivedAt` (`datetimeoffset`, nullable)
  - `ArchivedByUserId` (`nvarchar`, nullable)
  - `IsDeleted` (`bit`, default `false`)
  - `DeletedAt` (`datetimeoffset`, nullable)
  - `DeletedByUserId` (`nvarchar`, nullable)
  - `DeleteReason` (`nvarchar(512)`, nullable)
  - `DeleteMethod` (`nvarchar(32)`, nullable) – values: `"Trash"` or `"Purge"`
  - `DeleteApprovedByUserId` (`nvarchar`, nullable)
- Indexing:
  - Composite index on `(IsDeleted, IsArchived)` for primary listings.
  - Filtered index on `IsDeleted = 1` to accelerate Trash queries.

## Query Behaviour
- **Default listings, counts, KPIs:** `WHERE IsDeleted = 0 AND IsArchived = 0`.
- **Archived filter:** include `IsArchived = 1 AND IsDeleted = 0`.
- **Trash view (Admin only):** `WHERE IsDeleted = 1`.
- **Search:** exclude `IsDeleted = 1` by default; optionally include archived when "Include archived" is selected.

## UI Requirements
- Project card and header kebab actions (Ongoing/Completed/Cancelled states):
  - `Archive` (toggle to `Restore from Archive` when archived)
  - `Move to Trash` (requires reason via modal)
- Trash tab (Admin only):
  - Each row exposes `Restore` and `Permanently delete` actions.
- Confirmation dialogs:
  - Move to Trash modal requires a reason textarea.
  - Permanently delete modal requires typing the project name and a checked "Also remove uploaded files and search indices" option by default.
- Visual indicators:
  - Display an `Archived` badge on applicable cards.
  - Hide trashed items from non-Admin views.

## Endpoints and Service Methods
- `POST /projects/{id}/archive`
- `POST /projects/{id}/restore-archive`
- `POST /projects/{id}/trash` (payload: `{ reason }`)
- `POST /projects/{id}/restore-trash` (Admin only)
- `POST /projects/{id}/purge` (Admin only)
- `GET /projects?filter=archived` (HoD/Admin)
- `GET /projects?filter=trash` (Admin only)
- Apply existing role constants and policy handlers for authorization.

## Domain Rules and Validations
- HoD and Admin can move any project to Trash regardless of lifecycle state.
- Only Admin can restore from Trash or purge.
- Archiving is lifecycle-independent; archived projects are read-only until restored.
- Moving to Trash sets `IsDeleted = 1`, timestamps, user IDs, `DeleteReason`, and `DeleteMethod = "Trash"`.
- Purging first writes an audit record, then removes database rows and physical assets.

## Retention and Purge Job
- Daily scheduled job:
  - Select projects where `IsDeleted = 1` and `DeletedAt <= now - 30 days`.
  - Invoke the same purge routine used by the Admin endpoint.
- Purge routine must:
  - Remove dependent records (remarks, tasks, documents, links, timeline entries, search index documents) with correct ordering or cascading.
  - Delete file blobs and cached thumbnails.
  - Write an immutable audit log summarizing deleted children and file keys.
  - Remove the project row last.

## Analytics, Search, and KPIs
- Update aggregate queries to exclude archived and trashed projects by default.
- Advanced search exposes "Include archived" toggle; trashed projects never appear outside Admin Trash view.
- Exclude trashed projects from global typeahead.

## Audit Logging
- Record actions in `ProjectAudit`:
  - `ProjectId`, `Action` (`Archive`, `RestoreArchive`, `Trash`, `RestoreTrash`, `Purge`)
  - `PerformedByUserId`, `PerformedAt`
  - `Reason` when supplied.
  - For purge actions, include counts of deleted child entities and file key digests.

## Testing Strategy
- **Unit Tests**
  - Archive toggles and permissions.
  - Trash flow validates required reason.
  - Admin-only restore and purge enforcement.
  - Default listings exclude archived and trashed.
- **Integration Tests**
  - Retention job purges after 30 days.
  - Purge removes children and files without orphans.
  - Search and KPIs ignore archived/trashed entries.
  - Audit entries are emitted for each action.
- **UI Tests**
  - Verify role-based action visibility and Trash tab access.
  - Validate modal confirmations (reason, project name typing, checkbox state).
  - Ensure accessibility with focus trap and keyboard support.

## Migration and Rollout Steps
1. Deploy database migration introducing new fields and indexes.
2. Implement global repository filters for archived/trashed states.
3. Add archive/trash endpoints and connect UI actions.
4. Build the Admin-only Trash tab with restore and purge actions.
5. Introduce retention job and shared purge routine.
6. Update analytics and search queries.
7. Backfill:
   - Trash obvious junk with reason "Backfill cleanup".
   - Archive long-inactive projects.
8. Optionally gate UI updates behind feature flags for gradual rollout.

## Operational Safeguards
- Perform backups of project data and file storage before enabling purge.
- Add metrics and alerting:
  - Track trashed project counts by age.
  - Produce daily purge reports with project IDs and counts.
- Provide an Admin-only "Export project" option (ZIP with JSON + files) from Trash to support audits before purge.
