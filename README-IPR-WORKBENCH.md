# PRISM ERP — IPR Workbench

## Installation

1. Stop the running application or IIS application pool.
2. Extract this package.
3. Copy the package contents over the **ProjectManagement solution root**.
4. Allow the listed files to replace the existing files.
5. Clean and rebuild the solution.
6. Start the application and perform the verification checks below.

No database migration or configuration change is required.

## What this package implements

### Records workbench

- Compact operational insight ribbon instead of oversized KPI cards.
- Row-based enterprise register retained for fast comparison.
- Persistent record inspector for filing, project, notes and attachments.
- Sticky table header and internally scrollable record surface.
- Compact and comfortable density modes, saved in the browser.
- Searchable project filter using the same ID-backed combobox pattern as the editor.
- Page-size selector for 10, 25 or 50 records.
- Clearer status semantics: green for granted and amber for awaiting grant.
- Attachment count shown only when supporting files exist.

### Project dossiers

- Each project is shown once instead of repeating the project on every filing row.
- Expandable project groups show all linked IPR records.
- Search by project name or IPR title.
- Filter for awaiting, granted and unassigned records.
- Project-level filed/granted/awaiting summary and lifecycle information.

### Follow-up view

- Long-pending filings awaiting grant for more than three years.
- Missing project linkage.
- Missing filing number or filing date.
- Granted records without a grant date.
- Records without supporting attachments.
- Unique-record attention count so a record with several issues is not double-counted in the headline.

### Analytics

- Grant rate.
- Median time to grant.
- Median age of pending filings.
- Long-pending count.
- Filing and grant events by year.
- Awaiting-age bands and oldest-pending list.
- Records with missing dates are excluded from age calculations and remain visible in Follow-up.

### Existing write workflow retained

- Explicit Razor Page handler routing.
- Reliable project assignment, reassignment and removal.
- Handler-specific validation.
- Searchable editor project picker.
- Correct Post/Redirect/Get redirects.
- Attachment and delete workflows retained.

## Files replaced

See `CHANGED-FILES.txt`.

## Verification

Run from the solution root:

```powershell
dotnet clean ProjectManagement.sln
dotnet build ProjectManagement.sln
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj
node --check wwwroot/js/project-office-reports/ipr/index.js
node --test wwwroot/js/project-office-reports/ipr/index.test.js
```

Then verify in the browser:

1. Records opens with the compact insight ribbon, grid and inspector.
2. Clicking different rows updates the inspector without opening the editor.
3. Project filtering supports typing, keyboard selection and clearing.
4. Edit an unassigned record, link a project and save; the row, inspector and project count must update after redirect.
5. Projects shows one expandable dossier per project and a separate unassigned group.
6. Follow-up opens and lists records requiring administrative action.
7. Analytics displays the chart, age bands and oldest-pending records.
8. Attachments open from both the row inspector and edit drawer.
9. Compact/comfortable density persists after refresh.
10. No horizontal scrollbar appears inside the edit drawer.

## Validation performed in the preparation environment

- JavaScript syntax check: passed.
- Front-end regression suite: 10 of 10 tests passed.
- C# and Razor source received static structural checks.

The .NET SDK was not available in the preparation environment, so `dotnet build` and the C# test suite could not be executed there. Run the commands above before publishing to IIS.
