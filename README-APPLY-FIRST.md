# PRISM Project Briefing Deck — Adaptive Project Update Sheets

This package is ready to paste over the latest PRISM source used for this implementation.

## Scope

The package adds a production-oriented Project Update Sheet configuration without changing the database schema:

- Project Update Sheets support **Editorial Light** and **Graphite Dark** themes.
- The facts table is user-configurable: rows can be selected, reordered, reset and optionally hidden when completely empty.
- Project name remains the slide title and is no longer duplicated in the facts table.
- Slide geometry adapts to the selected and visible facts rows.
- The PDC/completion row resolves contextually:
  - ongoing + Development: recorded PDC, otherwise a blank editable cell;
  - ongoing + another stage: blank editable PDC cell;
  - completed: `Completion Status` with `Project completed` and the recorded completion date/precision when available;
  - cancelled: `Project Status` with `Project cancelled`.
- Preflight follows the selected rows and the hide-empty policy.
- Existing legacy deck selection-rule JSON is read safely and upgraded only when deck settings are saved.

## Apply

1. Back up the solution.
2. Extract this ZIP into the solution root—the folder that contains `ProjectManagement.csproj`.
3. Allow the listed files to overwrite the existing files.
4. New files must also be copied:
   - `Services/ProjectBriefings/ProjectBriefingDeckConfigurationCodec.cs`
   - `ProjectManagement.Tests/ProjectBriefings/ProjectBriefingDeckConfigurationCodecTests.cs`
5. No EF migration or database update is required.
6. Build and test locally:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release --no-build
```

7. Restart the application and perform a hard browser refresh.

## Acceptance checks

- Open a Project Update Sheet deck and verify both theme choices.
- Select and reorder facts rows, save, reopen and confirm the order persists.
- Generate a completed project: the row must show `Completion Status` and `Project completed...`.
- Generate an ongoing Development project: the PDC row must show its recorded PDC or remain blank.
- Generate an ongoing non-Development project: the PDC row must remain present with a blank editable value.
- Enable **Hide fields with no recorded value**: fully empty optional rows disappear, partially completed rows remain reportable, and PDC/completion remains present.
- Confirm no document-level horizontal scrollbar appears in the builder/settings drawer.

## Alternate application method

`IMPLEMENTATION.patch` contains the same changes and can be applied from the solution root:

```bash
git apply --check IMPLEMENTATION.patch
git apply IMPLEMENTATION.patch
```
