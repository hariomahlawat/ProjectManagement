# PRISM Project Briefing Deck Builder
## Workflow and Responsive Stabilisation

This package is ready to paste over the current project root. It is based on the latest Project Briefing Deck Builder source supplied for review.

## Apply

1. Back up the six files listed in `REPLACEMENT-MANIFEST.txt`.
2. Copy the folders in this ZIP into the solution root and allow the six files to be replaced.
3. Clear the browser cache or force-refresh the Briefing Deck Builder page.
4. Build the solution and run the test project locally.

## Implemented

- Removes page-level horizontal overflow pressure through corrected flex/grid minimum sizing and responsive rail behaviour.
- Moves **Projects in this deck** ahead of Deck preflight.
- Replaces the multi-screen inline settings accordion with a focused right-side settings drawer.
- Adds canonical settings dirty-state detection, disabled-clean Save, discard confirmation, navigation protection and generation protection.
- Preserves template-specific settings when switching presentation templates.
- Keeps a compact live settings summary in the main workspace.
- Replaces the summed metadata-gap headline with the number of affected projects and field-specific counts.
- Makes preflight metrics actionable: selecting a metric filters and reveals matching projects.
- Collapses the shared-decks rail at office-laptop widths and preserves the selected deck context.
- Updates Project Update Sheet wording to name the actual slide content.
- Adds JavaScript and C# UI-contract coverage.

## Scope

No database migration, entity change, service contract change, project-ordering change, selection-rule change or PowerPoint-generation/composition change is included.

## Local verification

Run:

```powershell
dotnet build -c Release
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release
node --test .\wwwroot\js\projects\project-briefing-decks.test.js
```

The package was statically validated in the preparation environment. The .NET SDK was not available there, so the .NET build and xUnit suite must be run locally.
