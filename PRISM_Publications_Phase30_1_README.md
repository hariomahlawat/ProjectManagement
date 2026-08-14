# PRISM Publications Phase 30.1 — Cover Composer Fidelity & UX Polish

## Purpose
Phase 30.1 is a focused stabilization pass on top of the **corrected Phase 30 source** (including the `BrochurePhotoService.cs` CS0136 hotfix). It does not add another publication subsystem or database model. It tightens the Cover Composer so that the live proof, automatic multi-image behavior, inherited publication identity, compact Compendium controls and institutional marks behave like a finished publishing workflow.

## Installation
1. Back up or commit the current ProjectManagement source.
2. Confirm Phase 30 is already applied, including migration `20261208180000_AddCompendiumCoverComposer` and the CS0136 hotfix.
3. Extract `PRISM_Publications_Phase30_1_ReadyToPaste.zip` into the **ProjectManagement project root** and overwrite matching files.
4. No new EF migration is required for Phase 30.1.
5. Run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase30_1.ps1
```

## 1. Authoritative Cover Proof geometry
The browser proof no longer responsively reflows the internal cover layout. The A4 cover is composed at a fixed **595 × 842 design surface** and then displayed/scaled as a unit.

Phase 30.1 aligns the browser proof with the PDF renderer for:
- title/content width;
- title/subtitle/edition typography;
- identity block placement;
- gold rule placement;
- image-slot geometry for Institutional Hero, Full-bleed Hero, Editorial Split and Portfolio Triptych;
- back-cover layout families;
- institutional mark bounding boxes.

This specifically removes the misleading case where the Cover Editor wrapped the title differently from the generated PDF simply because the browser proof had a narrower responsive text block.

## 2. Explicit inheritance / override UX
Cover identity still inherits Publication Settings by default, but inheritance is now visible rather than encoded as an empty textbox.

For Title, Subtitle and Edition/Issue line, the editor exposes:
- **Inherited** state and the inherited value;
- **Override** action;
- explicit override editor;
- **Reset to inherited** action;
- normal Show/Hide control.

The Eyebrow / Series label remains a cover-only optional field.

No hard-coded renderer copy is introduced.

## 3. Multi-image automatic diversity
Automatic Editorial Split and Triptych composition now avoids duplicate imagery more deliberately.

The browser Cover Editor and server export pipeline both prefer:
1. an unused photo from an unused project;
2. an unused photo from an already-used project;
3. a ranked fallback only when necessary.

Explicitly chosen images remain authoritative, so a publisher may intentionally repeat an image if required.

Automatic async slot hydration also rejects stale responses when the user changes template or imagery while a preview request is still in flight.

## 4. Template-aware compact cover controls
The normal Compendium page no longer presents Hero controls for every template:
- **Institutional Hero / Full-bleed Hero:** quick Automatic Hero and Choose Hero controls remain available.
- **Editorial Split / Triptych:** the primary action becomes **Edit cover images**.
- **Minimal:** the primary action is simply **Edit cover** and imagery controls are suppressed.

The dedicated Cover Editor remains the authoritative place for complete front/back composition.

## 5. Fit / Fill interaction
The Phase 30 Fit/Fill behavior is retained. Phase 30.1 explicitly protects the interaction contract:
- **Fill** → focal crop / Adjust crop is available.
- **Fit** → the complete source is preserved and Adjust crop is unavailable because there is no destructive crop to adjust.

## 6. Institutional mark polish
Generic `Left mark` / `Right mark` wording is replaced in the Cover Editor with meaningful controls and thumbnail previews:
- **Formation mark**
- **SDD mark**

The PDF and browser proof use separate optical boxes rather than treating dissimilar marks as identical raw-size assets:
- Formation mark: 44 × 44 design units
- SDD mark: 48 × 48 design units
- centred lockup gap: 20 design units

This improves both Top Corners and Top Centre arrangements.

## 7. Cover-suitable fallback severity
When automatic front-cover imagery has no image explicitly marked **Cover suitable**, Readiness now reports a **Warning** rather than Information.

It remains non-blocking: PRISM may still use ranked fallback imagery, but the publisher receives an appropriately prominent editorial warning before formal issue.

## 8. Build identity and compatibility
Compendium build stamp:

`CompendiumPdf_2026-08-14_cover-fidelity-v9`

Phase 30.1:
- retains Compendium preset schema v6;
- adds **no database migration**;
- retains all Phase 30 Cover Composer persistence contracts;
- retains first-class Custom Sections and Structure Editor behavior;
- retains Review / Focus Review / Readiness semantics;
- does not add stock-image, watermark or test-content governance beyond the deterministic warnings already present in Phase 30.

## Validation completed in the delivery environment
- `projects-compendium.js` syntax: PASS
- `projects-compendium-structure-editor.js` syntax: PASS
- `projects-compendium-cover-editor.js` syntax: PASS
- Complete Compendium JavaScript contract suite (base + Phase 29 + 29.1 + 30 + 30.1): **93 / 93 PASS**
- Phase 30.1 adds no EF migration: confirmed by source diff
- Ready-to-paste reconstruction over corrected Phase 30 baseline: verified during packaging

The delivery environment does **not** contain the .NET SDK. No `dotnet build` or `dotnet test` pass is claimed here; the supplied validation script runs both automatically when executed on the development workstation.
