PRISM Brochure Builder — Phase 21
Digital Final Hardening & Design Freeze

PURPOSE
This phase finishes the Digital / Comfortable profile and establishes a strict cover-authoring rule: PRISM must not add any visible front- or back-cover text that the user cannot edit or suppress. Text that is baked into a selected approved artwork remains part of that artwork and can be changed by selecting another artwork.

IMPLEMENTED
1. Editable/suppressible Digital cover copy
   - Front institution/kicker and publication descriptor are optional editable fields.
   - Title, subtitle, edition and strapline have independent visibility switches.
   - Back-cover organisation line, closing strapline and edition line are optional editable fields; blank means do not render.
   - Cover text persists in Shared Saved Brochures (schema version 2).
   - Cover B approval fingerprint includes all front-cover visible-copy choices.
   - Graphic fallback/montage placeholders contain no system-owned text.

2. Digital feature-page hardening
   - Single-project feature hero grows automatically for shorter narratives.
   - Digital body-copy readability floor remains 10.2 pt.
   - Explicit Gallery 2 gets a dedicated feature page with two independent 16:9 image frames.
   - Digital image-quality assessment uses the largest feature width used by the compositor.

3. About SDD
   - Correct 5:4 institutional-artwork geometry (174 x 139.2 pt).
   - Cover A does not repeat the same institutional artwork immediately on About SDD.
   - Cover B retains institutional artwork on About SDD, creating a deliberate contrast with the project-driven cover.
   - Small spacing/leading improvements improve page balance without adding filler content.

4. Cover B final polish
   - Hero is optically moved upward while preserving its validated crop geometry.
   - Internal publication-process text is removed from the PDF.
   - "Use automatic hero" is explicit in the builder.
   - Editorial approval and technical image quality remain distinct.

5. Digital readiness
   - "Institutional pages" is replaced with "Editorial pages".
   - The count includes Cover + About SDD + optional introduction pages + institutional closing + optional back cover.
   - Low-resolution Cover B hero uses its own issue code and routes to "Fix cover"; project imagery routes to "Fix image".

6. Verification and regression hardening
   - Successful physical PDF verification remains visible for Digital as well as Print.
   - Automatic-resolved Cover B, explicit Cover B, Gallery 2, cover-copy suppression, shared-preset cover-copy roundtrip and fingerprint invalidation have dedicated regression coverage.
   - The Cover B QuestPDF PrimaryLayer fix from Phase 20.3 is retained.

7. Offline/browser hygiene
   - No active Pages/Views/CSS/JS source in this package references fonts.googleapis.com or fonts.gstatic.com.
   - Local PRISM font packaging remains authoritative. A browser console request to Google Fonts after replacement therefore indicates stale/cached markup or injected browser/extension content, not a Phase 21 source dependency.

IMPORTANT COVER RULE
Metadata fields may remain required as publication metadata, but front-cover visibility switches can suppress them completely. Optional front/back lines can be cleared. Back-cover text reserves no mandatory copy. Logos and approved artwork are graphic assets, not hard-coded PRISM text overlays.

MIGRATION
AddBrochureCoverTextControls advances shared brochure preset SettingsSchemaVersion from 1 to 2 and preserves the previous visible cover defaults for existing presets. Those migrated defaults immediately become user-editable/removable.

VALIDATION
Run:
  Set-ExecutionPolicy -Scope Process Bypass
  .\tools\Test-PrismPublicationsPhase21.ps1

Then perform the final visual checks:
  A. Digital / Comfortable + Cover A; toggle every front copy element off and generate.
  B. Digital / Comfortable + Cover B explicit hero; approve, preview and verify.
  C. Digital / Comfortable + Cover B automatic hero; approve, preview and verify.
  D. Select Gallery 2 for a project with two suitable images and verify both crops on one feature page.
  E. Clear every back-cover text field and confirm the back cover contains graphics/decor only.
  F. Save the configuration as a Shared Saved Brochure, reload it and confirm all cover values/visibility choices round-trip.
