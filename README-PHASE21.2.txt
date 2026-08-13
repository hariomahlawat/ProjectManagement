PRISM Publications — Phase 21.2
Print Cover Compliance & Final Freeze
=====================================

BASELINE
--------
Apply this package on top of the completed Phase 21.1 Brochure Builder implementation.
No Digital / Comfortable page-design changes are introduced in this phase.

WHAT THIS PHASE FIXES
---------------------
1. Print / Compact Cover B blank hero band
   - Cover B is generated as an 1800 x 1055 publication crop.
   - The physical QuestPDF hero frame now uses the exact same aspect ratio.
   - FitArea therefore no longer leaves the unintended dark-green reserve below the hero.
   - The recovered first-page space is retained inside the measured front-page composition so
     the contacts/strapline remain anchored and the page remains deterministically measured.

2. No fixed PRISM section labels on the compact first/final pages
   The following publication labels are now authoritative editable data rather than renderer literals:
   - Procurement:
   - CONTACTS
   - Developing Agency
   - Manufacturing Agency
   - Visionary Horizons & Strategic Objectives
   - New Simulators.

   Each label can be changed freely. Leave it blank to suppress it. Punctuation is part of the
   editable value; PRISM does not append its own colon/full stop.

3. Measurement and renderer stay in lockstep
   - Removing CONTACTS or agency headings removes their reserved layout height.
   - Removing the Visionary heading removes its heading/gap height.
   - Removing the New Simulators heading measures only the guidance text.
   - Procurement measurement uses the same optional heading/body composition as QuestPDF.

4. Institutional Content UI
   - Adds a compact two-column "Print / Compact section labels" editor inside Institutional content.
   - All six label fields participate in preflight refresh, Restore approved text and shared-brochure
     dirty-state tracking.
   - The controls collapse with the existing Institutional content workspace and do not add another
     top-level settings panel.

5. Shared Saved Brochures
   - Preset schema advances from v3 to v4.
   - All six labels are persisted with HoD/Comdt shared brochures.
   - Existing presets are migrated to the exact legacy visible wording, so the migration is visually
     non-breaking.
   - A deliberately blank label remains blank after save/reload.

DATABASE
--------
New additive migration:
  20261208120000_AddBrochureInstitutionalSectionLabels

The migration adds six nullable BrochurePresets columns and advances SettingsSchemaVersion to 4.
The existing immutable migration IDs are not renamed or modified.

DESIGN/BEHAVIOUR DELIBERATELY NOT CHANGED
-----------------------------------------
- Print project packing and project typography
- Project selection/reordering
- Project publication approval rules
- Cover B hero approval/fingerprint rules
- Cover B focal-point/crop interaction
- Low-DPI warning policy
- Digital / Comfortable cover, project, About SDD, closing and back-cover layouts
- Gallery 2 behaviour
- PDF post-compose verification

VALIDATION
----------
Completed in the generation environment:
- node --check projects-brochure.js: PASS
- complete Publications brochure contract suite against an assembled current source tree:
  105 passed / 0 failed
- Phase 21.2 focused contract tests: PASS
- structural C# delimiter validation across all changed C# sources: PASS
- immutable migration ID occurrence check: PASS (exactly once)
- renderer literal check: PASS (none of the six fixed section labels remain in the Print compositor)

The .NET SDK is not installed in the generation environment. Run the supplied validator on the
development machine; it performs dotnet build and dotnet test when the SDK is available.

APPLY
-----
Replace/add the files exactly as listed in REPLACEMENT-MANIFEST.txt, then run:

  Set-ExecutionPolicy -Scope Process Bypass
  .\tools\Test-PrismPublicationsPhase21_2.ps1

FINAL RUNTIME CHECK
-------------------
Use the same validated case:
  Print / Compact -> B Contemporary / Premium -> approved Cover B hero -> approved projects

Verify:
- no blank dark-green strip below the hero;
- PDF verified page count still matches preflight;
- edit each compact section label and regenerate;
- clear each label once and confirm the label disappears without leaving label-only space;
- save/reload the shared brochure and confirm custom/blank label values persist.
