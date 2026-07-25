# Project Overview — Transfer of Technology header card

This is an incremental ready-to-replace update for the current Project Overview implementation after the multi-JDP, proliferation, completion-precision and lifecycle-actions updates.

## Apply

Run `Apply-Project-Tot-Header-v2.ps1` from the directory containing `ProjectManagement.csproj`, or copy the replacement folder contents into that directory while preserving the relative paths. Replace existing files when prompted. Files marked **new** must be added.

## Files

- `Pages/Projects/_ProjectCommandHeader.cshtml` — replace
- `Pages/Projects/_ProjectWorkspaceHosts.cshtml` — replace
- `Pages/Projects/_ProjectTotDrawer.cshtml` — new
- `Pages/Projects/Overview.Tot.cs` — new
- `Pages/Projects/Overview.cshtml` — replace
- `wwwroot/js/projects/overview-tot.js` — new
- `wwwroot/css/pages/project-tot-drawer.css` — new

No database migration is required.

## Implemented behaviour

- Active projects retain the **Current stage** card.
- Completed projects use the first card for **Transfer of Technology**.
- Cancelled projects show **Project status** instead of an obsolete stage.
- The ToT card opens a right-side summary/editor drawer.
- ToT status, partial start/completion dates, MET details and first-production-model information can be recorded from the drawer.
- The latest ToT remark is visible in the drawer.
- Pending ToT approval is displayed and editing is disabled until the request is decided.
- The lower Technology Transfer panel is removed from Project Overview.
- ToT updates from Project Overview are rejected unless the project is completed and not archived/deleted.
- Permissions remain Admin, HoD and the assigned Project Officer.

## Verification

Run:

```powershell
dotnet clean
dotnet build
dotnet test
```

Verify separately with:

1. an active project;
2. a completed project without a ToT record;
3. a completed project with ToT in progress;
4. a completed project with a pending ToT request;
5. a cancelled project;
6. a read-only user.
