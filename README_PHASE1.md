# Conference Remarks — Phase 1 Foundation

This bundle contains only the files added or changed for Phase 1. Extract it over the root of the existing `ProjectManagement` solution and allow matching files to be replaced.

## Implemented

- Added the `ConferenceRemarks.Manage` authorization policy, restricted to `Comdt` and `HoD`.
- Added `Conference` as a native type for Project remarks, Project Idea comments and Action Task updates.
- Added server-side command-role enforcement for Conference remarks.
- Added immutable state snapshots for Idea comments and Action Task updates.
- Added canonical automatic stage snapshotting for Project Conference remarks.
- Extracted `IOfficerWorkloadReadService` / `OfficerWorkloadReadService` and reused it from Command and Project Officer workspaces.
- Replaced workspace-specific project-stage calculation with `PresentStageHelper` and workflow-aware stage metadata.
- Added migration `20261201170000_AddConferenceRemarkFoundation`, updated the EF snapshot and immutable migration manifest.
- Added regression tests for policy restrictions, native remark types, state snapshots, canonical stage resolution and migration lineage.

## Deployment sequence

1. Back up the application and PostgreSQL database.
2. Copy this bundle over the project root.
3. From the project root, restore frontend dependencies if required by the build: `npm ci`.
4. Run `dotnet restore`.
5. Run `dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj`.
6. Publish and deploy through the existing process. The application’s mandatory startup migration mechanism will apply the new migration.

Do not rename, edit or remove the migration ID after deployment. Phase 1 intentionally contains no Conference page or remarks-stream UI; those belong to the subsequent implementation phases.
