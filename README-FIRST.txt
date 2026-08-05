PRISM BRIEFING DECK — REMAINING READABILITY FIXES
=================================================

Replace/add the files in this package using the exact project-relative paths.
No database migration is required.

PRODUCTION CHANGES
------------------
1. SDD institutional timeline
   - Moves the upper milestone blocks upward to create clear separation from the chronology line.
   - Lower milestone geometry remains unchanged.

2. Project Update Sheet typography and pagination
   - Raises fact-table typography to presentation-readable sizes.
   - Sets Project Brief body typography to 15 pt / 13.4 pt / 12 pt according to content density.
   - Removes PowerPoint auto-shrink from Project Brief text.
   - Paginates long Project Brief content onto editable “BRIEF OF THE PROJECT — CONTINUED” slides.
   - Keeps compact sheets at an approximately 50:50 photograph/content split.

3. Project Update Sheet photographs
   - Crops and resizes selected photographs to fill the complete photo panel.
   - Preserves aspect ratio and avoids the large unused bands seen previously.
   - Falls back to the original image if an unexpected decoder error occurs.

4. Slide estimate
   - Includes likely Project Brief continuation slides in the preflight slide estimate.
   - The composer remains the final geometry-aware source of truth during generation.

FILES
-----
Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.UpdateSheet.cs
Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.InstitutionalProfile.cs
Services/ProjectBriefings/ProjectBriefingDataService.cs
ProjectManagement.Tests/ProjectBriefings/ProjectBriefingSlideComposerTests.cs
ProjectManagement.Tests/ProjectBriefings/ProjectBriefingRemainingIssuesContractTests.cs   (new)

VALIDATION AFTER REPLACEMENT
----------------------------
1. Clean and rebuild the solution in Visual Studio.
2. Run the ProjectBriefings test suite.
3. Generate a Project Update Sheet with a long Project Brief and verify:
   - body text remains at least 12 pt;
   - a continuation slide is created;
   - the photograph fills its panel;
   - slide estimate includes the continuation.
4. Generate the SDD profile and verify clear space between upper milestones and the timeline.
