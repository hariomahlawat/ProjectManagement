PRISM Publications — Phase 23
Compendium Review, Publication Imagery & Readiness

PURPOSE
Phase 22 established the Compendium authoring foundation: all-project selection, ordering and shared saved configurations. Phase 23 makes Review publication and Publication readiness operational without redesigning the physical Compendium PDF.

IMPLEMENTED
1. Focused project review workspace
   - Previous / next / next requiring attention navigation.
   - Current authoritative project facts shown together.
   - Current project description shown in the publication workspace.
   - Review progress and per-project review state.
   - Permission-safe Open project / Edit record actions.

2. Publication-specific imagery
   - Automatic image selection remains the default.
   - User may lock an explicit photograph for the Compendium.
   - Focal crop is adjustable and persisted in the Saved Compendium.
   - Explicit missing saved photos recover safely to automatic selection with a diagnostic.
   - Review fingerprints are deliberately NOT persisted in presets.

3. Shared source/crop pipeline
   - Probe, browser preview and final PDF image rendering use IBrochurePhotoService.
   - Final PDF uses BrochurePhotoRenderRequest with the same focal X/Y reviewed in the browser.
   - Current QuestPDF physical image viewport is 198 x 152 pt after existing spacing/border/padding; browser crop and effective-DPI policy use this exact viewport.

4. Image quality
   - >= 180 DPI: Good.
   - 150–179 DPI: Usable / information.
   - < 150 DPI: Warning.
   - Effective DPI uses cropped source pixels divided by the actual current PDF image viewport.

5. Review integrity
   - SHA-256 contract: compendium-review-v1.
   - Fingerprint includes live project facts, resolved publication image, image-selection mode and focal crop.
   - Changing project facts or publication imagery invalidates the review.
   - Mark reviewed confirms the current version only; it does not alter the project record and does not dirty the Saved Compendium.

6. Readiness policy
   - Stable Blocker / Warning / Information finding model.
   - Actionable findings with severity filtering and Current project only mode.
   - Missing image, low DPI, missing Arm/Service, missing description, proliferation-data conditions, completion year, title anomaly and stale review are distinguished semantically.
   - Normal quality/data warnings do not become hard publication blockers.

7. Saved Compendium schema v2
   CompendiumPresetProjects adds:
   - PrimaryPhotoId nullable
   - PrimaryFocalX
   - PrimaryFocalY
   - ImageSelectionMode
   Authoritative facts remain in Project/related PRISM records.

8. Performance boundary
   - Preflight probes only the resolved publication image for each selected project.
   - The review endpoint probes all photos only for the single project being reviewed.
   - Unselected projects are not loaded into the publication snapshot/export.

INTENTIONALLY DEFERRED TO PHASE 24
- Compendium PDF visual redesign.
- New cover system.
- Category-divider redesign.
- New detailed project-page layout.
- Physical page planner / PDF post-compose verification.
- Multi-image project layouts.

INSTALLATION
1. Stop the application/debug session.
2. Replace/add files listed in REPLACEMENT-MANIFEST.txt.
3. Keep the Phase 22 migration in place.
4. Clean bin/obj.
5. Run tools/Test-PrismPublicationsPhase23.ps1.
6. Start PRISM. Startup migration should discover and apply 20261208140000_AddCompendiumPublicationImagery.

RECOMMENDED RUNTIME CHECK
- Select several ongoing and completed projects.
- Open Review publication and navigate projects.
- Change one image, adjust its crop and save a shared Compendium.
- Reload the saved Compendium and confirm image/crop restoration.
- Mark a project reviewed; change its crop and confirm review becomes required again.
- Verify low-resolution imagery appears as a warning, not a blocker.
- Preview/download and confirm the PDF uses the selected/cropped publication image.
