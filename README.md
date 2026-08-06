# PRISM Executive Summary Harmonisation

This package is built on the current Stage-wise Summary implementation, including the responsive layouts and Stage Icon and Badge Precision pass.

## Replace/add these production files together

1. `Services/ProjectBriefings/ProjectBriefingSummaryPlanning.cs` — **new**
2. `Services/ProjectBriefings/ProjectBriefingDataService.cs` — replace
3. `Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.cs` — replace
4. `Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.ExecutiveSummary.cs` — **new**
5. `Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.StageSummary.cs` — replace
6. `Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.StageIcons.cs` — replace
7. `Pages/Workspace/BriefingDecks/Index.cshtml` — replace

The project uses SDK-style wildcard compilation, so the two new `.cs` files are included automatically. No `.csproj` change is required.

## Implemented

### Portfolio at a glance

- Harmonised KPI cards for Selected, Completed, Ongoing and optional Cancelled projects.
- Common semantic colours:
  - institutional burgundy for the selected total;
  - green for completed;
  - blue for ongoing;
  - red only when cancelled projects exist.
- Completed/Ongoing cards include portfolio share.
- Cost cards now use accurate labels:
  - `RECORDED R&D COST`
  - `RECORDED PROLIFERATION COST`
  - `RECORDED IPA COST` for update-sheet decks.
- Each cost card shows source coverage and a proportional coverage bar.
- Light and Graphite Dark themes use the same information hierarchy.

### Project-category and technical-category summaries

- Single-category summaries are automatically suppressed because they add no analytical value.
- Two to five categories use a ranked full-width horizontal distribution.
- Six to ten categories use a compact two-column ranked layout.
- Eleven or twelve categories use the full-width compact ranked layout.
- More than twelve categories generate balanced continuation pages, with a maximum of twelve categories per slide.
- Continuation page counts are shared between the workspace estimate and the composer.
- Counts and portfolio shares are shown consistently.
- Project categories use the secondary teal accent; technical categories use the institutional blue accent.
- Green is reserved for completion semantics and is no longer used for technical-category charts.

### Stage icon normalisation

- Development icon enlarged approximately 8–10%.
- Technical Evaluation icon reduced slightly.
- Bidding/Tendering icon moved upward optically.
- Scope of Work Vetting icon shifted left.
- Icon strokes are slightly darker in Editorial Light and slightly lighter in Graphite Dark.
- Existing slide geometry and responsive rules remain unchanged.

### Workspace behaviour

- Project-category helper text now states that the slide is automatically omitted when only one category is represented.
- Slide estimates and generated slide counts use the same category-pagination rules.

## Apply

1. Stop the running application/IIS Express.
2. Copy the contents of `ReadyToPaste` into the project root, preserving folders.
3. Clean the solution.
4. Delete `bin` and `obj` only if Visual Studio retains stale compilation output.
5. Rebuild and generate both Editorial Light and Graphite Dark decks.

## Verification

Optional focused regression tests are provided under `VerificationTests`.

Reference geometry previews are provided under `ReferencePreviews`; they are not application assets and do not need to be copied into the project.

No database migration, NuGet package, dependency-injection registration, JavaScript or CSS change is required.

A complete .NET build could not be executed in the packaging environment because the .NET SDK is unavailable. The files were checked for balanced source structure, shared pagination consistency, patch applicability and archive integrity.
