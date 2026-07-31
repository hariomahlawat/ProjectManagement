# PRISM Project Briefing Deck Builder — Responsive Preflight Completion

This is a focused UI/UX completion package for the existing Project Briefing Deck Builder.
It preserves the current data model, service layer, ordering rules and PowerPoint generation logic.

## What this phase implements

- Prevents page-level horizontal overflow by correcting flex/grid minimum widths and containing wide tables inside their own scroller.
- Adds a responsive **Shared decks** control. The secondary deck rail remains visible on large monitors and collapses by default on office-laptop widths.
- Replaces the overloaded warning-chip area with a template-aware **Deck preflight**:
  - headline checks include only content actually used by the selected presentation configuration;
  - additional Project Update Sheet metadata is grouped separately;
  - detailed gaps remain collapsed until requested;
  - generation remains available and the placeholder policy is explicit.
- Removes the vague headline **Project facts** metric.
- Gives the selected-project collection a clear **Projects in this deck** work area with search, filtering, bulk management and a direct **Add projects** action.
- Converts project-addition methods into a collapsed workflow that opens on demand without losing the current deck.
- Removes the empty-description warning, shortens audit metadata and uses compact dates in the shared-deck list.
- Shows a precise slide composition such as Cover, Portfolio summary and Project sheets.
- Adds responsive layouts for laptop, tablet and narrow-screen use.
- Adds JavaScript and C# UI contract coverage for the new behaviour.

## Apply by replacing files

1. Back up the five files listed in `REPLACEMENT-MANIFEST.txt`.
2. Copy this package into the project root, preserving folders.
3. Allow the five listed files to overwrite the existing files.
4. Do not copy unrelated files from any older package.

## Apply as a patch

From the project root:

```powershell
patch -p1 < IMPLEMENTATION.patch
```

A Git-compatible review can also be performed before replacement:

```powershell
git apply --check IMPLEMENTATION.patch
git apply IMPLEMENTATION.patch
```

## Build and verification

```powershell
dotnet restore
dotnet build ProjectManagement.sln -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release --no-build
node --test wwwroot/js/projects/project-briefing-decks.test.js
```

## Browser acceptance checks

Validate at minimum:

- 1366 × 768 with Windows display scaling at 125%
- 1536 × 864
- 1920 × 1080
- 2560 × 1440
- tablet portrait and landscape

Confirm:

- no page-level horizontal scrollbar;
- Shared decks can be shown and hidden without changing the selected deck;
- Generate PowerPoint remains visible;
- deck preflight changes when template, narrative or cost settings change;
- additional metadata remains collapsed by default;
- selected-project search/filter/reorder/remove behaviour is unchanged;
- **Add projects** opens and focuses the existing selection workflow;
- project addition/removal preserves the current page position;
- wide project tables scroll only inside their table container.

## Data and deployment impact

- Database migration: **none**
- Entity/model change: **none**
- Service/business-rule change: **none**
- PowerPoint ordering or cost-resolution change: **none**
