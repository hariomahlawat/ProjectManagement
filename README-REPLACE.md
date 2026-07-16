# PRISM Proliferation Corrective Refactor

This focused package is intended for the currently running proliferation release shown in the supplied screenshots. It fixes the Manage runtime failure, makes both charts discoverable, and further aligns module density with the rest of PRISM ERP.

## Replacement

1. Stop the application or recycle the IIS application pool after replacement.
2. Copy the contents of this package into the solution root containing `ProjectManagement.csproj`.
3. Preserve the directory structure and replace the existing files.
4. Rebuild and republish the application.
5. Perform a hard refresh in the browser (`Ctrl+F5`) so the revised JavaScript and CSS are loaded.

No database migration is required.

## Principal corrections

- Removes the Manage-page temporal-dead-zone runtime error caused by invoking contextual actions before editor state was initialised.
- Loads existing records automatically on Manage page opening.
- Restores detailed/annual field visibility, validation, Save, approval, rejection, deletion and record selection.
- Requires an explicit project selection for a new record; the first project is no longer silently preselected.
- Moves Manage actions into the editor workbench and removes redundant full-width command layers.
- Replaces the hidden technical-category disclosure with a visible Analytics tab beside Year-wise trend.
- Shows only correction-required items in the red Data quality badge; possible duplicates remain visible inside Data quality review.
- Collapses the Reports chooser to the selected-report strip on initial load; Change report reopens it.
- Reduces page, filter, card, chart, KPI, project-year and Manage-workbench spacing.

## Verification

Run:

```powershell
dotnet restore
dotnet build ProjectManagement.csproj -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release
```

Then verify:

1. Open **Proliferation > Manage** and confirm that records load without touching a filter.
2. Confirm the browser console contains no `ReferenceError` from `proliferation-manage.js`.
3. Start a new detailed entry; only Date and Receiving unit are shown.
4. Switch to Annual quantity; Year is shown and detailed-only fields are hidden.
5. Confirm Save remains disabled until Project, Source, required date/year and Quantity are valid.
6. Select an existing record and confirm approval status and contextual actions load.
7. Open Pending approval and confirm only pending records are listed.
8. Open Overview > Analytics and switch between Year-wise and Technical category.
9. Open Reports and confirm the selected-report strip appears; Change report reopens the report choices.
10. Verify Overview, Records, Project total, Reports and Manage at 1366×768 and 125% browser zoom.
