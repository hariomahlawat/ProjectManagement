# PRISM Standard Briefing — Project Brief Layouts

## Scope

This package adds professional layout control to **Standard PRISM Briefing** detailed slides while retaining the existing cost behaviour.

### Project Brief layout

- **Automatic — recommended:** selects Photo Emphasis for a suitable photograph with concise/medium narrative content; otherwise uses Standard.
- **Standard:** retains the compact photograph/context rail and wide narrative panel.
- **Photo Emphasis:** allocates approximately 45% of the usable slide width to a substantially larger photograph and places the brief, selected context and cost information in the remaining column.

### Project context

Users can independently select:

- Show present stage
- Show present status

These choices apply to Capability Overview and Project Brief slides. When either field is not selected, its space is released automatically. Preflight requires external status only when a generated section actually uses status; executive tables continue to require it.

### Cost information

The established deck-level Cost Mode remains authoritative and unchanged:

- R&D cost only
- Proliferation cost only
- Both costs
- No cost

Both Standard and Photo Emphasis layouts use the same Cost Mode. No duplicate or competing cost control has been introduced.

## Persistence and compatibility

- New options are stored in the existing deck configuration JSON field under `standardBriefing`.
- Existing saved decks open with `Automatic`, Present Stage enabled and Present Status enabled.
- Existing project selection provenance and Project Update Sheet configuration are preserved.
- No database migration is required.
- This package is based on the uploaded source with the previously supplied **Adaptive Project Update Sheet Layouts** package applied.

## Application

Copy the folders in this package into the `ProjectManagement` project root and replace the matching files. Then:

1. Clean the solution.
2. Rebuild in Visual Studio.
3. Run the `ProjectBriefings` test group.
4. Open a Standard PRISM Briefing deck and save the desired Project Brief layout/context settings.
5. Generate a test PowerPoint with R&D only, proliferation only, both and no-cost modes.

## Validation completed here

- JavaScript syntax check passed.
- Targeted briefing-deck JavaScript tests: **25 passed**.
- Unified patch dry-run and application verification passed.
- Changed-file integrity and SHA-256 checks completed.

The .NET SDK is not installed in this execution environment, so C# compilation and xUnit execution must be completed in Visual Studio before deployment.
