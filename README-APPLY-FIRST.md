# PRISM Project Briefing — Presentation Visual-System Completion

This package is based on the latest **Adaptive Project Update Sheets** implementation.

## Apply

1. Back up the solution.
2. Copy the supplied folders into the `ProjectManagement-master` solution root.
3. Allow the listed files to replace the existing files.
4. Add the new file:
   `Services/ProjectBriefings/Presentation/ProjectBriefingNarrativeTypography.cs`
5. Clean and rebuild the solution.
6. Run the Project Briefing test suite and generate representative light/dark decks.

The included `IMPLEMENTATION.patch` is an alternative to file replacement and applies against the latest Adaptive Update Sheets baseline.

## Database

No migration or database update is required.

## Scope

- Shared semantic project-slide header renderer.
- Graphite Dark project titles use primary white text; the operational top rule remains blue.
- Editorial Light Project Update Sheets retain a centrally defined formal maroon rule.
- Semantic operational, narrative and update-sheet colour roles.
- Template-aware theme previews in Deck settings.
- Sparse, standard and dense narrative typography profiles.
- Improved photograph/status/cost vertical balance.
- Exact colour and shape-level presentation tests.

No project selection, ordering, cost resolution, PDC/completion resolution, update-sheet row selection, or database behaviour is changed.
