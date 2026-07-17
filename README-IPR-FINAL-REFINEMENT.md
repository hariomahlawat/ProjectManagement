# IPR Workbench — Final Refinement

## Installation

Copy the contents of this folder over the **ProjectManagement solution root** and replace the existing files.

Stop the running application before replacement. Then clean and rebuild the solution.

## Replaced files

- `Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml`
- `Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml.cs`
- `wwwroot/css/site-ipr.css`
- `wwwroot/js/project-office-reports/ipr/index.js`
- `wwwroot/js/project-office-reports/ipr/index.test.js`
- `Pages/Shared/_Layout.cshtml`

## Implemented refinements

### Records workbench

- Fixed-height desktop workbench to avoid competing page and panel scroll behaviour.
- Independent record-grid and inspector scrolling.
- Resizable inspector with mouse, touch pointer, keyboard arrows and saved width preference.
- Strong selected-row treatment and proper `aria-selected` state.
- Roving keyboard selection using Arrow Up, Arrow Down, Home, End, Enter and Space.
- Two-line title clamping retained.
- Row edit actions are de-emphasised until hover/focus; the inspector remains the primary edit action.
- Explicit **Compact** and **Comfortable** density labels.

### Project dossiers

- Clear count: assigned projects plus the unassigned group.
- Unambiguous **All IPR statuses** filter wording.
- Sorting by attention, project name, total IPRs or latest filing.
- **Expand awaiting** opens only relevant visible dossiers; the control changes to **Collapse all** when any dossier is open.

### Follow-up

- Long-pending filings span the full width.
- Smaller exception groups flow below in balanced columns.
- Deduplication logic is explained in the interface.
- Repetitive Review buttons are replaced by unobtrusive open-record chevrons.
- Age values use one compact format: `3m`, `3y`, `5y 9m`.

### Analytics

- Repeated grant-rate tile replaced with evidence coverage.
- Existing grant-rate information remains available in the global insight ribbon.

### Production footer

- Personal developer attribution is replaced by a neutral **System information** link while retaining the Developer page.

## Validation performed

- JavaScript syntax check passed with Node.js.
- 14 IPR frontend regression tests passed.
- CSS brace and parenthesis balance verified.

The execution environment does not contain the .NET SDK, so run the normal .NET build and C# test suite locally before deployment.

## Build procedure

1. Stop the running PRISM application.
2. Replace the files.
3. Delete stale `bin` and `obj` folders only if Visual Studio continues to show obsolete Razor diagnostics.
4. Rebuild the solution.
5. Verify Records, Projects, Follow-up and Analytics tabs.

No database migration or configuration change is required.
