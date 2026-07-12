# PRISM ERP — Ongoing Projects Timeline Fix

## Replacement scope

Copy the following files into the project root while preserving their relative paths:

- `Services/Projects/OngoingProjectsReadService.cs`
- `Services/Projects/OngoingStagePresentationPolicy.cs` *(new file)*
- `Pages/Projects/Ongoing/Index.cshtml.cs`
- `Pages/Projects/Ongoing/Index.cshtml`
- `wwwroot/css/projects/ongoing-conference.css`
- `ProjectManagement.Tests/OngoingStagePresentationPolicyTests.cs` *(new test file)*

## Corrections implemented

1. Skipped lifecycle stages are treated as terminal stages throughout current-stage resolution, bucket counts and timeline rendering.
2. A skipped stage now displays `Skipped`; it is no longer counted or displayed as a missing completion date.
3. Development (`DEVP`) is considered prolonged only after six calendar months.
4. Other stages retain the existing 90-day prolonged threshold.
5. Table warning thresholds are aligned: three calendar months for Development and 45 days for other stages.
6. Later-to-earlier is now the default project ordering for page load, filters, clear-filter action, export and direct service calls.
7. Current-stage resolution is centralised so the card view, table view and stage-distribution counts cannot disagree.
8. The application day is resolved in IST through the existing `IClock` registration.

## Database impact

No EF Core migration is required.

## Validation

Run locally:

```powershell
dotnet clean
dotnet build -c Release
dotnet test -c Release
```

Perform a hard browser refresh after deployment so the updated CSS is loaded.
