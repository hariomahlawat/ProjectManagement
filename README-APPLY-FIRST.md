# PRISM Project Briefing Deck Builder — Final Workflow Polish

This is a focused replacement package for the latest **Project Briefing Deck Workflow and Responsive Stabilisation** implementation.

## What this phase changes

- Keeps **Deck preflight above Projects in this deck**, as the compact decision checkpoint requested.
- Removes the empty **Content and layout** panel when **Project Update Sheets** is selected.
- Makes secondary settings sections collapsible and remembers their open/closed state separately for each presentation template and saved deck.
- Keeps the General section permanently visible; Standard Briefing opens Content and layout by default, while Project Update Sheets opens Appearance and Summary and handling by default.
- Adds clear hover/focus tooltips and screen-reader labels to every row-level readiness indicator.
- Adds an explanatory readiness legend in the table header.
- Keeps readiness indicator order consistent: photograph, external status, R&D cost, update-sheet facts where applicable, then selected project content.
- Strengthens Shared decks collapse context by retaining the active deck name in its tooltip and accessible label.
- Moves project-ordering guidance onto a dedicated second line at constrained desktop/laptop widths.
- Completes unsaved-settings protection for Close, Cancel, Escape, backdrop dismissal, explicit navigation, form navigation, PowerPoint generation and browser unload.

## Apply

1. Stop the application or use your normal controlled replacement procedure.
2. Back up the five files listed in `REPLACEMENT-MANIFEST.txt`.
3. Copy the folders from this package into the project root, preserving paths and replacing the existing files.
4. Clear browser cache or force-refresh the page so the updated CSS and JavaScript are loaded.
5. Build and test locally:

```powershell
dotnet build ProjectManagement.sln -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release --no-build
```

6. Verify the module at 1366×768 with Windows scaling at 125%, and at the normal command-workstation resolution.

## Operational checks

- Project Update Sheets must not show an empty Content and layout section.
- Deck preflight must appear above Projects in this deck.
- Preflight metrics must still filter and reveal matching projects.
- Readiness icons must show tooltips on mouse hover and keyboard focus.
- Switch between Standard PRISM Briefing and Project Update Sheets and confirm each template remembers its section expansion state and configuration choices.
- Change a setting and verify Unsaved settings appears and Save settings enables.
- Verify Close, Cancel, Escape and backdrop click request confirmation before discarding changes.
- Verify shared-deck switching/navigation and browser refresh are protected while settings are unsaved.
- Verify Generate PowerPoint directs the user to save or discard pending settings first.
- At laptop width, Shared decks should collapse without losing the active-deck context.
- At constrained widths, ordering guidance should move below the bulk-action controls rather than crowding them.

## Scope

There are no migrations, entity/model changes, service changes, project-selection changes, ordering-rule changes, or PowerPoint-generation changes.
