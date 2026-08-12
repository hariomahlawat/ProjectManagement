PRISM PUBLICATIONS - PHASE 20
Digital / Comfortable Editorial Publication

PURPOSE
Phase 20 takes the existing Digital / Comfortable profile from a spacious derivative of the
compact brochure to a deliberate screen-first A4 editorial publication. Print / Compact remains
frozen except for shared contracts used by both profiles.

IMPLEMENTED
1. DIGITAL PAGE ARCHITECTURE
   - Cover
   - Dedicated About SDD institutional opening when approved institutional matter is present
   - Optional Additional Introduction page(s)
   - Project capability pages
   - Dedicated Future capability & engagement closing page
   - Optional minimal back cover

2. SCREEN-FIRST PROJECT PLANNER
   - Digital pages are restricted to one or two project modules.
   - SingleFeature is used for long narratives, continuations and Gallery 2.
   - TwoFeature is used only where the pair remains comfortably readable.
   - Project order remains authoritative.

3. COVER A - INSTITUTIONAL / EVOLUTIONARY
   - Retains the dark institutional visual system.
   - Removes the previous wide empty artwork frame.
   - Uses the selected institutional artwork as an intentional editorial object.
   - Removes duplicate edition text below the artwork.

4. COVER B - CONTEMPORARY / PREMIUM
   - Becomes a genuinely image-led cover.
   - Uses an independent large 1800 x 1360 publication crop.
   - Uses a 543 pt physical hero frame with a restrained institutional title / strapline system.
   - Cover crop editor, automatic hero selection and effective-DPI preflight use the same aspect.

5. PROFILE-AWARE INSTITUTIONAL CONTENT
   - The UI is now labelled Institutional content rather than Print institutional content.
   - The same approved authoritative fields serve both profiles.
   - Digital uses the material on dedicated opening and closing pages rather than silently dropping it.

6. DIGITAL CLOSING
   - Restores Visionary Horizons & Strategic Objectives as a dedicated editorial closing panel.
   - Adds procurement / engagement and developing / manufacturing agency information.
   - Retains New Simulators as the final institutional green call-to-action.
   - The minimal back cover remains separate and uncluttered.

7. PAGE NUMBERING AND PREFLIGHT
   - Inner pages show full physical-document numbering, e.g. 4 / 10, rather than project-page-only 1 / 6.
   - Publication Readiness receives a Digital-specific plan: total pages, project pages, feature pages,
     two-project pages, institutional pages and an explicit physical page map.
   - Digital institutional opening and closing have readability word limits.

8. IMAGE QUALITY
   - Digital effective-DPI checks now follow the actual layout selected by the Digital planner.
   - Single feature frame: largest feature geometry.
   - Two-project page: dedicated editorial-split geometry.
   - Cover B: actual premium-cover geometry.
   - The warning remains based on pixels surviving the publication crop, not raw source dimensions.

9. POST-COMPOSITION VERIFICATION
   - Digital PDFs are reopened after QuestPDF composition.
   - Physical page count and project page membership are verified before preview/download is issued.
   - Institutional opening and closing page membership is also verified.

EXPECTED RESULT FOR THE CURRENT NINE-PROJECT TEST BROCHURE
With standard institutional content, no extra introduction and the back cover enabled, the current
six Digital project pages become a 10-page publication:
  1 Cover
  2 About SDD
  3-8 Project capabilities
  9 Future capability & engagement
 10 Back cover
The Publication Readiness plan remains the authority if project lengths or options differ.

APPLY
Copy the REPLACE and ADD files over the corresponding paths in the PRISM project.
No database migration or package update is required.

VALIDATE
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase20.ps1

The validation script runs the JavaScript syntax/contract suite and, when the .NET SDK is present,
runs dotnet build and the project test suite.
