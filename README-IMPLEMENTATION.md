# Project Briefing Deck Builder — Phase 4

## Scope

This package implements the approved **Audience Portfolio and Full Capability Content** refinement over the previously deployed Phase 3 Briefing Deck Builder.

### PowerPoint changes

- Simplifies **Portfolio at a glance** to audience-facing portfolio facts:
  - selected projects;
  - ongoing projects;
  - completed projects;
  - Cost (R&D);
  - proliferation cost.
- Keeps preparation/readiness indicators on the web builder only; they are not repeated in the generated presentation.
- Preserves the complete capability source text. The presentation pipeline no longer limits the project description to 1,200 characters before export.
- Parses recognisable structure into presentation blocks:
  - section headings;
  - paragraphs;
  - bullets;
  - numbered items;
  - lettered items.
- Adds professional **Capability overview — continued** slides whenever the complete content cannot fit at a readable size on the main project slide.
- Uses the same paginator for deck-size estimation and actual PowerPoint generation.
- Removes the redundant `Proliferation` basis line from executive-table proliferation-cost cells.
- Shows missing executive-table status as muted `Not recorded`; detailed project slides retain `No external status recorded`.

### Deliberately unchanged

- Photograph selection and integrity logic.
- Inclusion of low-information detailed project slides.
- Source-text editorial rewriting.
- Cost and external-status data rules.
- Existing shared-deck and membership-management behaviour.

## Files

Add:

```text
Services/ProjectBriefings/Presentation/ProjectBriefingCapabilityContracts.cs
Services/ProjectBriefings/Presentation/ProjectBriefingCapabilityPaginator.cs
Services/ProjectBriefings/Presentation/ProjectBriefingRichTextParser.cs
ProjectManagement.Tests/ProjectBriefings/ProjectBriefingCapabilityPaginationTests.cs
```

Replace:

```text
Pages/Workspace/BriefingDecks/Index.cshtml
Services/ProjectBriefings/ProjectBriefingContracts.cs
Services/ProjectBriefings/ProjectBriefingDataService.cs
Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.cs
wwwroot/js/pages/project-briefing-decks.js
ProjectManagement.Tests/ProjectBriefings/ProjectBriefingContractTests.cs
ProjectManagement.Tests/ProjectBriefings/ProjectBriefingSlideComposerTests.cs
```

## Database and configuration

- No database migration is required.
- No `Program.cs` change is required.
- No NuGet or npm package change is required.
- No PowerPoint template replacement is required.

## Deployment

Stop PRISM, extract this ZIP into the project root and replace matching files.

```powershell
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

Restart PRISM and hard-refresh the browser with `Ctrl+F5`.

## Verification

Generate a combined deck containing a project with a long, structured capability description and verify:

1. The portfolio slide contains no status/photo/data-readiness indicators.
2. The complete capability text appears across the main and continuation slides.
3. Headings and list structures remain visually differentiated.
4. No capability block ends with an artificial truncation ellipsis.
5. The builder's estimated slide count matches the generated slide count.
6. Executive proliferation-cost cells contain the amount only.
7. Missing executive status displays as `Not recorded`.

## Validation performed while preparing this package

- JavaScript syntax check passed.
- Changed C# files passed lexical delimiter validation.
- Required Phase 4 source-contract checks passed.
- Patch dry-run passed against the Phase 3 baseline.
- ZIP integrity and SHA-256 verification passed.

The preparation runtime does not contain the .NET SDK. Run the final .NET build and tests locally before deployment.
