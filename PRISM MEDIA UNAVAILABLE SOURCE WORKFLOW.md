# PRISM Media Unavailable Source Workflow

## Purpose

Separates genuine missing source files from processing failures and gives administrators a controlled restoration workflow.

## Behaviour

- Source-unavailable assets remain in the catalogue for audit.
- They are excluded from the normal Photos timeline through `IsAvailable = false`.
- Missing source files are no longer displayed as general dead-letter processing failures.
- Available, completed, processing and unavailable counts are shown independently.
- Administrators can recheck one unavailable item or up to 100 items at a time.
- Rechecking only restores an asset after a provider successfully opens and reads its source stream.
- A restored photo is reset safely and its existing analysis job is requeued idempotently.
- Rechecking does not alter, move or delete the original source file.
- Routine permanent retry operations no longer revive source-unavailable assets.

## Deployment

Copy the contents of this folder into the ProjectManagement application root, replacing matching files.

No database migration is required.

Run:

```powershell
dotnet clean
dotnet restore
dotnet build
dotnet test
```

Restart the application after deployment.
