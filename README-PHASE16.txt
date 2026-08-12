PRISM Capability Brochure — Phase 16
Render-Verified Compact Publication + UI Polish
=================================================

Purpose
-------
This replacement bundle addresses the issues observed in the six-project Print / Compact brochure:
1. Publication preflight predicted 3 pages while the physical PDF rendered 4.
2. A sparse intermediate page could therefore be issued even though preflight reported 100% non-final fill.
3. Cover A title/subtitle/edition controls implied visible output changes that the approved full-artwork cover intentionally does not render.
4. Compact project body copy was over-justified and the closing Visionary Horizons panel used a disconnected visual language.
5. Publication order and review action controls needed a final pass for clarity and density.

Implementation summary
----------------------
A. Render verification is now mandatory for Print / Compact.
   - BrochurePdfReportBuilder plans once, renders from that exact plan, then re-opens the generated PDF.
   - Physical page count must equal the preflight plan.
   - Every planned project title must appear on its planned physical page.
   - Closing matter must appear on the planned final project page.
   - Any drift throws BrochurePdfCompositionException; preview/download returns HTTP 409 and NO mismatched PDF is issued.
   - Successful compact responses expose verified page-count headers used by the Final output UI.

B. Planner/render geometry is made more conservative and deterministic.
   - A 12 pt physical compositor reserve prevents planning to the final few points of the QuestPDF content box.
   - Closing-panel measurement uses the same border/padding/spacing constants as the renderer.
   - The existing optimiser remains page-count-first and may select a denser approved 9 pt candidate to preserve compact packing where it genuinely fits.

C. PDF visual refinements.
   - Project narrative is ragged-right rather than full-justified.
   - Visionary Horizons is harmonised with the institutional green system: neutral paper, thin green border, green heading, regular body copy.
   - New Simulators remains a strong green institutional band.

D. UI refinements.
   - In Print / Compact + Cover A, title/subtitle/edition inputs are hidden because the approved full artwork owns visible identity. Their values are retained as metadata/file naming values.
   - Publication-order names use two lines; up/down controls stay quiet until hover or keyboard focus.
   - Clear selection is explicit and confirms before discarding an approved working selection.
   - Review actions are grouped as Publication image vs Authoritative source.
   - Preflight metrics use editorial labels: Average page fill / Lowest page fill / Final page fill.
   - Final output reports “composition verified (N pages)” after a successful physical verification.

Files to replace/add
--------------------
See REPLACEMENT-MANIFEST.txt. Preserve the folder structure and copy files over the project root.
BrochurePdfCompositionVerifier.cs is NEW.
Test-PrismPublicationsPhase16.ps1 is NEW.

Recommended validation on your development machine
---------------------------------------------------
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase16.ps1

dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
node --check .\wwwroot\js\pages\projects-brochure.js
node --test .\wwwroot\js\projects\publications-brochure-contract.test.js

Then regenerate the same six-project Print / Compact brochure. The Publication readiness page count and the physical PDF page count must agree. A mismatched render is now blocked rather than downloaded.

Environment validation performed for this bundle
-------------------------------------------------
- JavaScript syntax: PASS
- Browser/source contract tests: 55/55 PASS
- Structural balance check on modified C# files: PASS
- .NET build/test: NOT RUN in the packaging environment because the .NET SDK is not installed there.
