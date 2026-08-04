# PRISM Standard Briefing — Consolidated Status Strip

## Scope

This update refines the **Project Brief** slides in both Standard and Photo-emphasis layouts.

### Implemented behaviour

- Replaces the separate Present Stage, Present Status and Status treatments with one heading: **PRESENT STATUS**.
- Displays only the source values selected in deck settings:
  - stage only;
  - external status only;
  - stage and external status together;
  - neither, in which case the status cell is omitted.
- Does not derive, infer or append any additional status, qualifier or remark.
- Silently suppresses the existing `No external status recorded` placeholder in generated slides.
- Moves Present Status and the selected cost fields into one slim bottom information strip.
- Preserves the existing cost choices: R&D only, proliferation only, both or none.
- Automatically redistributes the bottom strip:
  - status plus selected cost cells spans the usable width;
  - cost-only configurations remain compact and right-aligned;
  - when neither status nor cost is selected, the strip is omitted and the photograph/brief panels use the released space.
- Applies the same single-heading status treatment to Capability Overview context cards for visual consistency.

## Files to replace

1. `Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.cs`
2. `ProjectManagement.Tests/ProjectBriefings/ProjectBriefingSlideComposerTests.cs`
3. `ProjectManagement.Tests/ProjectBriefings/ProjectBriefingContractTests.cs`

The two test files are recommended for source-controlled deployments but are not required on a published production server.

## Prerequisite

Apply this package over the code containing the earlier **Standard Briefing Photo Layouts** implementation. It relies on the existing `ProjectBriefLayout`, `ShowPresentStage` and `ShowPresentStatus` configuration introduced there.

## Database

No database migration or data conversion is required.

## Verification

1. Clean and rebuild the solution.
2. Run the `ProjectBriefings` test suite.
3. Generate one Standard and one Photo-emphasis Project Brief deck with:
   - both stage and status enabled;
   - stage enabled but no external status present;
   - both costs selected;
   - no status and no costs selected.
4. Confirm the output contains no generated status wording and no `No external status recorded` text.
