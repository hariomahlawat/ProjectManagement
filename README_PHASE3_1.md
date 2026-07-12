# PRISM Officer Conference — Phase 3.1 Stabilisation

Apply this package after Phase 1, Phase 2, Phase 3 and the RZ1011 Razor hotfix.
The archive preserves project-relative paths. Replace existing files and add the new files.

## What this package fixes

- Fixes Project conference-direction saving by writing valid structured JSON to `RemarkAudit.Meta` (`jsonb`).
- Validates all remark audit metadata before any database write.
- Adds structured server-side error logging with a trace reference returned to the browser.
- Introduces a validated page-handler input model.
- Adds a narrow `IProjectIdeaCommandService` dependency for testability and module boundaries.
- Stops the conference read service from loading complete remark/comment/update histories.
- Makes the conference page more compact: no redundant item-type pills or duplicate empty-state text.
- Aligns the inline editor with the direction/progress area and makes the textarea use the available width.
- Adds row-local save/failure feedback, focus restoration, `aria-busy`, and `aria-invalid` handling.
- Adds command-path and audit-metadata regression tests.

## Database

No migration is required.

## Validation commands

From the project root:

```powershell
npm ci
node --check .\wwwroot\js\pages\officer-conference.js
node --test .\wwwroot\js\pages\officer-conference.test.js
dotnet clean
dotnet build -c Release
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release --no-build
```

## Focused verification

1. Open Officer Workload as Comdt or HoD.
2. Open an officer's Conference Review.
3. Add a direction against a Project, Idea and Other Task.
4. Confirm each save completes without a page reload.
5. Confirm the same entry appears in the native remarks/discussion/updates stream.
6. Confirm an unauthorised role cannot create a Conference-type remark.
7. Confirm Project remark audit metadata is stored as JSON and contains:
   - `origin: officer-conference-review`
   - `officerUserId: <selected officer id>`
