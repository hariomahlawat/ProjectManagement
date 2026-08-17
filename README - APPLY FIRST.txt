PRISM COMPENDIUM — COVER COMPOSER RELIABILITY & PREVIEW PARITY
================================================================

READY TO PASTE
--------------
Copy the files from this package over the matching paths in the current project.
New files are created automatically when pasted. No database migration is required.
No Program.cs / DI registration change is required.

WHAT THIS PHASE FIXES
---------------------
1. Saved theme/background now survive the main Compendium workspace.
   - publicationTheme and backgroundTreatment are preserved in the canonical cover state.
   - Preview and later Compendium Save no longer reset Burgundy/Navy/etc. to defaults.

2. False "Modified" state is removed.
   - Dirty comparison uses only persisted cover fields.
   - previewUrl/sourceWidth/sourceHeight and other hydration data no longer create fake edits.

3. Browser and PDF automatic imagery now share one deterministic server policy.
   - New CompendiumCoverAutomaticImagePolicy ranks curated Suitable/Preferred images and
     the resolved project cover/default photo consistently.
   - Browser receives the server-ranked candidates instead of maintaining another ranking algorithm.
   - Automatic focal points round-trip into browser proof and final PDF.

4. Automatic-image rendering is resilient.
   - Final export tries the next ranked automatic candidate when a candidate cannot render.
   - Explicit user-selected images remain strict and fail visibly rather than being silently substituted.

5. Required image slots are enforced generically.
   - Required-slot rules come from CompendiumCoverTemplatePolicy for all templates, not only Quartet.
   - Browser disables No image for a required slot.
   - Cover Save and publication preflight validate the same requirement server-side.

6. Fit proof background now follows the active cover theme.
   - Browser uncovered Fit areas match QuestPDF themed surfaces instead of generic grey.

7. Institutional Hero proof spacing is aligned with PDF composition.

8. Long cover wording is hardened.
   - Browser and QuestPDF use the same conservative adaptive title/subtitle typography policy.
   - Wording is never silently truncated.
   - Excessively dense identity wording raises a report/preflight warning to inspect Front/Back proof.

9. Invalid photo-preference JSON no longer means "clear all preferences".
   - Invalid payloads fail validation and preserve existing saved data.

10. Viewport handling is less brittle.
    - Editor measures its real workspace top and uses 100dvh rather than relying only on 100vh - 190px.

TEST / CONTRACT UPDATES
-----------------------
- Added Phase 38 cover reliability JS contracts.
- Added C# policy tests for automatic ranking, focal points, typography and required slots.
- Updated older source-contract tests whose implementation moved into the shared automatic-image policy.

VALIDATION PERFORMED HERE
-------------------------
- node --check projects-compendium.js: PASS
- node --check projects-compendium-cover-editor.js: PASS
- all Compendium Node/source contract tests: 220 PASS / 0 FAIL
- static C# brace/source checks: PASS

The execution environment does not contain the .NET SDK, so run the real C# build/tests locally.

RECOMMENDED LOCAL VALIDATION
----------------------------
dotnet build .\ProjectManagement.csproj
dotnet build .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj `
    --filter "FullyQualifiedName~Compendium"

node --check .\wwwroot\js\pages\projects-compendium.js
node --check .\wwwroot\js\pages\projects-compendium-cover-editor.js
node --test .\wwwroot\js\projects\publications-compendium*.test.js

MANUAL ACCEPTANCE CHECKS
------------------------
A. Theme round-trip
   Burgundy + Technical Grid -> Save Cover -> Back -> Preview -> final PDF.
   The same theme/treatment must remain after also saving/reopening the Compendium.

B. Dirty state
   Open an already-saved cover and allow photos to load. Status must remain Saved.
   Zoom/Front/Back navigation must not mark Modified. Real cover changes must.

C. Automatic imagery
   Browser proof and PDF must resolve the same automatic photos and focal points.
   If the top automatic candidate is unavailable, the next ranked candidate should be used.

D. Explicit imagery
   Remove/break an explicitly selected photo. Preflight/export must flag it; no silent substitution.

E. Required slots
   Full-Bleed Hero / Editorial Split / Triptych / Image Echo etc. must not allow a required hero to be saved as None.

F. Fit
   Select Fit and compare browser proof vs PDF. Uncovered image area should use the same theme surface.

G. Long wording
   Test a long title/subtitle. Typography should reduce within safe limits and preflight should warn when wording is unusually dense.
