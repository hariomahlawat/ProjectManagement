PRISM Publications — Phase 24.1
Compendium Review Flow, Readiness Rationalisation & Publication Freeze

PURPOSE
-------
This is the final planned Compendium hardening pass on top of Phase 24. It removes the remaining high-friction review interaction, makes readiness exception-driven, aligns browser review metadata with the physical publication, replaces the unpredictable three-image cover mosaic with one controlled hero, introduces content-aware project-page balance, and hardens the PDF text layer.

KEY USER EXPERIENCE CHANGES
---------------------------
1. Continuous review
   - The primary review action is now Review & next.
   - One click marks the current live fingerprint reviewed and automatically advances to the next unreviewed project in Publication Order.
   - A reviewed project with warnings does not interrupt the initial review run.
   - The final unreviewed project shows Finish review and remains visible after completion.
   - Ctrl+Enter invokes Review & next / Finish review when focus is not inside an editable control or modal.
   - After all projects are reviewed, the centre navigation action becomes Review warnings; when no actionable warning/blocker remains it becomes No further attention.

2. Review state is no longer a publication warning
   - Unreviewed / stale-review status remains visible in review progress, project state and final issue gating.
   - ReviewRequired and ProjectChangedAfterReview are no longer counted as publication-quality warnings.
   - Automatic image selection and normal unassessed/not-available proliferation states no longer flood Publication Readiness.
   - Findings are ordered Blocker -> Warning -> Information and then by Publication Order.

3. Context-sensitive project facts
   - Ongoing projects no longer show a redundant Completion = Ongoing tile.
   - Completion appears only for completed projects.
   - Proliferation appears only when assessed.
   - Indicative cost is shown only where relevant and includes its unit, for example ₹20 lakh.

4. Controlled Compendium cover
   - The three-image automatic mosaic is removed.
   - Cover imagery supports Automatic hero, a locked project hero, or No imagery.
   - Use as cover hero in Review copies the current publication photograph and focal point into an independent cover decision.
   - Automatic cover selection favours reviewed projects, then image quality, then Publication Order.
   - No imagery or an unavailable render falls back to the existing disciplined forest/gold institutional graphic cover.
   - Cover mode/photo/focal configuration participates in Saved Compendium dirty state and PDF-verification invalidation.

5. Adaptive project-page balance
   - Project pages use PhotoShort, PhotoMedium or PhotoLong layouts based on narrative length.
   - Short/missing narratives receive a substantially larger hero image.
   - Long narratives retain more text capacity and deterministic continuation-page behaviour.
   - Browser review crop/DPI and QuestPDF use the same per-project geometry contract.

6. Publication text hardening
   - CompendiumPublicationTextSanitizer is applied before planning/rendering.
   - Control/non-printing Unicode is removed, line endings are normalised and legitimate Unicode/Markdown/list text is preserved.
   - This prevents hidden PDF text-layer artifacts from changing search/copy/index behaviour.

7. Final issue governance retained
   - Preview remains available whenever the selected publication is technically valid.
   - Final Download still requires zero blockers and every selected current project version reviewed.
   - Warnings do not automatically block final issue after review.
   - PDF verified · N pages persists until a publication-affecting mutation invalidates it.

DATABASE
--------
Additive migration:
20261208150000_AddCompendiumCoverHeroControls

It adds to CompendiumPresets only:
- CoverImageMode
- CoverHeroProjectId
- CoverHeroPhotoId
- CoverFocalX
- CoverFocalY

Saved Compendium settings schema advances from v2 to v3. Existing presets default to Automatic cover imagery. Existing project membership, project order, publication image choices and focal crops are unchanged.

IMPORTANT
---------
Do not remove/recreate the Phase 22 or Phase 23 Compendium migrations. The new migration contains the EF Core discovery attributes required by PRISM's immutable migration startup gate.

INSTALLATION
------------
1. Copy the package contents over the ProjectManagement root, preserving folders.
2. Clean bin/obj.
3. Run tools/Test-PrismPublicationsPhase24_1.ps1.
4. Start PRISM normally so the startup migrator can apply 20261208150000_AddCompendiumCoverHeroControls.

RECOMMENDED RUNTIME CHECK
-------------------------
Use 6-8 mixed ongoing/completed projects. Confirm Review & next advances continuously; a warning project does not stop the sequence; the last project shows Finish review; Ctrl+Enter behaves only outside editable/modal contexts; readiness contains only meaningful exceptions; automatic/locked/no-image cover modes generate correctly; a missing/short description receives the larger project image; long narrative continuation remains stable; and Preview/Download return PDF verified with the same physical page count.
