# PRISM ARPP/PPP Production Finishing

This package completes the focused UI/UX finishing pass on top of the **ARPP/PPP Fresh Experience Redesign**.

## Scope

The package addresses the remaining issues visible in the latest screenshots:

- Rebuilds the Print / PDF summary and table for A4 landscape output.
- Replaces the broken category summary box with a balanced four-part strip.
- Distinguishes an unlocked working copy from the active published revision in print.
- Combines issued and linked project information into one wider print column.
- Removes the residual blank print column and uses nine explicit columns.
- Uses compact Crore/Lakh currency presentation in print.
- Keeps repeating print table headers and page-safe rows.
- Makes the workspace **Issues** action compact and removes validation text from the save-state message.
- Shortens workspace search to **Find row or project**.
- Adds a collapsible, preference-preserving row navigator.
- Keeps row actions reachable through a sticky right-hand actions column on desktop.
- Reduces the visual weight of the horizontal grid scrollbar.
- Restores **Search ARPP records** in the published library.
- Removes the duplicate page-level Reconciliation action from Administration.
- Clarifies the Administration KPIs as **Active published records** and **Records under work**.
- Compresses document identity on the record Overview tab.

## Important

- **No database migration is included or required.**
- No entity, publication-rule, IPA-resolution, stage-synchronisation or service-contract change is included.
- Apply this package to the codebase containing the immediately preceding **Fresh Experience Redesign**.

## Apply

1. Stop the running PRISM debug or IIS process.
2. Back up the current source.
3. Copy the contents of `PRISM_ARPP_Production_Finishing` into the project root.
4. Replace matching files while preserving the supplied folder paths.
5. Clean and rebuild the solution.

```powershell
dotnet clean ProjectManagement.sln
dotnet restore ProjectManagement.sln
dotnet build ProjectManagement.sln -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release --no-build
```

## Focused browser verification

After building, verify:

1. Published ARPP search displays **Search ARPP records**.
2. Administration shows only **New ARPP / Addendum** as the page-level action.
3. Workspace Issues is compact and save state reads only `No unsaved changes`, `Unsaved changes` or `Saving…`.
4. The Rows control collapses and restores the row navigator.
5. Duplicate/Delete actions remain visible while the grid is horizontally scrolled.
6. Print preview shows a horizontal category strip, no blank column and a combined project-reference column.
7. A4 landscape print repeats the table heading across multiple pages.

Recommended viewports: 1366×768 at 125% scaling, 1536×864, 1920×1080, 1024×768 and 768×1024.
