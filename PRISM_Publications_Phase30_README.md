# PRISM Publications Phase 30 — Cover Composer & Publication Imagery

## Purpose
Phase 30 completes the next major Compendium publication-quality layer: user-authored front/back covers and editorially controlled publication imagery. It is designed to sit directly on top of the Phase 29.1 source.

## Installation
1. Back up the project or commit the current source.
2. Extract `PRISM_Publications_Phase30_ReadyToPaste.zip` into the ProjectManagement project root and overwrite the matching files.
3. Build the solution.
4. Apply the included EF Core migration using the project's normal migration process. PRISM already applies migrations at startup in the current application configuration; alternatively use `dotnet ef database update` where appropriate.
5. Run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPublicationsPhase30.ps1
```

## Database change
Phase 30 advances Compendium preset schema to **v6** and includes:

`Migrations/20261208180000_AddCompendiumCoverComposer.cs`

The migration introduces first-class front/back cover settings, cover image slots, editorial photo preferences, project image Fit/Fill mode, and migrates the legacy single hero into the new front-cover Hero slot.

## Front cover composer
The publisher can independently choose one of these controlled layouts:

- Institutional Hero — one principal image with the established institutional visual language.
- Full Bleed Hero — one dominant image with publication identity overlaid safely.
- Editorial Split — hero + supporting image.
- Triptych — three curated image slots.
- Minimal — typography-led cover without required imagery.

Each image slot supports:

- Automatic / explicit photo / no image where allowed.
- Fill (editorial crop) or Fit (preserve complete diagram/screenshot).
- Independent focal point/crop.
- Technical quality/DPI evaluation.

## Back cover composer
Front and back are independent. Available back designs:

- Minimal Institutional
- Image Echo
- Portfolio Strip
- Typography Only
- Clean

The back can therefore remain restrained even when the front uses a multi-image composition.

## Publication-controlled cover identity
The PDF renderer no longer contains hard-coded publication identity such as `Simulators Compendium`, `Detailed Project Reference`, or `Capability Edition`.

Cover content is publication data. The editor supports:

- Eyebrow/series line
- Title
- Subtitle
- Edition/issue line
- Show/hide controls
- Left/right mark visibility
- Controlled mark placement: Top corners or Top centre

Blank optional values remain blank rather than being replaced with renderer-authored copy.

## Dedicated Cover Editor
A full-width Cover Editor is added at:

`/Projects/Publications/Compendium/Cover?presetId=<id>`

It provides:

- Front / Back editing tabs
- Template gallery
- Near-final A4 live proof
- Content inspector
- Cover image slot editor
- Project photo picker with dimensions and technical quality
- Fit / Fill control
- Focal crop editor
- Editorial suitability controls
- Saved/modified state and row-version-safe persistence

The normal Compendium keeps only a compact Cover Design summary and `Edit cover` action.

## Editorial image suitability
Phase 30 separates technical image quality from editorial suitability.

Per publication photo, the publisher can mark:

- **Cover preferred** — preferred for automatic publication-cover selection.
- **Cover suitable** — explicitly suitable to serve as a cover hero.

Automatic cover selection prioritises these authored decisions before falling back to project cover/marked cover/available publication imagery. The preference is publication metadata only; it does not alter the underlying ProjectPhoto record.

## Project dossier Fit / Fill
Fit/Fill is also added to normal project dossier imagery:

- **Fill** — fills the publication frame and uses the saved focal crop. Best for photographs.
- **Fit** — preserves the complete source inside the frame. Best for diagrams, screenshots, charts and process graphics.

The same choice is used by the Review proof, DPI calculation, image render and final PDF.

This directly addresses destructive cropping of diagrams/infographics in earlier Compendium output.

## Cover readiness
Cover composition participates in preflight/readiness. Examples include:

- explicit cover image is missing/unavailable — blocker;
- explicit cover image is below print-quality target — warning;
- Automatic Hero has no explicitly cover-suitable candidate — information;
- required explicit cover image cannot be rendered — final export fails rather than silently issuing a broken cover.

Automatic image selection remains resilient and can fall back when an automatic candidate cannot be rendered.

## Deterministic content hygiene warnings
Publication readiness now detects obvious source-quality problems without modifying source data:

- probable placeholder text (`Lorem ipsum`, dummy/testing/sample text);
- probable duplicate narrative paragraph/content.

These are warnings only. PRISM never silently rewrites the Project Brief, Description or Capability Overview.

## Project Selection polish
At wide desktop breakpoints the publication register has been rebalanced so Lifecycle, Cost and Photography stay intact rather than breaking words such as `Completed` or `Photography` mid-word. Narrative missing states also use amber semantics rather than a success-coloured shell.

## Compatibility / integrity
- Existing first-class Custom Sections remain unchanged.
- Structure Editor remains unchanged functionally and preserves Phase 30 image-fit/cover handoff state.
- Existing saved Compendiums are migrated forward.
- Legacy single cover hero remains compatible through migration/fallback mapping.
- Review fingerprint contract advances to v4 where publication image fit changes the issued page.
- Compendium PDF build stamp: `CompendiumPdf_2026-08-14_cover-composer-v8`.

## Validation completed in the delivery environment
- `projects-compendium.js` syntax: PASS
- `projects-compendium-structure-editor.js` syntax: PASS
- `projects-compendium-cover-editor.js` syntax: PASS
- Compendium JavaScript publication contracts: **85 / 85 PASS**
- Combined Brochure + Compendium contracts: **189 / 190**. The single failure is the pre-existing Brochure Phase 9 `IBrochurePrintMeasurementService` Singleton-vs-Scoped expectation; the same failure is present in the untouched Phase 29.1 baseline.
- Changed C# delimiter/structure sanity check: PASS (20 changed C# files, 0 mismatches)
- Phase 30 migration present in immutable migration manifest: PASS
- PDF builder hard-coded cover identity check: PASS

The delivery environment does **not** contain the .NET SDK, therefore no `dotnet build` or `dotnet test` pass is claimed here. Run the supplied validation script on the development workstation before deployment.
