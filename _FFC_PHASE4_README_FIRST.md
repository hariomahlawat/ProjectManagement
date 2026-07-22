# FFC Phase 4 — Interaction Hardening

## Apply this package when

Phase 1, Phase 2, Phase 3 and the Phase 3 `Context` fix are already present.
Extract this package into the **ProjectManagement project root**, preserve the folder structure, and replace matching files.

No database migration is required.

## Implemented

- Replaced browser `window.confirm()` prompts with one reusable, accessible PRISM confirmation dialog.
- Corrected the false dirty-state warning in the untouched attachment drawer.
- Restores captured form values when the user confirms **Discard changes**; reopening a drawer no longer shows supposedly discarded edits.
- Keeps the native browser `beforeunload` safeguard only for actual page navigation.
- Prevents stale country and PRISM Project IDs from being submitted when visible search text changes.
- Adds keyboard selection, exact-match Enter handling and accessible active-option behaviour to searchable selectors.
- Prevents invalid submissions from disabling dirty-form protection.
- Prevents duplicate submissions and shows action-specific busy states for save, create, upload, archive, restore and delete operations.
- Validates attachment type and size before submission and disables upload until a valid file is selected.
- Prevents linked canonical progress from appearing to clear successfully when blank input cannot represent a safe deletion.
- Replaces fragile project-editor `data-*` hydration with a JSON editor payload.
- Removes duplicate workspace header actions, repeated shared-progress wording and duplicated attachment actions.
- Presents missing delivery/installation dates as data-completeness warnings.
- Adds JavaScript and .NET contract/command regression tests.

## Protected assets

The following Detailed Table files are unchanged:

- `Areas/ProjectOfficeReports/Pages/FFC/MapTableDetailed.cshtml`
- `Areas/ProjectOfficeReports/Pages/FFC/_DetailedTablePartial.cshtml`
- `wwwroot/js/pages/project-office-reports/ffc/ffc-map-table-detailed-inline-edit.js`

## Deployment

From the project root:

```powershell
Remove-Item -Recurse -Force .\bin, .\obj -ErrorAction SilentlyContinue

npm ci
npm test
dotnet restore
dotnet build ProjectManagement.csproj -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release
```

Restart the application and hard-refresh the browser after deployment.

## Verification completed while preparing the package

- `npm test`: **212 passed, 0 failed**
- JavaScript syntax checks: passed
- Patch application check against the Phase 3 + Context-fix baseline: passed
- XML validation for the modified test project file: passed
- Git whitespace/error check: passed
- Protected Detailed Table diff: empty

The preparation environment does not contain the .NET SDK, so `dotnet build` and .NET tests must be run on the development machine.
