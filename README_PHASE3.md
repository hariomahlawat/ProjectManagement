# PRISM Conference Remarks — Phase 3

## Purpose

This package adds the compact officer-specific conference review to the existing Command Workspace. It is designed to be applied after the Phase 1 and Phase 2 conference-remarks packages.

## Delivered functionality

- Conference-review icon on each officer card in the Comdt/HoD Officer Workload view.
- Dedicated officer review route: `/Workspace/Conference/{officerUserId}`.
- Compact Projects, Ideas and Other Tasks sections using the existing workload-card visual language.
- Latest conference direction, state snapshot and progress since direction for every current item.
- Previous/next officer navigation and a selector that follows the saved officer-card order.
- Inline addition of conference directions without full-page reload.
- Keyboard controls: `Ctrl+Enter`/`Cmd+Enter` to save and `Esc` to cancel.
- Server-side Comdt/HoD authorization and item-to-officer validation.
- Native writes to the existing Project, Project Idea and Action Task remark streams.
- Project stage snapshot contrast correction and consistent `HoD` presentation.
- Batched read queries; no per-row remark queries.

## Data model

No database migration is required for Phase 3. It uses the Phase 1/2 Conference remark types and snapshot fields.

## Installation

1. Confirm that Phase 1 and Phase 2 have already been applied.
2. Back up the current solution.
3. Copy the folders and files from this archive into the project root, preserving paths.
4. Allow replacement of existing files when prompted.
5. Restore frontend dependencies and validate the solution:

```powershell
npm ci
npm test
dotnet restore
dotnet build -c Release
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release --no-build
dotnet publish .\ProjectManagement.csproj -c Release -r win-x64 --self-contained false
```

6. Deploy the resulting publish output through the established IIS deployment process.

## Operational checks

- Sign in as Comdt or HoD and open **My Workspace → Officer workload**.
- Confirm the conference icon appears on each officer card.
- Open an officer and verify Projects, Ideas and Other Tasks.
- Add a direction inline against one item.
- Confirm the same Conference remark appears in that Project, Idea or Action Task's normal remarks/update stream.
- Confirm Project Officer mode does not display the conference icon.
- Confirm non-Comdt/HoD users cannot access the conference route directly.

## Validation completed in the preparation environment

- Officer conference JavaScript syntax check: passed.
- Officer conference JavaScript tests: 4 passed.
- Modified/new Razor view guardrails: passed.
- Source delimiter and structural checks: passed.
- Patch dry-run and archive integrity: passed.

The .NET SDK was not available in the preparation environment, so the C# build and xUnit suite must be run on the development machine before deployment.
