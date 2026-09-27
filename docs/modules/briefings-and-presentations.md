# Project Briefing Decks and FFC Presentation

## Purpose

This module generates PowerPoint (`.pptx`) decks from live PRISM data using DocumentFormat.OpenXml 3.1.1. It covers two features:

- **Project Briefing Deck Builder**: saved, shared decks of selected projects in one of two layouts:
  - Standard briefing (executive table, detailed project slides, or combined)
  - Formal Project Update Sheets

  Optional additional slides can be added.
- **FFC Global Footprint PowerPoint**: a one-click portfolio deck exported from the FFC Footprint page.

## Routes and pages

| Route | Page model | Notes |
|---|---|---|
| `/Workspace/BriefingDecks/{deckId:long?}` | `Pages/Workspace/BriefingDecks/Index.cshtml.cs` | Deck CRUD, settings, project membership/order, additional slides, `SearchProjects`, and `Generate` (returns the `.pptx`). Editors: `_InstitutionalProfileEditor`, `_RoleCharterEditor`, `_FfcGlobalFootprintEditor` |
| `/ProjectOfficeReports/FFC/Footprint` `?handler=ExportPowerPoint` | `Areas/ProjectOfficeReports/Pages/FFC/Footprint.cshtml.cs` | FFC deck export |

## Authorization

- The Briefing Decks page has `[Authorize(Policy = Policies.ProjectBriefingDecks.Manage)]`, which means role **Comdt or HoD**.
- Decks are **shared**: every holder of the policy can see and edit every deck. `ProjectBriefingDeckService.ListAsync` and `GetEntityAsync` do not filter by `OwnerUserId`. `OwnerUserId` is recorded only for attribution.
- The FFC Footprint page (and its PowerPoint export) is `[Authorize]` only, so any authenticated user can use it.

## Main types (Briefing Decks)

| Concern | Type (file under `Services/ProjectBriefings/`) |
|---|---|
| Deck persistence, settings, membership, audit | `ProjectBriefingDeckService` |
| Project selection/search and rules | `ProjectBriefingSelectionService` |
| Presentation snapshot | `ProjectBriefingDataService.BuildPresentationDataAsync` |
| Config JSON codec | `ProjectBriefingDeckConfigurationCodec` (stored in `ProjectBriefingDeck.SelectionRulesJson`) |
| Cost / status / facts | `ProjectBriefingCostResolver`, `ProjectBriefingExternalStatusService`, `ProjectBriefingUpdateSheetFactsResolver`, `ProjectBriefingInstitutionalProfileService` |
| Photos | `ProjectBriefingPhotoLoader` |
| Ordering / pagination | `ProjectBriefingProjectOrdering`, `ProjectBriefingStageOrder`, `ProjectBriefingTablePagination`, `ProjectBriefingRoleCharterPaginator`, `ProjectBriefingSummaryPlanning` |
| Additional slides | `ProjectBriefingAdditionalSlideCatalog` |
| Export orchestration | `Presentation/ProjectBriefingPowerPointExportService` |
| Slide rendering | `Presentation/ProjectBriefingSlideComposer` (partial classes: `.ExecutiveSummary`, `.StageSummary`, `.UpdateSheet`, `.InstitutionalProfile`, `.RoleCharter`, `.FfcGlobalFootprint`, `.StageIcons`), `ProjectBriefingUpdateSheetPlanner`, `ProjectBriefingCapabilityPaginator`, `ProjectBriefingRichTextParser`, `ProjectBriefingThemeDefinition`, `ProjectBriefingNarrativeTypography` |
| Output validation | `Presentation/ProjectBriefingPresentationIntegrityValidator` (throws `ProjectBriefingPresentationIntegrityException`) |

DI registrations are in `Program.cs`. `IProjectBriefingSlideComposer` is a singleton. The rest are scoped.

## Data entities (`Models/ProjectBriefings/ProjectBriefingDeck.cs`)

- `ProjectBriefingDeck` fields:
  - Name/NormalizedName (unique)
  - `Layout` (`StandardBriefing` | `ProjectUpdateSheet`)
  - `PresentationMode` (`ExecutiveTable` | `DetailedProjects` | `Combined`)
  - `CostMode` (`CostRdOnly` | `ProliferationOnly` | `Both` | `None`)
  - `NarrativeMode`, `PresentationTheme` (`EditorialLight` | `GraphiteDark`), `BrandingScope`
  - include-slide flags, `HandlingMarking`, `SelectionRulesJson`, `LastGeneratedAtUtc`, `RowVersion`
- `ProjectBriefingDeckItem` fields: `ProjectId`, `SortOrder`, `BriefDescriptionOverride`.
- Additional slide types (`ProjectBriefingAdditionalSlideType`): `InstitutionalProfile`, `RoleAndCharter`, `FfcGlobalFootprint`. Their placement and order are stored in the deck configuration.
- Update-sheet rows (`ProjectBriefingUpdateSheetRow`): cost, ARPP PPP No., funding authority, AoN date, supply order, PDC/completion, present status, PO, line directorate.

## Generation pipeline

1. `ProjectBriefingDataService` loads a snapshot of the deck. A deck with no projects throws `InvalidOperationException("Add at least one project…")`.
2. For update sheets and detailed/combined modes, cover photos are attached one project at a time. A failed photo load is logged as a warning and skipped.
3. `ProjectBriefingSlideComposer.Compose` works in memory:
   - reads `Resources/ProjectBriefing/ProjectBriefingTemplate.pptx` from the content root (copied to output by the `.csproj`)
   - removes the template's own slides
   - renders one slide per plan onto the blank layout
   - adds logos from `wwwroot/img/logos/artrac.png` and `sdd.png` if they are present
4. The integrity validator reopens the package. Failures return HTTP 422 on AJAX calls, or a redirect with a message.
5. `MarkGeneratedAsync` stamps `LastGeneratedAtUtc`. The file is named `{DeckName}_{yyyyMMdd IST}.pptx`, and the `X-Project-Briefing-Slides` header carries the slide count.

No temporary files are used.

## Business rules and edge cases

- At most 120 projects per deck (`maximumProjectsPerDeck` in `ProjectBriefingDeckService`).
- Every mutating handler uses row-version concurrency. A `DbUpdateConcurrencyException` is shown to the user as a reload message.
- Unexpected generation errors are logged with the TraceId, and the user is shown that reference.

## FFC presentation

- Services: `Services/Ffc/Presentation/FfcPowerPointExportService`, `FfcPresentationDataService`, `FfcSlideComposer` (template `Resources/FfcPresentation/FfcPortfolioTemplate.pptx`), `FfcPresentationMapRenderer` (map image).
- The Briefing Deck's `FfcGlobalFootprint` additional slide reuses FFC presentation data.
- See `docs/ffc-module-architecture.md` for the FFC domain.

## Configuration

There are no dedicated configuration keys. The template paths are fixed relative to `ContentRootPath`.
