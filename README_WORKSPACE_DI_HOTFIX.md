# PRISM Admin Phase B — Workspace DI Hotfix

## Purpose
Restores the Conference/Workspace registrations and Conference authorization policy that were unintentionally omitted when the Admin Phase B `Program.cs` replaced the newer Conference Phase 3.7 version.

## Apply order
1. Conference Phase 3.7
2. Admin Phase A
3. Admin Phase B
4. Admin Phase B build hotfix
5. This Workspace DI hotfix

## Replace
Copy `Program.cs` to the project root, replacing the existing file.

## Restored registrations
- `IProjectIdeaCommandService`
- `IOfficerWorkloadReadService`
- `IOfficerConferenceReadService`
- `IConferenceRemarkCommandService`
- `IConferenceTaskCommandService`
- `IConferenceIdeaCommandService`
- `ConferenceRemarks.Manage` authorization policy

## Validation
```powershell
dotnet clean
dotnet build -c Release
dotnet test -c Release
```

No database migration is required.
