PRISM Publications — Phase 24
Compendium Publication Design & Composition Engine

PURPOSE
-------
Phase 22 established all-project selection, ordering and shared Saved Compendiums.
Phase 23 established project review, publication-specific imagery/crop and readiness.
Phase 23.1 hardened the runtime authoring workflow.

Phase 24 freezes that authoring model and replaces the legacy Compendium PDF path with a deterministic, physically verified A4 portrait publication engine.

IMPLEMENTED
-----------
1. Final-issue governance
   - Preview remains available whenever the selected publication is technically valid.
   - Final Download requires every selected project's current fingerprint to be reviewed.
   - Warnings do not automatically block issue once review is complete.
   - Final-output states are now Select projects / Checking / Blocked / Review required / Ready with warnings / Ready to issue.

2. Review parity
   - Project descriptions in Review render safe Markdown rather than exposing Markdown source syntax.
   - Routine automatic-image information is suppressed after the current project version has been reviewed.

3. Deterministic physical page planning
   - New CompendiumPagePlanner decides the complete physical sequence before QuestPDF composition.
   - Planned page kinds: Cover, Index, Project, ProjectContinuation and BackCover.
   - Indexes can span multiple pages.
   - Very long project names reserve extra index row units.
   - Long descriptions are paragraph/sentence/word chunked into deterministic continuation pages.
   - Normal project body text remains 10 pt; the publication contract reserves a 9.5 pt minimum rather than shrinking text to force one-page fit.

4. New A4 portrait Compendium design
   - Formal forest/gold institutional cover with a selected-project image mosaic and graphical no-image fallback.
   - Cover visible copy is limited to the publication title, subtitle, edition and optional handling marking; no fixed narrative slogan is injected.
   - Technical-category index with selected projects only, status/year and planned physical page number.
   - The first project in each technical category receives a stronger category band instead of wasting a full category-divider page.
   - Detailed project pages use adaptive metadata: lifecycle, project/technical category, Arm/Service, completion and proliferation/cost only where meaningful.
   - Reviewed focal crop is used through one full-width 519 x 214 pt publication-image geometry.
   - Missing imagery receives an intentional text-led layout rather than a large 'photograph not available' placeholder.
   - Long descriptions receive continuation pages.
   - Minimal back cover uses publication identity only.

5. One image/crop contract
   - Browser crop, effective-DPI assessment and final QuestPDF render now use the same Phase 24 geometry.
   - Render derivative: 1800 x 742 px.
   - >=180 DPI Good; 150–179 DPI Usable; <150 DPI Warning.
   - Existing Saved Compendium PrimaryPhotoId / ImageSelectionMode / FocalX / FocalY data is reused; no new persistence model is introduced.

6. Physical verification before release
   - QuestPDF output is reopened with the existing PdfPig dependency.
   - Expected page count must equal actual physical page count.
   - Cover identity, every planned index page, index membership, each project start/continuation placement and back-cover edition are verified.
   - A verification failure prevents the PDF from being issued.
   - Preview and Download use the same export/composition/verifier pipeline.
   - Browser receives X-PRISM-Publication-Composition-Verified and X-PRISM-Publication-Page-Count headers.
   - UI reports 'PDF verified · N pages' after a successful composition.
   - Any publication mutation invalidates the previous verification state.

7. Large/heterogeneous catalogue safety
   - Ongoing and completed projects are both supported.
   - Proliferation metadata is rendered only when it has meaning.
   - Missing image render derivatives fall back to the text-led physical layout.
   - Index planning accounts for long names and can continue a technical category across index pages.

DATABASE / MIGRATIONS
---------------------
None.
Keep both existing Compendium migrations unchanged:
- 20261208130000_AddSharedCompendiumPresets
- 20261208140000_AddCompendiumPublicationImagery

INSTALLATION
------------
1. Stop the application/debug session.
2. Copy ADD/REPLACE files from this package into the project root, preserving paths.
3. Do not modify the migration manifest.
4. Clean bin/obj.
5. Run tools/Test-PrismPublicationsPhase24.ps1.
6. Start PRISM and perform the runtime checks below.

RECOMMENDED RUNTIME CHECK
-------------------------
Use a Saved/unsaved Compendium with 6–10 mixed projects:
- at least one ongoing project;
- at least one completed project;
- at least one project with no photo;
- one long project title/description if available;
- at least two technical categories.

Confirm:
- Preview works before all projects are reviewed, but final Download does not.
- Once all projects are reviewed, final Download becomes available even if non-blocking warnings remain.
- Review renders Markdown formatting cleanly.
- the reviewed crop equals the physical project-page crop.
- cover, index, project pages, any continuation page and back cover render correctly.
- index page numbers point to the actual project pages.
- no unselected project appears.
- after successful Preview/Download the UI reports 'PDF verified · N pages'.
- changing selection/order/title/image/crop removes the previous PDF-verified state.

IMPORTANT VALIDATION NOTE
-------------------------
The artifact environment used to prepare this package does not contain the .NET SDK, so no dotnet build/test result is claimed here. Node/static validation completed successfully; run the included validator on the actual development workstation for authoritative C# build/test validation.
