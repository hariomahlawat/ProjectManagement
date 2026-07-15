# PRISM ERP — Proliferation Corrective Release

## Purpose

This package is a path-preserved overlay for the current PRISM ERP solution. It corrects the functional and usability defects identified after the proliferation-module redesign, without changing the intended counting doctrine:

- **SDD default:** annual quantity + approved detailed entries.
- **515 ABW default:** approved annual quantity.
- A deliberate project/source/year counting exception may select another rule.
- Admin and HoD submissions continue to receive immediate approval.

## Main corrections included

### Overview

- Restores both charts by loading the existing local Chart.js asset.
- Uses stacked SDD/515 ABW trend bars.
- Creates the technical-category chart only after its disclosure panel is opened.
- Adds chart failure and no-data fallbacks.
- Replaces the misleading table-filter search with a shared project picker that opens the selected project total.
- Limits the project table to the top 15 initially, with controlled disclosure of all projects.
- Adds a visible warning when invalid historical years are present.

### Records

- Project filtering now applies only after a project is explicitly selected.
- Adds keyboard-accessible project suggestions, clear state, deep-link project loading and unavailable-project handling.
- Validates the year before issuing requests.
- Searches project name, project code, unit and remarks consistently.
- Loads detailed entries only when a project-year row is expanded.
- Preserves filter state during paging and export.
- Marks invalid historical years instead of presenting them as normal data.

### Reports

- Synchronises the selected report card, hidden report value, clear action and saved views.
- Uses the shared project picker for consistent keyboard and selection behaviour.
- Prevents a report from running when the selected project conflicts with selected categories.
- Adds request cancellation, stale-response protection and a visible generating state.
- Makes chosen display columns apply to Excel export.
- Converts report validation exceptions into clear HTTP 400 responses.
- Changes row action wording from **Manage** to **Open record**.

### Manage records

- Detailed-entry search now includes remarks.
- Existing-record rows show the receiving unit, making similar records distinguishable.
- Adds mandatory rejection reasons and records them in audit data.
- Requires a reason for any non-default counting exception.
- Shows annual, detailed, current and proposed totals before a counting-rule change.
- Keeps a saved record selected and reports whether it was approved or submitted for approval.
- Non-Admin/HoD edits to an approved record return it to Pending and clear prior approval metadata.
- Approved records can be deleted only by Admin or HoD.
- Adds minimum-date validation of **01 Jan 2000** in both client and server code.
- Moves counting-rule export into the counting-exceptions section.
- Improves JSON/API error messages shown to the user.

### Security and integrity

- Enforces antiforgery validation on proliferation mutation endpoints.
- Escapes user search text used in PostgreSQL `ILIKE` patterns.
- Keeps CSV formula-injection protection for counting-exception export.
- Uses lazy detail endpoints and grouped summary responses to reduce initial payload size.

## Database impact

**No EF Core migration is required.**

A read-only audit script is supplied at:

`Deployment/ProliferationDataQualityAudit.sql`

Run it before deployment. The screenshots showed at least one malformed historical year (`24`). The release prevents new dates before 01 Jan 2000, but it cannot infer the correct year for an existing record. Verify each flagged row against the original source document and correct it deliberately.

## Replacement procedure

1. Back up the application folder and production database.
2. Run `Deployment/ProliferationDataQualityAudit.sql` against a database copy or production under the authorised maintenance procedure.
3. Extract this package into the solution root—the folder containing `ProjectManagement.csproj`.
4. Allow all matching files to be overwritten.
5. Build and test:

```powershell
dotnet restore .\ProjectManagement.sln
dotnet build .\ProjectManagement.sln -c Release --no-restore
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release --no-build
```

6. Publish using the normal production profile:

```powershell
dotnet publish .\ProjectManagement.csproj -c Release -o .\publish
```

7. Deploy through the existing IIS maintenance procedure and perform a browser hard refresh.

## Mandatory functional verification

### Overview

- Both charts render.
- Last 10 years / All years changes the trend chart.
- Opening Additional analysis renders the category chart.
- Project search supports mouse, Arrow keys, Enter, Escape and Clear.
- View total opens the selected project.
- Show all projects / Show top 15 works.
- Both export links download data.

### Records

- Typed project text alone does not falsely filter the list.
- Selecting a suggestion filters records.
- Clear restores all projects.
- Source, year and general search work independently and together.
- Remarks search returns matching records.
- Expanding a row loads detailed entries.
- Previous, Next and row-count selection retain filters.
- Export uses the same project/source/year/search filters.

### Reports

- Every report card activates the correct controls and report type.
- Saved view load restores the corresponding active report card.
- Clear filters returns to the default report consistently.
- Generate prevents duplicate/stale requests.
- Category/project mismatch is blocked with a clear message.
- Sorting and paging preserve the report context.
- Column selection affects both the screen and Excel export.
- Open record deep-links to the appropriate manager context.

### Manage records

- New detailed and annual actions preserve useful project/source/year context.
- Date before 01 Jan 2000 is rejected in the browser and by the API.
- Save leaves the saved record selected.
- Approve works on Pending records.
- Reject requires a reason.
- Search checks project, code, unit and remarks.
- Approved-record edit by a non-Admin/HoD becomes Pending.
- Approved-record delete is blocked for non-Admin/HoD.
- Counting exception requires a reason and shows calculation impact.
- Restore source default and export counting exceptions work.

## Validation completed in this environment

- JavaScript syntax check passed for all six changed module scripts.
- Changed C# files passed lexical delimiter validation.
- Razor pages passed literal-ID uniqueness checks.
- Local script references were verified.

The detailed log is in `Deployment/STATIC-VALIDATION.txt`.

The .NET SDK/compiler was not available in the execution environment, so `dotnet build` and `dotnet test` must be completed on the development machine before production deployment.
