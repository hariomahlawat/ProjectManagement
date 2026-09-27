# Publications: Capability Brochure and Simulators Compendium

Code is the authority. The root-level `PRISM_Publications_Phase*_README.md`, `README-PHASE*.txt` and
`*compendium*.patch` files are historical delivery notes. They do not describe current behaviour.

## Purpose

This module builds PDF publications from live PRISM project records:

- **Capability Brochure**: a short, image-led capability publication. It has two profiles: print-compact and digital-comfortable.
- **Simulators Compendium**: a structured reference publication with authored sections, a cover design, per-project dossiers, editorial review and a governed final issue.

Both use QuestPDF (`QuestPDF` 2024.10.3) for layout and PdfPig for verification after composition. Images are rendered through SkiaSharp/ImageSharp in `BrochurePhotoService`.

## Routes and pages

| Route | Page model | Notes |
|---|---|---|
| `/Projects/Publications` | `Pages/Projects/Publications/Index.cshtml` (no model) | Landing page with a choice of publication type |
| `/Projects/Publications/Brochure` | `Pages/Projects/Publications/Brochure/Index.cshtml.cs` `IndexModel` | Handlers: `Photo`, `SavePreset`, `RenamePreset`, `DuplicatePreset`, `DeletePreset`, `ProjectState`, `Preflight`, `Preview`, `Generate` |
| `/Projects/Publications/Compendium` | `Pages/Projects/Publications/Compendium/Index.cshtml.cs` | Handlers: `Photo`, `Preflight`, `Review`, `Preview`, `Generate`, `SavePreset`, `RenamePreset`, `DuplicatePreset`, `DeletePreset` |
| `/Projects/Publications/Compendium/Cover` | `.../Compendium/Cover.cshtml.cs` | Cover authoring. Handlers: `Pattern`, `ProjectPhotos`, `AutomaticCandidates`, `Save` |
| `/Projects/Publications/Compendium/Structure` | `.../Compendium/Structure.cshtml.cs` | Section and ordering authoring (`Save`) |
| `/Projects/Compendium` | `Pages/Projects/Compendium/Index.cshtml.cs` | Legacy route. GET redirects to the canonical page. `POST ?handler=Generate` still produces the automatic proliferation compendium (see Review notes) |

## Authorization

- `Program.cs` protects the whole `/Projects/Publications` folder with `AuthorizeFolder`. Every page model also carries `[Authorize]`. Any authenticated user can open the workspace, preview or generate PDFs, and read project photos through the `Photo` handler.
- Shared presets (save, rename, duplicate, delete, and Compendium cover/structure authoring) require `Policies.Publications.CanManageSharedPublications`, which means role **Comdt, HoD or ITO** (`Configuration/Policies.cs`, `Policies.Publications.SharedPublicationManagerRoles`). The page models check this and return `Forbid()`, and the services enforce it again by throwing `UnauthorizedAccessException` (`BrochurePresetService`, `CompendiumPresetService`). Admin is not in this list.

## Main types

**Composition root:** `Services/Publications/PublicationServiceCollectionExtensions.AddProjectPublications()`. `Program.cs` also registers `ICompendiumReadService`, `ICompendiumExportService` and `ICompendiumPdfReportBuilder` directly.

| Concern | Type (file) |
|---|---|
| Brochure build/preflight | `BrochurePublicationService` (`Services/Publications/BrochurePublicationService.cs`) |
| Brochure page planning | `BrochureLayoutPlanner`, `BrochurePrintPagePlanner`, `BrochurePrintCompactPlanner`, `BrochurePrintMeasurementService`, `BrochurePrintLayoutMetrics` |
| Brochure policies | `BrochurePrintPublicationPolicy`, `BrochureDigitalPublicationPolicy`, `BrochureNarrativeTypographyPolicy`, `BrochurePhotoPrintQualityEvaluator`, `BrochureReviewFingerprint` |
| Brochure PDF | `Utilities/Reporting/BrochurePdfReportBuilder`, `BrochurePrintCompactComposer`, `BrochurePdfCompositionVerifier` |
| Photos | `BrochurePhotoService` (shared by Brochure and Compendium) |
| Presets | `BrochurePresetService`, `CompendiumPresetService` (+ `*PresetContracts.cs`) |
| Compendium data | `Services/Compendiums/CompendiumReadService` (`ICompendiumReadService`) |
| Compendium export orchestration | `Services/Compendiums/CompendiumExportService` (`ICompendiumExportService`) |
| Compendium readiness | `CompendiumReadinessPolicy` (blockers vs warnings), `CompendiumReviewFingerprint`, `CompendiumImageQualityPolicy` |
| Compendium dossier layout | `CompendiumDossierLayoutPlanner`, `CompendiumDossierPaginationPlanner`, `CompendiumDossierNarrativeFlowPlanner`, `CompendiumDossierTextMeasurementService`, `CompendiumDossierImageGeometryPolicy`, `CompendiumDossierEditorialPolicy`, `CompendiumDossierPresentationPolicy`, `CompendiumProjectParticularsLayoutPolicy` |
| Compendium cover | `CompendiumCoverTemplatePolicy`, `CompendiumCoverIdentityPolicy`, `CompendiumCoverSlotAssignmentPolicy`, `CompendiumCoverAutomaticImagePolicy`, `CompendiumCoverTypographyPolicy` |
| Compendium PDF | `Utilities/Reporting/CompendiumPagePlanner` (`ICompendiumPagePlanner`), `CompendiumPdfReportBuilder`, `CompendiumNarrativePdfRenderer`, `CompendiumPdfCompositionVerifier`, `CompendiumPublicationTextSanitizer`, `CompendiumGenerationDiagnostics`, `CompendiumPdfGenerationException` |
| Fonts | `Utilities/Reporting/PublicationFontRegistry.cs` (`PublicationFontService`), `PublicationFontContract` |
| Build identity | `Utilities/Reporting/CompendiumBuildIdentity` (phase, build stamp, `X-PRISM-Compendium-Build` header, PDF producer) |
| Startup checks | `PublicationRuntimeValidationHostedService` (resolves the whole service graph and registers fonts at startup), `PublicationFontWarmupHostedService` |
| Offline self-test | `Utilities/Reporting/CompendiumOfflineSelfTest`. Run with `--compendium-offline-self-test`. It runs before the host is built and exits with a status code |

## Data entities

Defined in `Models/Publications`, with DbSets in `Data/ApplicationDbContext`:

- `BrochurePreset` → `BrochurePresetProject`. Saved brochure configuration: cover text, profile, narrative source, hero photo and project order. It also has `RowVersion` and a soft-retire flag `IsActive`.
- `CompendiumPreset` → `CompendiumPresetSection`, `CompendiumPresetProject`, `CompendiumPresetCoverImage`, `CompendiumPresetPhotoPreference`.

Publications do not store generated PDFs. Output streams straight to the response.

## Pipelines

**Brochure** (`Brochure/Index.cshtml.cs` `GenerateInternalAsync`):

1. Validate the input. At most 100 projects (`MaximumSelectedProjects`, also enforced in `BrochurePublicationService`).
2. `BrochurePublicationService.BuildAsync` loads live project data, runs preflight and throws `BrochurePublicationValidationException` if blockers remain.
3. `BrochurePdfReportBuilder.Build` composes and verifies the PDF. A page-count mismatch between plan and output raises `BrochurePdfCompositionException`. The page returns 409 and no PDF is issued.
4. Response headers: `X-PRISM-Publication-FileName`, `-Composition-Verified`, `-Page-Count`. Preview is served inline. Final output is an attachment and sets `RequirePublicationReview: true`.

**Compendium** (`CompendiumExportService.GenerateAsync`):

- A process-wide `SemaphoreSlim` limits generation to one at a time (`CompendiumBuildIdentity.MaximumConcurrentGenerations = 1`).
- Stages: read snapshot → review gate (`RequireAllReviewed` is true for final issue, false for preview) → photo rendering → cover design resolution → `ICompendiumPagePlanner.Plan` → `CompendiumPdfReportBuilder.Build` → `CompendiumPdfCompositionVerifier.Verify`.
- Each failure is wrapped in `CompendiumPdfGenerationException` with a `CompendiumPdfGenerationStage`. If photo rendering fails, it is logged and the build continues with text-led layouts.
- File name: `{FileNamePrefix}_{yyyyMMdd IST}.pdf`.

## Business rules and edge cases

- Candidate projects (Brochure and authored Compendium) must be not deleted, not archived, and Active or Completed.
- The canonical Compendium page requires at least one selected project, and at most 500 (`MaximumSelectedProjects = 500`).
- The legacy automatic compendium (`CompendiumReadService.GetProliferationCompendiumAsync`) includes only projects that are Completed, not archived, not deleted, and have `ProjectTechStatus.AvailableForProliferation == true`.
- Review state is held as a fingerprint, so a change in content makes the review stale (`CompendiumReviewFingerprint`, `BrochureReviewFingerprint`, `CompendiumBuildIdentity.ReviewContract`).
- `CompendiumReadinessPolicy` separates blockers, which stop generation, from warnings and information. Ordinary gaps in project data do not block.
- Missing DM Sans fonts raise a `FontInitialization`-stage error that tells the operator to deploy the font package.

## Configuration

- `CompendiumPdf` section (`Configuration/CompendiumPdfOptions`): `Title`, `Subtitle`, `UnitDisplayName`, `IssuerDisplayName`, `FileNamePrefix`, `MiscCategoryNames`, `CoverPhotoDerivativeKey` (must exist under `ProjectPhotos:Derivatives`), `PreferredPhotoFormat`, `PreferWebp`, `ShowMissingPhotoPlaceholder`. `appsettings.CompendiumPdf.fragment.json` is a sample fragment.
- Font search roots, in order (`PublicationFontContract.CandidatePublicationRoots`):
  - env `PRISM_PUBLICATION_FONTS_DIR`
  - `<ContentRoot>/Resources/Publications/Fonts`
  - `wwwroot/fonts/publications`
  - `<AppBase>/fonts/publications`

  Families: "PRISM DM Sans" (primary), "PRISM Alatsi" (display), fallback Lato.

## Exports

Two PDF outputs: the Capability Brochure and the Simulators Compendium. There are no Excel or Word exports.
