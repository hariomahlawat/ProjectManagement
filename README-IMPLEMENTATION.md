# PRISM ERP — Project Officer Workspace Finalisation

## Delivery contents

This implementation finalises the multi-page Project Officer workspace and addresses the reviewed functional, semantic, performance and UX issues.

### Implemented

- Closed-set workspace routing through `ProjectOfficerWorkspaceView`; unknown values fall back to Overview.
- View-specific data composition so dedicated pages no longer load the entire overview model.
- Lightweight navigation summaries with a short-lived exact action-count cache.
- Typed, stable action grouping for projects, tasks, ideas, AOTS documents and conference directions.
- Exact action totals, including unread AOTS documents beyond the preview limit.
- Operational summary generated from the actual action queue, including overdue updates and all action categories.
- Item-level action filtering inside grouped work items.
- Exact action labels such as `Set current-stage PDC` and `Add Project Officer update`.
- Compact Assigned Projects summary on the Overview page.
- Date-only handling for workspace task due dates; the impossible `No due date` filter has been removed.
- Due-or-pinned reminder behaviour aligned with the Follow-ups copy.
- ERP activity presentation now shows monitoring coverage and the actual latest activity category without a misleading percentage.
- Full-row document links, improved favourite/unread placement and denser document rows.
- State-aware Clear buttons and simplified empty-task and empty-follow-up states.
- Short, non-truncated workspace rail identity and corrected browser title composition.
- Updated C# presentation/action-queue tests and JavaScript workspace tests.

## Installation

1. Back up the current project.
2. Extract `ProjectOfficerWorkspace-ready-to-paste.zip`.
3. Copy the extracted files into the project root, preserving the directory structure and replacing existing files when prompted.
4. No Entity Framework migration is required for this implementation.
5. Run:

```powershell
npm ci
npm test
npm run build
dotnet build
dotnet test
```

`Program.cs` already registers `AddMemoryCache()`, so no service-registration change is required.

## Verification completed in the delivery environment

- `npm test`: passed — 168/168 tests.
- `npm run build`: passed.
- Static balance check on all modified C# files: passed.
- `dotnet build` and `dotnet test` were not executable because the delivery environment does not contain the .NET SDK. They must be run on the development machine before deployment.

## Files included

- `Pages/Workspace/_ProjectOfficerProjectSummary.cshtml`
- `Services/Workspace/ProjectOfficerWorkspaceView.cs`
- `Pages/Workspace/Index.cshtml`
- `Pages/Workspace/Index.cshtml.cs`
- `Pages/Workspace/_ProjectOfficerActionGroups.cshtml`
- `Pages/Workspace/_ProjectOfficerActionQueue.cshtml`
- `Pages/Workspace/_ProjectOfficerActivity.cshtml`
- `Pages/Workspace/_ProjectOfficerDocumentList.cshtml`
- `Pages/Workspace/_ProjectOfficerFollowUps.cshtml`
- `Pages/Workspace/_ProjectOfficerIdeas.cshtml`
- `Pages/Workspace/_ProjectOfficerOverview.cshtml`
- `Pages/Workspace/_ProjectOfficerProjectTable.cshtml`
- `Pages/Workspace/_ProjectOfficerProjects.cshtml`
- `Pages/Workspace/_ProjectOfficerTasks.cshtml`
- `Pages/Workspace/_ProjectOfficerWorkspace.cshtml`
- `ProjectManagement.Tests/WorkspaceActionQueueBuilderTests.cs`
- `ProjectManagement.Tests/WorkspaceDisplayHelpersTests.cs`
- `ProjectManagement.Tests/WorkspaceViewModelPresentationTests.cs`
- `Services/Workspace/ProjectOfficerWorkspaceService.cs`
- `Services/Workspace/WorkspaceActionQueueBuilder.cs`
- `Services/Workspace/WorkspaceNudgeService.cs`
- `ViewModels/Workspace/ErpActivityStripViewModels.cs`
- `ViewModels/Workspace/WorkspaceViewModels.cs`
- `wwwroot/css/workspace.css`
- `wwwroot/js/pages/workspace-index.js`
- `wwwroot/js/pages/workspace-index.test.js`

## Deployment note

After replacing the files, publish from a clean build output. Do not copy `node_modules`, local runtime uploads, database data or prior publish folders into the production deployment unless they are deliberately part of the deployment process.
