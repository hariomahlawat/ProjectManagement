# File manifest

Copy the paths below into the project root. `Replace` means the path exists in the supplied
baseline; `Add` means it is new.

| Action | Project-relative path | Purpose |
|---|---|---|
| Replace | `Program.cs` | Run the offline PDF self-test before host/database startup. |
| Replace | `Pages/Projects/Publications/Compendium/Cover.cshtml` | Load the deterministic cover-state helper before the editor. |
| Replace | `Pages/Projects/Publications/Compendium/Index.cshtml.cs` | Typed HTTP failures, build header and durable diagnostics. |
| Add | `ProjectManagement.Tests/Publications/CompendiumPhase41ProductionConvergenceTests.cs` | Font, pagination and typed-failure tests. |
| Replace | `Services/Compendiums/CompendiumDossierTextMeasurementService.cs` | Use the shared exact DM Sans resolver for Skia planning. |
| Replace | `Services/Compendiums/CompendiumExportService.cs` | Bounded generation, stage isolation and independent cover-surface allocation. |
| Add | `Utilities/Reporting/CompendiumBuildIdentity.cs` | One correlation identity for HTTP, PDF, diagnostics and self-test. |
| Add | `Utilities/Reporting/CompendiumGenerationDiagnostics.cs` | Offline-safe JSONL failure diagnostics. |
| Replace | `Utilities/Reporting/CompendiumLayoutMetrics.cs` | Increase the cross-engine shaping reserve from 1 to 12 points. |
| Add | `Utilities/Reporting/CompendiumOfflineSelfTest.cs` | Exercise DM Sans, SkiaSharp, QuestPDF and PdfPig without web/database access. |
| Add | `Utilities/Reporting/CompendiumPdfGenerationException.cs` | Typed generation stages and safe page/project context. |
| Replace | `Utilities/Reporting/CompendiumPdfReportBuilder.cs` | Hard font parity, QuestPDF failure classification and failure-only page probing. |
| Add | `Utilities/Reporting/PublicationFontContract.cs` | Authoritative local six-face DM Sans discovery contract. |
| Replace | `Utilities/Reporting/PublicationFontRegistry.cs` | Register fonts through the shared contract and forbid silent Compendium fallback. |
| Replace | `docs/deployment/offline-ws2022.md` | Offline IIS publish, self-test and diagnostics runbook. |
| Replace | `ops/publish/create-publish-folder.ps1` | Self-contained win-x64 publish and release-gate validation. |
| Add | `ops/publish/test-compendium-offline-payload.ps1` | Revalidate the exact staged/deployed payload. |
| Replace | `wwwroot/js/pages/projects-compendium-cover-editor.js` | Race-safe image changes, crop ownership and canonical save rehydration. |
| Add | `wwwroot/js/projects/compendium-cover-editor-state.js` | Pure/testable cover photo identity and request-version rules. |
| Replace | `wwwroot/js/projects/publications-compendium-phase30-1-contract.test.js` | Advance the legacy allocation contract to surface-scoped keys. |
| Add | `wwwroot/js/projects/publications-compendium-phase41-cover-change.test.js` | Cover replacement and allocation regression coverage. |
| Add | `wwwroot/js/projects/publications-compendium-phase41-offline-runtime.test.js` | Offline runtime and publish-contract coverage. |
| Add | `wwwroot/js/projects/publications-compendium-phase41-production-convergence.test.js` | Pagination/font/failure-stage regression coverage. |

The package intentionally does not duplicate the existing TTF binaries. The hardened publish script
requires and validates all six files already present under `wwwroot/fonts/publications/dm-sans`.
