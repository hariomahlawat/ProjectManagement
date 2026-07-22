# PRISM — Project Briefing Deck Builder

## Purpose

This package adds **Briefing decks** to the Comdt/HoD Command Workspace and provides a reusable, professional PowerPoint-deck workflow for selected projects.

Route:

```text
/Workspace/BriefingDecks
```

Access is restricted by the new policy:

```text
ProjectBriefingDecks.Manage
```

Allowed roles in this phase:

```text
Comdt
HoD
```

## Implemented functionality

### Saved decks

- Create, rename, duplicate and delete multiple personal saved decks.
- Save project membership, briefing order, deck format, cost mode, summary-slide options and handling/classification marking.
- Refresh stage, costs, latest external status, project description and cover photograph from live PRISM data whenever PowerPoint is generated.
- Optimistic concurrency protection for settings and inline edits.
- Audit logging for collection changes and PowerPoint generation.

### Project selection

Projects can be added through:

- All ongoing projects.
- Recently completed projects using a completion-year range.
- One or more project categories, including child categories.
- One or more technical categories.
- All projects explicitly marked available for proliferation.
- Searchable individual selection by project name, case-file number, Project Officer, project category or technical category.

Duplicates are ignored. The final collection can be reordered by drag-and-drop or keyboard arrow keys.

### Authoritative presentation rules

```text
STATUS
Latest non-deleted General External remark only.
No internal, conference, Project Officer, MCO or other remark is used.

COST (R&D)
Latest valid L1 value; otherwise latest valid AoN value; otherwise latest valid IPA value.

PROLIFERATION COST
ProjectProductionCostFact.ApproxProductionCost, stored in lakh and presented as currency.
```

The user selects one deck-wide cost mode:

- Cost (R&D) only.
- Proliferation cost only.
- Both costs, shown separately.
- Do not include cost.

### PowerPoint formats

- **Executive table deck** — cover, portfolio summary, selected summary charts and native editable project tables.
- **Detailed project deck** — cover, portfolio summary, selected summary charts and one slide per project.
- **Combined deck** — executive table slides followed by one slide per project.

Detailed project slides include:

- Project name.
- Lifecycle and category context.
- Present stage.
- Selected cost information.
- Latest external status.
- Cover photograph or deliberate professional placeholder.
- Brief capability overview, with an optional deck-specific override.

### Professional presentation standard

- 16:9 widescreen.
- Native editable PowerPoint text, tables and chart shapes.
- Institutional navy/white visual system with restrained accents.
- Native editable horizontal summary charts rather than screenshots.
- SDD/PRISM footer, slide number and optional handling/classification marking.
- High-resolution centre-cropped project photographs.
- No browser screenshots and no AI-generated rewriting of external remarks.

## Database migration

This implementation adds:

```text
20261202090000_AddProjectBriefingDecks
```

Tables:

```text
ProjectBriefingDecks
ProjectBriefingDeckItems
```

Back up the production database before deployment. The application can apply the migration through the existing automatic-startup migration path. For a controlled local deployment, run `dotnet ef database update` after building.

## Replacement procedure

1. Stop IIS Express/IIS or the running PRISM process.
2. Back up the application directory and PostgreSQL database.
3. Copy the contents of this folder into the project root, preserving paths, and replace matching files.
4. Do not omit the PowerPoint template:

```text
Resources/ProjectBriefing/ProjectBriefingTemplate.pptx
```

5. Clean and validate:

```powershell
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
npm ci
npm test
dotnet restore .\ProjectManagement.csproj
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
dotnet ef database update
```

6. Start PRISM and force-refresh the browser with `Ctrl+F5`.

## Program.cs note

The supplied `Program.cs` is based on the latest corrected Program file available for this implementation and retains the previously required:

- Proliferation analysis/data-quality registrations.
- FFC Word/Excel export registrations.
- Simulators Compendium registrations.

A source patch is also supplied for review. Avoid replacing `Program.cs` with an older copy.

## Release verification

Verify the following after deployment:

1. **Briefing decks** appears in the Comdt/HoD Command Workspace rail.
2. A Comdt/HoD can create at least two named saved decks.
3. Every bulk-selection method adds the expected projects without duplicates.
4. Individual search finds projects by name, case file and Project Officer.
5. Drag-and-drop and keyboard reordering persist after reload.
6. The preview uses only the latest external remark for Status.
7. Cost (R&D) follows L1 → AoN → IPA and shows its basis.
8. Proliferation cost remains separate and is not added to Cost (R&D).
9. Each of the four cost modes changes the generated deck correctly.
10. Executive, detailed and combined `.pptx` files open in PowerPoint and remain editable.
11. Project photographs are cropped without distortion; missing images use the intentional placeholder.
12. Handling/classification marking appears consistently when entered.

## Validation completed in the preparation environment

- JavaScript syntax checks passed.
- Full JavaScript suite passed: **226/226 tests**.
- Modified/new C# files passed syntax parsing.
- CSS parsed without errors.
- Project files parsed as valid XML.
- PowerPoint template ZIP/XML integrity passed.
- Git whitespace validation passed.

The preparation environment did not contain the .NET SDK, so the final C# build and .NET test execution must be completed locally before deployment.
