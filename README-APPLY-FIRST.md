# PRISM ERP — IPR Register Filter Scope Completion

## Apply

1. Stop the application or IIS application pool.
2. Back up or commit the current source.
3. Copy this folder's contents over the **ProjectManagement solution root**.
4. Allow all listed code files to replace the existing files.
5. Keep the four new files:
   - `Application/Ipr/IprQueryFilter.cs`
   - `Areas/ProjectOfficeReports/Pages/Ipr/_ActiveFilterChips.cshtml`
   - `Areas/ProjectOfficeReports/Pages/Ipr/_IprFilterDrawer.cshtml`
   - `Areas/ProjectOfficeReports/Pages/Ipr/_StructuredFilterStateFields.cshtml`
6. Clean `bin` and `obj`, rebuild the solution, and restart the application.

This package is based on the latest **IPR Register Access and Navigation Stabilisation** implementation. It contains only the files changed by this filter-scope phase.

## Delivered behaviour

- Adds a module-header **Filters** button alongside guidance, export and record creation.
- Shows the number of active structured filters on the header button.
- Opens a professional right-side filter drawer with:
  - IPR category;
  - current protection status;
  - project;
  - project linkage;
  - date basis;
  - year;
  - supporting-evidence state.
- Supports both:
  - **Filed year**; and
  - **Grant / registration year**.
- Grant / registration year uses the patent grant or copyright registration date. Pending records are therefore excluded from that year scope.
- Applies the structured scope consistently to:
  - Records;
  - Projects;
  - Follow-up;
  - Analytics;
  - Export.
- Keeps Records search visible and explicit, with Search and Clear search controls.
- Displays removable active-filter chips and **Clear all**.
- Adds focused Follow-up issue filters for long pending, data gaps, unassigned records and missing evidence.
- Shows the active scope explicitly on Analytics.
- Keeps insight-ribbon navigation within the current project/date/linkage/evidence scope while changing category or status.
- Preserves the working `pageNumber` pagination and selected-filing navigation.
- Retains natural browser scrolling and existing IPR edit permissions.

## Database

No database migration or configuration change is required.

## Verification after replacement

1. Open `/ProjectOfficeReports/Ipr`.
2. Confirm **Filters** appears in the page header.
3. Select **Filed year**, choose a year, and apply; confirm the active chip and filtered KPIs/records.
4. Change to **Grant / registration year**; confirm only records protected in that year remain.
5. Apply Project, Linkage or Evidence filters and confirm the scope remains while moving among Records, Projects, Follow-up and Analytics.
6. Confirm Export follows the same active scope.
7. Remove individual chips and use **Clear all**.
8. Confirm top and bottom pagination still navigate with `pageNumber`.
9. Confirm a read-only authenticated user can filter and export but cannot edit IPR records.

## Recommended commands

```powershell
dotnet clean .\ProjectManagement.sln
dotnet build .\ProjectManagement.sln -c Release
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release --filter "FullyQualifiedName~Ipr"
node --check .\wwwroot\js\project-office-reports\ipr\index.js
node --test .\wwwroot\js\project-office-reports\ipr\index.test.js
```
