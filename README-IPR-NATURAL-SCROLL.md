# IPR Records — Natural Page Scrolling

Copy the package contents over the **ProjectManagement solution root** and replace the existing files.

## Files replaced

- `Application/Ipr/IprFilter.cs`
- `Areas/ProjectOfficeReports/Pages/Ipr/Index.cshtml.cs`
- `Areas/ProjectOfficeReports/Pages/Ipr/Index.SelectLists.cs`
- `wwwroot/css/site-ipr.css`
- `wwwroot/js/project-office-reports/ipr/index.test.js`

## Behaviour implemented

- Removes the fixed-height Records workbench.
- Removes the internal vertical scrollbar from the record grid.
- Removes the internal vertical scrollbar from the selected-filing inspector.
- Uses the browser page as the single vertical scrolling surface.
- Retains the side-by-side grid and filing inspector on desktop.
- Retains horizontal overflow only where a narrow viewport genuinely requires it.
- Keeps column headings visible beneath the 52 px application header while the page scrolls.
- Changes the default page size from 25 to 15 records.
- Provides page-size options of 10, 15, 25 and 50.
- Preserves compact/comfortable density, keyboard selection, inspector resizing and pagination.

## Build procedure

1. Stop the running application.
2. Replace the five code files.
3. Delete the project `bin` and `obj` folders if Visual Studio retains stale Razor diagnostics.
4. Rebuild the solution.
5. Open `/ProjectOfficeReports/Ipr` without a `pageSize` query value and confirm that 15 records are shown by default.

## Verification

- The browser should show only its normal page scrollbar.
- The record grid should expand to display the complete current page.
- The inspector should expand naturally and must not show its own vertical scrollbar.
- Page navigation should remain below the final row.
- At narrower widths, the table may scroll horizontally, but it must not acquire a nested vertical scroll area.

No database migration or configuration change is required.
