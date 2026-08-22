PRISM ERP — Compendium Phase 43: Cover Proof/PDF Parity
Date: 23 Aug 2026

PURPOSE
Correct the Institutional Hero cover regression and restore browser/PDF parity.

READY-TO-PASTE FILES
1. wwwroot/css/pages/projects-publications.css
2. Utilities/Reporting/CompendiumPdfReportBuilder.cs
3. Utilities/Reporting/CompendiumBuildIdentity.cs
4. wwwroot/js/projects/publications-compendium-phase41-offline-runtime.test.js
5. wwwroot/js/projects/publications-compendium-phase43-cover-proof-parity.test.js (new)
6. ProjectManagement.Tests/Publications/CompendiumPhase41ProductionConvergenceTests.cs

WHAT IS FIXED
- Institutional Hero is no longer forced to position:absolute by the Phase 37.7 pattern-stacking rule.
- The Institutional Hero identity and hero frame remain in normal flex/document flow.
- Browser geometry now explicitly owns 52px side margins, 12px column spacing, 22px hero top padding and a fixed 300px hero frame.
- Stale contradictory absolute Institutional Hero geometry has been removed.
- QuestPDF now renders the gold identity rule before eyebrow/title, matching the browser proof.
- Build identity is advanced to Phase 43 / physical-a4-v43 so production diagnostics identify the corrected renderer.
- A regression test prevents Institutional Hero from being accidentally reintroduced into absolute pattern stacking.

NO DATABASE MIGRATION
No schema or data migration is required.

RECOMMENDED VALIDATION ON THE DEVELOPMENT MACHINE
node --test .\wwwroot\js\projects\publications-compendium-phase38-cover-reliability.test.js .\wwwroot\js\projects\publications-compendium-phase41-offline-runtime.test.js .\wwwroot\js\projects\publications-compendium-phase43-cover-proof-parity.test.js
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter "FullyQualifiedName~CompendiumPhase38CoverReliabilityTests|FullyQualifiedName~CompendiumPhase41ProductionConvergenceTests|FullyQualifiedName~CompendiumPhase42SlotStabilityTests"

VISUAL ACCEPTANCE
Open Cover Design -> Institutional Hero and confirm:
- hero frame spans the full 491-design-unit content width;
- hero remains a 300-design-unit landscape frame;
- hero begins below the complete identity block with no overlap;
- changing title/subtitle length pushes the hero down rather than drawing through it;
- Full-Bleed Hero and Image Echo remain layered/absolute as before;
- generated PDF uses the same gold-rule-before-title identity order as the browser proof.
