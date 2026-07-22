# Project Briefing Deck Builder — Phase 2

## Shared Command Decks and Professional Presentation Hardening

This package is a ready-to-replace update for the existing **Project Briefing Deck Builder** in PRISM ERP.

It assumes the Phase 1 / v2 briefing-deck implementation is already present, including migration:

```text
20261202090000_AddProjectBriefingDecks
```

Extract the ZIP into the `ProjectManagement` project root and replace the matching files.

## Implemented

### Shared command decks

- Saved decks are now visible to every user authorised for the Comdt/HoD workspace.
- `OwnerUserId` is retained as the creator/audit record and is no longer used as a visibility filter.
- The latest modifying user is stored and shown in the saved-deck rail and deck header.
- Shared deck names are unique across the command workspace.
- Create, settings changes, project additions/removals, slide order, description overrides, duplication, generation and deletion continue to be audited.
- Optimistic concurrency is enforced across settings, additions, removals, reordering, description changes and deletion.

### Builder UX

- New-deck placeholder changed to **Project Update Review**.
- Saved-deck area is labelled **Shared decks**.
- Added estimated slide count with cover/summary/table/project-slide breakdown.
- Added pre-generation content-readiness warnings.
- Photo readiness now means an image can actually be decoded for PowerPoint, not merely that a photo database record exists.
- Readiness icons include explanatory tooltips and capability-overview status.

### Stage-wise summary

- All stage bars are rendered on one slide.
- Stages are ordered by reverse workflow sequence, with Completed first and IPA last.
- Share percentages are shown beside project counts.
- A separate native editable PowerPoint table follows the chart.
- The table contains Present stage, Projects and Share, plus a total row.

### Executive table deck

- Slide title changed to **Project status summary**.
- Native editable PowerPoint tables are retained.
- Status receives the largest column.
- Internal borders are lighter, alternate row shading is used, and missing values are visually muted.
- Cost basis remains a smaller second line.

### Detailed project slide

- The slide is rebalanced to prioritise the project explanation.
- Left column: photograph, combined **Project position** card, then cost card(s).
- Project position contains both Present stage and latest External status.
- Right column: enlarged **Capability overview** occupying nearly the full content height.
- Capability overview supports substantially more text and deterministic readable font sizing.
- The no-photo state uses a compact deliberate placeholder and releases space to project facts.
- One selected cost expands to full width; two costs remain separate.

### Photograph pipeline

- Added `ProjectBriefingPhotoLoader`.
- Checks configured derivatives from larger to smaller sizes and then preserved master files.
- Supports JPEG, PNG and WebP source files.
- Verifies actual decodability for builder readiness.
- Auto-orients and crops to a presentation-ready 16:9 JPEG using ImageSharp.
- A missing or corrupt project photograph does not fail the complete deck generation.

## Authoritative briefing rules retained

```text
Status = latest non-deleted General External remark only
Cost (R&D) = L1 → AoN → IPA
Proliferation cost = recorded indicative proliferation cost
```

Cost (R&D) and proliferation cost remain separate and are never added into one combined value.

## Database migration

This phase adds:

```text
20261203090000_ShareProjectBriefingDecksAndHardenPresentation
```

The migration:

- adds `LastModifiedByUserId`;
- backfills it from the deck creator;
- converts deck-name uniqueness from per-user to command-workspace-wide;
- safely renames duplicate pre-existing personal deck names;
- adds shared-deck indexes and creator/modifier foreign keys.

Back up the PostgreSQL database before applying the migration.

## Deployment

1. Stop the running PRISM application.
2. Back up the database and current source tree.
3. Extract this ZIP into the project root and replace matching files.
4. Run:

```powershell
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
dotnet ef database update
```

The existing production startup migration process may also apply the migration automatically, but a controlled `dotnet ef database update` is recommended before deployment.

No package, appsettings or PowerPoint-template change is required.

## Verification checklist

After deployment, verify:

1. A deck created by one Comdt/HoD user is visible to another.
2. Another authorised user can edit and generate the shared deck.
3. The stage chart is one slide and begins with Completed.
4. A native stage table immediately follows the chart.
5. Detailed slides use left facts and right Capability overview.
6. Present stage and latest external status appear in one Project position card.
7. Projects with usable cover photographs display them in PowerPoint.
8. No-photo slides use the compact layout.
9. The builder slide estimate matches the generated deck count.
10. Cost and external-status policies remain unchanged.

## Validation performed in the preparation environment

- Briefing-deck JavaScript tests: **8/8 passed**.
- JavaScript syntax check passed.
- Changed C# files passed lexical delimiter/static structural checks.
- CSS parsed without errors.
- Project and test `.csproj` files passed XML validation.

The preparation environment does not contain the .NET SDK, so the final .NET build, EF migration validation and .NET test suite must be run locally before publishing.
