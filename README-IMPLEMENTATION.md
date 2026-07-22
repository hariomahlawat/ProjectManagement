# Project Briefing Deck Builder — Phase 3

## Scope

This package refines two areas of the existing Briefing Deck Builder:

1. **Adaptive detailed-project PowerPoint slides**
2. **Faster, searchable deck-membership management**

It is cumulative over the previously supplied **Phase 2** and **Audience Refinement** packages. Apply those earlier packages first if they are not already present.

## Implemented PowerPoint refinements

- Renames **PROJECT POSITION** to **PRESENT STATUS**.
- Dynamically allocates left-column height according to the latest external-status length.
- Shrinks the photograph region when more status space is required.
- Anchors cost cards to the bottom of the left column.
- Clips and auto-fits text inside its own shape so that it cannot overlap adjacent cards.
- Uses word-boundary truncation for exceptionally long status text.
- Uses a compact no-photo placeholder.
- Uses neutral styling for an unavailable proliferation cost; green is retained only for a recorded value.
- Normalises common private-use bullet characters before writing capability text to PowerPoint.
- Uses the quieter phrase **Capability overview not recorded.** for empty descriptions.

## Implemented builder UX

- Project removal is asynchronous and no longer reloads or jumps the page to the top.
- Adds **Search within this deck** with stage and readiness filters.
- Adds selected-row management:
  - Select visible
  - Move selected to top
  - Move selected to bottom
  - Remove selected
- Disables drag-and-drop while the selected-project table is filtered to protect global slide order.
- Renames **Select individually** to **Manage individually**.
- Individual search now shows all matching projects, including projects already in the deck.
- Search results show **IN DECK** / **NOT IN DECK** and are initially checked according to actual membership.
- Users can add and remove projects in one batch through **Apply changes**.
- Updates deck counts, readiness, estimated slide count and selected rows without a full-page reload.
- Preserves active selection tab, selected-project filters and scroll position in the browser session.
- Retains row-version concurrency checks for shared Comdt/HoD decks.

## Files to replace

```text
Pages/Workspace/BriefingDecks/Index.cshtml
Pages/Workspace/BriefingDecks/Index.cshtml.cs
Services/ProjectBriefings/ProjectBriefingContracts.cs
Services/ProjectBriefings/ProjectBriefingDeckService.cs
Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.cs
wwwroot/css/pages/project-briefing-decks.css
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

Stop the running application, copy the replacement files into the project root, then run:

```powershell
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

Restart PRISM and hard-refresh the browser (`Ctrl+F5`) so the updated JavaScript and CSS are loaded.

## Verification

### Builder

1. Open a shared deck with several projects.
2. Remove one project near the bottom of the page.
3. Confirm that the page does not jump to the top.
4. Search within selected projects and apply stage/readiness filters.
5. Open **Manage individually**, search for a project already in the deck, and confirm it appears checked with an **IN DECK** badge.
6. Uncheck one project, check another, and select **Apply changes**.
7. Confirm counts and rows update without a full-page refresh.

### PowerPoint

Generate a detailed or combined deck containing:

- a project with a photograph and a long external status;
- a project without a photograph;
- a project with and without proliferation cost;
- a project whose capability description contains bullets.

Confirm that:

- the card is titled **PRESENT STATUS**;
- status text remains inside its card;
- cost cards do not overlap the status;
- the photograph becomes slightly shorter when status text is longer;
- missing proliferation cost uses neutral styling;
- bullets render normally.

## Preparation validation

- JavaScript syntax check passed with Node.js.
- C# lexical/bracket structure checks passed for all supplied C# files.
- CSS parsing completed without syntax errors.
- The .NET SDK is not installed in the preparation environment; the final `dotnet build` and .NET tests must therefore be run locally.
