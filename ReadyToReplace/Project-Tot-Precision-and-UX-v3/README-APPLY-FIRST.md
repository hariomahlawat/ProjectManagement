# Project Overview — ToT precision, lifecycle guard and drawer refinement

This is an incremental ready-to-replace update for the current Project Overview implementation after the multi-JDP, proliferation, project-completion precision and ToT header-card updates.

## Apply

Extract the ZIP into the directory containing `ProjectManagement.csproj`, preserving all relative paths. Replace existing files when prompted. Files that do not already exist must be added.

## Main outcomes

- Transfer of Technology date precision is preserved as **year**, **month and year**, or **exact date**.
- A year-only ToT completion is displayed as `2026`, not as an invented `31 Dec 2026` date.
- Existing ToT dates are migrated as exact dates to preserve current meaning.
- ToT creation, update and request submission are rejected unless the project is completed and operationally editable.
- The ToT drawer has distinct summary and edit footers.
- Status-specific guidance is shown and irrelevant date controls are disabled.
- ToT remarks can be added directly inside the drawer through the existing Remarks API.
- Success feedback is shown inside the drawer instead of obscuring its header.
- Broken cover images reliably switch to the designed fallback, including images that fail before page scripts initialise.

## Migration

The package adds:

`20261206100000_AddProjectTotDatePrecision`

It adds precision columns to `ProjectTots` and `ProjectTotRequests` and backfills existing non-null dates as exact-day precision.

## Verification

Run:

```powershell
dotnet clean
dotnet build
dotnet test
```

Then verify:

1. ToT completed with year only displays only the year.
2. Month-and-year and exact-date entries retain their respective precision after reload.
3. An active, cancelled, archived or deleted project cannot be updated through ToT service paths.
4. Summary-mode footer shows `Close`, `Open tracker`, `Update details`.
5. Edit-mode footer shows only `Cancel`, `Save details`.
6. A ToT remark can be posted without leaving the drawer.
7. A missing/broken cover photo shows the designed fallback.