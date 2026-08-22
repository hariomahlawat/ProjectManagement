# File manifest

All paths are relative to the project root.

## Phase 42 cover-slot implementation

- `Pages/Projects/Publications/Compendium/Cover.cshtml`
- `Pages/Projects/Publications/Compendium/Cover.cshtml.cs`
- `Pages/Projects/Publications/Compendium/Index.cshtml.cs`
- `Services/Compendiums/CompendiumCoverSlotAssignmentPolicy.cs` (new)
- `Services/Compendiums/CompendiumExportService.cs`
- `Services/Publications/CompendiumPresetService.cs`
- `Utilities/Reporting/CompendiumBuildIdentity.cs`
- `wwwroot/css/pages/projects-publications.css`
- `wwwroot/js/pages/projects-compendium-cover-editor.js`
- `wwwroot/js/projects/compendium-cover-editor-state.js`

## Validation files

- `ProjectManagement.Tests/Publications/CompendiumPhase41ProductionConvergenceTests.cs`
- `ProjectManagement.Tests/Publications/CompendiumPhase42SlotStabilityTests.cs` (new)
- `tools/Test-PrismPublicationsPhase30_1.ps1`
- `tools/Test-PrismPublicationsPhase42.ps1` (new)
- `wwwroot/js/projects/publications-compendium-phase30-1-contract.test.js`
- `wwwroot/js/projects/publications-compendium-phase41-cover-change.test.js`
- `wwwroot/js/projects/publications-compendium-phase41-offline-runtime.test.js`
- `wwwroot/js/projects/publications-compendium-phase41-production-convergence.test.js`
- `wwwroot/js/projects/publications-compendium-phase42-slot-stability.test.js` (new)

## Cumulative Phase 41 air-gapped PDF production files

- `Program.cs`
- `Services/Compendiums/CompendiumDossierTextMeasurementService.cs`
- `Utilities/Reporting/CompendiumGenerationDiagnostics.cs`
- `Utilities/Reporting/CompendiumLayoutMetrics.cs`
- `Utilities/Reporting/CompendiumOfflineSelfTest.cs`
- `Utilities/Reporting/CompendiumPdfGenerationException.cs`
- `Utilities/Reporting/CompendiumPdfReportBuilder.cs`
- `Utilities/Reporting/PublicationFontContract.cs`
- `Utilities/Reporting/PublicationFontRegistry.cs`
- `docs/deployment/offline-ws2022.md`
- `ops/publish/create-publish-folder.ps1`
- `ops/publish/test-compendium-offline-payload.ps1`

## Package documents

- `README-FIRST.md`
- `RELEASE-NOTES.md`
- `VALIDATION.md`
- `ACCEPTANCE-TESTS.md`
- `FILE-MANIFEST.md`
- `CHANGESET.patch`
- `SHA256SUMS.txt`
