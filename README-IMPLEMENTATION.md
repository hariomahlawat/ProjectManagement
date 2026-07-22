# Project Briefing Deck Builder — Audience-Ready Presentation Refinement

This is a focused ready-to-replace update for the existing Phase 2 Project Briefing Deck Builder.

It removes developer-facing explanations from generated PowerPoint decks and improves table density, typography and pagination. The shared-deck model, project selection, photographs, cost rules and external-status rules are unchanged.

## Implemented

### Audience-facing slide cleanup

Removed from generated slides:

- explanatory subtitles such as reverse-workflow descriptions;
- implementation notes about editable shapes;
- repeated explanations of the external-status and Cost (R&D) resolution rules;
- the default centre-footer text `STATUS: LATEST EXTERNAL REMARK ONLY`.

Non-cover footers now contain only:

- `SIMULATOR DEVELOPMENT DIVISION · PRISM ERP`;
- optional handling/classification marking when configured;
- slide number.

The project-detail metadata line remains because lifecycle, project category and technical category are substantive briefing information.

### Summary slides

- Stage chart title: **Stage-wise summary**.
- Stage table title: **Stage-wise project distribution**.
- Project-category and technical-category charts no longer contain redundant explanatory notes.
- Chart labels, project counts and percentages use larger typography and better vertical centring.
- Stage-table text and row heights are increased while retaining the complete stage distribution on one slide.

### Project status tables

- Uses intelligent pagination with a preferred maximum of seven project rows per slide.
- Row height is estimated from wrapped project name, stage, status and cost-basis content.
- A small final page is rebalanced with the preceding page where space permits.
- The builder slide estimate uses the same pagination engine as PowerPoint generation, so the displayed estimate matches the generated deck.
- For the reviewed 49-project Cost (R&D)-only deck, the project-status section becomes seven balanced slides instead of nine slides with a one-row final page.
- Body text is increased to approximately 10–11 pt.
- Status remains the widest column.
- Project names receive more width and a higher truncation threshold.
- Short rows expand to use the available slide height rather than leaving excessive unused space.

### Cost presentation

- The Cost (R&D) basis remains visible when a cost is recorded.
- Missing Cost (R&D) no longer displays the internal fallback sequence `L1 → AoN → IPA` on the slide.
- Portfolio cost cards show recorded-project coverage without explaining internal resolution logic.

## Authoritative rules retained in code

```text
Status = latest non-deleted General External remark only
Cost (R&D) = L1 → AoN → IPA
Proliferation cost = recorded indicative proliferation cost
```

These rules continue to govern the data, but they are no longer repeated throughout the audience deck.

## Files

Add:

```text
Services/ProjectBriefings/ProjectBriefingTablePagination.cs
```

Replace:

```text
Services/ProjectBriefings/ProjectBriefingDataService.cs
Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.cs
ProjectManagement.Tests/ProjectBriefings/ProjectBriefingContractTests.cs
ProjectManagement.Tests/ProjectBriefings/ProjectBriefingSlideComposerTests.cs
```

## Deployment

1. Stop PRISM.
2. Extract the ZIP into the `ProjectManagement` project root and replace matching files.
3. Run:

```powershell
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

No database migration, package update, configuration change or PowerPoint-template replacement is required.

## Verification

Generate the same 49-project combined deck and confirm:

1. The estimated deck size reduces from 64 to 62 slides.
2. Stage and category slides contain no explanatory subtitle or implementation note.
3. The centre footer is blank unless a handling marking is configured.
4. The project-status section contains seven slides labelled `(1/7)` through `(7/7)`.
5. Each project-status slide normally contains seven rows.
6. Table text is larger and the table uses the available vertical space.
7. No slide displays the internal Cost (R&D) fallback sequence for an unrecorded cost.

## Validation performed

- The new pagination rules were simulated against the reviewed 49-row deck and produced seven balanced pages of seven projects each.
- Modified C# files passed delimiter and structural checks.
- Production source was checked to confirm removal of the identified audience-inappropriate phrases.

The preparation environment does not contain the .NET SDK. Run the local build and test commands above before publishing.
