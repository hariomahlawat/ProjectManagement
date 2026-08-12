PRISM Capability Brochure — Phase 17 Publication Hardening
==========================================================

Purpose
-------
This is an incremental replacement bundle for a project that already has Phase 16 applied.
It completes the remaining Print / Compact hardening and UI polish identified during review.

Implemented
-----------
1. Publication-readiness terminology now consistently uses PAGE rather than SHEET in user-facing UI and preflight messages.
2. Final Output now keeps a persistent post-compose confirmation: "PDF verified · N pages" after a successful Print / Compact preview/download.
   The confirmation is automatically invalidated whenever publication geometry/content changes and preflight is rerun.
3. Findings no longer force the user through Locate -> Configure image -> Manage photos.
   - Image findings: Fix image -> opens the publication image editor directly.
   - Cover findings: Fix cover -> takes the user to Cover B configuration.
   - Source link is reduced to Photos / Add photo.
   - Other project findings use the clearer "Show project" action.
4. Low-resolution warnings are now based on EFFECTIVE CROPPED DPI at the physical publication frame, rather than raw source dimensions alone.
   - Print / Compact warning floor: 240 effective dpi.
   - Digital / Comfortable screen-first floor: 180 effective dpi.
   - The calculation accounts for the publication crop before computing usable pixels.
   - A 1024x1024 or 1024x540 source is therefore not incorrectly warned merely because it is below a generic 1100/1400 px threshold when used in a small compact project frame.
5. 9 pt is now a hard minimum for Print / Compact project body copy, including emergency compact geometry. The optimiser may reduce image width/padding/spacing, but not project body typography below 9 pt.
6. Singular/plural approval grammar is corrected on both client and server.
7. The technical preflight explanation is shortened to editorial language while preserving the same render-verification behaviour.

New file
--------
Services/Publications/BrochurePhotoPrintQualityEvaluator.cs

Replace the other files at their matching project paths.

Validation performed in this environment
----------------------------------------
- node --check wwwroot/js/pages/projects-brochure.js : PASS
- node --test wwwroot/js/projects/publications-brochure-contract.test.js : PASS (61/61)
- Effective-DPI reference calculations checked:
  * 400x400 in largest Print / Compact project frame -> approx. 185 effective dpi -> warning
  * 1024x1024 -> approx. 473 effective dpi -> no warning
  * 1024x540 -> approx. 443 effective dpi after 16:9 crop -> no warning

The container does not contain the .NET SDK, so dotnet build/test could not be executed here.
Run the supplied tools/Test-PrismPublicationsPhase17.ps1 and the normal dotnet build/test commands on the development machine.

Post-paste functional check
---------------------------
Use the same nine-project Print / Compact test set:
- Publication readiness should say Planned pages.
- Preview the PDF.
- Final Output should then show "PDF verified · 4 pages" (or the actual verified count for the selected data).
- The preflight page count and physical PDF page count must agree.
- The previous 1024x1024 and 1024x540 image warnings should disappear when their effective DPI is sufficient.
- A genuinely small source such as 400x400 should remain a warning.
- Final download remains governed by project/cover publication approval exactly as before.
